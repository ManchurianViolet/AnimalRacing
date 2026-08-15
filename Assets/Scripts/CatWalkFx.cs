using UnityEngine;

/// <summary>
/// [고양이] 사뿐한 발놀림 중 드리프트 스파크 — 코너를 도는 동안만 발밑에서 불꽃이 튄다.
/// "남들 브레이크 밟을 때 혼자 풀스피드 코너링"을 그림으로 번역한 연출 (유저 A안 확정).
///
/// 발동 감지 = 전 클라로 중계되는 스킬 사건(OnSkillEvent — CatWalk + 내 RacerId). 통신 0.
/// 코너 감지 = 트랙 데이터가 아니라 **몸의 회전 속도(요잉)** — TransformView가 미러하는
/// 회전만 보므로 호스트/클라가 같은 코드로 돌고, 직선에선 자동으로 조용해진다.
/// RaceManager가 고양이(CatWalk 스킬)에만 부착.
/// </summary>
public class CatWalkFx : MonoBehaviour
{
    private Racer racer;
    private GameConfig config;
    private ParticleSystem ps;
    private ParticleSystem.EmissionModule emission;
    private float timer = -1f;      // -1 = 대기
    private float lastYaw;

    private static Material sparkMat;

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        this.config = config;
        BuildParticles();
        lastYaw = transform.eulerAngles.y;
    }

    private void OnEnable() => GameEvents.OnSkillEvent += HandleSkillEvent;
    private void OnDisable() => GameEvents.OnSkillEvent -= HandleSkillEvent;

    private void HandleSkillEvent(SkillFeedEvent evt, int rid)
    {
        if (racer == null || ps == null) return;
        if (evt != SkillFeedEvent.CatWalk || rid != racer.RacerId) return;
        timer = 0f;   // 지속시간은 SkillTuning이 단일 출처 — 밸런스가 바뀌면 연출도 자동 추종
    }

    private void LateUpdate()
    {
        if (timer < 0f || ps == null) return;

        // 완주·탈락 시 즉시 정리 (죽은 고양이가 불꽃을 뿜으면 안 된다)
        if (racer == null || racer.HasFinished) { Stop(); return; }

        timer += Time.deltaTime;
        if (timer >= SkillTuning.CatWalkDuration) { Stop(); return; }

        // 요잉 속도 실측 — 코너에서만 문턱을 넘는다 (±180 래핑 처리)
        float yaw = transform.eulerAngles.y;
        float turnSpeed = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) / Mathf.Max(0.0001f, Time.deltaTime);
        lastYaw = yaw;

        float threshold = config != null ? config.catSparkTurnThreshold : 20f;
        float maxRate = config != null ? config.catSparkRate : 90f;

        // 문턱 아래 = 0, 문턱의 3배 회전이면 최대 방출 — 급코너일수록 격렬하게
        float t = Mathf.Clamp01((turnSpeed - threshold) / (threshold * 2f));
        emission.rateOverTime = maxRate * t;
    }

    private void Stop()
    {
        timer = -1f;
        emission.rateOverTime = 0f;   // 이미 뜬 입자는 수명대로 사라진다
        lastYaw = transform.eulerAngles.y;
    }

    // ---- 파티클 생성 (프리팹 무수정 — BoostDustFx와 같은 코드 생성 철학) ----
    private void BuildParticles()
    {
        if (sparkMat == null)
        {
            Shader sh = Shader.Find("Sprites/Default");   // ⚠ URP Particles/Unlit 금지 법칙과 무관하게
            if (sh == null) return;                        //   파티클엔 안전하지만 통일성 위해 Sprites 사용
            sparkMat = new Material(sh) { name = "CatSpark (runtime)" };
        }

        var go = new GameObject("CatSparkFx");
        go.transform.SetParent(transform, false);

        // 배출 지점: 몸 뒤 발밑 (스킨드메시 실측 — 고양이 1.5배 스케일 자동 대응)
        var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
        float backZ = -0.35f;
        if (smr != null && smr.sharedMesh != null)
        {
            Bounds lb = smr.localBounds;
            Vector3 back = transform.InverseTransformPoint(
                smr.transform.TransformPoint(new Vector3(lb.center.x, lb.min.y, lb.min.z)));
            backZ = back.z;
        }
        go.transform.localPosition = new Vector3(0f, 0.06f, backZ);

        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // 조립 전 정지 (§11)

        float size = config != null ? config.catSparkSize : 0.09f;
        float speed = config != null ? config.catSparkSpeed : 3.2f;
        float bright = config != null ? config.catSparkBrightness : 1.6f;

        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.gravityModifier = 1.2f;                      // 스파크는 튀었다가 바닥으로 떨어진다
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // 달려나가도 불꽃은 그 자리에
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f * bright, 0.85f * bright, 0.25f * bright),   // 노랑
            new Color(1f * bright, 0.45f * bright, 0.10f * bright));  // 주황

        emission = ps.emission;
        emission.rateOverTime = 0f;   // 평소 0 — 코너 회전이 문턱을 넘을 때만 LateUpdate가 올린다

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = 0.12f;
        shape.rotation = new Vector3(-100f, 0f, 0f);      // 뒤·위쪽으로 벌어지게 (살짝 눕힌 원뿔)

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        // 스파크 느낌의 핵심 — 진행 방향으로 길게 늘어난 빌보드
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.08f;
        renderer.lengthScale = 2.2f;
        renderer.material = sparkMat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
    }
}
