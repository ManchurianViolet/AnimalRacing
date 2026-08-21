using System.Linq;
using UnityEngine;

/// <summary>
/// [북극곰] 콜라 원샷 연출 — ① 캔을 입가에서 흔들다가(셰이킹) 젖혀 들이키고 ② 캔을 뒤로 던지며
/// 몸이 작아지고 ③ 부스트 내내 뒤로 부스터 연기를 뿜는다. 끝나면 원래 크기로 복귀.
/// 발동 감지 = 전 클라로 중계되는 스킬 사건(OnSkillEvent — Cola + 내 RacerId). 통신 0.
/// 타이밍은 SkillTuning(들이키기/지속)이 단일 출처 — 호스트 부스트 개시와 연출이 같은 시계를 쓴다.
/// 캔 = GameConfig.colaCanPrefab 실물 모델 (비면 코드 생성 빨간 캔 폴백 — 유저가 에셋 주면 교체 예정).
/// 축소는 루트 localScale — TransformView는 위치/회전만 미러하므로 전 클라가 각자 로컬로 걸어도 안 싸운다.
/// RaceManager가 북극곰(Cola 스킬)에만 부착.
/// </summary>
public class ColaFx : MonoBehaviour
{
    private Racer racer;
    private GameConfig cfg;
    private Transform head;
    private Vector3 headBaseLocalPos;   // 들이킬 때 머리 젖히기용 기준 (⚠ += 금지 — 매 프레임 기준+오프셋 재계산 §11)
    private GameObject can;
    private ParticleSystem smoke;
    private Vector3 baseScale;
    private float timer = -1f;

    // 캔 던지기 (들이키기 끝 → 뒤로 포물선)
    private bool canTossed;
    private Vector3 tossPos, tossVel;
    private float tossSpin;

    // 부스터 연기 배출 (BoostDustFx와 같은 위치-변화 기반 속도 측정 — 클라 미러 동물도 동작)
    private Vector3 rearLocal = new Vector3(0f, 0.3f, -0.6f);
    private float bodyHeight = 1f;
    private float emitAccum;
    private Vector3 lastPos;
    private float speed;

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        cfg = config;
        baseScale = transform.localScale;
        head = FindHeadBone();
        if (head != null) headBaseLocalPos = head.localPosition;
        MeasureBody();
        BuildCan();
        BuildSmoke();
        lastPos = transform.position;
    }

    private void OnEnable() => GameEvents.OnSkillEvent += HandleSkillEvent;
    private void OnDisable() { GameEvents.OnSkillEvent -= HandleSkillEvent; Stop(); }
    // 캔은 씬 루트에 산다 (레이서에 붙이면 몸 축소 스케일이 캔까지 왜곡) — 레이서가 사라질 때 직접 정리
    private void OnDestroy() { if (can != null) Destroy(can); }

    private void HandleSkillEvent(SkillFeedEvent evt, int rid)
    {
        if (racer == null || evt != SkillFeedEvent.Cola || rid != racer.RacerId) return;
        timer = 0f;
        canTossed = false;
        if (can != null) can.SetActive(true);
    }

    private void Update()
    {
        // 속도 측정은 상시 (연기 배출 판단용)
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector3 p = transform.position;
        speed = Mathf.Lerp(speed, (p - lastPos).magnitude / dt, 12f * dt);
        lastPos = p;

        if (timer < 0f) return;
        if (racer == null || racer.HasFinished) { Stop(); return; }

        timer += dt;
        float drink = SkillTuning.ColaDrinkSeconds;
        float total = drink + SkillTuning.ColaDuration;
        float shrinkT = Mathf.Max(0.05f, cfg != null ? cfg.colaShrinkSeconds : 0.35f);
        float shrink = cfg != null ? cfg.colaShrinkScale : 0.6f;

        if (timer >= total) { Stop(); return; }

        // ---- ① 들이키기 (0 ~ drink): 흔들기 45% → 젖혀서 원샷 55% ----
        // 캔 피벗 = 입구(윗면). 피벗을 입 위치에 두면 기울일 때 입구는 입에 붙어 있고
        // 밑동만 하늘로 돌아 "입에 대고 부어 마시는" 그림이 된다.
        if (timer < drink)
        {
            if (can != null && head != null)
            {
                float k = timer / drink;
                Vector3 fwd = racer.transform.forward;
                // 머리 본(두개골 중심)에서 주둥이 앞·아래로 — 값은 북극곰 실측 (head→코끝 0.28m)
                Vector3 mouthPos = head.position
                    + fwd * (cfg != null ? cfg.colaCanForward : 0.42f)
                    + Vector3.up * (cfg != null ? cfg.colaCanUp : -0.22f);

                if (k < 0.45f)
                {
                    // 흔들기 — 빠른 상하 셰이킹 + 좌우 랜덤 지터 (탄산 압축 연기)
                    float wob = Mathf.Sin(Time.time * 40f) * 0.03f;
                    can.transform.position = mouthPos + Vector3.up * wob
                        + racer.transform.right * (Mathf.Sin(Time.time * 31f) * 0.015f);
                    can.transform.rotation =
                        Quaternion.AngleAxis(Mathf.Sin(Time.time * 37f) * 14f, racer.transform.right);
                    if (head != null) head.localPosition = headBaseLocalPos;
                }
                else
                {
                    // 원샷 — 입구는 입에 고정, 밑동이 하늘로 (피벗이 입구라 AngleAxis 하나면 됨)
                    float tiltK = Mathf.SmoothStep(0f, 1f, (k - 0.45f) / 0.55f);
                    can.transform.position = mouthPos;
                    can.transform.rotation = Quaternion.AngleAxis(-tiltK * 125f, racer.transform.right);
                    // 머리도 같이 젖힌다 — 기준 localPosition + 오프셋 재계산 (누적 금지 §11)
                    Vector3 upLocal = head.parent != null
                        ? head.parent.InverseTransformDirection(Vector3.up) : Vector3.up;
                    Vector3 backLocal = head.parent != null
                        ? head.parent.InverseTransformDirection(-fwd) : -fwd;
                    head.localPosition = headBaseLocalPos + upLocal * (0.10f * tiltK) + backLocal * (0.04f * tiltK);
                }
            }
        }
        else
        {
            // ---- 들이키기 끝: 머리 복귀 + 캔을 뒤로 휙 던진다 (1회) ----
            if (!canTossed && can != null)
            {
                if (head != null) head.localPosition = headBaseLocalPos;
                canTossed = true;
                tossPos = can.transform.position;
                tossVel = -racer.transform.forward * 2.2f + Vector3.up * 3.2f
                    + racer.transform.right * Random.Range(-0.8f, 0.8f);
                tossSpin = Random.Range(300f, 520f);
            }
            if (canTossed && can != null && can.activeSelf)
            {
                float tossAge = timer - drink;
                if (tossAge > 0.9f) can.SetActive(false);
                else
                {
                    tossVel += Physics.gravity * dt;
                    tossPos += tossVel * dt;
                    can.transform.position = tossPos;
                    can.transform.Rotate(racer.transform.right, tossSpin * dt, Space.World);
                }
            }

            // ---- ② 몸 축소 (부스트 시작에 줄고, 끝나기 직전 복원) ----
            float f;
            if (timer < drink + shrinkT) f = Mathf.Lerp(1f, shrink, (timer - drink) / shrinkT);
            else if (timer > total - shrinkT) f = Mathf.Lerp(shrink, 1f, (timer - (total - shrinkT)) / shrinkT);
            else f = shrink;
            transform.localScale = baseScale * f;

            // ---- ③ 부스터 연기 — 달리는 동안 뒤로 연속 배출 ----
            if (smoke != null && speed > 1.5f)
            {
                float rate = (cfg != null ? cfg.colaSmokeRate : 26f) * Mathf.Clamp(speed / 8f, 0.6f, 1.6f);
                emitAccum += rate * dt;
                int n = Mathf.FloorToInt(emitAccum);
                emitAccum -= n;
                for (int i = 0; i < Mathf.Min(n, 6); i++) EmitSmoke();
            }
        }
    }

    private void Stop()
    {
        if (timer < 0f) return;
        timer = -1f;
        transform.localScale = baseScale;
        if (head != null) head.localPosition = headBaseLocalPos;
        if (can != null) can.SetActive(false);
    }

    private void EmitSmoke()
    {
        Vector3 back = -transform.forward;
        Vector3 right = transform.right;
        Color c = cfg != null ? cfg.colaSmokeColor : new Color(0.93f, 0.93f, 0.97f, 0.9f);
        float size = Mathf.Clamp(bodyHeight * 0.4f, 0.3f, 1.0f)
            * (cfg != null ? Mathf.Max(0.05f, cfg.colaSmokeSize) : 1f);

        var ep = new ParticleSystem.EmitParams
        {
            // rearLocal은 루트 로컬이라 축소 중엔 TransformPoint가 알아서 작아진 몸 뒤를 가리킨다
            position = transform.TransformPoint(rearLocal)
                       + right * Random.Range(-0.12f, 0.12f),
            // 로켓 배기 — 뒤로 강하게, 위로는 살짝만 (먼지구름과 결이 다르게 깔린 분사)
            velocity = back * Random.Range(2.6f, 4.2f)
                       + Vector3.up * Random.Range(0.1f, 0.6f)
                       + right * Random.Range(-0.3f, 0.3f),
            startSize = size * Random.Range(0.7f, 1.2f),
            startLifetime = Random.Range(0.5f, 0.8f),
            startColor = c * Random.Range(0.92f, 1.05f),
            rotation = Random.Range(0f, 360f),
            angularVelocity = Random.Range(-90f, 90f),
            applyShapeToPosition = false
        };
        smoke.Emit(ep, 1);
    }

    // ---- 머리 본 탐색 (RoarFx/NeckSweepFx와 같은 다단 전략) ----
    private Transform FindHeadBone()
    {
        var bones = GetComponentsInChildren<Transform>(true);
        foreach (var tr in bones)
        {
            string n = tr.name.ToLowerInvariant();
            if (n == "scull" || n == "skull" || n == "head") return tr;
        }
        foreach (var tr in bones)
            if (tr.name.ToLowerInvariant() == "jaw" && tr.parent != null) return tr.parent;
        Transform deepest = null;
        int bestIdx = -1;
        foreach (var tr in bones)
        {
            if (!tr.name.StartsWith("spine.")) continue;
            if (int.TryParse(tr.name.Substring(6), out int idx) && idx > bestIdx)
            { bestIdx = idx; deepest = tr; }
        }
        return deepest;
    }

    // ---- 몸 치수 실측: 연기 배출 지점(엉덩이 뒤)과 크기 (BoostDustFx와 같은 로컬 바운즈 스캔) ----
    private void MeasureBody()
    {
        var rends = GetComponentsInChildren<Renderer>()
            .Where(r => r is SkinnedMeshRenderer).ToArray();
        if (rends.Length == 0) return;

        bool has = false;
        Bounds local = default;
        foreach (var r in rends)
        {
            Bounds lb = r.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? lb.min.x : lb.max.x,
                    (i & 2) == 0 ? lb.min.y : lb.max.y,
                    (i & 4) == 0 ? lb.min.z : lb.max.z);
                Vector3 p = transform.InverseTransformPoint(r.transform.TransformPoint(corner));
                if (!has) { local = new Bounds(p, Vector3.zero); has = true; }
                else local.Encapsulate(p);
            }
        }
        if (!has) return;

        float feet = Mathf.Max(local.min.y, 0f);
        // 배기구는 먼지(바닥)보다 살짝 위 — 엉덩이 높이에서 뿜는 로켓 그림
        rearLocal = new Vector3(0f, feet + local.size.y * 0.35f, local.min.z + local.size.z * 0.05f);
        bodyHeight = local.size.y * Mathf.Abs(transform.lossyScale.y);
    }

    // ---- 콜라 캔: 실물 모델(GameConfig) 우선, 비면 코드 생성 폴백 ----
    private void BuildCan()
    {
        float targetH = cfg != null ? cfg.colaCanHeight : 0.22f;

        if (cfg != null && cfg.colaCanPrefab != null)
        {
            // 홀더로 감싸고 모델을 "입구(바운즈 최상단) = 홀더 피벗"에 정렬한다 —
            // 기울이기 회전이 홀더 피벗 기준이라, 모델 피벗이 어디든(보통 바닥) 입구가 입에 붙는다
            can = new GameObject("Prop_ColaCan");
            var model = Instantiate(cfg.colaCanPrefab, can.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (var col in can.GetComponentsInChildren<Collider>()) Destroy(col);   // CC/동물과 충돌 금지

            var rs = can.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                // 목표 높이 정규화 (§11 — 고정 배율은 모델 바뀌면 다시 잡아야 해서 금지)
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float h = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (h > 1e-4f) model.transform.localScale *= targetH / h;

                // 정규화 후 바운즈 재측정 → 입구(최상단)를 피벗으로, 수평은 중심 정렬
                b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                model.transform.localPosition = new Vector3(-b.center.x, -b.max.y, -b.center.z);
            }
        }
        else
        {
            // 코드 생성 폴백 — 빨간 몸통 + 은색 위아래 캡 (유저 에셋 오면 colaCanPrefab에 드래그).
            // ⚠ 피벗 = 캔 입구(윗면): 몸통이 피벗 아래로 뻗는다 — 기울이기 회전이 입에 딱 붙게 (위 주석 참조)
            can = new GameObject("Prop_ColaCan");
            var red = MakeMat(new Color(0.82f, 0.10f, 0.12f));
            var silver = MakeMat(new Color(0.78f, 0.80f, 0.84f));
            float r = targetH * 0.30f, h = targetH;
            AddCylinder(can, "Body", red, new Vector3(0f, -h * 0.5f, 0f), new Vector3(r, h * 0.38f, r));
            AddCylinder(can, "CapTop", silver, new Vector3(0f, -h * 0.07f, 0f), new Vector3(r * 0.88f, h * 0.05f, r * 0.88f));
            AddCylinder(can, "CapBottom", silver, new Vector3(0f, -h * 0.93f, 0f), new Vector3(r * 0.88f, h * 0.05f, r * 0.88f));
        }
        can.SetActive(false);
    }

    // ---- 부스터 연기 파티클 시스템 (전부 코드 — BoostDustFx 패턴, 배출은 수동 Emit) ----
    private void BuildSmoke()
    {
        var go = new GameObject("ColaBoosterSmoke");
        go.transform.SetParent(transform, false);
        smoke = go.AddComponent<ParticleSystem>();
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // 재생 중 duration 변경 금지 (§11)

        var main = smoke.main;
        main.loop = true;
        main.duration = 5f;
        main.playOnAwake = false;
        main.maxParticles = 220;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // 연기가 뒤에 남아야 함
        main.scalingMode = ParticleSystemScalingMode.Shape;           // 크기는 월드 단위로 직접 계산 — 몸 축소에 안 딸려감
        main.gravityModifier = -0.05f;
        main.startSpeed = 0f;

        var emission = smoke.emission; emission.enabled = false;   // 수동 Emit
        var shape = smoke.shape; shape.enabled = false;

        // 로켓 배기 — 갈수록 부풀며 옅어진다 (먼지 조각과 반대 결)
        var sol = smoke.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.55f), new Keyframe(0.4f, 1.1f), new Keyframe(1f, 1.7f)));

        var col = smoke.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.45f, 0.5f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var limit = smoke.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.28f;
        limit.limit = new ParticleSystem.MinMaxCurve(0.9f);

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.alignment = ParticleSystemRenderSpace.View;
        rend.sharedMaterial = GetSmokeMaterial();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.sortingFudge = -1f;

        smoke.Play();   // 배출 0이지만 재생 중이어야 수동 Emit 입자가 시뮬레이션됨
    }

    private static Material smokeMat;

    private Material GetSmokeMaterial()
    {
        if (smokeMat != null) return smokeMat;
        // 셰이더 공급원은 먼지 머티리얼 재사용 (빌드 스트립 안전) — 텍스처만 부드러운 원으로 교체
        if (cfg != null && cfg.boostDustMaterial != null)
            smokeMat = new Material(cfg.boostDustMaterial);
        else
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            smokeMat = new Material(sh);
        }
        smokeMat.name = "ColaBoosterSmoke (runtime)";
        smokeMat.mainTexture = BuildSoftCircle();   // 먼지의 종이조각 아틀라스 대신 부드러운 연기 원
        return smokeMat;
    }

    /// <summary>부드러운 원형 연기 텍스처 — 중심 불투명, 가장자리로 갈수록 소멸.</summary>
    private static Texture2D BuildSoftCircle()
    {
        const int S = 96;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        { name = "ColaSmokePuff", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S - 0.5f, v = (y + 0.5f) / S - 0.5f;
                float d = Mathf.Sqrt(u * u + v * v) * 2f;
                float t = Mathf.Clamp01((0.95f - d) / 0.55f);
                float a = t * t * (3f - 2f * t);   // 진짜 smoothstep (§11 — Mathf.SmoothStep 아님)
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private static void AddCylinder(GameObject parent, string name, Material mat, Vector3 pos, Vector3 scale)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(p.GetComponent<Collider>());
        p.name = name;
        p.transform.SetParent(parent.transform, false);
        p.transform.localPosition = pos;
        p.transform.localScale = scale;
        p.GetComponent<Renderer>().sharedMaterial = mat;
    }

    private static Material MakeMat(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.color = c;
        return m;
    }
}
