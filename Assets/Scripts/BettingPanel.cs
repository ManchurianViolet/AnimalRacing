using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 베팅 패널 (금액 베팅판).
/// 출전표(배당/게이지) + 드래그 2존 + 금액 입력 + 적중 수령액 + 확정.
/// 규칙: 우승픽/꼴등픽 둘 다 필수, 각 최소 $1, 합계 ≤ 보유금.
/// 쓰기는 MatchManager.SubmitBet 단일 관문만 사용.
/// </summary>
public class BettingPanel : MonoBehaviour
{
    [Header("씬 레퍼런스")]
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private RaceManager raceManager;

    [Header("출전표")]
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private Transform rowsParent;
    [SerializeField] private BetRowView rowPrefab;

    [Header("존 + 금액")]
    [SerializeField] private BetDropZone zoneFirst;        // 왕관
    [SerializeField] private BetDropZone zoneLast;         // 똥
    [SerializeField] private ZoneAmountView amountFirst;   // 왕관 존 금액 위젯
    [SerializeField] private ZoneAmountView amountLast;

    [Header("하단")]
    [SerializeField] private TMP_Text balanceText;         // 보유 $
    [SerializeField] private Button confirmButton;
    [SerializeField] private TMP_Text confirmLabel;        // 버튼 안 텍스트
    [SerializeField] private TMP_Text statusText;

    private readonly List<BetRowView> rows = new();
    private int playerId;
    private System.Action onClose;
    private PlayerState Me
    {
        get
        {
            foreach (var p in matchManager.Players)
                if (p.PlayerId == playerId) return p;
            return null;
        }
    }

    private int amtFirst = 0;   // 0 = 미입력
    private int amtLast = 0;

    private void Awake()
    {
        gameObject.SetActive(false);
        confirmButton.onClick.AddListener(Confirm);
        zoneFirst.onChanged = () => HandleZoneChanged(zoneFirst, zoneLast);
        zoneLast.onChanged  = () => HandleZoneChanged(zoneLast, zoneFirst);

        amountFirst.onAmountChanged = v => ChangeAmount(true, v);
        amountLast.onAmountChanged  = v => ChangeAmount(false, v);
    }

    public bool IsOpen => gameObject.activeSelf;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    public void Open(int playerId, System.Action onClose)
    {
        this.playerId = playerId;
        this.onClose = onClose;
        gameObject.SetActive(true);

        amtFirst = amtLast = 0;   // 미입력 상태로 시작 (플레이스홀더 노출)
        BuildRows();
        Refresh();
    }

    private void BuildRows()
    {
        foreach (var r in rows) if (r != null) Destroy(r.gameObject);
        rows.Clear();
        zoneFirst.Clear(notify: false);
        zoneLast.Clear(notify: false);

        var odds = matchManager.CurrentOdds;
        foreach (var racer in raceManager.Racers)
        {
            var row = Instantiate(rowPrefab, rowsParent);
            OddsCalculator.AnimalOdds? o =
                (odds != null && racer.RacerId < odds.Length) ? odds[racer.RacerId] : null;
            row.Bind(racer, rootCanvas, o);
            rows.Add(row);
        }
    }

    private void HandleZoneChanged(BetDropZone changed, BetDropZone other)
    {
        if (changed.SelectedId >= 0 && changed.SelectedId == other.SelectedId)
            other.Clear(notify: false);
        Refresh();
    }

    private void ChangeAmount(bool isFirst, int value)
    {
        if (isFirst) amtFirst = value; else amtLast = value;
        ClampAmounts(prioritizeFirst: isFirst);
        Refresh();
    }

    /// <summary>입력된 값만 $10 단위 반올림 + 최소 $10 + 합계 ≤ 보유금. 0 = 미입력 유지.</summary>
    private void ClampAmounts(bool prioritizeFirst = true)
    {
        int money = Me != null ? Me.Money : 0;
        amtFirst = Normalize(amtFirst);
        amtLast = Normalize(amtLast);

        if (prioritizeFirst)
        {
            int otherMin = amtLast > 0 ? amtLast : 0;
            if (amtFirst > 0) amtFirst = Mathf.Min(amtFirst, FloorTo10(money - otherMin));
            if (amtLast > 0)  amtLast  = Mathf.Min(amtLast,  FloorTo10(money - amtFirst));
        }
        else
        {
            int otherMin = amtFirst > 0 ? amtFirst : 0;
            if (amtLast > 0)  amtLast  = Mathf.Min(amtLast,  FloorTo10(money - otherMin));
            if (amtFirst > 0) amtFirst = Mathf.Min(amtFirst, FloorTo10(money - amtLast));
        }
    }

    /// <summary>0 이하 = 미입력(0). 양수는 $10 단위 반올림, 최소 $10.</summary>
    private static int Normalize(int v) =>
        v <= 0 ? 0 : Mathf.Max(10, Mathf.RoundToInt(v / 10f) * 10);

    private static int FloorTo10(int v) => Mathf.Max(10, (v / 10) * 10);

    private void Refresh()
    {
        var me = Me;
        int money = me != null ? me.Money : 0;
        var odds = matchManager.CurrentOdds;

        bool firstSet = zoneFirst.SelectedId >= 0;
        bool lastSet  = zoneLast.SelectedId >= 0;

        // 출전표 배당 하이라이트: 우승픽=노랑, 꼴등픽=갈색
        foreach (var row in rows)
        {
            if (row == null) continue;
            row.SetHighlight(
                firstSet && row.RacerId == zoneFirst.SelectedId,
                lastSet && row.RacerId == zoneLast.SelectedId);
        }

        // 금액 위젯은 존이 채워졌을 때만
        amountFirst.gameObject.SetActive(firstSet);
        amountLast.gameObject.SetActive(lastSet);

        float oddsF = firstSet && odds != null ? odds[zoneFirst.SelectedId].winOdds : 1f;
        float oddsL = lastSet && odds != null ? odds[zoneLast.SelectedId].lastOdds : 1f;
        if (firstSet) amountFirst.SetView(amtFirst, oddsF);
        if (lastSet)  amountLast.SetView(amtLast, oddsL);

        if (balanceText != null)
            balanceText.text = me != null && me.Debt > 0
                ? $"보유 ${money:N0}   <color=#FF6B6B>빚 -${me.Debt:N0}</color>"
                : $"보유 ${money:N0}";

        int total = (firstSet ? amtFirst : 0) + (lastSet ? amtLast : 0);
        bool valid = firstSet && lastSet
                     && zoneFirst.SelectedId != zoneLast.SelectedId
                     && amtFirst >= 10 && amtLast >= 10
                     && total <= money;
        confirmButton.interactable = valid;

        if (confirmLabel != null)
            confirmLabel.text = "베팅하기";

        if (statusText != null)
            statusText.text = !firstSet && !lastSet ? "행을 예상 칸으로 드래그하세요 (양쪽 모두 필수)"
                            : !firstSet ? "우승 예상을 채우세요"
                            : !lastSet ? "꼴등 예상을 채우세요"
                            : amtFirst < 10 || amtLast < 10 ? "베팅 금액을 입력하세요 ($10 단위)"
                            : total > money ? "보유 금액이 부족합니다"
                            : "베팅하기를 눌러 확정하세요 (칸 클릭 = 취소)";
    }

    private void Confirm()
    {
        var ticket = new BetTicket
        {
            firstId = zoneFirst.SelectedId,
            lastId = zoneLast.SelectedId,
            firstAmount = amtFirst,
            lastAmount = amtLast
        };

        if (matchManager.SubmitBet(playerId, ticket))
            Close();
        else if (statusText != null)
            statusText.text = "제출 실패 (시간 초과 또는 잔액 부족)";
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
