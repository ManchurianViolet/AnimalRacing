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

    public static event Action<int, int> OnRacerFinished;
    public static void RaiseRacerFinished(int rid, int rank) => OnRacerFinished?.Invoke(rid, rank);

    public static event Action<RaceResult> OnRaceSettled;
    public static void RaiseRaceSettled(RaceResult r) => OnRaceSettled?.Invoke(r);

    // ---- 배당 (베팅 페이즈 시작 시 계산 완료 알림 — 베팅 UI가 구독) ----
    public static event Action<OddsCalculator.AnimalOdds[]> OnOddsReady;
    public static void RaiseOddsReady(OddsCalculator.AnimalOdds[] odds) => OnOddsReady?.Invoke(odds);
}

public enum GamePhase { Lobby, Betting, Loadout, Countdown, Racing, Settlement }
