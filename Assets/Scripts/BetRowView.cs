using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 출전표 한 행: [번호] [아이콘] [이름] — 드래그로 예측 슬롯에 배치,
/// 짧은 클릭으로 상세 안내 팝업. 선택 시 이름이 금/은/동으로 표시.
/// </summary>
public class BetRowView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TMP_Text laneLabel;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private DraggableBetIcon icon;
    [SerializeField] private RectTransform ghostTemplate;
    [SerializeField] private Image iconImage;

    [Tooltip("번호 배지 배경 — 등번호 색(RacerColors)으로 칠해 전광판·미니맵과 같은 색 언어를 쓴다")]
    [SerializeField] private Image laneBadge;
    [Tooltip("아이콘 칸 — 아직 초상화가 없는 동물은 빈 사각형이 남으므로 통째로 숨긴다")]
    [SerializeField] private GameObject iconSlot;

    private static readonly Color[] SlotColors =
    {
        new Color32(0xFF, 0xD7, 0x00, 0xFF),   // 1등 금
        new Color32(0xC0, 0xC0, 0xC0, 0xFF),   // 2등 은
        new Color32(0xCD, 0x7F, 0x32, 0xFF),   // 3등 동
    };

    public int RacerId { get; private set; } = -1;

    private Racer racer;
    private AnimalInfoPopup infoPopup;

    private Color nameDefaultColor;
    private bool colorsCached;

    public void Bind(Racer racer, Canvas rootCanvas, AnimalInfoPopup infoPopup)
    {
        var def = racer.Definition;
        RacerId = racer.RacerId;
        this.racer = racer;
        this.infoPopup = infoPopup;

        int post = racer.RacerId + 1;
        laneLabel.text = post.ToString();
        nameLabel.text = def.displayName;
        icon.Init(racer.RacerId, def.displayName, rootCanvas, ghostTemplate);

        // 번호 배지는 등번호판/전광판/미니맵과 같은 팔레트를 쓴다 (단일 출처)
        if (laneBadge != null) laneBadge.color = RacerColors.Of(post);
        laneLabel.color = RacerColors.TextOn(post);

        // 초상화가 없는 동물은 아이콘 칸이 빈 사각형으로 남으므로 칸째로 감춘다
        bool hasIcon = def.icon != null;
        if (iconSlot != null) iconSlot.SetActive(hasIcon);
        if (hasIcon && iconImage != null) iconImage.sprite = def.icon;

        if (!colorsCached)
        {
            if (nameLabel != null) nameDefaultColor = nameLabel.color;
            colorsCached = true;
        }

        SetHighlight(-1);
    }

    /// <summary>패널이 선택 상태에 맞춰 호출. slot: -1=없음, 0=1등(금), 1=2등(은), 2=3등(동).</summary>
    public void SetHighlight(int slot)
    {
        if (nameLabel == null) return;
        nameLabel.color = slot >= 0 && slot < SlotColors.Length ? SlotColors[slot] : nameDefaultColor;
        nameLabel.fontStyle = slot >= 0 ? FontStyles.Bold : FontStyles.Normal;
    }

    /// <summary>짧은 클릭 = 상세 안내 팝업. 드래그 시엔 발동 안 함 (EventSystem이 클릭 무효화).</summary>
    public void OnPointerClick(PointerEventData e)
    {
        if (infoPopup != null && racer != null) infoPopup.Show(racer);
    }
}
