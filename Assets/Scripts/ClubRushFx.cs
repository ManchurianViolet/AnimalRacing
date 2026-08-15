using UnityEngine;

/// <summary>
/// [인간] 몽둥이 질주 연출 — 발동하면 오른손에 몽둥이가 나타나고, 끝나면 사라진다.
/// 발동 감지 = 전 클라로 중계되는 스킬 사건(OnSkillEvent — ClubRush + 내 RacerId). 통신 0.
/// 몽둥이는 플레이어 빠따와 같은 코드 생성(원기둥 — PlayerEquipment 폴백 스타일).
/// 컨트롤러에 ArmedUpper 레이어가 있으면 지속 중 켜서 몽둥이 든 자세를 얹는다 (없으면 그냥 손에만).
/// RaceManager가 인간(ClubRush 스킬)에만 부착.
/// </summary>
public class ClubRushFx : MonoBehaviour
{
    private Racer racer;
    private Animator animator;
    private Transform rightHand;
    private GameObject club;
    private int armedLayer = -1;
    private float timer = -1f;

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        animator = GetComponentInChildren<Animator>(true);
        rightHand = FindBone(transform, "RightHand");
        if (animator != null) armedLayer = animator.GetLayerIndex("ArmedUpper");
        BuildClub();
    }

    private void OnEnable() => GameEvents.OnSkillEvent += HandleSkillEvent;
    private void OnDisable() => GameEvents.OnSkillEvent -= HandleSkillEvent;

    private void HandleSkillEvent(SkillFeedEvent evt, int rid)
    {
        if (racer == null || club == null || rid != racer.RacerId) return;

        if (evt == SkillFeedEvent.ClubRush)
        {
            timer = 0f;
            club.SetActive(true);
            if (armedLayer >= 0)
            {
                animator.SetLayerWeight(armedLayer, 1f);
                // 레이어 기본 상태가 Empty라 weight만 올리면 아무 자세도 안 나온다 — 2H 파지로 진입
                animator.CrossFadeInFixedTime("ArmedIdle", 0.15f, armedLayer);
            }
            // 질주 내내 부스트 먼지구름 — 주사기 부스트와 같은 연출 재사용 (유저 결정)
            var dust = GetComponent<BoostDustFx>();
            if (dust != null) dust.Play(SkillTuning.ClubRushDuration);
            return;
        }

        // 명중 순간 — 달리면서 휘두르기 (상체 레이어라 다리는 그대로 질주)
        if (evt == SkillFeedEvent.ClubHit && timer >= 0f && armedLayer >= 0)
        {
            // ⚠ 같은 상태 재-CrossFade는 얼어붙는다 (§11) — 재진입은 Play로 처음부터 (연타 콤보)
            if (animator.GetCurrentAnimatorStateInfo(armedLayer).IsName("Attack2H") ||
                animator.GetNextAnimatorStateInfo(armedLayer).IsName("Attack2H"))
                animator.Play("Attack2H", armedLayer, 0f);
            else
                animator.CrossFadeInFixedTime("Attack2H", 0.05f, armedLayer);
        }
    }

    private void Update()
    {
        if (timer < 0f) return;

        // 완주·탈락 시 즉시 정리
        if (racer == null || racer.HasFinished) { Stop(); return; }

        timer += Time.deltaTime;
        if (timer >= SkillTuning.ClubRushDuration) Stop();
    }

    private void Stop()
    {
        timer = -1f;
        if (club != null) club.SetActive(false);
        if (armedLayer >= 0 && animator != null) animator.SetLayerWeight(armedLayer, 0f);
    }

    // ---- 몽둥이 코드 생성 (플레이어 빠따 폴백과 같은 구성 — 손 +Y로 뻗는 규약) ----
    private void BuildClub()
    {
        if (rightHand == null)
        {
            Debug.LogWarning("[몽둥이질주] RightHand 본 없음 — 소품 생략");
            return;
        }

        club = new GameObject("Prop_RushClub");
        club.transform.SetParent(rightHand, false);

        var wood = MakeMat(new Color(0.55f, 0.36f, 0.18f));
        var grip = MakeMat(new Color(0.15f, 0.15f, 0.15f));
        AddCylinder(club, "Grip", grip, new Vector3(0f, 0.10f, 0f), new Vector3(0.045f, 0.14f, 0.045f));
        AddCylinder(club, "Body", wood, new Vector3(0f, 0.52f, 0f), new Vector3(0.075f, 0.28f, 0.075f));
        AddSphere(club, "Tip", wood, new Vector3(0f, 0.80f, 0f), 0.075f);
        club.SetActive(false);
    }

    private static void AddCylinder(GameObject parent, string name, Material mat, Vector3 pos, Vector3 scale)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(p.GetComponent<Collider>());   // 소품이 동물/CC와 부딪히면 안 됨
        p.name = name;
        p.transform.SetParent(parent.transform, false);
        p.transform.localPosition = pos;
        p.transform.localScale = scale;
        p.GetComponent<Renderer>().sharedMaterial = mat;
    }

    private static void AddSphere(GameObject parent, string name, Material mat, Vector3 pos, float r)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(p.GetComponent<Collider>());
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
