using System.Linq;
using UnityEngine;

/// <summary>
/// [연출] 부스트 먼지구름 — 카툰. 동물 뒷다리 뒤에서 동글동글한 먼지가 퐁퐁 터져 뒤에 남는다.
/// RaceManager가 스폰(호스트)/등록(클라) 시 붙인다. 아이템 사용은 게이트웨이가 이미 전 클라로
/// 중계하므로(OnItemUsed) 각자 로컬에서 재생 — 네트워크 추가 통신 0.
/// 스킬 부스트 등 다른 소스도 Play(초)로 그대로 재사용 가능.
/// </summary>
public class BoostDustFx : MonoBehaviour
{
    private Racer racer;
    private GameConfig cfg;
    private ParticleSystem ps;

    // 배출 지점: 뒷다리 뒤 (루트 로컬 — 동물이 기울면 같이 기운다)
    private Vector3 anchorLocal = new Vector3(0f, 0.08f, -0.5f);
    private float bodyWidth = 0.6f;
    private float puffSize = 0.5f;

    private float playUntil;
    private float emitAccum;
    private Vector3 lastPos;
    private float speed;

    public bool IsPlaying => Time.time < playUntil;

    public void Init(Racer r, GameConfig config)
    {
        racer = r;
        cfg = config;
        MeasureBody();
        BuildSystem();
        lastPos = transform.position;
    }

    private void OnEnable()  => GameEvents.OnItemUsed += HandleItemUsed;
    private void OnDisable() => GameEvents.OnItemUsed -= HandleItemUsed;

    private void HandleItemUsed(int pid, ItemDefinition item, int racerId)
    {
        if (racer == null || item == null || ps == null) return;
        if (item.kind != ItemKind.Boost || racerId != racer.RacerId) return;

        // [펭귄] 무관심: 부스트가 실제로 먹히지 않는다 — 먼지도 안 난다 (연출이 거짓말하면 안 됨)
        if (racer.Definition != null && racer.Definition.skill == AnimalSkill.Apathy) return;

        Play(item.duration);
    }

    /// <summary>지정 시간만큼 먼지 배출 (시작 순간 큰 먼지 한 방 포함). 중첩되면 더 긴 쪽으로 연장.</summary>
    public void Play(float seconds)
    {
        if (ps == null) return;
        bool wasIdle = !IsPlaying;
        playUntil = Mathf.Max(playUntil, Time.time + seconds);
        if (wasIdle)
            for (int i = 0; i < Mathf.Max(0, cfg.dustBurst); i++) EmitPuff(1.45f);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // 속도는 위치 변화로 측정 — 클라의 미러 동물(Photon 받아쓰기)에도 똑같이 통한다
        Vector3 p = transform.position;
        speed = Mathf.Lerp(speed, (p - lastPos).magnitude / dt, 12f * dt);
        lastPos = p;

        if (ps == null || !IsPlaying) return;
        if (racer != null && racer.HasFinished) return;
        if (speed < cfg.dustMinSpeed) return;   // 스턴/정지 중엔 먼지 안 남

        // 빠를수록 촘촘하게 (기준 8m/s에서 100%, 최대 1.6배)
        float rate = cfg.dustRate * Mathf.Clamp(speed / 8f, 0.5f, 1.6f);
        emitAccum += rate * dt;
        int n = Mathf.FloorToInt(emitAccum);
        emitAccum -= n;
        for (int i = 0; i < Mathf.Min(n, 6); i++) EmitPuff(1f);
    }

    private void EmitPuff(float sizeMul)
    {
        Vector3 back = -transform.forward;
        Vector3 up = transform.up;
        Vector3 right = transform.right;

        var ep = new ParticleSystem.EmitParams
        {
            position = transform.TransformPoint(anchorLocal)
                       + right * Random.Range(-bodyWidth * 0.22f, bodyWidth * 0.22f)
                       + back * Random.Range(0f, 0.15f),
            velocity = back * Random.Range(1.2f, 2.4f)
                       + up * Random.Range(1.1f, 2.2f)      // 바닥에 깔리지 말고 붕 떠오르게
                       + right * Random.Range(-0.35f, 0.35f),
            startSize = puffSize * sizeMul * Random.Range(0.75f, 1.25f),
            startLifetime = cfg.dustLifetime * Random.Range(0.8f, 1.2f),
            startColor = cfg.dustColor * Random.Range(0.9f, 1.05f),
            rotation = Random.Range(0f, 360f),
            angularVelocity = Random.Range(-190f, 190f),   // 팔랑팔랑 뒤집히는 회전
            applyShapeToPosition = false
        };
        ps.Emit(ep, 1);
    }

    // ---- 몸 치수 실측: 배출 지점(뒷다리 뒤 바닥)과 먼지 크기를 동물 크기에 맞춘다 ----
    private void MeasureBody()
    {
        var all = GetComponentsInChildren<Renderer>();
        var rends = all.Where(r => r is SkinnedMeshRenderer).ToArray();
        if (rends.Length == 0)   // 스킨드가 없으면 번호판 TMP만 제외하고 사용
            rends = all.Where(r => r.GetComponent<TMPro.TMP_Text>() == null).ToArray();
        if (rends.Length == 0) return;

        // 월드 AABB는 동물이 어느 방향을 보느냐에 따라 축이 어긋난다 —
        // 렌더러 로컬 바운즈의 꼭짓점을 루트 로컬로 옮겨 담아야 앞뒤(z)를 제대로 잡는다
        bool has = false;
        Bounds local = default;
        foreach (var r in rends)
        {
            Bounds lb = r.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3(
                    (i & 1) == 0 ? lb.min.x : lb.max.x,
                    (i & 2) == 0 ? lb.min.y : lb.max.y,
                    (i & 4) == 0 ? lb.min.z : lb.max.z);
                Vector3 p = transform.InverseTransformPoint(r.transform.TransformPoint(c));
                if (!has) { local = new Bounds(p, Vector3.zero); has = true; }
                else local.Encapsulate(p);
            }
        }
        if (!has) return;

        // 바인드 포즈가 루트 밑으로 뻗은 모델(펭귄)이 있어 발밑은 루트 기준으로 클램프
        float feet = Mathf.Max(local.min.y, 0f);
        anchorLocal = new Vector3(0f, feet + 0.1f, local.min.z + local.size.z * 0.1f);

        // 파티클 크기는 월드 단위 — 로컬 치수에 스케일을 곱해야 한다
        // (치킨/고양이 1.5배 프리팹: §11 "콜라이더 로컬 단위 법칙"과 같은 함정)
        Vector3 s = transform.lossyScale;
        bodyWidth = local.size.x * Mathf.Abs(s.x);
        float bodyHeight = local.size.y * Mathf.Abs(s.y);
        // 너무 키우면 뒤따르는 동물을 가려 순위를 못 읽는다 — 조각은 몸 높이의 절반 이하로
        // 하한 0.45 = 고양이/치킨처럼 작은 동물도 조각이 알아볼 만큼은 나오게 (몸 비례만 쓰면 점처럼 작아짐)
        puffSize = Mathf.Clamp(bodyHeight * 0.55f, 0.45f, 1.3f) * Mathf.Max(0.05f, cfg.dustSize);
    }

    // ---- 파티클 시스템 조립 (전부 코드 — 프리팹 7종에 손댈 필요 없음) ----
    private void BuildSystem()
    {
        var go = new GameObject("BoostDust");
        go.transform.SetParent(transform, false);
        ps = go.AddComponent<ParticleSystem>();
        // 갓 붙인 시스템은 이미 재생 중 — 재생 중엔 duration 변경이 거부된다 (콘솔 에러)
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.playOnAwake = false;
        main.maxParticles = 160;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // 뒤에 남아야 하므로 월드
        main.scalingMode = ParticleSystemScalingMode.Shape;           // 크기는 내가 월드 단위로 계산해 넣는다
        main.gravityModifier = -0.12f;                                // 살짝 떠오름
        main.startSpeed = 0f;                                         // 전부 EmitParams로 직접 지정
        main.startSize = 1f;
        main.startLifetime = 1f;

        var emission = ps.emission;
        emission.enabled = false;    // 자동 배출 없음 — Update에서 수동 Emit

        var shape = ps.shape;
        shape.enabled = false;

        // 조각 4종 중 하나를 파티클마다 랜덤 배정 (프레임 진행 0 = 뽑은 그림 그대로 유지)
        var tsa = ps.textureSheetAnimation;
        tsa.enabled = true;
        tsa.mode = ParticleSystemAnimationMode.Grid;
        tsa.numTilesX = 2;
        tsa.numTilesY = 2;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 4f);
        tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

        // 팍 튀어나와 크기 유지 후 마지막에 살짝 오므리며 사라짐.
        // 계속 부풀리면 연기처럼 흩어져서 "종이 조각" 느낌이 죽는다
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.7f), new Keyframe(0.12f, 1.05f),
            new Keyframe(0.75f, 1f), new Keyframe(1f, 0.8f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // 불투명하게 버티다 끝에서 짧게 사라짐 — 길게 옅어지면 다시 에어브러시 연기처럼 보인다
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // 뒤로 튄 먼지가 곧 멈춰 제자리에서 흩어지도록 감쇠
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.35f;
        limit.limit = new ParticleSystem.MinMaxCurve(0.6f);

        // 회전은 파티클마다 EmitParams로 직접 준다 (모듈은 값이 겹쳐 더해져서 통제가 안 됨)
        var rot = ps.rotationOverLifetime;
        rot.enabled = false;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.alignment = ParticleSystemRenderSpace.View;
        rend.sharedMaterial = GetPuffMaterial();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.sortingFudge = -1f;

        ps.Play();   // 배출은 0이지만 재생 중이어야 수동 Emit 입자가 시뮬레이션된다
    }

    // ---- 머티리얼/텍스처: 전 동물 공용 1개 (드로우콜 절약) ----
    private static Material runtimeMat;

    private Material GetPuffMaterial()
    {
        if (runtimeMat != null) return runtimeMat;

        if (cfg != null && cfg.boostDustMaterial != null)
        {
            // 인스펙터 지정 머티리얼은 "셰이더 공급원" — 원본을 건드리면 에셋이 오염되므로 사본 사용
            runtimeMat = new Material(cfg.boostDustMaterial);
        }
        else
        {
            // 폴백: 빌드에서 셰이더가 스트립될 수 있으니 머티리얼은 GameConfig에 채워두는 게 안전
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            if (sh == null)
            {
                Debug.LogWarning("[BoostDustFx] 먼지용 셰이더를 못 찾았습니다. GameConfig의 먼지 머티리얼을 지정하세요.");
                return null;
            }
            runtimeMat = new Material(sh);
        }
        runtimeMat.name = "BoostDust (runtime)";
        // 머티리얼에 텍스처가 이미 박혀 있으면 존중 — 비어 있을 때만 코드로 구워 넣는다
        if (runtimeMat.mainTexture == null) runtimeMat.mainTexture = BuildPuffAtlas();
        return runtimeMat;
    }

    /// <summary>
    /// 셀 애니메이션식 연기 조각 4종을 2×2 아틀라스로 굽는다.
    /// 부드러운 그라데이션(에어브러시) 대신 ① 꽉 찬 단색 ② 굵은 검은 테두리 ③ 딱 떨어지는 외곽선 —
    /// 이 셋이 있어야 "손으로 그린 종이 조각"처럼 읽힌다. 알파는 경계 1px만 부드럽게(계단 방지).
    /// 파티클마다 4종 중 하나를 랜덤으로 뽑아 쓰므로 같은 모양이 반복되지 않는다.
    /// </summary>
    private static Texture2D BuildPuffAtlas()
    {
        const int TILE = 128;
        const int S = TILE * 2;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            name = "BoostDustPuffAtlas",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int tile = (x < TILE ? 0 : 1) + (y < TILE ? 0 : 2);
                float u = ((x % TILE) + 0.5f) / TILE;
                float v = ((y % TILE) + 0.5f) / TILE;
                px[y * S + x] = SampleToonPuff(Shapes[tile], u, v);
            }

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // 조각 4종의 실루엣 (x, y = 로브 중심 0~1, z = 반지름).
    // 로브를 깊게 겹쳐 한 덩어리로 만들되 개수/배치를 달리해 실루엣에 개성을 준다.
    private static readonly Vector3[][] Shapes =
    {
        new[] { new Vector3(0.50f, 0.46f, 0.30f), new Vector3(0.32f, 0.55f, 0.24f),
                new Vector3(0.68f, 0.54f, 0.25f), new Vector3(0.50f, 0.66f, 0.23f) },
        new[] { new Vector3(0.38f, 0.48f, 0.26f), new Vector3(0.60f, 0.45f, 0.30f),
                new Vector3(0.52f, 0.65f, 0.22f) },
        new[] { new Vector3(0.48f, 0.42f, 0.25f), new Vector3(0.30f, 0.52f, 0.20f),
                new Vector3(0.66f, 0.57f, 0.24f), new Vector3(0.46f, 0.63f, 0.22f),
                new Vector3(0.58f, 0.34f, 0.17f) },
        new[] { new Vector3(0.44f, 0.50f, 0.28f), new Vector3(0.64f, 0.56f, 0.22f),
                new Vector3(0.52f, 0.36f, 0.18f) },
    };

    // 테두리 두께 (타일 정규화 단위). 얇으면 관전 거리에서 선이 뭉개져 그냥 흰 덩어리로 보인다 — 굵게
    private const float OutlineWidth = 0.085f;
    private const float EdgeAA = 0.005f;         // 외곽선 안티에일리어싱 폭 — 이것만 부드럽게

    /// <summary>
    /// 진짜 smoothstep (0→1 문턱값). ⚠ Unity의 Mathf.SmoothStep(from, to, t)는 이게 아니라
    /// from~to 사이를 부드럽게 "보간"하는 함수다 — 문턱값처럼 쓰면 값이 from~to 범위로 나와서
    /// 알파가 통째로 뭉개진다 (실제로 이 함정에 빠져 먼지가 최대 26% 불투명도로 나왔음).
    /// </summary>
    private static float SmoothThreshold(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-6f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private static Color32 SampleToonPuff(Vector3[] lobes, float u, float v)
    {
        // 로브 합집합의 거친 SDF: 양수 = 안쪽, 클수록 중심에서 깊다
        float m = float.MinValue;
        foreach (var l in lobes)
            m = Mathf.Max(m, l.z - new Vector2(u - l.x, v - l.y).magnitude);

        float alpha = SmoothThreshold(-EdgeAA, EdgeAA, m);                       // 딱 끊기는 실루엣
        float fill = SmoothThreshold(OutlineWidth - EdgeAA, OutlineWidth + EdgeAA, m);  // 0 = 테두리, 1 = 속

        // 테두리는 텍스처에 검게 구워둔다 — 파티클 색을 어떻게 바꿔도 윤곽선이 살아남는다
        // (미니맵 도넛 마커와 같은 수법)
        byte c = (byte)(Mathf.Lerp(0.10f, 1f, fill) * 255f);
        return new Color32(c, c, c, (byte)(Mathf.Clamp01(alpha) * 255f));
    }

    /// <summary>[미사용 보존] 예전 에어브러시식 부드러운 먼지 — 되돌릴 일이 있으면 이걸로.</summary>
    private static Texture2D BuildSoftPuffTexture()
    {
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            name = "BoostDustPuff",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var lobes = Shapes[0];
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                float a = 0f;
                foreach (var l in lobes)
                {
                    float d = new Vector2(u - l.x, v - l.y).magnitude / l.z;
                    a = Mathf.Max(a, 1f - SmoothThreshold(0.74f, 1f, d));
                }
                px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
