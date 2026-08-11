using UnityEngine;

/// <summary>모든 튜닝 변수.</summary>
[CreateAssetMenu(fileName = "GameConfig", menuName = "HorseRace/GameConfig")]
public class GameConfig : ScriptableObject
{
    [Header("레이스 기본")]
    public int racerCount = 9;
    [Tooltip("완주에 필요한 바퀴 수 — 완주 거리 = 트랙 길이 × 랩")]
    public int lapCount = 2;

    [Header("아이템 (고정 지급, 재화와 분리)")]
    public int boostCount = 3;
    public int slowCount = 3;
    [Tooltip("발동 무전기 지급 개수 (라운드당)")]
    public int radioSkillCount = 1;
    [Tooltip("처형 무전기 지급 개수 (라운드당)")]
    public int radioExecCount = 1;
    public float itemCooldown = 12f;
    public float cooldownFor2P = 8f;

    [Header("아이템 — 무전기 (사용 후 지연 발동)")]
    [Tooltip("무전 후 실제 발동까지 지연 (초)")]
    public float radioDelaySeconds = 5f;
    [Tooltip("처형된 동물이 쓰러진 뒤 애니메이션이 완전 정지하기까지 (초) — 이후 레이스 끝까지 그 자세 유지")]
    public float elimAnimFreezeSeconds = 5f;

    [Header("포인트 (예측 적중 보상 — 1등 정확 / 2등 이상 / 3등 이상)")]
    public int pointsFirst = 90;
    public int pointsSecond = 50;
    public int pointsThird = 30;

    [Header("플레이어 전투 — 빠따 (전 페이즈 허용, 맞으면 한 방에 쓰러짐)")]
    [Tooltip("타격 판정 사거리 (m)")]
    public float meleeRange = 2.2f;
    [Tooltip("타격 판정 부채꼴 전체 각도 (도) — 바라보는 방향 기준")]
    public float meleeArcAngle = 150f;
    [Tooltip("스윙 시작 후 판정(임팩트)까지 지연 (초) — 빠따가 휙 돌아가는 타이밍")]
    public float meleeImpactDelay = 0.45f;
    [Tooltip("기상 후 무적 시간 (초) — 무한 스턴 방지")]
    public float knockdownInvulnSeconds = 3f;
    [Tooltip("빠따 내구도 — 명중한 스윙이 이 횟수에 도달하면 부서진다 (헛스윙은 무료, 라운드마다 회복)")]
    public int batDurabilityMax = 10;
    [Tooltip("HUD 내구도 게이지가 주황색으로 바뀌는 잔량 비율 (0.5 = 50%)")]
    [Range(0f, 1f)]
    public float batGaugeWarnRatio = 0.5f;
    [Tooltip("HUD 내구도 게이지가 빨간색으로 바뀌는 잔량 비율 (0.2 = 20%)")]
    [Range(0f, 1f)]
    public float batGaugeDangerRatio = 0.2f;
    [Tooltip("내구도 게이지 색 — 충분할 때. 알파를 낮추면 '들고 있음' 앰버 하이라이트와 겹쳐 경계가 안 읽힌다")]
    public Color batGaugeColorFull = new Color(0.25f, 0.8f, 0.3f, 0.85f);
    [Tooltip("내구도 게이지 색 — 경고(warnRatio 이하)")]
    public Color batGaugeColorWarn = new Color(1f, 0.6f, 0.1f, 0.85f);
    [Tooltip("내구도 게이지 색 — 위험(dangerRatio 이하)")]
    public Color batGaugeColorDanger = new Color(0.95f, 0.2f, 0.15f, 0.85f);

    [Header("베팅 방 — 피트스탑 개인실 + 피규어 베팅")]
    [Tooltip("봇 방 문이 열리는 시점 (베팅 시작 후 초) — '베팅 끝내고 나왔다' 연기")]
    public float roomBotDoorDelay = 20f;
    [Tooltip("봇 방 문이 열려 있는 시간 (초)")]
    public float roomBotDoorLinger = 5f;
    [Tooltip("방 문(차고 셔터) 슬라이드 시간 (초)")]
    public float roomDoorSlideSeconds = 0.6f;
    [Tooltip("피규어 크기 (동물 실물 대비 배율)")]
    public float figurineScale = 0.33f;

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

    [Header("주행 — 완주 연출 (결승선 통과 후)")]
    [Tooltip("완주 후 관성 주행으로 더 나아가는 거리 최소 (m) — 동물마다 랜덤")]
    public float finishCoastMin = 3f;
    [Tooltip("완주 후 관성 주행으로 더 나아가는 거리 최대 (m)")]
    public float finishCoastMax = 8f;
    [Tooltip("완주 후 좌우로 흩어지는 폭 (중심선 기준 ± m)")]
    public float finishSpread = 2.2f;

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

    [Header("연출 — 부스트 먼지구름")]
    [Tooltip("먼지 파티클 머티리얼. 비우면 런타임 자동 생성하지만, 빌드 셰이더 스트립을 피하려면 채워두는 게 안전")]
    public Material boostDustMaterial;
    [Tooltip("초당 먼지 개수 (속도에 따라 0.5~1.6배 가감). 너무 많으면 뭉개져서 카툰 느낌이 죽는다")]
    public float dustRate = 14f;
    [Tooltip("먼지 하나가 사는 시간 (초) — 길수록 꼬리가 길게 남는다")]
    public float dustLifetime = 0.7f;
    [Tooltip("먼지 크기 배율 (기본 크기는 동물 몸 높이에 비례)")]
    public float dustSize = 1f;
    [Tooltip("먼지 색")]
    public Color dustColor = new Color(0.95f, 0.90f, 0.78f, 1f);
    [Tooltip("이 속도(m/s) 미만이면 먼지 안 남김 — 스턴/정지 중 헛김 방지")]
    public float dustMinSpeed = 1.5f;
    [Tooltip("부스트 시작 순간 터지는 큰 먼지 개수")]
    public int dustBurst = 8;

    [Header("연출 — 루돌프 비행 애니 배속 (사슴)")]
    [Tooltip("비행 중 달리기 애니메이션 배속 (평상시 최대 1.8)")]
    public float rudolphFlightAnimSpeed = 4f;
    [Tooltip("지면 위 이 높이(m)부터 배속 시작")]
    public float rudolphLiftStart = 0.6f;
    [Tooltip("이 높이(m)에서 최대 배속 도달")]
    public float rudolphLiftFull = 2.5f;
    [Tooltip("꼬리 트레일이 남는 시간 (초) — 길수록 리본이 길다")]
    public float rudolphTrailTime = 0.7f;
    [Tooltip("꼬리 트레일 시작 폭 (m)")]
    public float rudolphTrailWidth = 0.28f;
    [Tooltip("트레일 색 A (꼬리 쪽 — 루돌프 빨강)")]
    public Color rudolphTrailColorA = new Color(1f, 0.30f, 0.22f);
    [Tooltip("트레일 색 B (끝 쪽 — 금색)")]
    public Color rudolphTrailColorB = new Color(1f, 0.85f, 0.35f);

    [Header("연출 — 포효 (호랑이)")]
    [Tooltip("포효 순간 머리 확대 배율")]
    public float roarHeadScale = 1.7f;
    [Tooltip("포효 순간 머리를 앞으로 내미는 거리 (m)")]
    public float roarHeadForward = 0.18f;
    [Tooltip("포효 연출 전체 길이 (초) — 확대 0.2초 + 유지 + 복귀 0.4초 포함")]
    public float roarFxSeconds = 1.2f;

    [Header("디버그")]
    [Tooltip("Scene 뷰에 동물별 조향 목표/상태 라벨 표시")]
    public bool debugMotorGizmos = true;
    [Tooltip("진행도 점프/NaN 콘솔 감시")]
    public bool debugProgressLog = true;

    public float GetCooldownFor(int playerCount) =>
        playerCount <= 2 ? cooldownFor2P : itemCooldown;
}
