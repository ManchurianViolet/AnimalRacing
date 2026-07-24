using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 매치 순환 + 경제(자동대출/이자/은행) + 베팅 제출 관문 + 배당 스냅샷.
/// UI가 게임 상태를 쓰는 관문: SubmitBet / TryAtmLoan [멀티: 전부 RPC 지점].
/// </summary>
public class MatchManager : MonoBehaviour
{
    [SerializeField] private GameConfig config;
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private OddsSettlement settlement;

    private readonly List<PlayerState> players = new();
    public IReadOnlyList<PlayerState> Players => players;

    public int CurrentRound { get; private set; }
    public int TotalRounds { get; private set; }
    public float PhaseEndTime { get; private set; }

    /// <summary>이번 라운드 배당 (베팅 페이즈 시작 시 계산, 인덱스 = racerId).</summary>
    public OddsCalculator.AnimalOdds[] CurrentOdds { get; private set; }

    private readonly HashSet<int> submitted = new();

    public void RegisterPlayer(PlayerState p)
    {
        players.Add(p);
        GameManager.Instance.SetPlayerCount(players.Count);
    }

    public bool HasSubmitted(int playerId) => submitted.Contains(playerId);

    public PlayerState GetPlayer(int playerId) =>
        players.FirstOrDefault(p => p.PlayerId == playerId);

    /// <summary>[네트워크] 로스터 재구성용 전체 초기화.</summary>
    public void ClearPlayers() => players.Clear();

    /// <summary>[네트워크] 특정 플레이어 제거 (늦은 입장자에게 봇 자리 양보 등).</summary>
    public void RemovePlayer(int playerId)
    {
        players.RemoveAll(p => p.PlayerId == playerId);
        submitted.Remove(playerId);
        GameManager.Instance.SetPlayerCount(players.Count);
    }

    public int[] GetSubmittedIds() => submitted.ToArray();

    /// <summary>[클라] 호스트가 방송한 제출 상태 반영.</summary>
    public void ApplyNetworkSubmitted(int[] ids)
    {
        submitted.Clear();
        foreach (var id in ids) submitted.Add(id);
    }

    // ================= 베팅 관문 =================

    /// <summary>★ 베팅 제출. 검증 + 즉시 차감. 성공 여부 반환.</summary>
    public bool SubmitBet(int playerId, BetTicket ticket)
    {
        if (GameManager.Instance.CurrentPhase != GamePhase.Betting) return false;
        if (!ticket.IsValid(config.racerCount)) return false;
        if (submitted.Contains(playerId)) return false;

        var p = players.FirstOrDefault(x => x.PlayerId == playerId);
        if (p == null) return false;
        if (!p.TrySpend(ticket.Total)) return false;   // 잔액 부족

        p.SetBet(ticket);
        submitted.Add(playerId);
        GameEvents.RaiseBetAccepted(playerId, ticket);
        return true;
    }

    // ================= 은행 관문 =================

    /// <summary>ATM 추가 대출. 자격: 지정 라운드 이후 + 총자산(보유-빚) 기준 미만 + 라운드 1회 + 누적 한도.</summary>
    public bool TryAtmLoan(int playerId, int amount)
    {
        if (CurrentRound < config.atmAvailableFromRound) return false;
        if (amount != config.atmLoanSmall && amount != config.atmLoanLarge) return false;

        var p = players.FirstOrDefault(x => x.PlayerId == playerId);
        if (p == null) return false;
        if (p.NetWorth >= config.atmLoanThreshold) return false;   // 총 자산(보유-빚) 기준
        if (p.BorrowedThisRound) return false;
        if (p.TotalBorrowed + amount > config.totalBorrowLimit) return false;

        p.Borrow(amount);
        p.BorrowedThisRound = true;
        return true;
    }

    // ================= 네트워크 수신 (클라 전용 — NetworkMatchSync가 호출) =================

    /// <summary>[클라] 호스트가 방송한 페이즈 타이머 반영.</summary>
    public void ApplyNetworkPhaseTimer(float remainingSeconds) =>
        PhaseEndTime = Time.time + Mathf.Max(0f, remainingSeconds);

    /// <summary>[클라] 호스트가 방송한 라운드 반영.</summary>
    public void ApplyNetworkRound(int round) => CurrentRound = round;

    /// <summary>[클라] 호스트가 방송한 배당 테이블 반영.</summary>
    public void ApplyNetworkOdds(OddsCalculator.AnimalOdds[] odds) => CurrentOdds = odds;

    // ================= 매치 흐름 =================

    public void StartMatch(int rounds = -1)
    {
        TotalRounds = rounds > 0 ? rounds : config.defaultRounds;
        foreach (var p in players) p.ResetEconomy(config.startMoney);
        StartCoroutine(MatchFlow());
    }

    private IEnumerator MatchFlow()
    {
        var gm = GameManager.Instance;

        for (int round = 1; round <= TotalRounds; round++)
        {
            CurrentRound = round;
            GameEvents.RaiseRoundChanged(round, TotalRounds);

            foreach (var p in players)
            {
                p.ClearBet();   // 지난 라운드 베팅 무효화 (HUD '미제출' 표시 복원)

                // 라운드 경과 이자 (복리) — 2라운드부터
                if (round > 1) p.ApplyInterest(config.interestRate);
                p.BorrowedThisRound = false;

                // 자동 대출: 최소 베팅($10+$10)조차 불가능하면 $200 강제 대출 (참여 보장)
                if (p.Money < Mathf.Max(20, config.autoLoanThreshold))
                    p.Borrow(config.autoLoanAmount);
            }

            submitted.Clear();
            gm.SetPhase(GamePhase.Betting);   // → RaceManager 스폰 (동기 실행)

            // 배당 계산 (스폰 완료 후 라인업 기반)
            CurrentOdds = OddsCalculator.Calculate(
                raceManager.Lineup.ToArray(), raceManager.Path.TotalLength, config.oddsSimCount);
            GameEvents.RaiseOddsReady(CurrentOdds);

            PhaseEndTime = Time.time + config.bettingSeconds;
            yield return new WaitForSeconds(config.bettingSeconds);

            // 타임아웃: 미제출자 자동 베팅 (랜덤 픽 + $100씩, 잔액 내 조정)
            foreach (var p in players.Where(p => !submitted.Contains(p.PlayerId)))
                AutoBet(p);

            gm.SetPhase(GamePhase.Loadout);
            PhaseEndTime = Time.time + config.loadoutSeconds;
            yield return new WaitForSeconds(config.loadoutSeconds);

            gm.SetPhase(GamePhase.Countdown);
            PhaseEndTime = Time.time + config.countdownSeconds;
            yield return new WaitForSeconds(config.countdownSeconds);

            gm.SetPhase(GamePhase.Racing);
            PhaseEndTime = 0f;

            yield return new WaitUntil(() => gm.CurrentPhase == GamePhase.Settlement);

            settlement.Settle(players, CurrentOdds, round);
            PhaseEndTime = Time.time + config.resultSeconds;
            yield return new WaitForSeconds(config.resultSeconds);
        }

        GameEvents.RaiseMatchEnded();
        gm.SetPhase(GamePhase.Lobby);
        PhaseEndTime = 0f;
    }

    private void AutoBet(PlayerState p)
    {
        var ids = Enumerable.Range(0, config.racerCount)
                            .OrderBy(_ => Random.value).Take(2).ToArray();
        // 픽당 $100, 잔액 부족하면 반씩 ($10 단위, 자동대출 덕에 최소 $20 보장)
        int per = Mathf.Max(10, Mathf.Min(config.autoBetAmount, p.Money / 2) / 10 * 10);
        var ticket = new BetTicket
        { firstId = ids[0], lastId = ids[1], firstAmount = per, lastAmount = per };

        if (p.TrySpend(ticket.Total))
        {
            p.SetBet(ticket);
            submitted.Add(p.PlayerId);
            GameEvents.RaiseBetAccepted(p.PlayerId, ticket);
        }
    }
}
