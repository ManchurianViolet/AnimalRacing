using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 예측 패널: 출전표 + 드래그 3존 (1등/2등/3등 예상) + 확정.
/// 규칙: 세 슬롯 전부 필수, 서로 다른 동물. 금액 없음 — 적중 시 고정 포인트.
/// 쓰기는 MatchManager.SubmitBet(관문)만 사용 [멀티: gateway 경유].
/// </summary>
public class BettingPanel : MonoBehaviour
{
    [Header("씬 레퍼런스")]
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private NetworkGateway gateway;

    [Header("출전표")]
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private Transform rowsParent;
    [SerializeField] private BetRowView rowPrefab;

    [Header("예측 존 (1등/2등/3등)")]
    [SerializeField] private BetDropZone zoneFirst;
    [SerializeField] private BetDropZone zoneSecond;
    [SerializeField] private BetDropZone zoneThird;

    [Header("안내판 팝업")]
    [SerializeField] private AnimalInfoPopup infoPopup;

    [Header("하단")]
    [SerializeField] private TMP_Text balanceText;         // 내 포인트
    [SerializeField] private Button confirmButton;
    [SerializeField] private TMP_Text confirmLabel;
    [SerializeField] private TMP_Text statusText;

    private readonly List<BetRowView> rows = new();
    private int playerId;
    private System.Action onClose;
    private BetDropZone[] zones;

    private PlayerState Me
    {
        get
        {
            foreach (var p in matchManager.Players)
                if (p.PlayerId == playerId) return p;
            return null;
        }
    }

    private void Awake()
    {
        gameObject.SetActive(false);
        confirmButton.onClick.AddListener(Confirm);

        zones = new[] { zoneFirst, zoneSecond, zoneThird };
        foreach (var z in zones)
        {
            var captured = z;
            captured.onChanged = () => HandleZoneChanged(captured);
        }
    }

    public bool IsOpen => gameObject.activeSelf;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // 팝업이 열려 있으면 팝업만 닫고, 아니면 패널을 닫는다
            if (infoPopup != null && infoPopup.IsOpen) infoPopup.Hide();
            else Close();
        }
    }

    public void Open(int playerId, System.Action onClose)
    {
        this.playerId = playerId;
        this.onClose = onClose;
        gameObject.SetActive(true);
        if (infoPopup != null) infoPopup.Hide();   // 지난 세션 잔상 방지
        BuildRows();
        Refresh();
    }

    private void BuildRows()
    {
        foreach (var r in rows) if (r != null) Destroy(r.gameObject);
        rows.Clear();
        foreach (var z in zones) z.Clear(notify: false);

        foreach (var racer in raceManager.Racers)
        {
            var row = Instantiate(rowPrefab, rowsParent);
            row.Bind(racer, rootCanvas, infoPopup);
            rows.Add(row);
        }
    }

    /// <summary>같은 동물이 다른 존에 들어가면 기존 존을 비움 (한 동물 = 한 슬롯).</summary>
    private void HandleZoneChanged(BetDropZone changed)
    {
        if (changed.SelectedId >= 0)
            foreach (var z in zones)
                if (z != changed && z.SelectedId == changed.SelectedId)
                    z.Clear(notify: false);
        Refresh();
    }

    private void Refresh()
    {
        var cfg = GameManager.Instance.Config;
        var me = Me;

        // 출전표 하이라이트: 1등=금, 2등=은, 3등=동
        foreach (var row in rows)
        {
            if (row == null) continue;
            int slot = row.RacerId == zoneFirst.SelectedId ? 0
                     : row.RacerId == zoneSecond.SelectedId ? 1
                     : row.RacerId == zoneThird.SelectedId ? 2 : -1;
            row.SetHighlight(slot);
        }

        if (balanceText != null)
            balanceText.text = me != null ? $"내 포인트  {me.Points:N0} P" : "";

        bool valid = zoneFirst.SelectedId >= 0
                  && zoneSecond.SelectedId >= 0
                  && zoneThird.SelectedId >= 0;
        confirmButton.interactable = valid;

        if (confirmLabel != null) confirmLabel.text = "예측 확정";

        int filled = 0;
        foreach (var z in zones) if (z.SelectedId >= 0) filled++;

        if (statusText != null)
            statusText.text = valid
                ? $"적중 시: 1등 +{cfg.pointsFirst} · 2등↑ +{cfg.pointsSecond} · 3등↑ +{cfg.pointsThird}"
                : $"동물을 예상 칸으로 드래그하세요 ({filled}/3)";
    }

    private void Confirm()
    {
        var ticket = new BetTicket
        {
            firstId = zoneFirst.SelectedId,
            secondId = zoneSecond.SelectedId,
            thirdId = zoneThird.SelectedId
        };

        confirmButton.interactable = false;
        if (statusText != null) statusText.text = "제출 중...";

        gateway.RequestSubmitBet(ticket, ok =>
        {
            if (ok) Close();
            else
            {
                confirmButton.interactable = true;
                if (statusText != null) statusText.text = "제출 실패 (시간 초과)";
            }
        });
    }

    public void Close()
    {
        if (infoPopup != null) infoPopup.Hide();
        gameObject.SetActive(false);
        var cb = onClose; onClose = null;
        cb?.Invoke();
    }

    public void ForceClose()
    {
        if (IsOpen) Close();
    }
}
