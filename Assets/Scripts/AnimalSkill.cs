/// <summary>
/// 동물 스킬 목록 + 튜닝 상수 (단일 출처).
/// v9 개편(2바퀴 시대): 액티브 4종(포효/루돌프/사뿐한 발놀림/냅다 달리기)은 경기 중 1회
/// 진행률 15~85% 랜덤 지점에서 자동 발동. 발동 무전기는 1회 제한을 무시하고 재발동 가능(유일한 수단).
/// 패시브(말/개/펭귄)는 무전기 무반응 — 조준 단계에서 "사용 불가능한 동물" 차단.
/// 확장 규칙: 새 동물 = 스킬 + 무전기 반응 + 펭귄 상호작용 3종 세트 정의 필수.
/// enum 값 번호는 SO 에셋에 int로 저장되므로 절대 재배치 금지.
/// </summary>
public enum AnimalSkill
{
    None = 0,
    FinalSprint = 1,   // 말: 최후의 질주 (패시브 — 막판 가속)
    Rudolph = 2,       // 사슴: 루돌프 (액티브 — 전방 트랙 지점까지 직선 비행)
    Roar = 3,          // 호랑이: 포효 (액티브 — 자신 제외 전원 감속)
    CatWalk = 4,       // 고양이: 사뿐한 발놀림 (액티브 — 코너 감속 무시)
    Loyalty = 5,       // 개: 근성 (패시브 — 꼴등인 동안 가속, 처형 카운터)
    Dash = 6,          // 치킨: 냅다 달리기 (액티브 — 폭주)
    Apathy = 7,        // 펭귄: 무관심 (패시브 — 전 효과 면역)
    ClubRush = 8,      // 인간: 몽둥이 질주 (액티브 — 2배속 질주 + 접촉 스턴)
    Camouflage = 9,    // 얼룩말: 위장 (액티브 — 1.5배속 + 유체화 + 반투명)
    NeckSweep = 10,    // 기린: 목 휘두르기 (액티브 — 달리면서 목을 뻗었다 내리찍어 주변 360° 훑기, 닿으면 3초 기절)
}

public static class SkillTuning
{
    // 액티브 공통: 자동 발동 진행률 창 (무전기 강제 발동은 이 창과 무관하게 즉시)
    public const float ActiveMinRatio = 0.15f;
    public const float ActiveMaxRatio = 0.85f;

    // 말 — 최후의 질주
    public const float FinalSprintZone = 0.85f;     // 전체 레이스 진행률 (2랩째 막판)
    public const float FinalSprintMult = 1.20f;

    // 사슴 — 루돌프: "현재 속도 × LeadSeconds(12초)" 앞의 트랙 지점까지 직선(현)으로
    // FlightSeconds(5초) 만에 비행 — 이득 ≈ 7초치 거리 (비행 시간 = 리드 시간이면 본전이라 무의미).
    // 궤적 = 등변사다리꼴: 비스듬히 상승 → 수평 순항 → 비스듬히 하강 (기획 확정).
    public const float RudolphLeadSeconds = 10f;    // 목표 = 이 시간만큼 앞의 트랙 지점
    public const float RudolphFlightSeconds = 5f;   // 실제 비행에 걸리는 시간 (이 차이가 스킬의 이득)
    public const float RudolphPeakHeight = 3f;      // 순항 고도
    public const float RudolphClimbRatio = 0.25f;   // 비행 시간 중 상승/하강 사면 비율 (각 25%, 순항 50%)

    // 호랑이 — 포효
    public const float RoarDuration = 5f;
    public const float RoarMult = 0.5f;             // 자신 제외 전원 50% 감속 (펭귄 면역)

    // 고양이 — 사뿐한 발놀림
    public const float CatWalkDuration = 20f;       // 코너 감속 무시 지속 (v18: 8→20초 — 유저 결정)

    // 개 — 근성
    public const float LoyaltyMult = 1.30f;         // 꼴등인 동안 (처형 5초 예고를 탈출하는 발버둥)

    // 치킨 — 냅다 달리기
    public const float DashDuration = 8f;
    public const float DashMult = 1.50f;            // 슬럼프 없음 (v9 확정)

    // 인간 — 몽둥이 질주: 몽둥이를 든 채 2배속 폭주, 스치는 동물은 1초 스턴 (펭귄은 무관심 면역)
    public const float ClubRushDuration = 10f;
    public const float ClubRushMult = 2.0f;
    public const float ClubRushStunSeconds = 1f;
    public const float ClubRushHitRadius = 1.3f;    // 접촉 판정 반경 — 물리 충돌이 꺼져 있어 근접 판정 (§3-6)

    // 얼룩말 — 위장: 15초 1.5배속 + 유체화(몸싸움·회피·몽둥이 스턴 통과) + 반투명 연출.
    // 펭귄 상호작용: 셀프 버프라 없음 (3종 세트 정의 — 주사기/무전기 조준은 그대로 가능)
    public const float CamouflageDuration = 15f;
    public const float CamouflageMult = 1.5f;

    // 기린 — 목 휘두르기: 목을 뻗는 예열(Windup) 뒤 훑는 동안(SpinSeconds) 반경 안 전원 기절 (StunSeconds).
    // v22: 판정을 순간 1회 → 훑는 내내 창 판정으로 확장 (도는 원에 들어온 동물도 맞게 — 연출이 거짓말하지 않게).
    // 펭귄 상호작용: AddEffect 관문에서 자동 면역 (포효와 동일 — 관전 피드 발행)
    public const float NeckSweepRadius = 2.5f;        // 훑기 반경 (연출 원과 일치)
    public const float NeckSweepStunSeconds = 3f;   // v22: 2→3초 (유저 결정. v21: 1→2)
    public const float NeckSweepWindupSeconds = 0.8f; // 목 뻗기 예열 — 이 후에 기절 판정
    public const float NeckSweepSpinSeconds = 1.0f;   // 360도 회전 연출 시간 (판정과 무관)

    /// <summary>액티브 스킬(무전기 발동 가능) 여부 — 조준 UI/발사 차단/호스트 검증의 공통 기준.</summary>
    public static bool IsActive(AnimalSkill s) =>
        s == AnimalSkill.Roar || s == AnimalSkill.Rudolph
        || s == AnimalSkill.CatWalk || s == AnimalSkill.Dash
        || s == AnimalSkill.ClubRush || s == AnimalSkill.Camouflage
        || s == AnimalSkill.NeckSweep;

    /// <summary>안내판/타임라인용 표기. [로컬라이제이션] 문구는 strings.csv의 skill.name.* / skill.desc.*</summary>
    public static string DisplayName(AnimalSkill s) => s switch
    {
        AnimalSkill.FinalSprint => Loc.Get("skill.name.finalsprint"),
        AnimalSkill.Rudolph     => Loc.Get("skill.name.rudolph"),
        AnimalSkill.Roar        => Loc.Get("skill.name.roar"),
        AnimalSkill.CatWalk     => Loc.Get("skill.name.catwalk"),
        AnimalSkill.Loyalty     => Loc.Get("skill.name.loyalty"),
        AnimalSkill.Dash        => Loc.Get("skill.name.dash"),
        AnimalSkill.Apathy      => Loc.Get("skill.name.apathy"),
        AnimalSkill.ClubRush    => Loc.Get("skill.name.clubrush"),
        AnimalSkill.Camouflage  => Loc.Get("skill.name.camouflage"),
        AnimalSkill.NeckSweep   => Loc.Get("skill.name.necksweep"),
        _ => "-"
    };

    public static string Description(AnimalSkill s) => s switch
    {
        AnimalSkill.FinalSprint => Loc.Get("skill.desc.finalsprint"),
        AnimalSkill.Rudolph     => Loc.Get("skill.desc.rudolph"),
        AnimalSkill.Roar        => Loc.Get("skill.desc.roar"),
        AnimalSkill.CatWalk     => Loc.Get("skill.desc.catwalk"),
        AnimalSkill.Loyalty     => Loc.Get("skill.desc.loyalty"),
        AnimalSkill.Dash        => Loc.Get("skill.desc.dash"),
        AnimalSkill.Apathy      => Loc.Get("skill.desc.apathy"),
        AnimalSkill.ClubRush    => Loc.Get("skill.desc.clubrush"),
        AnimalSkill.Camouflage  => Loc.Get("skill.desc.camouflage"),
        AnimalSkill.NeckSweep   => Loc.Get("skill.desc.necksweep"),
        _ => Loc.Get("skill.none")
    };
}
