using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아이템 슬롯 하나 (4칸 공용: 빠따/부스트/감속/무전기).
/// 표시: 이름, 개수(아이템 슬롯만), 쿨다운 게이지(Filled Image), "들고 있음" 하이라이트.
/// 클릭으로도 선택 가능 (Button 연결 시).
/// </summary>
public class ItemSlotView : MonoBehaviour
{
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text countLabel;
    [Tooltip("Image Type=Filled 로 설정. 쿨다운 남은 비율만큼 채워짐")]
    [SerializeField] private Image cooldownFill;
    [Tooltip("들고 있을 때 켜지는 테두리/배경 오브젝트")]
    [SerializeField] private GameObject selectedFrame;
    [SerializeField] private TMP_Text hotkeyLabel;   // "1"~"4" (선택)
    [Tooltip("빠따 슬롯 전용 내구도 게이지 (Image Type=Filled). 잔량 비율만큼 채워지고 " +
             "GameConfig의 warn/danger 비율에서 초록→주황→빨강으로 바뀐다. 다른 슬롯은 비워둠")]
    [SerializeField] private Image durabilityFill;
    [Tooltip("다 쓴 소모형 슬롯의 투명도")]
    [SerializeField] private float emptyAlpha = 0.35f;
    [Tooltip("다 썼지만 커서가 올라가 있는 슬롯의 투명도 — 노란 테두리가 읽힐 만큼은 밝아야 한다")]
    [SerializeField] private float emptySelectedAlpha = 0.75f;
    [Tooltip("아이템 그림 — 비워두면 Init 때 자동 생성 (칸 중앙, 글자들 뒤). 소모형은 item.icon, " +
             "빠따처럼 ItemDefinition 없는 슬롯은 아래 fixedIcon을 쓴다. 그림이 있으면 이름 글자는 숨김")]
    [SerializeField] private Image iconImage;
    [Tooltip("ItemDefinition 없는 슬롯(빠따) 전용 아이콘 스프라이트")]
    [SerializeField] private Sprite fixedIcon;
    [Tooltip("아이콘이 칸 가장자리에서 띄우는 여백 (px)")]
    [SerializeField] private float iconPadding = 12f;

    private PlayerItemController controller;
    private ItemDefinition item;     // 주사기 슬롯만 사용, 빠따/무전기는 null
    private string nameKey;          // 아이템 없는 슬롯(빠따/무전기)의 이름 키 (strings.csv)
    private int slotIndex;

    /// <summary>
    /// slot: PlayerEquipment.Slot* 번호. item은 소모형 슬롯(주사기)만, 나머지는 null.
    /// [로컬라이제이션] nameKey는 완성 문자열이 아니라 키("item.bat") — 언어가 바뀌면
    /// 여기서 다시 조회해 갈아끼운다 (완성 문자열을 받으면 전환 때 옛 언어로 굳는다).
    /// </summary>
    public void Init(PlayerItemController controller, int slot, ItemDefinition item, string nameKey, string hotkey)
    {
        this.controller = controller;
        this.item = item;
        this.nameKey = nameKey;
        slotIndex = slot;
        SetupIcon();
        RefreshName();
        if (hotkeyLabel != null) hotkeyLabel.text = hotkey;

        var btn = GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(() => controller.SelectSlot(slot));
    }

    /// <summary>
    /// 아이콘 표시 — 스프라이트가 있으면 칸 중앙에 그림, 이름 글자는 숨긴다 (그림이 이름을 대신).
    /// 아이콘 Image가 씬에 없으면 코드로 생성 (첫 자식 = 배경 위·글자/게이지 뒤).
    /// </summary>
    private void SetupIcon()
    {
        Sprite sprite = item != null && item.icon != null ? item.icon : fixedIcon;
        if (sprite == null)
        {
            if (iconImage != null) iconImage.gameObject.SetActive(false);
            return;
        }

        if (iconImage == null)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);

            // 렌더 순서: 내구도 게이지(초록 채움) 바로 위 — 게이지가 그림의 "배경"이 되고,
            // 쿨다운 오버레이·글자들은 그림 위에 남는다 (유저 지적: 초록이 빠따를 덮었음)
            int idx = 0;
            if (durabilityFill != null && durabilityFill.transform.parent == transform)
                idx = durabilityFill.transform.GetSiblingIndex() + 1;
            else if (selectedFrame != null && selectedFrame.transform.parent == transform)
                idx = selectedFrame.transform.GetSiblingIndex() + 1;
            rt.SetSiblingIndex(idx);

            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(iconPadding, iconPadding + 10f);   // 아래는 이름 띠 자리
            rt.offsetMax = new Vector2(-iconPadding, -iconPadding);
            iconImage = go.GetComponent<Image>();
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
        }
        iconImage.gameObject.SetActive(true);
        iconImage.sprite = sprite;
        // 이름 글자는 유지 — 하단 띠에 그림과 함께 표시 (유저 지적: 뭔지 알려면 글자 필요)
    }

    /// <summary>
    /// 슬롯은 HUD가 페이즈/베팅방 상태에 따라 SetActive를 수시로 토글한다 —
    /// 꺼진 동안 언어가 바뀌면 이벤트 구독으로는 놓치므로, Update에서 값이 다를 때만
    /// 갈아끼운다 (같으면 TMP를 안 건드림 — 커마 패널 규칙).
    /// </summary>
    private void RefreshName()
    {
        if (nameLabel == null) return;
        string want = item != null ? item.LocalizedName
                    : !string.IsNullOrEmpty(nameKey) ? Loc.Get(nameKey) : null;
        if (want != null && nameLabel.text != want) nameLabel.text = want;
    }

    private void Update()
    {
        if (controller == null) return;

        RefreshName();   // 언어 전환 대응

        if (countLabel != null)
            countLabel.text = item != null ? $"×{controller.CountOf(item)}" : "";

        if (cooldownFill != null)
            cooldownFill.fillAmount = (item != null && controller.Me != null) ? controller.Me.CooldownRatio : 0f;

        // 하이라이트 기준은 "손에 든 것"이 아니라 "커서 위치" — 재고가 떨어진 칸을 고르면
        // 손은 빈손이 되지만 테두리는 그 칸에 남아 있어야 지금 몇 번인지 알 수 있다
        bool selected = controller.CursorSlot == slotIndex;
        if (selectedFrame != null)
            selectedFrame.SetActive(selected);

        // 빠따 내구도 게이지 — 잔량 비율만큼 채우고, 문턱값 아래로 내려가면 색 경고
        if (durabilityFill != null && PlayerEquipment.Local != null)
        {
            float ratio = PlayerEquipment.Local.BatDurabilityRatio;
            durabilityFill.fillAmount = ratio;
            var cfg = GameManager.Instance != null ? GameManager.Instance.Config : null;
            if (cfg != null)
                durabilityFill.color = ratio <= cfg.batGaugeDangerRatio ? cfg.batGaugeColorDanger
                                     : ratio <= cfg.batGaugeWarnRatio ? cfg.batGaugeColorWarn
                                     : cfg.batGaugeColorFull;
        }

        // 소모형 슬롯은 다 쓰면 흐리게 (빠따/무전기는 항상 또렷).
        // 단 커서가 올라간 칸은 덜 흐리게 — CanvasGroup은 테두리까지 같이 흐려서
        // 0.35로 두면 "지금 여기"가 안 읽힌다
        var cg = GetComponent<CanvasGroup>();
        if (cg != null)
        {
            bool empty = item != null && controller.CountOf(item) <= 0;
            cg.alpha = !empty ? 1f : (selected ? emptySelectedAlpha : emptyAlpha);
        }
    }
}
