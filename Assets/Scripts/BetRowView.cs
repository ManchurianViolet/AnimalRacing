using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 출전표 한 행: [레인번호칸] [아이콘] [이름] [우승배당] [꼴등배당]
/// 선택 시 해당 배당 숫자가 색으로 표시됨 (우승픽=노랑, 꼴등픽=갈색).
/// </summary>
public class BetRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text laneLabel;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private DraggableBetIcon icon;
    [SerializeField] private RectTransform ghostTemplate;
    [SerializeField] private Image iconImage;

    private static readonly Color[] SlotColors =
    {
        new Color32(0xFF, 0xD7, 0x00, 0xFF),   // 1등 금
        new Color32(0xC0, 0xC0, 0xC0, 0xFF),   // 2등 은
        new Color32(0xCD, 0x7F, 0x32, 0xFF),   // 3등 동
    };

    public int RacerId { get; private set; } = -1;

    private Color nameDefaultColor;
    private bool colorsCached;

    public void Bind(Racer racer, Canvas rootCanvas)
    {
        var def = racer.Definition;
        RacerId = racer.RacerId;

        laneLabel.text = (racer.RacerId + 1).ToString();
        nameLabel.text = def.displayName;
        icon.Init(racer.RacerId, def.displayName, rootCanvas, ghostTemplate);

        if (iconImage != null && def.icon != null)
            iconImage.sprite = def.icon;

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
}
