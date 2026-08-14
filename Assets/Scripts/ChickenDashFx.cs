using UnityEngine;

/// <summary>
/// [치킨] 냅다 달리기 중 몸 뒤로 무지개 자취를 남기는 연출.
///
/// 발동 감지는 전 클라로 이미 중계되는 스킬 피드(OnSkillProc) 문자열 — RoarFx와 같은 방식이라
/// 네트워크 추가 통신 0 (부스트 먼지·무전기 LCD와 같은 철학).
/// ⚠ 피드 문구("OO이 냅다 달린다")를 고치면 여기 키워드도 같이 고쳐야 한다 — 안 그러면
///   에러 없이 연출만 조용히 사라진다 (SfxRelay의 스킬음과 같은 취약점).
///
/// 자취는 TrailRenderer 여러 줄을 몸 뒤 좌우에 펼쳐 만든다. 줄마다 무지개 색 시작점을 어긋나게
/// 줘서 한 줄짜리보다 훨씬 무지개처럼 보인다.
/// RaceManager가 치킨(Dash 스킬)에만 부착.
/// </summary>
public class ChickenDashFx : MonoBehaviour
{
    private Racer racer;
    private GameConfig config;
    private TrailRenderer[] trails;
    private float timer = -1f;   // -1 = 대기

    // 무지개 7색 — 자취의 정체성이라 인스펙터로 빼지 않고 여기 고정 (밝기만 config로 조절)
    private static readonly Color[] Rainbow =
    {
        new Color(1.00f, 0.15f, 0.15f),   // 빨
        new Color(1.00f, 0.55f, 0.10f),   // 주
        new Color(1.00f, 0.92f, 0.15f),   // 노
        new Color(0.25f, 0.90f, 0.30f),   // 초
        new Color(0.20f, 0.60f, 1.00f),   // 파
        new Color(0.30f, 0.30f, 0.90f),   // 남
        new Color(0.70f, 0.35f, 0.95f),   // 보
    };

    private static Material trailMat;
    private static Texture2D rainbowTex;

    /// <summary>
    /// 무지개 띠 텍스처 — 세로(V축)에 7색을 깔아 굽는다.
    /// TrailRenderer의 V축은 리본의 폭 방향이라, 이걸 입히면 색이 진행 방향이 아니라
    /// 폭을 가로질러 나란히 놓인다 (colorGradient로는 길이 방향밖에 못 만든다).
    /// </summary>
    private static Texture2D BuildRainbowTexture()
    {
        const int H = 64;
        var tex = new Texture2D(4, H, TextureFormat.RGBA32, false)
        {
            name = "ChickenRainbow (runtime)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        for (int y = 0; y < H; y++)
        {
            // 0~1 위치를 무지개 7색 구간에 매핑해 선형 보간
            float t = y / (float)(H - 1);
            float scaled = t * (Rainbow.Length - 1);
            int i = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, Rainbow.Length - 2);
            Color c = Color.Lerp(Rainbow[i], Rainbow[i + 1], scaled - i);

            // 띠 위아래 가장자리는 살짝 투명하게 — 칼로 자른 듯한 경계를 없앤다
            float edge = Mathf.Min(t, 1f - t) / 0.12f;
            c.a = Mathf.Clamp01(edge);

            for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
        }

        tex.Apply();
        return tex;
    }

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        this.config = config;
        BuildTrails();
    }

    private void OnEnable() => GameEvents.OnSkillProc += HandleSkillProc;
    private void OnDisable() => GameEvents.OnSkillProc -= HandleSkillProc;

    private void HandleSkillProc(string line)
    {
        if (racer == null || trails == null || string.IsNullOrEmpty(line)) return;
        if (!line.StartsWith(racer.DisplayName)) return;
        if (!line.Contains("냅다 달린다")) return;

        timer = 0f;
        SetEmitting(true);
    }

    private void LateUpdate()
    {
        // 띠를 지면과 나란히 눕힌다 — 부모(동물)가 회전해도 따라 기울지 않게 매 프레임 월드 기준으로 고정.
        // 배출이 끝난 뒤에도 잔여 꼬리가 남아 있으므로 대기 상태에서도 계속 잡아준다.
        KeepFlat();

        if (timer < 0f) return;

        // 완주·탈락하면 즉시 정리 (죽은 닭이 무지개를 뿜으면 안 된다)
        if (racer == null || racer.HasFinished)
        {
            StopTrail();
            return;
        }

        timer += Time.deltaTime;
        if (timer >= SkillTuning.DashDuration) StopTrail();
    }

    /// <summary>
    /// 띠의 Z축(= 면의 법선)을 월드 위쪽으로 고정 → 띠가 수평으로 눕는다.
    /// 위쪽 벡터로는 동물의 진행 방향을 줘서 띠가 달리는 방향을 따라 펼쳐지게 한다.
    /// </summary>
    private void KeepFlat()
    {
        if (trails == null) return;
        foreach (var t in trails)
        {
            if (t == null) continue;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            t.transform.rotation = Quaternion.LookRotation(Vector3.up, fwd.normalized);
        }
    }

    private void StopTrail()
    {
        timer = -1f;
        SetEmitting(false);   // 이미 그려진 꼬리는 trail.time 동안 자연스럽게 사라진다
    }

    private void SetEmitting(bool on)
    {
        if (trails == null) return;
        foreach (var t in trails) if (t != null) t.emitting = on;
    }

    // ---- 자취 생성: 몸 뒤쪽에 앵커를 깔고 줄마다 무지개 위상을 어긋나게 ----
    private void BuildTrails()
    {
        if (config == null) return;

        if (rainbowTex == null) rainbowTex = BuildRainbowTexture();

        if (trailMat == null)
        {
            // ⚠ URP Particles/Unlit은 쓰면 안 된다 — 파티클 전용 vertex stream을 기대해서
            //    TrailRenderer에 물리면 띠가 새까맣게 나온다 (실측으로 확인).
            //    Sprites/Default는 텍스처 × 정점색을 알파 블렌딩으로 곱해줘서 리본에 딱 맞다.
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return;

            trailMat = new Material(sh) { name = "ChickenDashTrail (runtime)" };
            // URP 셰이더는 _BaseMap, 스프라이트 셰이더는 _MainTex라 둘 다 넣는다
            trailMat.mainTexture = rainbowTex;
            if (trailMat.HasProperty("_BaseMap")) trailMat.SetTexture("_BaseMap", rainbowTex);
            if (trailMat.HasProperty("_BaseColor")) trailMat.SetColor("_BaseColor", Color.white);

            // URP Unlit으로 폴백된 경우엔 투명 설정을 직접 켜야 알파가 먹는다
            if (trailMat.HasProperty("_Surface"))
            {
                trailMat.SetFloat("_Surface", 1f);
                trailMat.SetFloat("_Blend", 0f);
                trailMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                trailMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                trailMat.SetFloat("_ZWrite", 0f);
                trailMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                trailMat.renderQueue = 3000;
            }
        }

        // 몸 크기 실측 — 치킨/고양이는 루트 스케일이 1.5배라 고정값을 쓰면 크기가 안 맞는다
        Bounds b = LocalBodyBounds(out bool ok);
        float bodyWidth = ok ? Mathf.Max(0.2f, b.size.x) : 0.5f;
        float bodyHeight = ok ? Mathf.Max(0.2f, b.size.y) : 0.6f;
        float backZ = ok ? b.min.z : -0.4f;              // 몸 뒤끝
        float midY = ok ? b.center.y : bodyHeight * 0.5f;

        int count = Mathf.Max(1, config.dashTrailCount);
        trails = new TrailRenderer[count];

        for (int i = 0; i < count; i++)
        {
            // 좌우 배치: 1줄이면 중앙, 여러 줄이면 -1~+1 사이에 고르게
            float t01 = count == 1 ? 0.5f : i / (float)(count - 1);
            float lateral = (t01 - 0.5f) * bodyWidth * config.dashTrailSpread * 2f;

            var anchor = new GameObject($"DashTrailAnchor_{i}");
            anchor.transform.SetParent(transform, false);
            anchor.transform.localPosition = new Vector3(lateral, midY, backZ);

            var go = new GameObject($"DashTrail_{i}");
            go.transform.SetParent(anchor.transform, false);

            float w = config.dashTrailWidth * (bodyHeight / 0.6f);   // 몸 크기 비례

            var tr = go.AddComponent<TrailRenderer>();
            tr.material = trailMat;
            tr.time = config.dashTrailTime;
            tr.minVertexDistance = 0.06f;
            tr.startWidth = w;
            tr.endWidth = w * config.dashTrailEndScale;   // 뒤로 갈수록 살짝 퍼짐
            tr.numCapVertices = 0;      // 끝을 둥글리면 원통처럼 보인다 — 평평한 띠로
            // TransformZ = 띠의 면이 이 오브젝트의 Z축을 법선으로 삼는다.
            // LateUpdate에서 Z축을 월드 위쪽으로 고정하므로 띠가 지면과 나란히 눕는다.
            // (View로 두면 보는 각도에 따라 띠가 돌아가 버린다)
            tr.alignment = LineAlignment.TransformZ;
            tr.textureMode = LineTextureMode.Stretch;   // 무지개 텍스처를 띠 전체에 한 번 매핑
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.emitting = false;
            tr.colorGradient = BuildFade();

            trails[i] = tr;
        }
    }

    /// <summary>
    /// 색은 텍스처가 담당하므로 여기선 밝기와 페이드만 — 길이 방향으로 서서히 옅어지게.
    /// (색까지 여기서 주면 텍스처의 무지개와 곱해져 탁해진다)
    /// </summary>
    private Gradient BuildFade()
    {
        float bright = Mathf.Max(0.1f, config.dashTrailBrightness);
        Color tint = new Color(bright, bright, bright);

        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(tint, 0f),
                new GradientColorKey(tint, 1f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),      // 몸에 붙은 쪽은 진하게
                new GradientAlphaKey(0.85f, 0.55f),
                new GradientAlphaKey(0f, 1f),      // 꼬리 끝은 투명하게 사라짐
            });
        return g;
    }

    /// <summary>스킨드메시 바운즈를 루트 로컬로 환산 — 배출 지점과 크기 산출용.</summary>
    private Bounds LocalBodyBounds(out bool ok)
    {
        ok = false;
        var smrs = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (smrs.Length == 0) return new Bounds();

        Bounds result = new Bounds();
        bool first = true;
        foreach (var smr in smrs)
        {
            if (smr.sharedMesh == null) continue;
            // localBounds(바인드 포즈 기준)를 렌더러 → 루트 로컬로 옮긴다
            Bounds lb = smr.localBounds;
            Vector3 c = transform.InverseTransformPoint(smr.transform.TransformPoint(lb.center));
            Vector3 e = transform.InverseTransformVector(smr.transform.TransformVector(lb.extents));
            e = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));

            var bb = new Bounds(c, e * 2f);
            if (first) { result = bb; first = false; }
            else result.Encapsulate(bb);
        }

        ok = !first;
        return result;
    }
}
