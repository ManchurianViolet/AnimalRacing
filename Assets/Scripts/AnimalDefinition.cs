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
    [TextArea] public string description;
    public GameObject prefab;

    [Tooltip("동물 아이콘 (Texture Type: Sprite). 비워두면 UI 기본 이미지 유지")]
    public Sprite icon;

    [Header("속도 (0~100 단위, 리롤 주기마다 범위 내 랜덤)")]
    [Range(0, 130)] public float minSpeed = 65f;
    [Range(0, 130)] public float maxSpeed = 80f;
    [Tooltip("몇 초마다 속도를 다시 굴리나")]
    public float speedRerollInterval = 6f;

    [Header("가속 (0~100 단위)")]
    [Range(0, 100)] public int acceleration = 50;

    // ---- 변환 프로퍼티 (게임 내부는 전부 이걸 사용) ----
    public float MinSpeedMs => minSpeed * SpeedUnitToMs;
    public float MaxSpeedMs => maxSpeed * SpeedUnitToMs;
    public float MedianSpeed => (minSpeed + maxSpeed) * 0.5f;          // 표기용 (100단위)
    public float MedianSpeedMs => (MinSpeedMs + MaxSpeedMs) * 0.5f;
    public float AccelGain => AccelBaseGain + acceleration * AccelUnitGain;
}
