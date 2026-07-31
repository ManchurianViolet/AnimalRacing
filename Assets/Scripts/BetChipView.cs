using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정산판 예측 칩: [①/②/③] 이름 (+획득 포인트).
/// 적중 = 초록 강조, 빗나감 = 회색.
/// </summary>
public class BetChipView : MonoBehaviour
{
    [SerializeField] private Image betIcon;        // (구 왕관/똥 자리 — 미사용, 자동 숨김)
    [SerializeField] private TMP_Text label;
    [SerializeField] private Image background;

    private static readonly Color HitBg   = new Color32(0x12, 0x3D, 0x12, 0xFF);
    private static readonly Color MissBg  = new Color32(0x26, 0x26, 0x2C, 0xFF);
    private static readonly Color HitText = new Color32(0x7C, 0xFC, 0x00, 0xFF);
    private static readonly Color MissText= new Color32(0x9A, 0x9A, 0xA0, 0xFF);

    private static readonly string[] SlotMark = { "①", "②", "③" };

    /// <param name="slot">0=1등 예측, 1=2등, 2=3등</param>
    public void Bind(int slot, string playerName, bool hit, int points)
    {
        if (betIcon != null) betIcon.gameObject.SetActive(false);

        string mark = slot >= 0 && slot < 3 ? SlotMark[slot] : "?";
        if (label != null)
        {
            label.text = hit
                ? $"{mark} {playerName} <b>+{points}P</b>"
                : $"{mark} {playerName}";
            label.color = hit ? HitText : MissText;
        }
        if (background != null) background.color = hit ? HitBg : MissBg;
    }
}
