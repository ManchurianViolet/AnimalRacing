/// <summary>
/// 동물 스킬 목록 + 튜닝 상수 (단일 출처).
/// 확장 규칙: 새 동물은 기존 스킬 재사용 가능, 새 스킬은 enum 추가 + 세 곳 반영
/// (Racer/RaceManager 본 시뮬, OddsCalculator 몬테카를로, 안내판 문구).
/// </summary>
public enum AnimalSkill
{
    None = 0,
    FinalSprint = 1,   // 말: 최종 직선 가속
    Alert = 2,         // 사슴: 근처 아이템 반응 도주 (배당 미반영 — 플레이어 의존)
    Ambush = 3,        // 호랑이: 최근접 스턴 1회
    Whim = 4,          // 고양이: ±30% 1회
    Loyalty = 5,       // 개: 꼴등 시 가속
    Dash = 6,          // 치킨: 초반 폭주 후 숨참
    Apathy = 7,        // 펭귄: 모든 효과 면역
}

public static class SkillTuning
{
    // 말
    public const float FinalSprintZone = 0.85f;     // 진행률 85% 이후
    public const float FinalSprintMult = 1.12f;

    // 사슴
    public const float AlertRadius = 2f;
    public const float AlertDelay = 0.5f;
    public const float AlertDuration = 3f;
    public const float AlertMult = 1.15f;

    // 호랑이 (사거리 무제한 — 항상 최근접을 문다)
    public const float AmbushStun = 3f;
    public const float ActiveMinRatio = 0.15f;      // 액티브 발동 구간 (진행률)
    public const float ActiveMaxRatio = 0.85f;

    // 고양이
    public const float WhimDuration = 3f;
    public const float WhimUp = 1.30f;
    public const float WhimDown = 0.70f;

    // 개
    public const float LoyaltyMult = 1.15f;

    // 치킨
    public const float DashTime = 5f;
    public const float DashMult = 1.25f;
    public const float DashFatigueTime = 4f;        // 이후 4초
    public const float DashFatigueMult = 0.78f;

    /// <summary>안내판/타임라인용 표기.</summary>
    public static string DisplayName(AnimalSkill s) => s switch
    {
        AnimalSkill.FinalSprint => "우승 본능",
        AnimalSkill.Alert       => "경계 본능",
        AnimalSkill.Ambush      => "포식자의 습격",
        AnimalSkill.Whim        => "변덕",
        AnimalSkill.Loyalty     => "충성심",
        AnimalSkill.Dash        => "냅다 달리기",
        AnimalSkill.Apathy      => "무관심",
        _ => "-"
    };

    public static string Description(AnimalSkill s) => s switch
    {
        AnimalSkill.FinalSprint => "[패시브] 최종 직선주로에서 속도 +12%",
        AnimalSkill.Alert       => "[패시브] 근처(2m)에서 아이템이 터지면 놀라서 3초간 +15%",
        AnimalSkill.Ambush      => "[액티브] 경기 중 1회, 가장 가까운 주자를 3초 스턴",
        AnimalSkill.Whim        => "[액티브] 경기 중 1회, 3초간 +30% 또는 -30% (반반)",
        AnimalSkill.Loyalty     => "[패시브] 꼴등일 때 +15%",
        AnimalSkill.Dash        => "[패시브] 출발 5초간 +25%, 이후 4초간 -22%",
        AnimalSkill.Apathy      => "[패시브] 모든 스킬·아이템 효과를 무시한다",
        _ => "스킬 없음"
    };
}
