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
    /// <summary>맨손 — 아무것도 안 든 상태. 무전기를 다 쓰면 이 상태로 되돌아간다.</summary>
    public const int SlotNone = 0;
    public const int SlotBat = 1, SlotBoost = 2, SlotSlow = 3, SlotRadioSkill = 4, SlotRadioExec = 5;

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

    [Tooltip("집어넣기 연출 시간 — 이만큼 지난 뒤 상체 무장 레이어를 서서히 끈다 (초)")]
    [SerializeField] private float sheatheTime = 0.9f;

    [Header("빠따 모델")]
    [Tooltip("ithappy Weapons_FREE의 baseball_bat_001. 비우면 예전 코드 생성 임시 빠따로 폴백")]
    [SerializeField] private GameObject batModel;
    [Tooltip("노브에서 배럴 끝까지 목표 전체 길이 (m). 0이면 정규화 없이 아래 배율만 쓴다 — " +
             "glTF 모델은 원본 크기가 제각각(이 배트는 101유닛)이라 배율보다 길이 지정이 안전하다")]
    [SerializeField] private float batModelLength = 0.9f;
    [Tooltip("길이 정규화 위에 곱하는 미세조정 배율")]
    [SerializeField] private float batModelScale = 1f;
    [Tooltip("모델의 길이축을 손 기준 +Y(위로 뻗음)로 돌리는 보정. " +
             "손잡이가 -X면 (0,0,90) / 원본이 이미 +Y로 서 있으면 (0,0,0)")]
    [SerializeField] private Vector3 batModelEuler = new Vector3(0f, 0f, 90f);
    [Tooltip("손잡이 끝(노브)이 주먹 아래로 나오는 길이 (m) — 실제로 배트를 쥔 모양")]
    [SerializeField] private float batKnobBelowHand = 0.05f;

    [Header("주사기 모델")]
    [Tooltip("low_poly_radioactive_needle 구운 프리팹. 비우면 예전 코드 생성 임시 주사기로 폴백")]
    [SerializeField] private GameObject syringeModel;
    [Tooltip("플런저 끝에서 바늘 끝까지 목표 전체 길이 (m) — 원본 0.185유닛을 이 크기로 정규화한다")]
    [SerializeField] private float syringeModelLength = 0.26f;
    [Tooltip("모델의 길이축을 손 기준 +Y(바늘이 위)로 돌리는 보정. " +
             "이 모델은 바늘이 +X라 (0,0,90) — 바늘이 아래로 나오면 (0,0,-90)으로")]
    [SerializeField] private Vector3 syringeModelEuler = new Vector3(0f, 0f, 90f);
    [Tooltip("플런저 끝이 주먹 아래로 나오는 길이 (m) — 실제로 몸통을 쥔 모양")]
    [SerializeField] private float syringeButtBelowHand = 0.04f;
    [Tooltip("본체에 먹이는 액체색 틴트 세기 (0=원본 색 그대로, 1=지정색 100%). " +
             "유리(액체창) 재질은 이 값과 무관하게 항상 액체색 100% — 부스트/감속 식별의 주 신호")]
    [Range(0f, 1f)]
    [SerializeField] private float syringeTintStrength = 0.45f;

    [Header("무전기 모델")]
    [Tooltip("handheld_transceiver__lowpoly 구운 프리팹. 비우면 예전 코드 생성 임시 무전기로 폴백")]
    [SerializeField] private GameObject radioModel;
    [Tooltip("안테나 끝까지 포함한 목표 전체 높이 (m) — 원본 3.52유닛을 이 크기로 정규화한다")]
    [SerializeField] private float radioModelHeight = 0.20f;
    [Tooltip("모델 정면(스피커 그릴)이 -Z를 보게 하는 요 보정 (도) — 원본이 49.5도 돌아가 있다")]
    [SerializeField] private float radioModelYaw = -49.5f;
    [Tooltip("모델 본체에 먹이는 색 틴트의 세기 (0=원본 색 그대로, 1=지정색 100%)")]
    [Range(0f, 1f)]
    [SerializeField] private float radioTintStrength = 0.65f;

    [Header("무전기 디스플레이 (주황 패널)")]
    [Tooltip("LCD 글씨용 TMP 폰트 (LABDigital SDF). 비우면 화면을 안 만든다")]
    [SerializeField] private TMPro.TMP_FontAsset radioScreenFont;
    [Tooltip("주황 패널 중심 — 모델 로컬 좌표 (텍스처 UV로 실측한 값)")]
    [SerializeField] private Vector3 radioScreenCenter = new Vector3(-0.247f, 1.035f, -0.212f);
    [Tooltip("주황 패널 크기 — 모델 로컬 단위 (가로 × 세로)")]
    [SerializeField] private Vector2 radioScreenSize = new Vector2(0.820f, 0.358f);
    [Tooltip("패널 표면에서 글씨를 띄우는 거리 — z-fighting 방지 (모델 로컬 단위)")]
    [SerializeField] private float radioScreenLift = 0.02f;
    [Tooltip("LCD 글씨 색. TMP는 조명을 안 받으므로(Unlit) 밝게 주면 백라이트 켜진 것처럼 보인다 — " +
             "실내가 어두워서(ambient 0.4) 검정 글씨는 배경과 같이 묻힌다")]
    [SerializeField] private Color radioScreenTextColor = new Color(1f, 0.79f, 0.32f);

    [Header("소품 위치 (오른손 본 기준 로컬) — 플레이 중 바꾸면 즉시 반영")]
    [SerializeField] private Vector3 batLocalPos = new Vector3(0.04f, 0.03f, 0.01f);
    [SerializeField] private Vector3 batLocalEuler = Vector3.zero;
    [SerializeField] private Vector3 syringeLocalPos = new Vector3(0.04f, 0.03f, 0.01f);
    [SerializeField] private Vector3 syringeLocalEuler = Vector3.zero;
    [SerializeField] private Vector3 radioLocalPos = new Vector3(0.01000666f, 0.0437801f, -0.009236704f);
    [SerializeField] private Vector3 radioLocalEuler = new Vector3(-14.558f, -94.088f, 4.353f);

    /// <summary>
    /// 이 아바타가 내 것인가. static Local과 달리 인스턴스에 물어보므로 앞선 아바타가 파괴돼도 흔들리지 않는다.
    /// ⚠ Awake에 1회 캐시하면 안 된다 — 씬 배치 아바타는 Photon 접속이 확정되기 전에 Awake가 돌아
    ///    "내 것이 아님"으로 굳어버린다 (실사고). 매번 계산해도 비용은 없다.
    /// </summary>
    public bool IsLocalAvatar => !PhotonNetwork.IsConnected || photonView == null || photonView.IsMine;

    public int HeldSlot { get; private set; } = SlotBat;   // 준비 페이즈가 없으므로 시작은 빠따
    public bool CanSwing => Time.time >= nextSwingTime;

    /// <summary>
    /// 빠따 내구도 (남은 명중 횟수). 내 아바타에서만 의미 있다 — 판정처럼 로컬에서 깎이고,
    /// 0이 되면 PlayerItemController의 재고 규칙(HasStockFor)이 자동 수납한다.
    /// 라운드 시작(Betting)마다 GameConfig.batDurabilityMax로 회복.
    /// </summary>
    public int BatDurability { get; private set; } = int.MaxValue;

    /// <summary>남은 내구도 비율 (HUD 게이지용, 0~1).</summary>
    public float BatDurabilityRatio
    {
        get
        {
            int max = BatDurabilityMax;
            return max <= 0 ? 1f : Mathf.Clamp01((float)BatDurability / max);
        }
    }

    private static int BatDurabilityMax
    {
        get
        {
            var cfg = GameManager.Instance != null ? GameManager.Instance.Config : null;
            return cfg != null ? cfg.batDurabilityMax : 10;
        }
    }

    /// <summary>오른손 본 — 소품이 붙는 자리. 베팅 방 피규어도 같은 손에 쥔다.</summary>
    public Transform RightHandBone => rightHand;

    private float nextSwingTime;
    private Transform rightHand;
    private GameObject batProp, boostProp, slowProp, radioSkillProp, radioExecProp;
    private int armedLayer = -1;   // 상체 무장 레이어(ArmedUpper) — 다리는 항상 기본 이동
    private int holdLayer = -1;
    private Coroutine batPropRoutine;
    private Coroutine armedLayerRoutine;
    private PlayerKnockdown knockdown;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);

        // 내 것 판정: 미접속=내 것 (프로젝트 공통 규칙)
        if (IsLocalAvatar) Local = this;

        rightHand = FindBone(transform, "RightHand");
        knockdown = GetComponent<PlayerKnockdown>();
        BuildProps();
    }

    private void Start()
    {
        if (animator != null)
        {
            armedLayer = animator.GetLayerIndex("ArmedUpper");
            holdLayer = animator.GetLayerIndex("HoldRight");
        }
        BatDurability = BatDurabilityMax;
        ApplyHeld(HeldSlot, HeldSlot, true);
    }

    private void OnEnable() => GameEvents.OnPhaseChanged += HandlePhaseChanged;
    private void OnDisable() => GameEvents.OnPhaseChanged -= HandlePhaseChanged;

    // 라운드가 새로 열리면(베팅) 빠따 수리 — 아이템 균등 지급과 같은 리듬. 로비 복귀도 회복.
    private void HandlePhaseChanged(GamePhase p)
    {
        if (p == GamePhase.Betting || p == GamePhase.Lobby)
            BatDurability = BatDurabilityMax;
    }

    /// <summary>
    /// 빠따 내구도 소모 (기본 1). 명중한 스윙에서만 호출 — 헛스윙은 무료.
    /// public인 이유: 테스트/연출(부서짐 이펙트 등)에서 재사용할 수 있게.
    /// </summary>
    public void ApplyBatWear(int amount = 1)
    {
        if (BatDurability <= 0) return;
        BatDurability = Mathf.Max(0, BatDurability - amount);

        // 부서지는 순간 한 번만. 내구도는 로컬 상태라 내 화면에서만 들린다 (2D)
        if (BatDurability == 0 && IsLocalAvatar) SoundManager.PlaySfx(SfxId.BatBreak);
    }

    // ---- 입력 진입점 (로컬 플레이어 전용, PlayerItemController가 호출) ----

    public void Select(int slot)
    {
        if (knockdown != null && knockdown.IsDown) return;   // 누워서 슬롯 전환 금지
        slot = Mathf.Clamp(slot, SlotNone, SlotRadioExec);
        if (slot == HeldSlot) return;

        if (PhotonNetwork.InRoom && photonView != null)
            photonView.RPC(nameof(RpcHeld), RpcTarget.All, slot);
        else
            RpcHeld(slot);
    }

    public void Swing()
    {
        if (HeldSlot != SlotBat || !CanSwing) return;
        if (BatDurability <= 0) return;                      // 부서진 빠따 — 자동 수납 직전 프레임 가드
        if (knockdown != null && knockdown.IsDown) return;   // 누워서 휘두르기 금지
        nextSwingTime = Time.time + swingCooldown;

        int idx = Random.Range(0, attackStates.Length);
        if (PhotonNetwork.InRoom && photonView != null)
            photonView.RPC(nameof(RpcSwing), RpcTarget.All, idx);
        else
            RpcSwing(idx);

        // 타격 판정은 로컬(때린 사람)이 임팩트 타이밍에 수행 — 명중자에게 쓰러짐 RPC
        StartCoroutine(MeleeImpact());
    }

    /// <summary>임팩트 순간 전방 부채꼴 안의 다른 플레이어를 쓰러뜨린다.</summary>
    private IEnumerator MeleeImpact()
    {
        var cfg = GameManager.Instance != null ? GameManager.Instance.Config : null;
        yield return new WaitForSeconds(cfg != null ? cfg.meleeImpactDelay : 0.45f);
        if (knockdown != null && knockdown.IsDown) yield break;   // 휘두르다 내가 먼저 맞음

        var victims = FindVictimsInArc();
        foreach (var v in victims)
            v.RequestKnockdown();

        // 명중한 스윙만 내구도 1 소모 (여럿을 한 번에 맞혀도 1) — 다 닳으면 재고 규칙이 자동 수납
        if (victims.Count > 0) ApplyBatWear();
    }

    /// <summary>전방 부채꼴(GameConfig: meleeRange/meleeArcAngle) 안의 타격 가능 대상 목록.</summary>
    public System.Collections.Generic.List<PlayerKnockdown> FindVictimsInArc()
    {
        var cfg = GameManager.Instance != null ? GameManager.Instance.Config : null;
        float range = cfg != null ? cfg.meleeRange : 2.2f;
        float halfArc = (cfg != null ? cfg.meleeArcAngle : 150f) * 0.5f;

        var list = new System.Collections.Generic.List<PlayerKnockdown>();
        foreach (var k in FindObjectsByType<PlayerKnockdown>(FindObjectsSortMode.None))
        {
            if (k.gameObject == gameObject || !k.CanBeHit) continue;

            Vector3 to = k.transform.position - transform.position;
            float dy = Mathf.Abs(to.y);
            to.y = 0f;
            if (dy > 2f || to.magnitude > range) continue;

            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (Vector3.Angle(fwd, to) > halfArc) continue;

            list.Add(k);
        }
        return list;
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
        // 스윙 소리 — 이 RPC는 전 클라에서 재생되므로 남이 휘두르는 소리도 그 자리에서 들린다 (3D)
        SoundManager.PlaySfx(SfxId.BatSwing, transform.position);

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
            // 주사기/무전기 = 오른손 1H 들기 포즈 공용
            if (holdLayer >= 0)
                animator.SetLayerWeight(holdLayer, (slot == SlotBoost || slot == SlotSlow ||
                                                    slot == SlotRadioSkill || slot == SlotRadioExec) ? 1f : 0f);

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
        if (radioSkillProp != null) radioSkillProp.SetActive(slot == SlotRadioSkill);
        if (radioExecProp != null) radioExecProp.SetActive(slot == SlotRadioExec);

        // 빠따 등장은 꺼내기 애니에 맞춰 늦추되, 퇴장은 즉시 —
        // 집어넣기 애니를 기다리면 그 사이 새로 든 소품과 손에서 겹쳐 보인다
        if (batProp == null) return;
        if (batPropRoutine != null) { StopCoroutine(batPropRoutine); batPropRoutine = null; }
        if (!bat || instant || !gameObject.activeInHierarchy)
            batProp.SetActive(bat);
        else
            batPropRoutine = StartCoroutine(ShowBatDelayed());
    }

    /// <summary>쓰러짐: 소품·무장/들기 레이어 전부 끔 (순수하게 누운 몸만 남게).</summary>
    public void SuppressForKnockdown()
    {
        if (batPropRoutine != null) { StopCoroutine(batPropRoutine); batPropRoutine = null; }
        if (armedLayerRoutine != null) { StopCoroutine(armedLayerRoutine); armedLayerRoutine = null; }

        if (batProp != null) batProp.SetActive(false);
        if (boostProp != null) boostProp.SetActive(false);
        if (slowProp != null) slowProp.SetActive(false);
        if (radioSkillProp != null) radioSkillProp.SetActive(false);
        if (radioExecProp != null) radioExecProp.SetActive(false);

        if (animator != null)
        {
            if (armedLayer >= 0) animator.SetLayerWeight(armedLayer, 0f);
            if (holdLayer >= 0) animator.SetLayerWeight(holdLayer, 0f);
        }
    }

    /// <summary>기상 완료: 들고 있던 슬롯 상태를 그대로 복원.</summary>
    public void RestoreAfterKnockdown()
    {
        ApplyHeld(HeldSlot, HeldSlot, true);
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

    private IEnumerator ShowBatDelayed()
    {
        yield return new WaitForSeconds(batShowDelay);
        batProp.SetActive(true);
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
        radioSkillProp = BuildRadio("Prop_RadioSkill",
            new Color(0.22f, 0.42f, 0.24f), new Color(0.98f, 0.83f, 0.10f));   // 밀리터리 그린 + 노랑 램프
        radioExecProp = BuildRadio("Prop_RadioExec",
            new Color(0.13f, 0.13f, 0.14f), new Color(0.86f, 0.16f, 0.14f));   // 검정 + 빨강 램프
        batProp.SetActive(false); boostProp.SetActive(false); slowProp.SetActive(false);
        radioSkillProp.SetActive(false); radioExecProp.SetActive(false);
    }

    private GameObject BuildBat()
    {
        var root = NewProp("Prop_Bat", batLocalPos, batLocalEuler);

        // 에셋 모델이 있으면 그것을 쓴다. batModelEuler로 길이축을 +Y(손 위로 뻗음)에 맞추면
        // 코드 생성판과 축 규약이 같아져서 인스펙터에 잡아둔 batLocalEuler(쥔 각도)가 그대로 유효하다.
        if (batModel != null)
        {
            var model = Instantiate(batModel, root.transform);
            model.name = "BatModel";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(batModelEuler);
            model.transform.localScale = Vector3.one;

            // 소품 콜라이더는 CC/동물과 부딪히면 안 된다 (에셋 프리팹에 MeshCollider가 붙어 있음)
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);

            // ⚠ 축 보정 "뒤"에 재야 한다 — 회전 전 크기로 계산하면 축이 뒤바뀐 값이 나온다
            var raw = LocalMeshBounds(root.transform);
            float scale = batModelScale;
            if (batModelLength > 0.0001f && raw.size.y > 0.0001f)
                scale *= batModelLength / raw.size.y;
            model.transform.localScale = Vector3.one * scale;

            // 노브가 주먹 아래 batKnobBelowHand에 오도록 세우고, 좌우 중심은 손에 맞춘다
            var fitted = LocalMeshBounds(root.transform);
            model.transform.localPosition = new Vector3(
                -fitted.center.x, -batKnobBelowHand - fitted.min.y, -fitted.center.z);
            return root;
        }

        var wood = MakeMat(new Color(0.55f, 0.36f, 0.18f));
        var grip = MakeMat(new Color(0.15f, 0.15f, 0.15f));

        // 폴백: 손잡이(검정) + 몸통(나무색) — 손 위치가 원점, 몸통이 위로 뻗는다. 총 길이 ~0.87m
        AddCylinder(root, "Grip", grip, new Vector3(0f, 0.10f, 0f), new Vector3(0.045f, 0.14f, 0.045f));
        AddCylinder(root, "Body", wood, new Vector3(0f, 0.52f, 0f), new Vector3(0.075f, 0.28f, 0.075f));
        AddSphere(root, "Tip", wood, new Vector3(0f, 0.80f, 0f), 0.075f);

        return root;
    }

    private GameObject BuildSyringe(string name, Color liquid)
    {
        var root = NewProp(name, syringeLocalPos, syringeLocalEuler);

        // 실제 모델이 있으면 그것을 쓴다. syringeModelEuler로 길이축을 +Y(바늘 위)에 맞추면
        // 코드 생성판과 축 규약이 같아져 인스펙터에 잡아둔 syringeLocalEuler(쥔 각도)가 그대로 유효하다.
        if (syringeModel != null)
        {
            var model = Instantiate(syringeModel, root.transform);
            model.name = "SyringeModel";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(syringeModelEuler);
            model.transform.localScale = Vector3.one;

            // 소품 콜라이더는 CC/동물과 부딪히면 안 된다
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);

            // ⚠ 축 보정 "뒤"에 재야 한다 — 회전 전 크기로 계산하면 축이 뒤바뀐 값이 나온다 (빠따와 동일)
            var raw = LocalMeshBounds(root.transform);
            float scale = 1f;
            if (syringeModelLength > 0.0001f && raw.size.y > 0.0001f)
                scale = syringeModelLength / raw.size.y;
            model.transform.localScale = Vector3.one * scale;

            // 플런저 끝이 주먹 아래 syringeButtBelowHand에 오도록 세우고, 좌우 중심은 손에 맞춘다
            var fitted = LocalMeshBounds(root.transform);
            model.transform.localPosition = new Vector3(
                -fitted.center.x, -syringeButtBelowHand - fitted.min.y, -fitted.center.z);

            TintSyringe(model, liquid);
            return root;
        }

        var glass = MakeMat(new Color(0.9f, 0.95f, 1f));
        var metal = MakeMat(new Color(0.6f, 0.62f, 0.65f));
        var fluid = MakeMat(liquid);

        AddCylinder(root, "Body", glass, new Vector3(0f, 0.09f, 0f), new Vector3(0.055f, 0.055f, 0.055f));
        AddCylinder(root, "Fluid", fluid, new Vector3(0f, 0.09f, 0f), new Vector3(0.042f, 0.045f, 0.042f));
        AddCylinder(root, "Needle", metal, new Vector3(0f, 0.20f, 0f), new Vector3(0.008f, 0.045f, 0.008f));
        AddCylinder(root, "Plunger", fluid, new Vector3(0f, -0.015f, 0f), new Vector3(0.045f, 0.02f, 0.045f));

        return root;
    }

    private GameObject BuildRadio(string name, Color body, Color lamp)
    {
        var root = NewProp(name, radioLocalPos, radioLocalEuler);

        // 실제 모델이 있으면 그것을 쓴다. 원본은 +Y가 안테나 방향이라 코드 생성판과 축 규약이 같고,
        // 요(yaw)만 49.5도 돌아가 있어 그것만 보정하면 스피커 그릴이 -Z(=코드 생성판 그릴 면)를 본다.
        if (radioModel != null)
        {
            var model = Instantiate(radioModel, root.transform);
            model.name = "RadioModel";
            model.transform.localRotation = Quaternion.Euler(0f, radioModelYaw, 0f);

            // 소품 콜라이더는 CC/동물과 부딪히면 안 된다
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);

            // 원본이 3.52유닛짜리라 목표 높이로 정규화 — 요 회전은 y를 안 건드리니 회전 전 실측으로 충분하다
            var raw = LocalMeshBounds(model.transform);
            float scale = raw.size.y > 0.0001f ? radioModelHeight / raw.size.y : 1f;
            model.transform.localScale = Vector3.one * scale;

            // 손 원점이 무전기 바닥이 되게 (코드 생성판처럼 +Y로 선다)
            model.transform.localPosition = new Vector3(0f, -raw.min.y * scale, 0f);

            TintRadio(model, body);
            var lampRenderer = AddRadioMarks(root, lamp);
            AddRadioScreen(model, name.Contains("Exec") ? ItemKind.Execute : ItemKind.SkillTrigger,
                           lampRenderer, lamp);
            return root;
        }

        var bodyMat = MakeMat(body);
        var darkMat = MakeMat(new Color(0.08f, 0.08f, 0.08f));
        var lampMat = MakeMat(lamp);

        // 폴백: 본체(세로 박스) + 스피커 그릴(어두운 판) + 안테나 + 상태 램프 — 손 위(+Y)로 서는 워키토키
        AddCube(root, "Body", bodyMat, new Vector3(0f, 0.09f, 0f), new Vector3(0.065f, 0.15f, 0.035f));
        AddCube(root, "Grille", darkMat, new Vector3(0f, 0.11f, -0.019f), new Vector3(0.045f, 0.06f, 0.004f));
        AddCylinder(root, "Antenna", darkMat, new Vector3(-0.02f, 0.215f, 0f), new Vector3(0.008f, 0.05f, 0.008f));
        AddSphere(root, "Lamp", lampMat, new Vector3(0.02f, 0.17f, -0.017f), 0.008f);

        return root;
    }

    /// <summary>
    /// 주사기 모델에 액체색을 먹인다. 유리(액체창) 서브메시는 100% 액체색 — 멀리서도 부스트/감속이 갈리는 주 신호.
    /// 본체는 syringeTintStrength만큼만 물들여 원본 텍스처 디테일을 살린다.
    /// ⚠ 'phong1SG' = 몸통 가운데 액체가 보이는 유리창 재질 (정점 실측) — 모델이 바뀌면 이름을 다시 확인할 것.
    /// </summary>
    private void TintSyringe(GameObject model, Color liquid)
    {
        float peak = Mathf.Max(liquid.r, Mathf.Max(liquid.g, liquid.b));
        Color vivid = peak > 0.01f ? new Color(liquid.r / peak, liquid.g / peak, liquid.b / peak) : Color.white;
        Color bodyTint = Color.Lerp(Color.white, vivid, syringeTintStrength);

        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.materials;                    // 인스턴스화 — 원본 에셋을 건드리지 않는다
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                // 인스턴스화되면 이름 뒤에 " (Instance)"가 붙으므로 StartsWith로 판별
                bool isGlass = mats[i].name.StartsWith("phong1SG");
                Color c = isGlass ? vivid : bodyTint;
                // ⚠ 유리창은 텍스처를 뗀다 — 원본 액체 텍스처(초록빛)가 남으면 곱셈 틴트라
                //    주황 × 초록 = 갈색이 돼 부스트가 "주황"으로 안 읽힌다 (캡처 실측)
                if (isGlass && mats[i].HasProperty("_BaseMap")) mats[i].SetTexture("_BaseMap", null);
                if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", c);
                else if (mats[i].HasProperty("_Color")) mats[i].SetColor("_Color", c);
            }
            r.materials = mats;
        }
    }

    /// <summary>모델 본체에 색을 먹인다. 원본 텍스처가 어두워서 곱셈 틴트가 죽지 않게 밝기를 정규화한다.</summary>
    private void TintRadio(GameObject model, Color body)
    {
        float peak = Mathf.Max(body.r, Mathf.Max(body.g, body.b));
        Color vivid = peak > 0.01f ? new Color(body.r / peak, body.g / peak, body.b / peak) : Color.white;
        Color tint = Color.Lerp(Color.white, vivid, radioTintStrength);

        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.materials;                    // 인스턴스화 — 원본 에셋을 건드리지 않는다
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", tint);
                else if (mats[i].HasProperty("_Color")) mats[i].SetColor("_Color", tint);
            }
            r.materials = mats;
        }
    }

    /// <summary>
    /// 주황 패널 위에 LCD 글씨(TMP)를 얹는다. 모델의 자식이라 회전·스케일을 그대로 따라간다.
    /// 패널 좌표는 텍스처의 주황 픽셀 UV로 역산한 실측값이라 모델이 바뀌면 다시 재야 한다.
    /// </summary>
    private void AddRadioScreen(GameObject model, ItemKind kind, Renderer lampRenderer, Color lampColor)
    {
        if (radioScreenFont == null) return;

        var go = new GameObject("Screen");
        go.transform.SetParent(model.transform, false);

        // 앞면 법선 — 원본 모델이 요 방향으로 돌아가 있으므로 radioModelYaw에서 역산한다
        Vector3 normal = Quaternion.Euler(0f, -radioModelYaw, 0f) * Vector3.back;
        go.transform.localPosition = radioScreenCenter + normal * radioScreenLift;
        // ⚠ TMP는 로컬 +Z가 "보는 쪽"이 아니라 "글자 뒤통수" 방향이다.
        //    forward를 패널 바깥(normal)으로 두면 좌우반전된 글자를 보게 된다 — 반드시 안쪽(-normal).
        //    셰이더가 Cull Off라 뒤를 향해도 정상적으로 그려진다.
        go.transform.localRotation = Quaternion.LookRotation(-normal, Vector3.up);

        var tmp = go.AddComponent<TMPro.TextMeshPro>();
        tmp.font = radioScreenFont;
        tmp.color = radioScreenTextColor;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.enableAutoSizing = true;
        // ⚠ 상한을 패널 크기로 주면 안 된다 — TMP fontSize는 rect 단위와 1:1이 아니라서
        //    이 폰트는 3.5쯤 돼야 패널을 채운다 (0.358로 묶으니 글자가 1/10 크기로 나왔음).
        //    넉넉히 열어두고 자동 크기가 rect에 맞춰 줄이게 둔다.
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 20f;
        tmp.text = "0000";

        var rt = tmp.rectTransform;
        rt.sizeDelta = radioScreenSize;
        rt.pivot = new Vector2(0.5f, 0.5f);

        go.AddComponent<RadioScreen>().Init(kind, this, lampRenderer, lampColor);
    }

    /// <summary>무전기 2종을 확실히 구분하는 표식 — 안테나 밑동의 컬러 밴드 + 상태 램프.</summary>
    private Renderer AddRadioMarks(GameObject root, Color lamp)
    {
        // ⚠ 밴드와 램프가 머티리얼을 공유하면 램프를 깜빡일 때 밴드까지 같이 깜빡인다 — 따로 만든다
        var bandMat = MakeMat(lamp);
        var lampMat = MakeMat(lamp);
        var b = LocalMeshBounds(root.transform);       // 이미 정규화된 크기 기준

        float bodyTop = b.min.y + b.size.y * 0.60f;    // 본체와 안테나가 갈리는 높이 (실측 2.11/3.52)
        float halfW = b.size.x * 0.5f;
        float halfD = b.size.z * 0.5f;

        // 본체 상단을 두르는 컬러 띠 — 사방에서 보이는 주 식별 신호.
        // ⚠ 원기둥으로 만들면 사각 본체에 내접해 파묻힌다 (실사고) — 반드시 큐브로.
        AddCube(root, "Band", bandMat,
            new Vector3(b.center.x, bodyTop - b.size.y * 0.035f, b.center.z),
            new Vector3(b.size.x * 1.06f, b.size.y * 0.026f, b.size.z * 1.06f));

        // 앞면(-Z) 상단 모서리에 걸치는 작은 상태 램프 — 보조 신호 + 발동 완료 시 깜빡임
        AddSphere(root, "Lamp", lampMat,
            new Vector3(b.center.x - halfW * 0.35f, bodyTop + b.size.y * 0.005f, b.min.z + halfD * 0.35f),
            b.size.y * 0.017f);

        var lampT = root.transform.Find("Lamp");
        return lampT != null ? lampT.GetComponent<Renderer>() : null;
    }

    /// <summary>
    /// 루트 로컬 기준 메시 바운즈.
    /// ⚠ 정점을 직접 훑는다 — mesh.bounds의 코너 8개를 변환하면 회전이 걸린 자식에서 AABB가
    /// 한 번 더 부풀어 실제보다 √2배 커진다 (밴드가 쟁반만 해지던 실사고).
    /// </summary>
    private static Bounds LocalMeshBounds(Transform root)
    {
        bool any = false;
        var min = Vector3.one * float.MaxValue;
        var max = Vector3.one * float.MinValue;

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            foreach (var v in mf.sharedMesh.vertices)
            {
                var p = root.InverseTransformPoint(mf.transform.TransformPoint(v));
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                any = true;
            }
        }

        if (!any) return new Bounds(Vector3.zero, Vector3.zero);
        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
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
    private Vector3 lastBatPos, lastBatEuler, lastSyrPos, lastSyrEuler, lastRadioPos, lastRadioEuler;

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
        if ((radioSkillProp != null || radioExecProp != null) &&
            (radioLocalPos != lastRadioPos || radioLocalEuler != lastRadioEuler))
        {
            lastRadioPos = radioLocalPos; lastRadioEuler = radioLocalEuler;
            foreach (var p in new[] { radioSkillProp, radioExecProp })
            {
                if (p == null) continue;
                p.transform.localPosition = radioLocalPos;
                p.transform.localRotation = Quaternion.Euler(radioLocalEuler);
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

    private static void AddCube(GameObject parent, string name, Material mat, Vector3 pos, Vector3 scale)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
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
