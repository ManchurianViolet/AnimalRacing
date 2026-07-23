using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ATM 패널: 대출 $300/$500 + 상환 $100/전액.
/// 로직은 MatchManager.TryAtmLoan/TryRepay 관문 사용, 여기는 표시/거절 사유만.
/// </summary>
public class BankPanel : MonoBehaviour
{
    [SerializeField] private MatchManager matchManager;

    [Header("표시")]
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private TMP_Text debtText;
    [SerializeField] private TMP_Text infoText;      // 안내/거절 사유

    [Header("버튼")]
    [SerializeField] private Button btnLoan300;
    [SerializeField] private Button btnLoan500;
    [SerializeField] private Button btnRepay100;
    [SerializeField] private Button btnRepayAll;
    [SerializeField] private Button btnClose;

    private int playerId;
    private System.Action onClose;

    private PlayerState Me
    {
        get
        {
            if (matchManager == null) return null;
            foreach (var p in matchManager.Players)
                if (p.PlayerId == playerId) return p;
            return null;
        }
    }

    private void Awake()
    {
        gameObject.SetActive(false);
        if (matchManager == null)
            Debug.LogError("[BankPanel] Match Manager 슬롯이 비어있습니다! 인스펙터에서 연결하세요.");
        btnLoan300.onClick.AddListener(() => Loan(GameManager.Instance.Config.atmLoanSmall));
        btnLoan500.onClick.AddListener(() => Loan(GameManager.Instance.Config.atmLoanLarge));
        btnRepay100.onClick.AddListener(() => Repay(100));
        btnRepayAll.onClick.AddListener(() => Repay(int.MaxValue));
        if (btnClose != null) btnClose.onClick.AddListener(Close);
    }

    public bool IsOpen => gameObject.activeSelf;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
        RefreshTexts();
    }

    public void Open(int playerId, System.Action onClose)
    {
        this.playerId = playerId;
        this.onClose = onClose;
        gameObject.SetActive(true);
        var cfg = GameManager.Instance.Config;
        if (infoText != null)
            infoText.text = $"고금리 대출 (라운드당 이자 {cfg.interestRate:P0} 복리)\n" +
                            $"자격: {cfg.atmAvailableFromRound}라운드부터 · 총 자산 ${cfg.atmLoanThreshold} 미만";
        RefreshTexts();
    }

    private void RefreshTexts()
    {
        var me = Me;
        if (me == null) return;
        if (moneyText != null) moneyText.text = $"보유  ${me.Money:N0}";
        if (debtText != null)
            debtText.text = me.Debt > 0
                ? $"<color=#FF6B6B>빚  -${me.Debt:N0}</color>"
                : "빚 없음";
    }

    private void Loan(int amount)
    {
        if (matchManager.TryAtmLoan(playerId, amount))
        {
            SetInfo($"<color=#5DCAA5>${amount:N0} 대출 완료.</color> 정산 전에 갚을수록 이자가 적습니다");
            return;
        }
        SetInfo($"<color=#FF6B6B>대출 불가:</color> {DenialReason(amount)}");
    }

    private string DenialReason(int amount)
    {
        var cfg = GameManager.Instance.Config;
        var me = Me;
        if (matchManager.CurrentRound < cfg.atmAvailableFromRound)
            return $"{cfg.atmAvailableFromRound}라운드부터 이용 가능";
        if (me.NetWorth >= cfg.atmLoanThreshold)
            return $"총 자산 ${cfg.atmLoanThreshold} 미만만 가능";
        if (me.BorrowedThisRound)
            return "라운드당 1회만 가능";
        if (me.TotalBorrowed + amount > cfg.totalBorrowLimit)
            return $"누적 대출 한도(${cfg.totalBorrowLimit:N0}) 초과";
        return "조건 미충족";
    }

    private void Repay(int amount)
    {
        int paid = matchManager.TryRepay(playerId, amount);
        SetInfo(paid > 0
            ? $"<color=#5DCAA5>${paid:N0} 상환 완료</color>"
            : "<color=#FF6B6B>상환 불가:</color> 갚을 빚이 없거나 잔액 부족");
    }

    private void SetInfo(string msg)
    {
        if (infoText != null) infoText.text = msg;
        RefreshTexts();
    }

    public void Close()
    {
        gameObject.SetActive(false);
        var cb = onClose; onClose = null;
        cb?.Invoke();
    }

    public void ForceClose()
    {
        if (IsOpen) Close();
    }
}
