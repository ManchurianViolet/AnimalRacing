using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정산판 (올림픽식):
/// 라운드 정산 = 1위부터 순차 슬라이드 인, 각 동물 행에 베팅 칩(왕관/똥 + 금액 + 수령액).
/// 매치 종료 = 최종 정산값 순위 (1위 금색).
/// 다음 베팅/카운트다운 시작 시 자동 숨김. 순수 표현 레이어 (이벤트 구독만).
/// </summary>
public class SettlementPanel : MonoBehaviour
{
    [Header("씬 레퍼런스")]
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private RaceManager raceManager;

    [Header("UI 레퍼런스")]
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Transform rowsParent;          // VerticalLayoutGroup
    [SerializeField] private ResultRowView rowPrefab;
    [SerializeField] private BetChipView chipPrefab;
    [SerializeField] private TMP_Text finalText;            // 최종 결과 전용 (라운드 땐 숨김)

    [Header("스프라이트")]
    [SerializeField] private Sprite crownSprite;            // 왕관 (우승픽)
    [SerializeField] private Sprite pooSprite;              // 똥 (꼴등픽)

    [Header("연출")]
    [SerializeField] private float rowInterval = 0.28f;     // 행 등장 간격

    private readonly List<ResultRowView> rows = new();
    private Coroutine playRoutine;

    private void OnEnable()
    {
        GameEvents.OnRaceSettled  += HandleSettled;
        GameEvents.OnMatchEnded   += HandleMatchEnded;
        GameEvents.OnPhaseChanged += HandlePhase;
    }

    private void OnDisable()
    {
        GameEvents.OnRaceSettled  -= HandleSettled;
        GameEvents.OnMatchEnded   -= HandleMatchEnded;
        GameEvents.OnPhaseChanged -= HandlePhase;
    }

    private void Start() => root.SetActive(false);

    private void HandlePhase(GamePhase p)
    {
        if (p == GamePhase.Betting || p == GamePhase.Countdown)
        {
            if (playRoutine != null) StopCoroutine(playRoutine);
            root.SetActive(false);
        }
    }

    // ================= 라운드 정산 =================

    private void HandleSettled(RaceResult r)
    {
        root.SetActive(true);
        if (finalText != null) finalText.gameObject.SetActive(false);
        rowsParent.gameObject.SetActive(true);
        titleText.text = $"ROUND {r.round} — 결과";

        ClearRows();

        // 레이아웃 그룹 재가동 (이전 라운드에서 동결했을 수 있음)
        var layout = rowsParent.GetComponent<VerticalLayoutGroup>();
        if (layout != null) layout.enabled = true;

        BuildRows(r);

        if (playRoutine != null) StopCoroutine(playRoutine);
        playRoutine = StartCoroutine(PlaySequence());
    }

    private void ClearRows()
    {
        foreach (var row in rows) if (row != null) Destroy(row.gameObject);
        rows.Clear();
    }

    private void BuildRows(RaceResult r)
    {
        var ranking = raceManager.GetFinalRanking();
        var odds = matchManager.CurrentOdds;

        foreach (var racer in ranking)
        {
            bool isTop = racer.FinishRank == 1;
            bool isLast = racer.FinishRank == ranking.Count;

            var row = Instantiate(rowPrefab, rowsParent);
            row.Bind(racer.FinishRank, racer, isTop, isLast);

            // 이 동물에게 걸었던 플레이어들의 칩 부착
            foreach (var p in matchManager.Players)
            {
                if (p.Bet.firstId == racer.RacerId && p.Bet.firstAmount > 0)
                {
                    bool hit = racer.RacerId == r.firstId;
                    int pay = hit && odds != null
                        ? Mathf.FloorToInt(p.Bet.firstAmount * odds[racer.RacerId].winOdds) : 0;
                    AddChip(row, crownSprite, p.Nickname, p.Bet.firstAmount, hit, pay);
                }
                if (p.Bet.lastId == racer.RacerId && p.Bet.lastAmount > 0)
                {
                    bool hit = racer.RacerId == r.lastId;
                    int pay = hit && odds != null
                        ? Mathf.FloorToInt(p.Bet.lastAmount * odds[racer.RacerId].lastOdds) : 0;
                    AddChip(row, pooSprite, p.Nickname, p.Bet.lastAmount, hit, pay);
                }
            }

            rows.Add(row);
        }
    }

    private void AddChip(ResultRowView row, Sprite icon, string name, int amount, bool hit, int payout)
    {
        var chip = Instantiate(chipPrefab, row.ChipContainer);
        chip.Bind(icon, name, amount, hit, payout);
    }

    private IEnumerator PlaySequence()
    {
        // 1프레임 대기 + 레이아웃 강제 확정 → 각 행의 최종 위치가 정해짐
        yield return null;
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)rowsParent);

        // 레이아웃 그룹 동결: 연출 중 위치 재계산(경합) 방지
        var layout = rowsParent.GetComponent<VerticalLayoutGroup>();
        if (layout != null) layout.enabled = false;

        foreach (var row in rows)
        {
            StartCoroutine(row.Appear());
            yield return new WaitForSeconds(rowInterval);
        }
        playRoutine = null;
    }

    // ================= 최종 결과 =================

    private void HandleMatchEnded()
    {
        root.SetActive(true);
        rowsParent.gameObject.SetActive(false);
        if (finalText == null) return;

        finalText.gameObject.SetActive(true);
        titleText.text = "최종 결과";

        var ordered = new List<PlayerState>(matchManager.Players);
        ordered.Sort((a, b) => b.NetWorth.CompareTo(a.NetWorth));

        var sb = new System.Text.StringBuilder();
        int rank = 1;
        foreach (var p in ordered)
        {
            if (rank == 1) sb.Append("<b><color=#FFD700>");
            sb.Append(rank).Append("위   ").Append(p.Nickname)
              .Append("   $").Append(p.NetWorth.ToString("N0"));
            if (rank == 1) sb.Append("   우승!</color></b>");
            sb.Append('\n');
            rank++;
        }
        finalText.text = sb.ToString();
    }
}
