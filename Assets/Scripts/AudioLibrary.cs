using System;
using UnityEngine;

/// <summary>
/// [사운드] 효과음 종류 — AudioLibrary SO가 int로 저장하므로 번호 재배치 금지 (AnimalSkill enum과 같은 규칙).
/// 새 소리는 해당 그룹의 뒷번호에 추가할 것 (그룹당 10칸 여유).
/// </summary>
public enum SfxId
{
    None = 0,

    // ---- UI (1x) ----
    UiClick = 10,
    UiHover = 11,
    PanelOpen = 12,
    PanelClose = 13,

    // ---- 레이스 진행 (2x) ----
    CountdownBeep = 20,
    RaceStart = 21,
    RacerFinish = 22,
    Execution = 23,

    // ---- 빠따 PvP (3x) ----
    BatSwing = 30,
    BatHit = 31,
    BatBreak = 32,
    Knockdown = 33,
    GetUp = 34,

    // ---- 아이템 (4x) ----
    SyringeShot = 40,
    SyringeHitBoost = 41,
    SyringeHitSlow = 42,
    RadioUse = 43,
    RadioTyping = 44,
    SlotSwitch = 45,

    // ---- 스킬 (5x) ----
    SkillRoar = 50,
    SkillRudolph = 51,
    SkillDash = 52,
    SkillCatWalk = 53,
    SkillClubRush = 54,   // [인간] 몽둥이 질주 (그룹 뒷번호 — 재배치 금지)

    // ---- 베팅 방 (6x) ----
    DoorSlide = 60,
    FigurinePick = 61,
    FigurinePlace = 62,
    BetConfirm = 63,

    // ---- 플레이어 이동 (7x) — 지면 재질별 발소리 ----
    FootstepDirt = 70,       // 잔디·흙 (터레인)
    FootstepAsphalt = 71,    // 도로 (트랙)
    FootstepConcrete = 72,   // 피트스탑·차고 실내 등 그 외
}

/// <summary>[사운드] 배경음 트랙 — 페이즈에 따라 SoundManager가 자동 전환.</summary>
public enum BgmTrack
{
    None = 0,        // 무음 (페이드 아웃)
    Title = 1,
    Lobby = 2,
    Betting = 3,
    Racing = 4,
    Settlement = 5,
}

/// <summary>
/// [사운드] 소리 데이터의 단일 출처 (SO) — 클립·볼륨·피치 랜덤을 전부 여기서 튜닝 (GameConfig 철학).
/// 클립이 비어 있는 항목은 재생 요청이 와도 조용히 스킵 — 에셋을 채우는 대로 소리가 나기 시작한다.
/// </summary>
[CreateAssetMenu(fileName = "AudioLibrary", menuName = "짜고치는레이스/오디오 라이브러리")]
public class AudioLibrary : ScriptableObject
{
    [Serializable]
    public class SfxEntry
    {
        public SfxId id;
        [Tooltip("변형 클립들 — 재생마다 랜덤 선택 (같은 소리 반복의 지루함 방지). 1개만 넣어도 됨")]
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("피치 랜덤 최소 (1=원음) — min/max를 같게 주면 랜덤 없음")]
        public float pitchMin = 0.95f;
        [Tooltip("피치 랜덤 최대")]
        public float pitchMax = 1.05f;
        [Tooltip("3D 재생(위치 지정) 시 최대 가청 거리(m). 2D 재생에는 무관")]
        public float maxDistance = 30f;
    }

    [Serializable]
    public class BgmEntry
    {
        public BgmTrack track;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("반복 재생 — 베팅 곡처럼 페이즈 길이와 동기된 곡은 끄면 곡이 끝난 뒤 침묵한다 " +
                 "(켜두면 카운트다운 전환 직전에 곡이 처음부터 다시 시작하는 사고)")]
        public bool loop = true;
    }

    [Header("효과음")]
    public SfxEntry[] sfx;

    [Header("배경음")]
    public BgmEntry[] bgm;

    [Tooltip("BGM 곡 전환 교차 페이드 시간(초)")]
    public float bgmCrossfadeSeconds = 1.2f;

    public SfxEntry FindSfx(SfxId id)
    {
        if (sfx == null) return null;
        for (int i = 0; i < sfx.Length; i++)
            if (sfx[i] != null && sfx[i].id == id) return sfx[i];
        return null;
    }

    public BgmEntry FindBgm(BgmTrack track)
    {
        if (bgm == null) return null;
        for (int i = 0; i < bgm.Length; i++)
            if (bgm[i] != null && bgm[i].track == track) return bgm[i];
        return null;
    }
}
