using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 매치 순환 + 예측 제출 관문 + 포인트 정산.
/// UI가 게임 상태를 쓰는 관문: SubmitBet [멀티: RPC 지점].
/// </summary>
public class MatchManager : MonoBehaviour
{
    [SerializeField] private GameConfig config;
    [SerializeField] private RaceManager raceManager;

    private readonly List<PlayerState> players = new();
    public IReadOnlyList<PlayerState> Players => players;

    /// <summary>매치 진행 중 여부 (레버 중복 방지 / 대기 상태 판정).</summary>
    public bool IsMatchRunning { get; private set; }

    public int CurrentRound { get; private set; }
    public int TotalRounds { get; private set; }
    public float PhaseEndTime { get; private set; }

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

    /// <summary>
    /// [5-3] 매치 강제 중단 (방장 이탈 등) — 정산 없이 즉시 대기실 상태로.
    /// 다음 레버가 로스터를 처음부터 재구성하므로 여기선 전부 비운다.
    /// </summary>
    public void AbortMatch()
    {
        StopAllCoroutines();          // MatchFlow 정지
        IsMatchRunning = false;
        submitted.Clear();
        players.Clear();
        PhaseEndTime = 0f;
        GameManager.Instance.SetPhase(GamePhase.Lobby);   // 벽 복원/레이스 정지 연쇄
    }

    /// <summary>[네트워크] 특정 플레이어 제거.</summary>
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

    // ================= 예측 제출 관문 =================

    /// <summary>★ 예측 제출 (1·2·3등, 전부 필수·중복 불가). 성공 여부 반환.</summary>
    public bool SubmitBet(int playerId, BetTicket ticket)
    {
        if (GameManager.Instance.CurrentPhase != GamePhase.Betting) return false;
        if (!ticket.IsValid(config.racerCount)) return false;
        if (submitted.Contains(playerId)) return false;

        var p = players.FirstOrDefault(x => x.PlayerId == playerId);
        if (p == null) return false;

        p.SetBet(ticket);
        submitted.Add(playerId);
        GameEvents.RaiseBetAccepted(playerId, ticket);
        return true;
    }

    // ================= 네트워크 수신 (클라 전용 — NetworkMatchSync가 호출) =================

    /// <summary>[클라] 호스트가 방송한 페이즈 타이머 반영.</summary>
    public void ApplyNetworkPhaseTimer(float remainingSeconds) =>
        PhaseEndTime = Time.time + Mathf.Max(0f, remainingSeconds);

    /// <summary>[클라] 호스트가 방송한 라운드 반영.</summary>
    public void ApplyNetworkRound(int round) => CurrentRound = round;

    // ================= 매치 흐름 =================

    public void StartMatch(int rounds = -1)
    {
        if (IsMatchRunning) return;
        IsMatchRunning = true;
        TotalRounds = rounds > 0 ? rounds : config.defaultRounds;
        foreach (var p in players) p.ResetPoints();
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
                p.ClearBet();   // 지난 라운드 예측 무효화 (HUD '미제출' 표시 복원)

            submitted.Clear();
            gm.SetPhase(GamePhase.Betting);   // → RaceManager 스폰 (동기 실행)

            PhaseEndTime = Time.time + config.bettingSeconds;
            yield return new WaitForSeconds(config.bettingSeconds);

            // 타임아웃: 미제출자 자동 예측 (랜덤 3두)
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

            SettlePoints(round);
            PhaseEndTime = Time.time + config.resultSeconds;
            yield return new WaitForSeconds(config.resultSeconds);
        }

        IsMatchRunning = false;
        GameEvents.RaiseMatchEnded();
        gm.SetPhase(GamePhase.Lobby);
        PhaseEndTime = 0f;
    }

    /// <summary>포인트 정산: 슬롯별 정확 일치 채점 (1등 100 / 2등 50 / 3등 30).</summary>
    private void SettlePoints(int round)
    {
        var ranking = raceManager.GetFinalRanking();
        if (ranking.Count < 3) return;

        var result = new RaceResult
        {
            round = round,
            firstId  = ranking[0].RacerId,
            secondId = ranking[1].RacerId,
            thirdId  = ranking[2].RacerId
        };

        foreach (var p in players)
        {
            int gained = 0;
            if (p.Bet.firstId == result.firstId)   gained += config.pointsFirst;
            if (p.Bet.secondId == result.secondId) gained += config.pointsSecond;
            if (p.Bet.thirdId == result.thirdId)   gained += config.pointsThird;

            p.AddPoints(gained);
            result.pointsGained[p.PlayerId] = gained;
        }

        GameEvents.RaiseRaceSettled(result);
    }

    private void AutoBet(PlayerState p)
    {
        var ids = Enumerable.Range(0, config.racerCount)
                            .OrderBy(_ => Random.value).Take(3).ToArray();
        var ticket = new BetTicket { firstId = ids[0], secondId = ids[1], thirdId = ids[2] };

        p.SetBet(ticket);
        submitted.Add(p.PlayerId);
        GameEvents.RaiseBetAccepted(p.PlayerId, ticket);
    }
}
