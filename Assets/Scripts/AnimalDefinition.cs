using UnityEngine;

/// <summary>
/// 동물 정의 — 스탯은 0~100 단위 (동물 수십 종 확장 대비 표준 스케일).
/// 변환 규칙 (단일 출처, 전 시스템 공용):
///   속도: 스탯 100 = 6.0 m/s
///   가속: 게인 = 1.5 + 스탯 × 0.025  (스탯 20 → 2.0, 스탯 80 → 3.5)
/// </summary>
[CreateAssetMenu(fileName = "Animal_", menuName = "HorseRace/Animal")]
public class AnimalDefinition : ScriptableObject
{
    // ---- 변환 상수 (여기만 바꾸면 전체 스케일 조정) ----
    public const float SpeedUnitToMs = 0.06f;    // 스탯 100 = 6.0 m/s
    public const float AccelBaseGain = 1.5f;
    public const float AccelUnitGain = 0.025f;

    public string displayName;

    [Tooltip("[로컬라이제이션] strings.csv의 이름 키 (animal.tiger 등). 비면 displayName(한국어) 그대로")]
    public string nameKey;

    [TextArea] public string description;
    public GameObject prefab;

    /// <summary>현재 언어 종명 — 표시처는 displayName 대신 전부 이걸 쓴다.</summary>
    public string LocalizedName => string.IsNullOrEmpty(nameKey) ? displayName : Loc.Get(nameKey, displayName);

    /// <summary>
    /// 무전기 LCD용 — 한국어/영어는 현재 언어, 일본어만 영문 폴백.
    /// LAB디지털 폰트가 가나를 미지원(§16)이라 일어만 영문 고정이고,
    /// 한글은 전 지원(v11 실측)이라 그대로 쓴다. 대문자 변환은 한글에 무해.
    /// </summary>
    public string LcdName
    {
        get
        {
            if (string.IsNullOrEmpty(nameKey)) return displayName.ToUpperInvariant();
            string s = Loc.Language == GameLanguage.Japanese
                ? Loc.GetIn(GameLanguage.English, nameKey, displayName)
                : Loc.Get(nameKey, displayName);
            return s.ToUpperInvariant();
        }
    }

    [Tooltip("동물 아이콘 (Texture Type: Sprite). 비워두면 UI 기본 이미지 유지")]
    public Sprite icon;

    [Header("속도 (0~100 단위, 리롤 주기마다 범위 내 랜덤)")]
    [Range(0, 130)] public float minSpeed = 65f;
    [Range(0, 130)] public float maxSpeed = 80f;
    [Tooltip("몇 초마다 속도를 다시 굴리나")]
    public float speedRerollInterval = 6f;

    [Header("가속 (0~100 단위)")]
    [Range(0, 100)] public int acceleration = 50;

    [Header("스킬 (동물당 1개)")]
    public AnimalSkill skill = AnimalSkill.None;

    [Header("연출")]
    [Tooltip("이동을 비행으로 연기 (비둘기) — 컨트롤러의 Walk/Run 자리에 Fly 클립이 구워져 있고(AnimalControllerBaker의 FlyMovers), 달리는 동안 HoverFlightFx가 몸을 띄운다. 높이 등 튜닝은 GameConfig '연출 — 비행 호버'")]
    public bool hoverFlight = false;

    // ---- 별점 (표시용 단일 출처 — v22 스탯 표기 리뉴얼, 유저 결정) ----
    // 속도 별 = (최저+최고)/2 평균 기준. 밸런스상 전 동물 평균이 69~74에 몰려 있어
    // 구간을 촘촘히 잡았다 — 현 15종이 대부분 2~4성에 분포하도록 (1·5성은 극단값 예비).
    public int SpeedStars
    {
        get
        {
            float avg = MedianSpeed;
            if (avg < 66f) return 1;
            if (avg < 71f) return 2;
            if (avg < 73f) return 3;
            if (avg < 75f) return 4;
            return 5;
        }
    }

    // 가속 별 = 0~100을 20 단위 균등 분할로 1~5성 (~20→1 / ~40→2 / ~60→3 / ~80→4 / 81+→5)
    public int AccelStars => Mathf.Clamp(1 + (acceleration - 1) / 20, 1, 5);

    // ---- 변환 프로퍼티 (게임 내부는 전부 이걸 사용) ----
    public float MinSpeedMs => minSpeed * SpeedUnitToMs;
    public float MaxSpeedMs => maxSpeed * SpeedUnitToMs;
    public float MedianSpeed => (minSpeed + maxSpeed) * 0.5f;          // 표기용 (100단위)
    public float MedianSpeedMs => (MinSpeedMs + MaxSpeedMs) * 0.5f;
    public float AccelGain => AccelBaseGain + acceleration * AccelUnitGain;
}
