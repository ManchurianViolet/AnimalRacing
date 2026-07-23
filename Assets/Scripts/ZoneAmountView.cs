using TMPro;
using UnityEngine;

/// <summary>
/// 드롭 존 금액 입력 위젯: 키보드 직접 입력.
/// 미입력 상태에선 플레이스홀더("베팅 금액을 입력하세요")가 보이고,
/// $10 단위 반올림/클램프는 패널이 담당.
/// </summary>
public class ZoneAmountView : MonoBehaviour
{
    [Tooltip("Content Type: Integer Number 로 설정")]
    [SerializeField] private TMP_InputField amountInput;
    [SerializeField] private TMP_Text payoutText;
    [SerializeField] private string placeholderText = "베팅 금액을 입력하세요";

    public System.Action<int> onAmountChanged;

    private void Awake()
    {
        if (amountInput != null)
        {
            amountInput.onEndEdit.AddListener(HandleEndEdit);
            if (amountInput.placeholder is TMP_Text ph)
                ph.text = placeholderText;
        }
    }

    private void HandleEndEdit(string s)
    {
        int.TryParse(s, out int v);
        onAmountChanged?.Invoke(v);
    }

    /// <summary>amount 0 = 미입력 (빈 칸 + 플레이스홀더). 입력 중엔 방해 안 함.</summary>
    public void SetView(int amount, float odds)
    {
        if (amountInput != null && !amountInput.isFocused)
            amountInput.SetTextWithoutNotify(amount > 0 ? amount.ToString() : "");

        if (payoutText != null)
        {
            if (amount > 0)
            {
                int pay = Mathf.FloorToInt(amount * odds);
                payoutText.text = $"적중 시 ${pay:N0}";
            }
            else payoutText.text = "";
        }
    }
}
