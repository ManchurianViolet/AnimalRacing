using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정산판 베팅 칩: [왕관/똥 아이콘] 이름 $금액(→수령액).
/// 적중 = 초록 강조, 빗나감 = 회색.
/// </summary>
public class BetChipView : MonoBehaviour
{
    [SerializeField] private Image betIcon;        // 왕관/똥 스프라이트 자리
    [SerializeField] private TMP_Text label;       // "나 $100 → $310"
    [SerializeField] private Image background;

    private static readonly Color HitBg   = new Color32(0x12, 0x3D, 0x12, 0xFF);
    private static readonly Color MissBg  = new Color32(0x26, 0x26, 0x2C, 0xFF);
    private static readonly Color HitText = new Color32(0x7C, 0xFC, 0x00, 0xFF);
    private static readonly Color MissText= new Color32(0x9A, 0x9A, 0xA0, 0xFF);

    public void Bind(Sprite icon, string playerName, int amount, bool hit, int payout)
    {
        if (betIcon != null && icon != null) betIcon.sprite = icon;

        if (label != null)
        {
            label.text = hit
                ? $"{playerName} ${amount:N0} → <b>${payout:N0}</b>"
                : $"{playerName} ${amount:N0}";
            label.color = hit ? HitText : MissText;
        }
        if (background != null) background.color = hit ? HitBg : MissBg;
    }
}
