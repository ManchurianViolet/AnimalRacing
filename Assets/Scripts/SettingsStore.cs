using UnityEngine;

/// <summary>
/// [설정] 옵션 값의 단일 출처 (static) — 저장(PlayerPrefs) + 전역 적용 담당.
/// UI는 SettingsPanel, 소비자는 AudioListener(볼륨)/FirstPersonController(감도).
/// 해상도·화면 모드는 유니티가 자체 저장(Screenmanager prefs)하므로 여기서 안 다룬다 —
/// SettingsPanel이 Screen.SetResolution을 직접 호출하면 다음 실행에도 유지된다.
/// </summary>
public static class SettingsStore
{
    public const string KeyLanguage = "opt_lang";        // GameLanguage int (0=한 1=영 2=일)
    public const string KeyVolume = "opt_volume";        // 0~1 (마스터)
    public const string KeyBgm = "opt_bgm";              // 0~1 (배경음 배율)
    public const string KeySfx = "opt_sfx";              // 0~1 (효과음 배율)
    public const string KeySensitivity = "opt_sens";     // 마우스 감도 배율
    public const string KeyFov = "opt_fov";              // 1인칭 시야각 (도)

    public const float SensMin = 0.2f;
    public const float SensMax = 3f;
    public const float FovMin = 50f;
    public const float FovMax = 90f;
    public const float FovDefault = 60f;   // NetPlayer 카메라 원래 값

    // 매 프레임 읽는 소비자(FPC)가 있으므로 캐시 — PlayerPrefs 왕복 방지
    private static GameLanguage? langCache;
    private static float? volumeCache;
    private static float? bgmCache;
    private static float? sfxCache;
    private static float? sensCache;
    private static float? fovCache;

    /// <summary>
    /// [로컬라이제이션] 표시 언어. 첫 실행이면 스팀/OS 언어에서 자동 감지해 저장하고,
    /// 이후엔 유저 선택을 존중한다 (스팀 언어를 바꿔도 안 따라감). 바뀌면 Loc 이벤트 발행.
    /// </summary>
    public static GameLanguage Language
    {
        get
        {
            if (langCache == null)
            {
                if (PlayerPrefs.HasKey(KeyLanguage))
                    langCache = (GameLanguage)Mathf.Clamp(
                        PlayerPrefs.GetInt(KeyLanguage), 0, Loc.LanguageCount - 1);
                else
                {
                    langCache = DetectDefaultLanguage();
                    PlayerPrefs.SetInt(KeyLanguage, (int)langCache.Value);
                }
            }
            return langCache.Value;
        }
        set
        {
            if (langCache == value) return;
            langCache = value;
            PlayerPrefs.SetInt(KeyLanguage, (int)value);
            Loc.NotifyLanguageChanged();
        }
    }

    /// <summary>스팀 게임 언어 우선 (스토어에서 고른 언어), 스팀이 없으면 OS 언어. 그 외 전부 영어.</summary>
    private static GameLanguage DetectDefaultLanguage()
    {
        string steam = SteamHub.GameLanguage;   // 스팀 API 코드명: "koreana"/"japanese"/"english"...
        if (steam == "koreana") return GameLanguage.Korean;
        if (steam == "japanese") return GameLanguage.Japanese;
        if (!string.IsNullOrEmpty(steam)) return GameLanguage.English;

        return Application.systemLanguage switch
        {
            SystemLanguage.Korean => GameLanguage.Korean,
            SystemLanguage.Japanese => GameLanguage.Japanese,
            _ => GameLanguage.English
        };
    }

    /// <summary>마스터 볼륨 0~1. 대입 즉시 AudioListener에 반영.</summary>
    public static float Volume
    {
        get
        {
            volumeCache ??= Mathf.Clamp01(PlayerPrefs.GetFloat(KeyVolume, 1f));
            return volumeCache.Value;
        }
        set
        {
            volumeCache = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyVolume, volumeCache.Value);
            AudioListener.volume = volumeCache.Value;
        }
    }

    /// <summary>배경음 볼륨 0~1. 대입 즉시 재생 중인 BGM에 반영 (SoundManager).</summary>
    public static float BgmVolume
    {
        get
        {
            bgmCache ??= Mathf.Clamp01(PlayerPrefs.GetFloat(KeyBgm, 0.8f));
            return bgmCache.Value;
        }
        set
        {
            bgmCache = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyBgm, bgmCache.Value);
            SoundManager.NotifyBgmVolumeChanged();
        }
    }

    /// <summary>효과음 볼륨 0~1. SFX는 재생 시점에 읽으므로 별도 통지 불필요.</summary>
    public static float SfxVolume
    {
        get
        {
            sfxCache ??= Mathf.Clamp01(PlayerPrefs.GetFloat(KeySfx, 1f));
            return sfxCache.Value;
        }
        set
        {
            sfxCache = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeySfx, sfxCache.Value);
        }
    }

    /// <summary>마우스 감도 배율 (기본 1). FPC가 mouseSensitivity에 곱해 쓴다.</summary>
    public static float Sensitivity
    {
        get
        {
            sensCache ??= Mathf.Clamp(PlayerPrefs.GetFloat(KeySensitivity, 1f), SensMin, SensMax);
            return sensCache.Value;
        }
        set
        {
            sensCache = Mathf.Clamp(value, SensMin, SensMax);
            PlayerPrefs.SetFloat(KeySensitivity, sensCache.Value);
        }
    }

    /// <summary>1인칭 시야각(도). FPC가 스폰 시 카메라에 적용 — 타이틀 카메라는 무관.</summary>
    public static float Fov
    {
        get
        {
            fovCache ??= Mathf.Clamp(PlayerPrefs.GetFloat(KeyFov, FovDefault), FovMin, FovMax);
            return fovCache.Value;
        }
        set
        {
            fovCache = Mathf.Clamp(value, FovMin, FovMax);
            PlayerPrefs.SetFloat(KeyFov, fovCache.Value);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyOnBoot()
    {
        AudioListener.volume = Volume;   // 전역이라 씬 무관 — 게임 씬도 자동 적용
    }
}
