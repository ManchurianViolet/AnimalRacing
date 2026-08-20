using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 우측 사건 피드 — v19 개편: 문장 나열 → 아이콘 행.
/// 표시 대상: ① 플레이어가 행한 행동(주사기/무전기 사용) ② 동물 셀프 스킬 발동(v21 복구 —
/// [배지][동물 이름][번개][빈칸] 순서, 무전기 강제 발동분은 무전기 행과 중복이라 억제). 펭귄 면역·완주/탈락은 제외.
/// 행 규격 통일(v22 유저 지정): 두 행 타입 모두 4칸 = (이름폭 칸 + 아이콘 + 배지 + 동물이름 칸) 조합이라
/// 배경 박스 총폭·높이가 동일하다 — 셀프 스킬 행의 끝 빈칸이 이름 칸 폭을 대신 채운다.
/// 행 = [플레이어 이름] [행동 아이콘] [번호 배지][동물 이름] — 처형 무전기(무조준)는 대상 칸 생략.
/// 행동 아이콘(v20 개편 — 유저 그림 발주): 부스트=초록 ↑ / 감속=빨강 ↓ / 발동 무전기=노랑 번개 /
/// 처형 무전기=해골. 스프라이트는 흰색 아이콘(Skymon 팩)에 색 틴트 — 비어 있으면 투명 공란으로
/// 자리만 지킨다 (해골은 팩에 없어 유저가 구해오면 인스펙터에 드래그).
/// UI는 코드로 자체 조립 (MinimapBoard와 같은 방식), 위치는 기존 feedText 자리를 물려받는다.
/// </summary>
public class TimelineFeed : MonoBehaviour
{
    [Tooltip("옛 문장 피드 TMP — 이제 표시엔 안 쓰고 위치 기준으로만 쓴다 (시작 시 숨김)")]
    [SerializeField] private TextMeshProUGUI feedText;
    [Tooltip("최대 행 수 — 넘치면 오래된 것부터 지움")]
    [SerializeField] private int maxLines = 8;
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private RaceManager raceManager;

    [Header("행동 아이콘 (흰색 스프라이트 — 아래 색으로 틴트. 비면 투명 공란)")]
    [Tooltip("부스트 주사기 — 위 화살표")]
    [SerializeField] private Sprite boostIcon;
    [Tooltip("감속 주사기 — 아래 화살표")]
    [SerializeField] private Sprite slowIcon;
    [Tooltip("발동 무전기 — 번개")]
    [SerializeField] private Sprite radioSkillIcon;
    [Tooltip("처형 무전기 — 해골 (팩에 없음 — 구해오면 여기 드래그, 그동안은 공란)")]
    [SerializeField] private Sprite radioExecIcon;
    [Tooltip("동물 셀프 스킬 발동 — 번개. 비워두면 발동 무전기 아이콘을 같이 쓴다 (씬 배선 0)")]
    [SerializeField] private Sprite selfSkillIcon;

    [Header("행동 아이콘 색 (흰 아이콘에 곱해짐)")]
    [SerializeField] private Color boostColor = new Color(0.55f, 0.92f, 0.18f);   // 연두
    [SerializeField] private Color slowColor = new Color(0.93f, 0.24f, 0.20f);    // 빨강
    [SerializeField] private Color radioSkillColor = new Color(1f, 0.85f, 0.20f); // 노랑
    [SerializeField] private Color radioExecColor = Color.white;                  // 해골은 원색
    [SerializeField] private Color selfSkillColor = new Color(1f, 0.85f, 0.20f); // 셀프 스킬 번개 — 노랑

    [Header("행 크기")]
    [Tooltip("행 높이(px) — 아이콘 칸도 이 크기의 정사각형")]
    [SerializeField] private float rowHeight = 38f;
    [Tooltip("행 사이 간격(px)")]
    [SerializeField] private float rowSpacing = 7f;
    [Tooltip("칸 사이 간격(px)")]
    [SerializeField] private float cellSpacing = 6f;
    [Tooltip("플레이어 이름 글자 크기")]
    [SerializeField] private float nameFontSize = 26f;

    [Header("칸 폭 고정 (세로 줄 맞춤 — 행마다 폭이 다르면 삐뚤삐뚤해진다)")]
    [Tooltip("플레이어 이름 칸 폭(px) — 긴 닉네임은 자동 축소. 셀프 스킬 행의 끝 빈칸도 이 폭 (행 총폭 통일)")]
    [SerializeField] private float nameColumnWidth = 145f;
    [Tooltip("동물 이름 칸 폭(px)")]
    [SerializeField] private float animalColumnWidth = 106f;

    [Header("행 배경 (검정 테두리 + 회색 채움 — 배경 없인 글자가 트랙에 묻힌다)")]
    [SerializeField] private Color rowBorderColor = new Color(0.03f, 0.03f, 0.04f, 0.95f);
    [SerializeField] private Color rowFillColor = new Color(0.30f, 0.30f, 0.33f, 0.92f);
    [Tooltip("테두리 두께(px)")]
    [SerializeField] private float rowBorderWidth = 3f;
    [Tooltip("배경과 내용 사이 여백(px) — 좌우")]
    [SerializeField] private float rowPaddingX = 12f;
    [Tooltip("배경과 내용 사이 여백(px) — 상하")]
    [SerializeField] private float rowPaddingY = 5f;

    private RectTransform container;
    private readonly LinkedList<GameObject> rows = new();

    // 발동 무전기를 쏜 대상 기록 — 5초 뒤 강제 발동의 스킬 행이 무전기 행과 중복으로 뜨는 것 억제
    private readonly Dictionary<int, float> radioSkillUsedAt = new();

    private void Awake()
    {
        BuildContainer();
    }

    private void Start()
    {
        // 옛 문장 피드는 표시에서 은퇴 — 오브젝트는 위치 기준으로 남겨둔다
        if (feedText != null) feedText.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        GameEvents.OnItemUsed     += HandleItemUsed;
        GameEvents.OnSkillEvent   += HandleSkillEvent;
        GameEvents.OnPhaseChanged += HandlePhase;
    }

    private void OnDisable()
    {
        GameEvents.OnItemUsed     -= HandleItemUsed;
        GameEvents.OnSkillEvent   -= HandleSkillEvent;
        GameEvents.OnPhaseChanged -= HandlePhase;
    }

    private void HandlePhase(GamePhase phase)
    {
        // 새 라운드(베팅) 시작 시 지난 라운드 피드 초기화
        if (phase == GamePhase.Betting) ClearRows();
    }

    // ================= 행 생성 =================

    private void HandleItemUsed(int pid, ItemDefinition item, int rid)
    {
        if (item == null) return;

        // 발동 무전기 기록 — 5초 뒤 강제 발동의 스킬 행은 이 행과 중복이라 억제한다 (유저 지적)
        if (item.kind == ItemKind.SkillTrigger && rid >= 0)
            radioSkillUsedAt[rid] = Time.time;

        var (icon, tint) = ActionIcon(item.kind);
        AddRow(PlayerName(pid), Color.white, icon, tint, rid);
    }

    /// <summary>동물 셀프 스킬 발동 — [배지][동물 이름][번개] 행 (유저 지정 순서).
    /// 무전기로 강제 발동된 것은 무전기 행이 이미 떠 있으므로 중복 표시하지 않는다.</summary>
    private void HandleSkillEvent(SkillFeedEvent evt, int rid)
    {
        switch (evt)
        {
            case SkillFeedEvent.Roar:
            case SkillFeedEvent.CatWalk:
            case SkillFeedEvent.Dash:
            case SkillFeedEvent.Rudolph:
            case SkillFeedEvent.ClubRush:
            case SkillFeedEvent.Camouflage:
            case SkillFeedEvent.NeckSweep:
            case SkillFeedEvent.Banana:
                // 무전기 발동분 억제 — 지연 5초(radioDelaySeconds) + 여유
                if (radioSkillUsedAt.TryGetValue(rid, out float usedAt))
                {
                    radioSkillUsedAt.Remove(rid);   // 1회분만 억제 — 이후 자동 발동은 다시 표시
                    if (Time.time - usedAt < 6.5f) return;
                }
                AddSkillRow(rid);
                break;
            // 펭귄 면역/처형/몽둥이 명중은 여전히 피드 제외 (v19 결정 유지)
        }
    }

    /// <summary>
    /// 셀프 스킬 행: [번호 배지][동물 이름][번개][빈칸] — 끝 빈칸은 플레이어 이름 칸과 같은 폭 (유저 지정).
    /// 이러면 두 행 타입의 칸 구성이 (배지 + 동물이름 + 아이콘 + 이름폭 칸)으로 같아져
    /// 행 배경 박스의 총폭·높이 규격이 플레이어 행동 행과 완전히 동일해진다.
    /// </summary>
    private void AddSkillRow(int rid)
    {
        var row = NewRowShell();
        if (row == null) return;

        var racer = raceManager != null ? raceManager.GetRacer(rid) : null;
        int post = racer != null ? racer.PostNumber : rid + 1;
        AddBadge(row.transform, post);

        string animalName = racer != null && racer.Definition != null ? racer.Definition.LocalizedName : "?";
        AddText(row.transform, animalName, nameFontSize, Color.white,
                animalColumnWidth, TextAlignmentOptions.MidlineLeft);

        Sprite icon = selfSkillIcon != null ? selfSkillIcon : radioSkillIcon;
        AddIcon(row.transform, icon, icon != null ? selfSkillColor : Color.clear);

        // 빈칸 — 이름 칸 폭으로 자리만 채워 행 규격 통일 (투명이라 안 보임)
        AddText(row.transform, "", nameFontSize, Color.clear,
                nameColumnWidth, TextAlignmentOptions.MidlineLeft);

        TrimRows(row);
    }

    private void AddRow(string nameText, Color nameColor, Sprite icon, Color iconTint, int rid)
    {
        var row = NewRowShell();
        if (row == null) return;

        // ① 플레이어 이름 — 고정폭 칸 (우측 정렬 = 아이콘 옆에 붙음)
        AddText(row.transform, nameText, nameFontSize, nameColor,
                nameColumnWidth, TextAlignmentOptions.MidlineRight);

        // ② 행동 아이콘 (스프라이트가 비면 투명 공란으로 자리만 지킴)
        AddIcon(row.transform, icon, icon != null ? iconTint : Color.clear);

        // ③ 대상 동물 — 번호 배지 + 동물 이름. 처형 무전기(무조준)는 내용 없이 투명 칸으로
        //    자리만 채운다 — 행마다 폭이 다르면 세로로 삐뚤삐뚤해진다 (유저 지적).
        if (rid >= 0)
        {
            var racer = raceManager != null ? raceManager.GetRacer(rid) : null;
            int post = racer != null ? racer.PostNumber : rid + 1;   // 레인 번호는 1부터
            AddBadge(row.transform, post);

            string animalName = racer != null && racer.Definition != null ? racer.Definition.LocalizedName : "?";
            AddText(row.transform, animalName, nameFontSize, Color.white,
                    animalColumnWidth, TextAlignmentOptions.MidlineLeft);
        }
        else
        {
            AddIcon(row.transform, null, Color.clear);   // 배지 자리
            AddText(row.transform, "", nameFontSize, Color.clear,
                    animalColumnWidth, TextAlignmentOptions.MidlineLeft);
        }

        TrimRows(row);
    }

    /// <summary>행 껍데기 (배경+레이아웃) — 칸은 호출부가 원하는 순서로 채운다.</summary>
    private GameObject NewRowShell()
    {
        if (container == null) return null;

        var row = new GameObject("FeedRow", typeof(RectTransform));
        var rt = (RectTransform)row.transform;
        rt.SetParent(container, false);
        rt.SetAsFirstSibling();   // 최신이 맨 위

        // ⚠ 행 크기는 부모 VLG(childControl=true)가 HLG의 preferred로 잡는다 —
        //    행에 ContentSizeFitter를 얹으면 부모 배치보다 늦게 커져서 우측 정렬이 밀린다 (캡처 실사고)
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.spacing = cellSpacing;
        hlg.padding = new RectOffset(
            Mathf.RoundToInt(rowPaddingX), Mathf.RoundToInt(rowPaddingX),
            Mathf.RoundToInt(rowPaddingY), Mathf.RoundToInt(rowPaddingY));

        // 행 배경 — 검정 테두리(행 루트) + 회색 채움(자식, 테두리 두께만큼 인셋).
        // 채움은 레이아웃에서 제외해야 HLG가 칸으로 세지 않는다.
        var border = row.AddComponent<Image>();
        border.sprite = GetRoundedSprite();
        border.type = Image.Type.Sliced;
        border.color = rowBorderColor;
        border.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        var fillRt = (RectTransform)fillGo.transform;
        fillRt.SetParent(row.transform, false);
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(rowBorderWidth, rowBorderWidth);
        fillRt.offsetMax = new Vector2(-rowBorderWidth, -rowBorderWidth);
        var fill = fillGo.AddComponent<Image>();
        fill.sprite = GetRoundedSprite();
        fill.type = Image.Type.Sliced;
        fill.color = rowFillColor;
        fill.raycastTarget = false;
        fillGo.AddComponent<LayoutElement>().ignoreLayout = true;

        return row;
    }

    private void TrimRows(GameObject newRow)
    {
        rows.AddFirst(newRow);
        while (rows.Count > maxLines)
        {
            var oldest = rows.Last.Value;
            rows.RemoveLast();
            if (oldest != null) Destroy(oldest);
        }
    }

    /// <summary>아이템 종류 → (스프라이트, 틴트). 스프라이트가 비어 있으면 호출부가 공란 처리.</summary>
    private (Sprite, Color) ActionIcon(ItemKind kind) => kind switch
    {
        ItemKind.Boost        => (boostIcon, boostColor),
        ItemKind.Slow         => (slowIcon, slowColor),
        ItemKind.SkillTrigger => (radioSkillIcon, radioSkillColor),
        ItemKind.Execute      => (radioExecIcon, radioExecColor),
        _ => (null, Color.white)
    };

    private void ClearRows()
    {
        foreach (var r in rows) if (r != null) Destroy(r);
        rows.Clear();
        radioSkillUsedAt.Clear();
    }

    // ================= UI 조립 (코드 생성 — 씬 배선 0) =================

    /// <summary>행 컨테이너 — 기존 feedText 자리(우상단)를 물려받아 아래로 쌓인다.</summary>
    private void BuildContainer()
    {
        var go = new GameObject("FeedRows", typeof(RectTransform));
        container = (RectTransform)go.transform;

        if (feedText != null)
        {
            var src = feedText.rectTransform;
            container.SetParent(src.parent, false);
            container.anchorMin = src.anchorMin;
            container.anchorMax = src.anchorMax;
            container.pivot = new Vector2(1f, 1f);
            // 피벗을 우상단으로 바꾸므로 기준점도 feedText 렉트의 우상단 모서리로 환산
            var half = src.rect.size * 0.5f;
            container.anchoredPosition = src.anchoredPosition + new Vector2(half.x, half.y);
        }
        else
        {
            container.SetParent(transform, false);
            container.anchorMin = container.anchorMax = new Vector2(1f, 1f);
            container.pivot = new Vector2(1f, 1f);
            container.anchoredPosition = new Vector2(-20f, -220f);
        }
        container.sizeDelta = new Vector2(420f, 600f);

        // childControl=true — 행 크기를 부모가 HLG preferred로 직접 잡아야 우측 정렬이 안 밀린다 (위 ⚠ 참조)
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperRight;
        vlg.spacing = rowSpacing;
    }

    private void AddText(Transform parent, string text, float size, Color color,
                         float fixedWidth = 0f, TextAlignmentOptions align = TextAlignmentOptions.MidlineRight)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        if (fixedWidth > 0f)
        {
            // 고정폭 칸 — 세로 줄 맞춤. 긴 텍스트(스팀 닉네임 등)는 칸 안에서 자동 축소
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = fixedWidth;
            le.minWidth = fixedWidth;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = size;
            tmp.fontSizeMin = 12f;
        }
    }

    private void AddIcon(Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject("Icon", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite = sprite;               // null이면 단색 사각형 = 검은 칸
        img.color = color;
        img.preserveAspect = true;
        img.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = rowHeight;
        le.preferredHeight = rowHeight;
    }

    /// <summary>레인 번호 배지 — 색은 RacerColors 단일 출처.</summary>
    private void AddBadge(Transform parent, int postNumber)
    {
        var go = new GameObject("Badge", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.color = RacerColors.Of(postNumber);
        img.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = rowHeight;
        le.preferredHeight = rowHeight;

        var numGo = new GameObject("Num", typeof(RectTransform));
        numGo.transform.SetParent(go.transform, false);
        var numRt = (RectTransform)numGo.transform;
        numRt.anchorMin = Vector2.zero;
        numRt.anchorMax = Vector2.one;
        numRt.offsetMin = numRt.offsetMax = Vector2.zero;
        var num = numGo.AddComponent<TextMeshProUGUI>();
        num.text = postNumber.ToString();
        num.fontSize = rowHeight * 0.62f;
        num.fontStyle = FontStyles.Bold;
        num.color = RacerColors.TextOn(postNumber);
        num.alignment = TextAlignmentOptions.Midline;
        num.raycastTarget = false;
    }

    private string PlayerName(int id)
    {
        if (matchManager != null)
            foreach (var p in matchManager.Players)
                if (p.PlayerId == id) return p.Nickname;
        return $"P{id}";
    }

    /// <summary>
    /// 둥근 모서리 9-슬라이스 스프라이트를 코드로 1회 굽는다 (커마 패널과 같은 방식 — 에셋 무의존).
    /// 파괴되면(플레이 재시작) 유니티 가짜 null 판정으로 자동 재생성된다.
    /// </summary>
    private static Sprite sRounded;
    private static Sprite GetRoundedSprite()
    {
        if (sRounded != null) return sRounded;

        const int S = 32;
        const float R = 8f;      // 모서리 반지름 — 행이 작아 커마 패널(11)보다 슴슴하게
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        float half = S * 0.5f;
        for (int yy = 0; yy < S; yy++)
            for (int xx = 0; xx < S; xx++)
            {
                float dx = Mathf.Max(Mathf.Abs(xx + 0.5f - half) - (half - R), 0f);
                float dy = Mathf.Max(Mathf.Abs(yy + 0.5f - half) - (half - R), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy) - R;
                byte a = (byte)(Mathf.Clamp01(0.5f - dist) * 255f);
                px[yy * S + xx] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();

        sRounded = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f,
            0, SpriteMeshType.FullRect, new Vector4(10f, 10f, 10f, 10f));   // border > R라 모서리 보존
        return sRounded;
    }
}
