using UnityEngine;

/// <summary>모든 튜닝 변수.</summary>
[CreateAssetMenu(fileName = "GameConfig", menuName = "HorseRace/GameConfig")]
public class GameConfig : ScriptableObject
{
    [Header("레이스 기본")]
    public int racerCount = 8;

    [Header("아이템 (고정 지급, 재화와 분리)")]
    public int boostCount = 3;
    public int slowCount = 3;
    public float itemCooldown = 12f;
    public float cooldownFor2P = 8f;

    [Header("경제")]
    public int startMoney = 1000;
    [Tooltip("라운드 시작 시 잔액이 이 값 미만이면 자동 대출 발동 (최소베팅 $1+$1 기준 = 2)")]
    public int autoLoanThreshold = 2;
    public int autoLoanAmount = 200;
    [Tooltip("ATM 추가 대출 자격: 총 자산(보유-빚)이 이 값 미만")]
    public int atmLoanThreshold = 200;
    public int atmLoanSmall = 300;
    public int atmLoanLarge = 500;
    [Tooltip("누적 대출 원금 상한")]
    public int totalBorrowLimit = 1000;
    [Tooltip("ATM 대출 가능 시작 라운드")]
    public int atmAvailableFromRound = 2;
    [Tooltip("라운드 경과당 빚 이자율 (0.3 = 복리 30%)")]
    public float interestRate = 0.3f;
    [Tooltip("타임아웃 자동 베팅 금액 (픽당)")]
    public int autoBetAmount = 100;

    [Header("배당")]
    [Tooltip("몬테카를로 시뮬 횟수")]
    public int oddsSimCount = 1000;

    [Header("매치")]
    public int defaultRounds = 3;
    public float bettingSeconds = 60f;
    public float loadoutSeconds = 2f;
    public float countdownSeconds = 3f;
    public float resultSeconds = 8f;

    [Header("주행")]
    public float lookAhead = 4f;
    public float maxAssistAccel = 20f;

    public float GetCooldownFor(int playerCount) =>
        playerCount <= 2 ? cooldownFor2P : itemCooldown;
}
