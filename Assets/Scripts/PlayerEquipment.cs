using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// 플레이어가 "손에 든 것" 상태와 그 연출 전담 (NetPlayer 프리팹에 부착).
/// 슬롯: 1=빠따 / 2=부스트 주사기 / 3=감속 주사기 / 4=무전기(미구현·맨손).
/// - 시각: 무장 애니 전환(Armed bool → 컨트롤러가 꺼내기/집어넣기 재생), 휘두르기(코드 CrossFade),
///         오른손 소품(빠따/주사기 — 코드 생성), 주사기 들 때 오른팔 들기 레이어(HoldRight).
/// - 동기화: 슬롯 변경/휘두르기는 RPC로 전 클라 로컬 재생.
///   PhotonAnimatorView 동기화 목록은 건드리지 않는다(파라미터는 각 클라가 RPC로 직접 세팅).
/// - 판정 없음: 지금은 순수 연출. 타격 판정·빠따 내구도는 피격 모션 확보 후 별도 작업.
/// </summary>
public class PlayerEquipment : MonoBehaviourPun
{
    public const int SlotBat = 1, SlotBoost = 2, SlotSlow = 3, SlotRadio = 4;

    /// <summary>내 아바타의 장비 컴포넌트 (미접속=내 것 규칙).</summary>
    public static PlayerEquipment Local { get; private set; }

    [SerializeField] private Animator animator;

    [Header("휘두르기")]
    [Tooltip("휘두르기 간격 (초) — 판정 생기면 내구도와 함께 재조정 예정")]
    [SerializeField] private float swingCooldown = 1.1f;
    [Tooltip("휘두르기 랜덤 풀 (컨트롤러 상태 이름) — 현재 Kevin Iglesias 2H 공격 1종")]
    [SerializeField] private string[] attackStates = { "Attack2H" };

    [Header("소품 타이밍")]
    [Tooltip("꺼내기 애니 시작 후 빠따가 손에 나타나기까지 (초)")]
    [SerializeField] private float batShowDelay = 0.35f;
    [Tooltip("집어넣기 애니 시작 후 빠따가 사라지기까지 (초)")]
    [SerializeField] private float batHideDelay = 0.5f;

    [Tooltip("집어넣기 연출 시간 — 이만큼 지난 뒤 상체 무장 레이어를 서서히 끈다 (초)")]
    [SerializeField] private float sheatheTime = 0.9f;

    [Header("소품 위치 (오른손 본 기준 로컬) — 플레이 중 바꾸면 즉시 반영")]
    [SerializeField] private Vector3 batLocalPos = new Vector3(0.04f, 0.03f, 0.01f);
    [SerializeField] private Vector3 batLocalEuler = Vector3.zero;
    [SerializeField] private Vector3 syringeLocalPos = new Vector3(0.04f, 0.03f, 0.01f);
    [SerializeField] private Vector3 syringeLocalEuler = Vector3.zero;

    public int HeldSlot { get; private set; } = SlotBat;   // 준비 페이즈가 없으므로 시작은 빠따
    public bool CanSwing => Time.time >= nextSwingTime;

    private float nextSwingTime;
    private Transform rightHand;
    private GameObject batProp, boostProp, slowProp;
    private int armedLayer = -1;   // 상체 무장 레이어(ArmedUpper) — 다리는 항상 기본 이동
    private int holdLayer = -1;
    private Coroutine batPropRoutine;
    private Coroutine armedLayerRoutine;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);

        // 내 것 판정: 미접속=내 것 (프로젝트 공통 규칙)
        if (!PhotonNetwork.IsConnected || photonView == null || photonView.IsMine)
            Local = this;

        rightHand = FindBone(transform, "RightHand");
        BuildProps();
    }

    private void Start()
    {
        if (animator != null)
        {
            armedLayer = animator.GetLayerIndex("ArmedUpper");
            holdLayer = animator.GetLayerIndex("HoldRight");
        }
        ApplyHeld(HeldSlot, HeldSlot, true);
    }

    // ---- 입력 진입점 (로컬 플레이어 전용, PlayerItemController가 호출) ----

    public void Select(int slot)
    {
        slot = Mathf.Clamp(slot, SlotBat, SlotRadio);
        if (slot == HeldSlot) return;

        if (PhotonNetwork.InRoom && photonView != null)
            photonView.RPC(nameof(RpcHeld), RpcTarget.All, slot);
        else
            RpcHeld(slot);
    }

    public void Swing()
    {
        if (HeldSlot != SlotBat || !CanSwing) return;
        nextSwingTime = Time.time + swingCooldown;

        int idx = Random.Range(0, attackStates.Length);
        if (PhotonNetwork.InRoom && photonView != null)
            photonView.RPC(nameof(RpcSwing), RpcTarget.All, idx);
        else
            RpcSwing(idx);
    }

    // ---- 전 클라 로컬 재생 ----

    [PunRPC]
    private void RpcHeld(int slot)
    {
        int prev = HeldSlot;
        HeldSlot = slot;
        ApplyHeld(slot, prev, false);
    }

    [PunRPC]
    private void RpcSwing(int idx)
    {
        if (animator == null || attackStates.Length == 0) return;
        // 상체 레이어에서만 휘두른다 — 다리는 기본 이동 그대로 (걸으면서 때려도 다리 안 망가짐)
        string state = attackStates[Mathf.Clamp(idx, 0, attackStates.Length - 1)];
        int layer = armedLayer >= 0 ? armedLayer : 0;

        // ⚠ 이미 그 공격을 재생/전환 중일 때 또 CrossFade하면 "끝나면 아이들로" 전환이 씹혀
        //    마지막 프레임에 얼어붙는다 — 재진입은 전환 없이 처음부터 즉시 재시작 (연타 콤보)
        if (animator.GetCurrentAnimatorStateInfo(layer).IsName(state) ||
            animator.GetNextAnimatorStateInfo(layer).IsName(state))
            animator.Play(state, layer, 0f);
        else
            animator.CrossFadeInFixedTime(state, 0.1f, layer);
    }

    private void ApplyHeld(int slot, int prev, bool instant)
    {
        bool bat = slot == SlotBat;

        if (animator != null)
        {
            if (holdLayer >= 0)
                animator.SetLayerWeight(holdLayer, (slot == SlotBoost || slot == SlotSlow) ? 1f : 0f);

            if (armedLayer >= 0)
            {
                if (armedLayerRoutine != null) { StopCoroutine(armedLayerRoutine); armedLayerRoutine = null; }

                if (bat)
                {
                    animator.SetLayerWeight(armedLayer, 1f);
                    animator.CrossFadeInFixedTime(instant ? "ArmedIdle" : "Draw", 0.1f, armedLayer);
                }
                else if (prev == SlotBat)
                {
                    if (instant || !gameObject.activeInHierarchy)
                        animator.SetLayerWeight(armedLayer, 0f);
                    else
                    {
                        animator.CrossFadeInFixedTime("Sheathe", 0.1f, armedLayer);
                        armedLayerRoutine = StartCoroutine(DropArmedLayer());
                    }
                }
            }
        }

        if (boostProp != null) boostProp.SetActive(slot == SlotBoost);
        if (slowProp != null) slowProp.SetActive(slot == SlotSlow);

        // 빠따는 꺼내기/집어넣기 애니에 맞춰 등장/퇴장
        if (batProp == null) return;
        if (batPropRoutine != null) StopCoroutine(batPropRoutine);
        if (instant || !gameObject.activeInHierarchy)
            batProp.SetActive(bat);
        else
            batPropRoutine = StartCoroutine(BatPropDelayed(bat));
    }

    /// <summary>집어넣기 연출이 끝나면 상체 무장 레이어를 부드럽게 끈다.</summary>
    private IEnumerator DropArmedLayer()
    {
        yield return new WaitForSeconds(sheatheTime);
        float t = 0f;
        while (t < 0.15f)
        {
            t += Time.deltaTime;
            animator.SetLayerWeight(armedLayer, Mathf.Lerp(1f, 0f, t / 0.15f));
            yield return null;
        }
        animator.SetLayerWeight(armedLayer, 0f);
    }

    private IEnumerator BatPropDelayed(bool show)
    {
        yield return new WaitForSeconds(show ? batShowDelay : batHideDelay);
        batProp.SetActive(show);
    }

    // ---- 소품 생성 (프리팹 무수정 — 전부 코드 생성) ----

    private void BuildProps()
    {
        if (rightHand == null)
        {
            Debug.LogWarning("[장비] RightHand 본을 못 찾음 — 소품 생략");
            return;
        }
        batProp = BuildBat();
        boostProp = BuildSyringe("Prop_SyringeBoost", new Color(1f, 0.42f, 0.12f));
        slowProp = BuildSyringe("Prop_SyringeSlow", new Color(0.25f, 0.55f, 1f));
        batProp.SetActive(false); boostProp.SetActive(false); slowProp.SetActive(false);
    }

    private GameObject BuildBat()
    {
        var root = NewProp("Prop_Bat", batLocalPos, batLocalEuler);

        var wood = MakeMat(new Color(0.55f, 0.36f, 0.18f));
        var grip = MakeMat(new Color(0.15f, 0.15f, 0.15f));

        // 손잡이(검정) + 몸통(나무색) — 손 위치가 원점, 몸통이 위로 뻗는다. 총 길이 ~0.87m
        AddCylinder(root, "Grip", grip, new Vector3(0f, 0.10f, 0f), new Vector3(0.045f, 0.14f, 0.045f));
        AddCylinder(root, "Body", wood, new Vector3(0f, 0.52f, 0f), new Vector3(0.075f, 0.28f, 0.075f));
        AddSphere(root, "Tip", wood, new Vector3(0f, 0.80f, 0f), 0.075f);

        return root;
    }

    private GameObject BuildSyringe(string name, Color liquid)
    {
        var root = NewProp(name, syringeLocalPos, syringeLocalEuler);

        var glass = MakeMat(new Color(0.9f, 0.95f, 1f));
        var metal = MakeMat(new Color(0.6f, 0.62f, 0.65f));
        var fluid = MakeMat(liquid);

        AddCylinder(root, "Body", glass, new Vector3(0f, 0.09f, 0f), new Vector3(0.055f, 0.055f, 0.055f));
        AddCylinder(root, "Fluid", fluid, new Vector3(0f, 0.09f, 0f), new Vector3(0.042f, 0.045f, 0.042f));
        AddCylinder(root, "Needle", metal, new Vector3(0f, 0.20f, 0f), new Vector3(0.008f, 0.045f, 0.008f));
        AddCylinder(root, "Plunger", fluid, new Vector3(0f, -0.015f, 0f), new Vector3(0.045f, 0.02f, 0.045f));

        return root;
    }

    private GameObject NewProp(string name, Vector3 pos, Vector3 euler)
    {
        var go = new GameObject(name);
        go.transform.SetParent(rightHand, false);
        // 손 +Y가 "쥔 것이 늘어지는 방향" — 기본 회전 0이면 빠따/주사기가 주먹 아래로 자연스럽게 늘어진다
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        return go;
    }

    // 인스펙터에서 소품 위치를 실시간 튜닝할 수 있게 — 값이 바뀐 프레임에만 적용
    private Vector3 lastBatPos, lastBatEuler, lastSyrPos, lastSyrEuler;

    private void LateUpdate()
    {
        if (batProp != null && (batLocalPos != lastBatPos || batLocalEuler != lastBatEuler))
        {
            lastBatPos = batLocalPos; lastBatEuler = batLocalEuler;
            batProp.transform.localPosition = batLocalPos;
            batProp.transform.localRotation = Quaternion.Euler(batLocalEuler);
        }
        if ((boostProp != null || slowProp != null) &&
            (syringeLocalPos != lastSyrPos || syringeLocalEuler != lastSyrEuler))
        {
            lastSyrPos = syringeLocalPos; lastSyrEuler = syringeLocalEuler;
            foreach (var p in new[] { boostProp, slowProp })
            {
                if (p == null) continue;
                p.transform.localPosition = syringeLocalPos;
                p.transform.localRotation = Quaternion.Euler(syringeLocalEuler);
            }
        }
    }

    private static void AddCylinder(GameObject parent, string name, Material mat, Vector3 pos, Vector3 scale)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(p.GetComponent<Collider>());   // 소품이 CC/동물과 부딪히면 안 됨
        p.name = name;
        p.transform.SetParent(parent.transform, false);
        p.transform.localPosition = pos;
        p.transform.localScale = scale;
        p.GetComponent<Renderer>().sharedMaterial = mat;
    }

    private static void AddSphere(GameObject parent, string name, Material mat, Vector3 pos, float r)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(p.GetComponent<Collider>());
        p.name = name;
        p.transform.SetParent(parent.transform, false);
        p.transform.localPosition = pos;
        p.transform.localScale = Vector3.one * (r * 2f);
        p.GetComponent<Renderer>().sharedMaterial = mat;
    }

    private static Material MakeMat(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.color = c;
        return m;
    }

    private static Transform FindBone(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
