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

    [Header("포인트 (예측 적중 보상 — 슬롯별 독립 채점)")]
    public int pointsFirst = 70;
    public int pointsSecond = 50;
    public int pointsThird = 10;

    [Header("매치")]
    public int defaultRounds = 3;
    public float bettingSeconds = 60f;
    public float loadoutSeconds = 2f;
    public float countdownSeconds = 3f;
    public float resultSeconds = 8f;

    [Header("주행 — 기본")]
    public float lookAhead = 4f;
    public float maxAssistAccel = 20f;

    [Header("주행 — 레이싱 라인 (인코스 수렴)")]
    [Tooltip("몇 m 앞의 코너를 미리 읽나")]
    public float racingLineLookAhead = 9f;
    [Tooltip("인코스로 붙는 강도 (0=직진성향, 1=풀 인코스)")]
    [Range(0f, 1f)] public float insideBiasStrength = 0.7f;
    [Tooltip("이 곡률(도/m)이면 풀 인코스로 판단")]
    public float curvatureSaturation = 6f;
    [Tooltip("도로 가장자리 여유 (m)")]
    public float roadMargin = 1.2f;

    [Header("주행 — 코너 감속 (가속 스탯의 무대)")]
    [Tooltip("끄면 기존 주행과 동일 (A/B 비교용)")]
    public bool cornerDecelEnabled = true;
    [Tooltip("풀 코너(포화 곡률)에서 깎는 속도 비율 (0.22 = 22% 감속)")]
    [Range(0f, 0.5f)] public float cornerDecelRate = 0.22f;
    [Tooltip("몇 m 앞의 코너부터 감속하나 — 레이싱 라인(9m)보다 짧아야 탈출 가속이 코너 '끝'에서 터짐")]
    public float cornerSenseAhead = 6f;
    [Tooltip("상한 초과 시(코너 진입·스턴·감속 아이템) 제동 게인. 최대 가속 게인(스탯 100 = 4.0)보다 커야 '제동은 전원 동일, 탈출 가속만 스탯 무대'가 성립")]
    public float cornerBrakeGain = 4.5f;

    [Header("주행 — 회피/자리다툼")]
    [Tooltip("전방 몇 m의 앞 주자를 장애물로 보나")]
    public float avoidLookAhead = 2.6f;
    [Tooltip("이 횡간격(m) 미만이면 같은 라인으로 판단")]
    public float bodyClearance = 1.1f;
    [Tooltip("추월 시 옆으로 비키는 폭 (m)")]
    public float overtakeShift = 1.6f;
    [Tooltip("갇혔을 때 앞 주자 속도의 몇 배로 추종하나")]
    [Range(0.5f, 1f)] public float blockedSpeedFactor = 0.9f;
    [Tooltip("횡 이동 반응 시간 (작을수록 민첩, 너무 작으면 진동)")]
    public float lateralSmoothTime = 0.45f;
    [Tooltip("횡 이동 최고 속도 (m/s)")]
    public float lateralMaxSpeed = 3.5f;
    [Tooltip("이 진행도 차이(m) 안이면 '나란히'로 보고 횡간격을 유지")]
    public float sideBySideRange = 1.5f;

    [Header("디버그")]
    [Tooltip("Scene 뷰에 동물별 조향 목표/상태 라벨 표시")]
    public bool debugMotorGizmos = true;
    [Tooltip("진행도 점프/NaN 콘솔 감시")]
    public bool debugProgressLog = true;

    public float GetCooldownFor(int playerCount) =>
        playerCount <= 2 ? cooldownFor2P : itemCooldown;
}
