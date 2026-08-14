using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 미니맵 전광판 — 좌측 트랙 실루엣 미니맵 + 우측 간단 순위표(레인 배지 + 이름).
/// 미니맵: TrackPath 중심선/폭을 런타임에 한 번 텍스처로 굽고(트랙은 정적),
/// 동물마다 "가운데 빈 원"(검정 테두리 도넛) 마커가 실시간 위치를 따라 움직인다.
/// 순위표: ScoreboardBoard와 동일한 로컬 위치 기반 진행도 계산(연속성 투영) —
/// 클라는 TransformView 미러 위치로 같은 계산을 하므로 호스트/클라 공용, 네트워크 무관.
/// 마커 색/배지 색은 RacerColors 단일 출처. 씬의 전광판 오브젝트는 통째로 복붙 가능.
/// </summary>
public class MinimapBoard : MonoBehaviour
{
    [Header("배선 (에디터 조립)")]
    [SerializeField] private RawImage mapImage;         // 트랙 실루엣이 그려질 정사각 이미지
    [SerializeField] private RectTransform markerRoot;  // 마커 부모 — mapImage 전체를 덮는 스트레치 자식
    [SerializeField] private RectTransform rowContainer;
    [SerializeField] private GameObject rowTemplate;    // 비활성 템플릿 (자식: Badge/BadgeText, NameText)

    [Header("미니맵")]
    [Tooltip("트랙 텍스처 해상도(px)")] public int textureSize = 640;
    [Tooltip("도로 색 — 밝아야 검정 마커도 보임")] public Color trackColor = new Color(0.78f, 0.78f, 0.82f);
    [Tooltip("출발선 색")] public Color startLineColor = new Color(0.12f, 0.12f, 0.12f);
    [Tooltip("마커(빈 원) 지름 — 캔버스 px")] public float markerSize = 48f;

    [Header("순위표")]
    [Tooltip("행 높이 상한 (캔버스 px) — 출전 수가 많아 컨테이너를 넘치면 자동으로 줄어든다")]
    public float rowHeight = 140f;
    [Tooltip("순위 변동 시 행 이동 속도")] public float rowMoveSpeed = 6f;

    // 실제로 쓰는 행 높이 — 출전 수 × rowHeight가 컨테이너를 넘으면 나눠 담는다
    // (9마리 × 140 = 1260px > 컨테이너 1120px 라서 마지막 행이 판 밖으로 튀어나왔음)
    private float fitRowHeight = 140f;

    private class Entry
    {
        public Racer racer;
        public RectTransform rowRect;
        public RectTransform marker;
        public float lastProg;      // 연속성 투영용 (8자 교차에서 반대편 변 포획 방지)
        public bool finished;
        public bool eliminated;
        public int finishRank;
    }

    private readonly List<Entry> entries = new();
    private RaceManager raceManager;
    private TrackPath path;

    // 월드(x,z) → 텍스처(px) 변환 계수 — 굽기 때 확정
    private Texture2D mapTexture;
    private Vector2 worldMin;
    private float worldToPx;
    private Vector2 pxOffset;

    private static Sprite ringSprite;   // 도넛 스프라이트 — 전 전광판 공유

    private void Awake()
    {
        raceManager = FindFirstObjectByType<RaceManager>();
        path = FindFirstObjectByType<TrackPath>();
        if (rowTemplate != null) rowTemplate.SetActive(false);
    }

    private void OnEnable()
    {
        GameEvents.OnRacerFinished += HandleFinished;
        Loc.OnLanguageChanged += Rebuild;   // 행 이름이 만들 때 구워지므로 언어 전환 = 재구성
    }
    private void OnDisable()
    {
        GameEvents.OnRacerFinished -= HandleFinished;
        Loc.OnLanguageChanged -= Rebuild;
    }

    private void HandleFinished(int racerId, int rank, bool eliminated)
    {
        var e = entries.Find(x => x.racer != null && x.racer.RacerId == racerId);
        if (e == null || e.finished) return;
        e.finished = true;
        e.eliminated = eliminated;
        e.finishRank = rank;

        if (!eliminated) return;
        // 탈락: 행 뿌옇게 + 미니맵 마커를 흐린 회색으로 (레인 색 제거 — 죽은 표시)
        if (e.rowRect != null)
        {
            var cg = e.rowRect.GetComponent<CanvasGroup>();
            if (cg == null) cg = e.rowRect.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0.45f;
        }
        if (e.marker != null)
        {
            var img = e.marker.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = new Color(0.45f, 0.45f, 0.45f, 0.5f);
        }
    }

    private void Update()
    {
        if (raceManager == null || path == null) return;

        // 트랙은 정적 — TrackPath 빌드(Awake) 이후 한 번만 굽는다
        if (mapTexture == null && path.TotalLength > 1f) BakeTrackTexture();

        // 라인업 변동(새 라운드 스폰/파괴) 감지 → 행·마커 재구성
        bool stale = entries.Count != raceManager.Racers.Count;
        if (!stale) foreach (var e in entries) if (e.racer == null) { stale = true; break; }
        if (stale) Rebuild();

        float dt = Time.deltaTime;
        Vector2 mapSize = markerRoot != null ? markerRoot.rect.size : Vector2.one;

        foreach (var e in entries)
        {
            if (e.racer == null) continue;
            var pos = e.racer.transform.position;
            e.lastProg = path.GetDistanceNear(pos, e.lastProg);   // 랩 누적 — 2랩째 선두가 뒤로 안 밀림

            if (e.marker != null && mapTexture != null)
            {
                Vector2 px = WorldToTex(pos);
                e.marker.anchoredPosition = new Vector2(px.x / textureSize * mapSize.x,
                                                        px.y / textureSize * mapSize.y);
            }
        }

        // 정렬: 완주(순위순) → 주행 중(진행도 내림차순) → 탈락(항상 맨 아래, 순위순)
        var order = new List<Entry>(entries);
        order.Sort((a, b) =>
        {
            if (a.eliminated != b.eliminated) return a.eliminated ? 1 : -1;
            if (a.eliminated) return a.finishRank.CompareTo(b.finishRank);
            if (a.finished != b.finished) return a.finished ? -1 : 1;
            if (a.finished) return a.finishRank.CompareTo(b.finishRank);
            return b.lastProg.CompareTo(a.lastProg);
        });

        for (int i = 0; i < order.Count; i++)
        {
            var rt = order[i].rowRect;
            if (rt == null) continue;
            var p = rt.anchoredPosition;
            p.y = Mathf.Lerp(p.y, -i * fitRowHeight, rowMoveSpeed * dt);
            rt.anchoredPosition = p;
        }
    }

    // ================= 재구성 =================

    private void Rebuild()
    {
        foreach (var e in entries)
        {
            if (e.rowRect != null) Destroy(e.rowRect.gameObject);
            if (e.marker != null) Destroy(e.marker.gameObject);
        }
        entries.Clear();
        if (rowTemplate == null || rowContainer == null) return;

        // 출전 수가 컨테이너를 넘치면 행을 통째로 비례 축소한다.
        // 행 배경만 줄이면 배지/이름이 원래 크기로 남아 따로 놀기 때문에 localScale로 함께 줄인다.
        int count = 0;
        foreach (var r in raceManager.Racers) if (r != null) count++;
        float avail = rowContainer.rect.height;
        float fitScale = (count > 0 && avail > 1f)
            ? Mathf.Min(1f, avail / (count * rowHeight)) : 1f;
        fitRowHeight = rowHeight * fitScale;

        int i = 0;
        foreach (var racer in raceManager.Racers)
        {
            if (racer == null) continue;
            int post = racer.RacerId + 1;   // 스폰 규칙: 등번호 = RacerId + 1 (호스트/클라 동일)

            var go = Instantiate(rowTemplate, rowContainer);
            go.SetActive(true);
            var entry = new Entry { racer = racer, rowRect = go.GetComponent<RectTransform>() };
            entry.rowRect.anchoredPosition = new Vector2(0f, -i * fitRowHeight);
            entry.rowRect.localScale = Vector3.one * fitScale;

            var badgeImg = go.transform.Find("Badge").GetComponent<Image>();
            badgeImg.color = RacerColors.Of(post);
            var badgeText = go.transform.Find("Badge/BadgeText").GetComponent<TMP_Text>();
            badgeText.text = post.ToString();
            badgeText.color = RacerColors.TextOn(post);

            var nameText = go.transform.Find("NameText").GetComponent<TMP_Text>();
            nameText.text = racer.Definition != null ? racer.Definition.LocalizedName : racer.DisplayName;

            entry.marker = CreateMarker(post);
            entries.Add(entry);
            i++;
        }
    }

    private RectTransform CreateMarker(int post)
    {
        if (markerRoot == null) return null;
        var go = new GameObject($"Marker{post}", typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(markerRoot, false);
        rt.anchorMin = rt.anchorMax = Vector2.zero;     // 좌하단 기준 px 배치
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(markerSize, markerSize);
        var img = go.GetComponent<Image>();
        img.sprite = GetRingSprite();
        img.color = RacerColors.Of(post);
        img.raycastTarget = false;
        return rt;
    }

    // ================= 미니맵 굽기 =================

    private void BakeTrackTexture()
    {
        // 중심선을 0.4m 간격으로 샘플 — 경계 포함 바운즈 산출
        const float step = 0.4f;
        int count = Mathf.CeilToInt(path.TotalLength / step) + 1;
        var centers = new Vector3[count];
        var halves = new float[count];
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            float d = Mathf.Min(i * step, path.TotalLength);
            centers[i] = path.GetPoint(d);
            halves[i] = path.GetHalfWidth(d);
            minX = Mathf.Min(minX, centers[i].x - halves[i]); maxX = Mathf.Max(maxX, centers[i].x + halves[i]);
            minZ = Mathf.Min(minZ, centers[i].z - halves[i]); maxZ = Mathf.Max(maxZ, centers[i].z + halves[i]);
        }

        float pad = textureSize * 0.05f;
        float w = maxX - minX, h = maxZ - minZ;
        worldToPx = (textureSize - pad * 2f) / Mathf.Max(w, h);
        worldMin = new Vector2(minX, minZ);
        // 짧은 축을 가운데로 정렬
        pxOffset = new Vector2(pad + (textureSize - pad * 2f - w * worldToPx) * 0.5f,
                               pad + (textureSize - pad * 2f - h * worldToPx) * 0.5f);

        var pixels = new Color32[textureSize * textureSize];
        var clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        // 도로: 샘플마다 반폭 원을 찍어 굵은 띠를 만든다
        var road = (Color32)trackColor;
        for (int i = 0; i < count; i++)
            StampCircle(pixels, WorldToTex(centers[i]), halves[i] * worldToPx, road);

        // 출발선: 진행도 0의 단면을 가로지르는 선
        var lineCol = (Color32)startLineColor;
        Vector3 a = path.GetPointAt(0f, -halves[0]);
        Vector3 b = path.GetPointAt(0f, halves[0]);
        int steps = Mathf.CeilToInt(halves[0] * 2f * worldToPx);
        for (int i = 0; i <= steps; i++)
            StampCircle(pixels, WorldToTex(Vector3.Lerp(a, b, (float)i / steps)), 2f, lineCol);

        mapTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };
        mapTexture.SetPixels32(pixels);
        mapTexture.Apply(false);
        if (mapImage != null) { mapImage.texture = mapTexture; mapImage.color = Color.white; }
    }

    private Vector2 WorldToTex(Vector3 world) =>
        new Vector2((world.x - worldMin.x) * worldToPx + pxOffset.x,
                    (world.z - worldMin.y) * worldToPx + pxOffset.y);

    private void StampCircle(Color32[] pixels, Vector2 center, float radius, Color32 col)
    {
        radius = Mathf.Max(radius, 1.5f);
        int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
        int x1 = Mathf.Min(textureSize - 1, Mathf.CeilToInt(center.x + radius));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
        int y1 = Mathf.Min(textureSize - 1, Mathf.CeilToInt(center.y + radius));
        float r2 = radius * radius;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - center.x, dy = y + 0.5f - center.y;
                if (dx * dx + dy * dy <= r2) pixels[y * textureSize + x] = col;
            }
    }

    // ================= 마커 스프라이트 =================

    /// <summary>가운데 빈 원(도넛) 스프라이트 — 몸통은 흰색(틴트 대상), 테두리는 검정 고정.
    /// 흰/검 레인 색도 어떤 배경에서든 보이도록 테두리를 굽는다.</summary>
    private static Sprite GetRingSprite()
    {
        if (ringSprite != null) return ringSprite;

        const int S = 64;
        const float rOut = 29f, rIn = 8f, border = 3.5f, aa = 1.25f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f));
                float ringA = Mathf.Clamp01((rOut - d) / aa) * Mathf.Clamp01((d - rIn) / aa);
                float bodyA = Mathf.Clamp01((rOut - border - d) / aa) * Mathf.Clamp01((d - rIn - border) / aa);
                byte v = (byte)(bodyA * 255f);
                px[y * S + x] = new Color32(v, v, v, (byte)(ringA * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        ringSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return ringSprite;
    }
}
