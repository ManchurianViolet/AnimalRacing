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

    [Header("모양")]
    [SerializeField] private Vector2 panelSize = new Vector2(620f, 700f);
    [SerializeField] private float rowHeight = 54f;
    [SerializeField] private Color panelColor = new Color(0.06f, 0.06f, 0.08f, 0.92f);
    [SerializeField] private Color buttonColor = new Color(0.18f, 0.18f, 0.22f, 1f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private int titleSize = 34;
    [SerializeField] private int labelSize = 24;

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
        SetMenuVisible(false);
        snapshot = target != null ? target.Encode() : "";
        RefreshAll();
    }

    public void Close()
    {
        SetMenuVisible(true);
        gameObject.SetActive(false);
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
        rt.sizeDelta = panelSize;

        var bg = gameObject.GetComponent<Image>();
        if (bg == null) bg = gameObject.AddComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = true;   // 뒤쪽 버튼 클릭 차단

        MakeText("Title", rt, new Vector2(0f, -18f), new Vector2(panelSize.x - 40f, 44f),
            "커스터마이징", titleSize, TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

        if (target == null || target.Library == null)
        {
            MakeText("Warn", rt, Vector2.zero, new Vector2(panelSize.x - 40f, 60f),
                "부위 라이브러리가 연결되지 않았습니다", labelSize, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            return;
        }

        // ---- 슬롯 행 ----
        var slots = target.Library.slots;
        float y = -80f;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].parts == null || slots[i].parts.Length == 0) continue;   // 무료팩에 없는 부위는 숨김
            BuildRow(rt, i, slots[i].displayName, y);
            y -= rowHeight;
        }

        // ---- 하단 버튼 ----
        float bw = (panelSize.x - 60f) / 3f;
        MakeButton("Random", rt, new Vector2(20f + bw * 0.5f, 24f), new Vector2(bw - 8f, 52f),
            "랜덤", () => { target.Randomize(); RefreshAll(); },
            new Vector2(0f, 0f), new Vector2(0f, 0f));
        MakeButton("Cancel", rt, new Vector2(30f + bw * 1.5f, 24f), new Vector2(bw - 8f, 52f),
            "취소", Cancel, new Vector2(0f, 0f), new Vector2(0f, 0f));
        MakeButton("Confirm", rt, new Vector2(40f + bw * 2.5f, 24f), new Vector2(bw - 8f, 52f),
            "확정", Confirm, new Vector2(0f, 0f), new Vector2(0f, 0f));
    }

    private void BuildRow(RectTransform parent, int slotIndex, string label, float y)
    {
        var row = new GameObject("Row_" + label, typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(parent, false);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(panelSize.x - 40f, rowHeight - 8f);
        row.anchoredPosition = new Vector2(0f, y);

        float w = row.sizeDelta.x;
        MakeText("Label", row, new Vector2(8f, 0f), new Vector2(w * 0.32f, rowHeight - 12f),
            label, labelSize, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));

        int captured = slotIndex;
        MakeButton("Prev", row, new Vector2(w * 0.34f + 4f, 0f), new Vector2(44f, rowHeight - 14f),
            "◀", () => { target.Cycle(captured, -1); Refresh(captured); },
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));

        var value = MakeText("Value", row, new Vector2(w * 0.5f + 22f, 0f),
            new Vector2(w * 0.34f, rowHeight - 12f), "", labelSize, TextAlignmentOptions.Center,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));

        MakeButton("Next", row, new Vector2(-8f, 0f), new Vector2(44f, rowHeight - 14f),
            "▶", () => { target.Cycle(captured, 1); Refresh(captured); },
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));

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
                            Vector2 anchor, Vector2 pivot)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        var img = go.AddComponent<Image>();
        img.color = buttonColor;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        MakeText("Text", rt, Vector2.zero, size, label, labelSize, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
    }
}
