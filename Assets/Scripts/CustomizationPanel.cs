using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [타이틀] 커스터마이징 패널. UI를 코드로 조립한다 (슬롯 개수가 라이브러리에 따라 변하므로).
/// 좌우 화살표로 부위를 넘기면 타이틀 화면의 캐릭터가 즉시 바뀌고,
/// 확정 = PlayerPrefs 저장, 취소 = 열기 전 상태로 복원.
/// </summary>
public class CustomizationPanel : MonoBehaviour
{
    [Header("대상")]
    [SerializeField] private CharacterCustomization target;

    [Header("글꼴 (씬의 다른 TMP와 같은 것)")]
    [SerializeField] private TMP_FontAsset font;

    [Tooltip("커마 중에는 숨길 것들 (메인 메뉴 버튼/닉네임 등)")]
    [SerializeField] private GameObject[] hideWhileOpen;

    [Header("카메라 연출 — 여는 동안 캐릭터 클로즈업")]
    [Tooltip("커마 동안 카메라가 이동할 위치 (월드)")]
    [SerializeField] private Vector3 camOpenPos = new Vector3(2.06f, 0.73f, -3.3f);
    [Tooltip("커마 동안의 카메라 회전 (오일러)")]
    [SerializeField] private Vector3 camOpenEuler = new Vector3(-12.348f, 0f, 0f);
    [Tooltip("카메라 이동 시간 (초) — 0이면 즉시 스냅")]
    [SerializeField] private float camMoveSeconds = 0.35f;

    [Header("모양")]
    [SerializeField] private Vector2 panelSize = new Vector2(620f, 700f);   // y는 내용에 맞춰 자동 재계산됨
    [SerializeField] private float rowHeight = 54f;
    [SerializeField] private Color panelColor = new Color(0.06f, 0.06f, 0.08f, 0.92f);
    [SerializeField] private Color buttonColor = new Color(0.18f, 0.18f, 0.22f, 1f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private int titleSize = 34;
    [SerializeField] private int labelSize = 24;

    // 레이아웃/색 상수 — 씬에 낡은 값이 박제되지 않게 의도적으로 직렬화하지 않음
    private const float Margin = 24f;    // 패널 좌우 여백
    private const float HeaderH = 84f;   // 제목 영역 높이
    private const float FooterH = 96f;   // 하단 버튼 영역 높이
    private const float ArrowW = 40f;    // ◀▶ 버튼 한 변 (값 박스 높이와 공유)
    private static readonly Color AccentColor = new Color(0.93f, 0.62f, 0.18f, 1f);   // 확정 버튼/포인트
    private static readonly Color AccentTextColor = new Color(0.12f, 0.08f, 0.02f, 1f);
    private static readonly Color RowBgColor = new Color(1f, 1f, 1f, 0.05f);          // 홀수 행 줄무늬
    private static readonly Color InsetColor = new Color(0f, 0f, 0f, 0.38f);          // 부위 이름 음각 박스
    private static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.75f);          // 좌측 부위명 (값보다 한 톤 낮게)

    private readonly List<TMP_Text> valueTexts = new();
    private string snapshot;          // 취소용 — 열 때의 상태
    private bool built;

    public bool IsOpen => gameObject.activeSelf;

    private void Awake()
    {
        Build();
        gameObject.SetActive(false);
    }

    public void Open()
    {
        Build();
        gameObject.SetActive(true);
        SoundManager.PlaySfx(SfxId.PanelOpen);
        SetMenuVisible(false);
        snapshot = target != null ? target.Encode() : "";
        RefreshAll();
        CameraGlide.To(camOpenPos, Quaternion.Euler(camOpenEuler), camMoveSeconds);
    }

    public void Close()
    {
        SetMenuVisible(true);
        gameObject.SetActive(false);
        SoundManager.PlaySfx(SfxId.PanelClose);
        // 복귀 이동은 카메라에 얹힌 헬퍼가 재생 — 패널이 방금 꺼졌어도 살아 있다
        CameraGlide.Home(camMoveSeconds);
    }

    private void SetMenuVisible(bool visible)
    {
        if (hideWhileOpen == null) return;
        foreach (var go in hideWhileOpen)
            if (go != null) go.SetActive(visible);
    }

    private void Confirm()
    {
        if (target != null)
        {
            target.SaveToPrefs();
            PlayerLook.Publish();   // 방 안이면 즉시, 접속 전이면 PUN이 캐시했다가 입장 때 전송
        }
        Close();
    }

    private void Cancel()
    {
        if (target != null)
        {
            target.Decode(snapshot);
            target.ApplyAll();
        }
        Close();
    }

    private void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
        RefreshAll();   // 외부에서 외형이 바뀌어도 라벨이 어긋나지 않게 (값이 같으면 건드리지 않음)
    }

    // ================= UI 조립 =================

    private void Build()
    {
        if (built) return;
        built = true;

        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        // 보이는 슬롯 수를 먼저 세서 패널 높이를 내용에 딱 맞춘다 (빈 여백/넘침 원천 차단)
        var slots = (target != null && target.Library != null) ? target.Library.slots : null;
        int visible = 0;
        if (slots != null)
            foreach (var s in slots)
                if (s.parts != null && s.parts.Length > 0) visible++;
        rt.sizeDelta = new Vector2(panelSize.x, HeaderH + Mathf.Max(visible, 1) * rowHeight + FooterH);

        var bg = gameObject.GetComponent<Image>();
        if (bg == null) bg = gameObject.AddComponent<Image>();
        bg.sprite = GetRoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.color = panelColor;
        bg.raycastTarget = true;   // 뒤쪽 버튼 클릭 차단

        float w = panelSize.x;

        // ---- 제목 (가운데 + 강조색 밑줄 + 구분선) ----
        MakeText("Title", rt, new Vector2(0f, -20f), new Vector2(w - Margin * 2f, 44f),
            "커스터마이징", titleSize, TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        MakeImage("TitleAccent", rt, new Vector2(0f, -64f), new Vector2(72f, 5f), AccentColor,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), false);
        MakeImage("HeaderLine", rt, new Vector2(0f, -HeaderH + 2f), new Vector2(w - Margin * 2f, 2f), DividerColor,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), false);

        if (slots == null)
        {
            MakeText("Warn", rt, Vector2.zero, new Vector2(w - Margin * 2f, 60f),
                "부위 라이브러리가 연결되지 않았습니다", labelSize, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            return;
        }

        // ---- 슬롯 행 ----
        float y = -HeaderH;
        bool odd = false;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].parts == null || slots[i].parts.Length == 0) continue;   // 무료팩에 없는 부위는 숨김
            BuildRow(rt, i, slots[i].displayName, y, odd);
            y -= rowHeight;
            odd = !odd;
        }

        // ---- 하단 버튼 (여백/간격 균등, 확정만 강조색) ----
        MakeImage("FooterLine", rt, new Vector2(0f, FooterH - 2f), new Vector2(w - Margin * 2f, 2f), DividerColor,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), false);

        const float gap = 12f;
        float bw = (w - Margin * 2f - gap * 2f) / 3f;
        MakeButton("Random", rt, new Vector2(Margin, 30f), new Vector2(bw, 54f),
            "랜덤", () => { target.Randomize(); RefreshAll(); },
            new Vector2(0f, 0f), new Vector2(0f, 0f), buttonColor, textColor);
        MakeButton("Cancel", rt, new Vector2(Margin + bw + gap, 30f), new Vector2(bw, 54f),
            "취소", Cancel, new Vector2(0f, 0f), new Vector2(0f, 0f), buttonColor, textColor);
        MakeButton("Confirm", rt, new Vector2(Margin + (bw + gap) * 2f, 30f), new Vector2(bw, 54f),
            "확정", Confirm, new Vector2(0f, 0f), new Vector2(0f, 0f), AccentColor, AccentTextColor);

        var hint = MakeText("Hint", rt, new Vector2(0f, 7f), new Vector2(w - Margin * 2f, 18f),
            "Esc = 취소", 15, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        hint.color = new Color(1f, 1f, 1f, 0.35f);
    }

    private void BuildRow(RectTransform parent, int slotIndex, string label, float y, bool odd)
    {
        var row = new GameObject("Row_" + label, typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(parent, false);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(panelSize.x - Margin * 2f, rowHeight);
        row.anchoredPosition = new Vector2(0f, y);

        float w = row.sizeDelta.x;

        // 홀수 행 줄무늬 — 은은한 가독 가이드 (구분선 대용)
        if (odd)
            MakeImage("RowBg", row, Vector2.zero, new Vector2(w, rowHeight - 4f), RowBgColor,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), true);

        var lab = MakeText("Label", row, new Vector2(12f, 0f), new Vector2(w * 0.30f, rowHeight - 10f),
            label, labelSize, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        lab.color = LabelColor;

        int captured = slotIndex;
        float stepperLeft = w * 0.32f;   // 라벨 열 끝 = 스테퍼(◀ 값 ▶) 시작

        MakeButton("Prev", row, new Vector2(stepperLeft, 0f), new Vector2(ArrowW, ArrowW),
            "◀", () => { target.Cycle(captured, -1); Refresh(captured); },
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), buttonColor, textColor, 20);

        MakeButton("Next", row, new Vector2(0f, 0f), new Vector2(ArrowW, ArrowW),
            "▶", () => { target.Cycle(captured, 1); Refresh(captured); },
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), buttonColor, textColor, 20);

        // 부위 이름 음각 박스 — 화살표 사이를 정확히 채운다 (양쪽 8px 간격)
        float boxX = stepperLeft + ArrowW + 8f;
        float boxW = w - boxX - ArrowW - 8f;
        var inset = MakeImage("ValueBox", row, new Vector2(boxX, 0f), new Vector2(boxW, ArrowW), InsetColor,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), true);

        var value = MakeText("Value", (RectTransform)inset.transform, Vector2.zero,
            new Vector2(boxW - 16f, ArrowW - 6f), "", labelSize, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        value.enableAutoSizing = true;    // 긴 부위 이름도 박스 안에서 해결 (넘침 방지)
        value.fontSizeMax = labelSize;
        value.fontSizeMin = 14f;

        while (valueTexts.Count <= slotIndex) valueTexts.Add(null);
        valueTexts[slotIndex] = value;
    }

    private void RefreshAll()
    {
        for (int i = 0; i < valueTexts.Count; i++) Refresh(i);
    }

    private void Refresh(int slot)
    {
        if (slot < 0 || slot >= valueTexts.Count || valueTexts[slot] == null || target == null) return;
        string s = target.GetSelectedName(slot);
        if (valueTexts[slot].text != s) valueTexts[slot].text = s;   // 같은 값이면 TMP 리빌드 안 하게
    }

    // ---- 작은 조립 헬퍼 ----

    private TMP_Text MakeText(string name, RectTransform parent, Vector2 pos, Vector2 size,
                              string content, int size_, TextAlignmentOptions align,
                              Vector2 anchor, Vector2 pivot)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = content;
        t.fontSize = size_;
        t.color = textColor;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    private void MakeButton(string name, RectTransform parent, Vector2 pos, Vector2 size,
                            string label, UnityEngine.Events.UnityAction onClick,
                            Vector2 anchor, Vector2 pivot, Color bgColor, Color txtColor, int fontSize = 0)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        var img = go.AddComponent<Image>();
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = bgColor;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;   // 평시 살짝 어둡게 → 호버에 밝아지고 클릭에 눌리는 반응
        cb.normalColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.68f, 0.68f, 0.68f, 1f);
        cb.selectedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        var t = MakeText("Text", rt, Vector2.zero, size, label, fontSize > 0 ? fontSize : labelSize,
            TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        t.color = txtColor;
    }

    private Image MakeImage(string name, RectTransform parent, Vector2 pos, Vector2 size, Color color,
                            Vector2 anchor, Vector2 pivot, bool rounded)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        var img = go.AddComponent<Image>();
        if (rounded) { img.sprite = GetRoundedSprite(); img.type = Image.Type.Sliced; }
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>
    /// 둥근 모서리 9-슬라이스 스프라이트를 코드로 1회 굽는다 (미니맵 도넛 마커와 같은 방식 — 에셋 무의존).
    /// 파괴되면(플레이 재시작) 유니티 가짜 null 판정으로 자동 재생성된다.
    /// </summary>
    private static Sprite sRounded;
    private static Sprite GetRoundedSprite()
    {
        if (sRounded != null) return sRounded;

        const int S = 40;        // 텍스처 한 변
        const float R = 11f;     // 모서리 반지름
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        float half = S * 0.5f;
        for (int yy = 0; yy < S; yy++)
            for (int xx = 0; xx < S; xx++)
            {
                // 둥근 사각형까지의 부호 거리 → 1px 부드러운 가장자리
                float dx = Mathf.Max(Mathf.Abs(xx + 0.5f - half) - (half - R), 0f);
                float dy = Mathf.Max(Mathf.Abs(yy + 0.5f - half) - (half - R), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy) - R;
                byte a = (byte)(Mathf.Clamp01(0.5f - dist) * 255f);
                px[yy * S + xx] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();

        sRounded = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f,
            0, SpriteMeshType.FullRect, new Vector4(14f, 14f, 14f, 14f));   // border=14 > R이라 모서리 보존
        return sRounded;
    }

    /// <summary>
    /// 메인 카메라 이동 도우미. 패널 오브젝트는 닫히며 비활성화되므로(코루틴 사망)
    /// 카메라 자신에게 얹어 이동을 재생한다. 최초 호출 시의 포즈를 "집"으로 기억해 복귀에 쓴다.
    /// </summary>
    private class CameraGlide : MonoBehaviour
    {
        private Vector3 homePos, toPos;
        private Quaternion homeRot, toRot;
        private Vector3 fromPos; private Quaternion fromRot;
        private float t, dur;
        private bool homeSaved;

        private static CameraGlide Get()
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var g = cam.GetComponent<CameraGlide>();
            if (g == null) g = cam.gameObject.AddComponent<CameraGlide>();
            return g;
        }

        public static void To(Vector3 pos, Quaternion rot, float seconds)
        {
            var g = Get();
            if (g == null) return;
            if (!g.homeSaved) { g.homePos = g.transform.position; g.homeRot = g.transform.rotation; g.homeSaved = true; }
            g.Begin(pos, rot, seconds);
        }

        public static void Home(float seconds)
        {
            var g = Get();
            if (g == null || !g.homeSaved) return;
            g.Begin(g.homePos, g.homeRot, seconds);
        }

        private void Begin(Vector3 pos, Quaternion rot, float seconds)
        {
            fromPos = transform.position; fromRot = transform.rotation;
            toPos = pos; toRot = rot;
            dur = Mathf.Max(0.0001f, seconds);
            t = 0f;
            enabled = true;
        }

        private void Update()
        {
            t += Time.deltaTime / dur;
            float k = Mathf.Clamp01(t);
            k = k * k * (3f - 2f * k);   // 부드러운 가감속 (Mathf.SmoothStep의 보간 본체)
            transform.position = Vector3.Lerp(fromPos, toPos, k);
            transform.rotation = Quaternion.Slerp(fromRot, toRot, k);
            if (t >= 1f) enabled = false;
        }
    }
}
