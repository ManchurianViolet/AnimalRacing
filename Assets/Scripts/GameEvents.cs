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

    public static event Action<int, RejectReason> OnItemRejected;
    public static void RaiseItemRejected(int pid, RejectReason reason) => OnItemRejected?.Invoke(pid, reason);

    /// <summary>
    /// 스킬/사건 피드 (호스트 발생 → 네트워크 중계). [로컬라이제이션]
    /// 완성된 문장이 아니라 (사건 종류 + 동물 id)를 방송하고 각 클라가 자기 언어로 조립한다(Loc) —
    /// 호스트가 한국어여도 영어 게스트는 영어 피드를 본다. 문자열 키워드 매칭(SfxRelay 옛 방식)의
    /// "문구 바꾸면 조용히 깨짐" 취약점도 이 enum이 원천 차단.
    /// </summary>
    public static event Action<SkillFeedEvent, int> OnSkillEvent;
    public static void RaiseSkillEvent(SkillFeedEvent evt, int racerId = -1) => OnSkillEvent?.Invoke(evt, racerId);

    /// <summary>완주/탈락 확정 (rid, rank, eliminated). eliminated=true면 결승선이 아니라 처형 탈락.</summary>
    public static event Action<int, int, bool> OnRacerFinished;
    public static void RaiseRacerFinished(int rid, int rank, bool eliminated = false) =>
        OnRacerFinished?.Invoke(rid, rank, eliminated);

    /// <summary>베팅 접수 확정 (수동/자동 공통). 네트워크 관문이 본인에게 영수증 회신용으로 구독.</summary>
    public static event Action<int, BetTicket> OnBetAccepted;
    public static void RaiseBetAccepted(int pid, BetTicket t) => OnBetAccepted?.Invoke(pid, t);

    public static event Action<RaceResult> OnRaceSettled;
    public static void RaiseRaceSettled(RaceResult r) => OnRaceSettled?.Invoke(r);

    // ---- 배당 (베팅 페이즈 시작 시 계산 완료 알림 — 베팅 UI가 구독) ----
}

public enum GamePhase { Lobby, Betting, Loadout, Countdown, Racing, Settlement }

/// <summary>
/// 스킬/사건 피드 종류 — RPC로 byte 하나에 실려 나간다.
/// ⚠ 값 재배치 금지 (구버전 빌드와 섞이면 다른 사건으로 해석됨 — AnimalSkill enum과 같은 규칙).
/// </summary>
public enum SkillFeedEvent : byte
{
    Roar = 0,            // [호랑이] 포효 (racerId = 호랑이)
    PenguinIgnore = 1,   // [펭귄] 무관심 면역 (racerId = 펭귄)
    CatWalk = 2,         // [고양이] 사뿐한 발놀림
    Dash = 3,            // [치킨] 냅다 달리기
    Rudolph = 4,         // [사슴] 루돌프 비행
    ExecuteWarning = 5,  // 처형 무전 예고 (racerId = -1, 대상은 5초 후 확정)
    ExecuteHit = 6,      // 처형 집행 (racerId = 희생자)
}

/// <summary>아이템 사용 거절 사유 — 개인 RPC로 byte 하나. 표시 문구는 수신 클라가 Loc로 조립.</summary>
public enum RejectReason : byte
{
    NotRacing = 0,       // 레이스 중이 아님
    Cooldown = 1,        // 쿨다운
    NotOwned = 2,        // 미보유
    InvalidTarget = 3,   // 유효하지 않은 타겟
    PassiveAnimal = 4,   // 패시브 동물 (발동 무전기 불가)
}

/// <summary>라운드 정산 결과 (포인트제): 상위 3두 + 플레이어별 획득 포인트.</summary>
public class RaceResult
{
    public int round;
    public int firstId, secondId, thirdId;
    public System.Collections.Generic.Dictionary<int, int> pointsGained = new();   // playerId → 획득
}
