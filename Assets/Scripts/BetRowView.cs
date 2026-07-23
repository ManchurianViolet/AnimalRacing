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

    [Header("배당")]
    [SerializeField] private TMP_Text winOddsText;
    [SerializeField] private TMP_Text lastOddsText;

    private static readonly Color WinPickColor  = new Color32(0xFF, 0xD7, 0x00, 0xFF); // 노랑
    private static readonly Color LastPickColor = new Color32(0xC9, 0x8D, 0x4B, 0xFF); // 갈색

    public int RacerId { get; private set; } = -1;

    private Color winDefaultColor;
    private Color lastDefaultColor;
    private bool colorsCached;

    public void Bind(Racer racer, Canvas rootCanvas, OddsCalculator.AnimalOdds? odds)
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
            if (winOddsText != null) winDefaultColor = winOddsText.color;
            if (lastOddsText != null) lastDefaultColor = lastOddsText.color;
            colorsCached = true;
        }

        if (winOddsText != null)
            winOddsText.text = odds.HasValue ? $"×{odds.Value.winOdds:F1}" : "—";
        if (lastOddsText != null)
            lastOddsText.text = odds.HasValue ? $"×{odds.Value.lastOdds:F1}" : "—";

        SetHighlight(false, false);
    }

    /// <summary>패널이 선택 상태에 맞춰 호출. 우승픽=노랑, 꼴등픽=갈색.</summary>
    public void SetHighlight(bool pickedAsWin, bool pickedAsLast)
    {
        if (winOddsText != null)
            winOddsText.color = pickedAsWin ? WinPickColor : winDefaultColor;
        if (lastOddsText != null)
            lastOddsText.color = pickedAsLast ? LastPickColor : lastDefaultColor;
    }
}
