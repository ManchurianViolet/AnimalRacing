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
    public const float CatWalkDuration = 8f;        // 코너 감속 무시 지속

    // 개 — 근성
    public const float LoyaltyMult = 1.30f;         // 꼴등인 동안 (처형 5초 예고를 탈출하는 발버둥)

    // 치킨 — 냅다 달리기
    public const float DashDuration = 8f;
    public const float DashMult = 1.50f;            // 슬럼프 없음 (v9 확정)

    /// <summary>액티브 스킬(무전기 발동 가능) 여부 — 조준 UI/발사 차단/호스트 검증의 공통 기준.</summary>
    public static bool IsActive(AnimalSkill s) =>
        s == AnimalSkill.Roar || s == AnimalSkill.Rudolph
        || s == AnimalSkill.CatWalk || s == AnimalSkill.Dash;

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
        _ => Loc.Get("skill.none")
    };
}
