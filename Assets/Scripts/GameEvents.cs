using System;

public static class GameEvents
{
    public static event Action<GamePhase> OnPhaseChanged;
    public static void RaisePhaseChanged(GamePhase p) => OnPhaseChanged?.Invoke(p);

    public static event Action<int, int> OnRoundChanged;
    public static void RaiseRoundChanged(int cur, int total) => OnRoundChanged?.Invoke(cur, total);

    public static event Action OnMatchEnded;
    public static void RaiseMatchEnded() => OnMatchEnded?.Invoke();

    public static event Action<int, ItemDefinition, int> OnItemUsed;
    public static void RaiseItemUsed(int pid, ItemDefinition item, int rid) => OnItemUsed?.Invoke(pid, item, rid);

    public static event Action<int, string> OnItemRejected;
    public static void RaiseItemRejected(int pid, string reason) => OnItemRejected?.Invoke(pid, reason);

    /// <summary>스킬 발동 소식 (타임라인용, 호스트 발생 → 네트워크 중계).</summary>
    public static event Action<string> OnSkillProc;
    public static void RaiseSkillProc(string line) => OnSkillProc?.Invoke(line);

    public static event Action<int, int> OnRacerFinished;
    public static void RaiseRacerFinished(int rid, int rank) => OnRacerFinished?.Invoke(rid, rank);

    /// <summary>베팅 접수 확정 (수동/자동 공통). 네트워크 관문이 본인에게 영수증 회신용으로 구독.</summary>
    public static event Action<int, BetTicket> OnBetAccepted;
    public static void RaiseBetAccepted(int pid, BetTicket t) => OnBetAccepted?.Invoke(pid, t);

    public static event Action<RaceResult> OnRaceSettled;
    public static void RaiseRaceSettled(RaceResult r) => OnRaceSettled?.Invoke(r);

    // ---- 배당 (베팅 페이즈 시작 시 계산 완료 알림 — 베팅 UI가 구독) ----
    public static event Action<OddsCalculator.AnimalOdds[]> OnOddsReady;
    public static void RaiseOddsReady(OddsCalculator.AnimalOdds[] odds) => OnOddsReady?.Invoke(odds);
}

public enum GamePhase { Lobby, Betting, Loadout, Countdown, Racing, Settlement }
