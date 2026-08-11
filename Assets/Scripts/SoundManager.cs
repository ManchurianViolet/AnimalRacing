using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [사운드] 전역 사운드 허브 — 씬을 넘어 유지되는 싱글턴.
/// - SFX: AudioSource 풀에서 원샷 재생. 호출부는 SoundManager.PlaySfx(id[, 위치]) 한 줄.
/// - BGM: 페이즈 방송(GameEvents.OnPhaseChanged)을 구독해 각 클라가 로컬 재생 —
///   페이즈는 이미 전 클라로 중계되므로 네트워크 추가 통신 0 (부스트 먼지와 같은 철학).
/// - 볼륨 3단: 마스터 = AudioListener(SettingsStore.Volume, 기존 그대로),
///   BGM/SFX = 여기서 배율 적용 (SettingsStore.BgmVolume/SfxVolume).
/// 씬마다 하나 배치하고 라이브러리는 인스펙터 배선 (Resources 규칙 회피) — 중복 인스턴스는 자기 파괴.
/// 라이브러리에 클립이 비어 있으면 조용히 스킵 — 에셋을 채우는 대로 소리가 나기 시작한다.
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Tooltip("소리 데이터의 단일 출처 (클립/볼륨/피치 랜덤 전부 SO에서 튜닝)")]
    [SerializeField] private AudioLibrary library;

    [Tooltip("SFX 동시 재생 한도 (풀 크기) — 넘치면 가장 오래된 소리를 재사용")]
    [SerializeField] private int sfxPoolSize = 16;

    [Tooltip("3D 재생 시 감쇠 시작 거리(m) — 이 안에서는 최대 음량")]
    [SerializeField] private float sfxMinDistance = 2f;

    // ---- SFX 풀 ----
    private AudioSource[] sfxPool;
    private int sfxCursor;

    // ---- BGM (교차 페이드용 2채널) ----
    private class BgmChannel
    {
        public AudioSource src;
        public float entryVolume = 1f;   // BgmEntry.volume
        public float weight;             // 페이드 가중치 0~1
    }
    private BgmChannel bgmFront;   // 현재 곡
    private BgmChannel bgmBack;    // 페이드 아웃 중인 이전 곡
    private BgmTrack currentTrack = BgmTrack.None;
    private Coroutine fadeCo;

    // ================= 공개 API =================

    /// <summary>2D 효과음 (UI·내 화면 전용음). 거리 감쇠 없음.</summary>
    public static void PlaySfx(SfxId id)
    {
        if (Instance != null) Instance.PlayInternal(id, null);
    }

    /// <summary>3D 효과음 — 월드 위치에서 재생, 거리 감쇠 적용 (동물 명중음·문 소리 등).</summary>
    public static void PlaySfx(SfxId id, Vector3 worldPos)
    {
        if (Instance != null) Instance.PlayInternal(id, worldPos);
    }

    /// <summary>BGM 수동 전환 (평소엔 페이즈 구독이 자동 처리 — 특수 연출용).</summary>
    public static void PlayBgm(BgmTrack track)
    {
        if (Instance != null) Instance.SwitchBgm(track);
    }

    /// <summary>SettingsStore.BgmVolume 변경 시 호출 — 재생 중인 곡에 즉시 반영.</summary>
    public static void NotifyBgmVolumeChanged()
    {
        if (Instance != null) Instance.ApplyBgmVolumes();
    }

    // ================= 수명 =================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);   // 이미 이전 씬에서 넘어온 본체가 있음
            return;
        }
        Instance = this;
        transform.SetParent(null);   // DontDestroyOnLoad는 루트만 가능
        DontDestroyOnLoad(gameObject);

        BuildPool();
        bgmFront = CreateBgmChannel("BGM_A");
        bgmBack = CreateBgmChannel("BGM_B");

        // 사건 중계기·UI 클릭음은 여기 얹혀 산다 — 씬마다 따로 배치할 필요가 없고,
        // DontDestroyOnLoad 덕에 타이틀↔게임 씬 양쪽에서 그대로 동작한다.
        if (GetComponent<SfxRelay>() == null) gameObject.AddComponent<SfxRelay>();
        if (GetComponent<UiClickSfx>() == null) gameObject.AddComponent<UiClickSfx>();

        GameEvents.OnPhaseChanged += HandlePhaseChanged;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        // 첫 씬은 sceneLoaded가 이미 지나갔으므로 직접 판단 (빌드 0 = 타이틀)
        SwitchBgm(SceneManager.GetActiveScene().buildIndex == 0 ? BgmTrack.Title : BgmTrack.Lobby);
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        GameEvents.OnPhaseChanged -= HandlePhaseChanged;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    // ================= BGM =================

    private void HandlePhaseChanged(GamePhase p) => SwitchBgm(TrackForPhase(p));

    private void HandleSceneLoaded(Scene s, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        // 게임 씬 진입 직후는 로비 취급 — 매치 중 합류/재접속자는 곧 도착하는 페이즈 방송이 교정한다
        SwitchBgm(s.buildIndex == 0 ? BgmTrack.Title : BgmTrack.Lobby);
    }

    /// <summary>페이즈 → 트랙 매핑. 카운트다운은 무음(페이드 아웃)으로 긴장감 — 취향 따라 여기만 바꾸면 됨.</summary>
    private static BgmTrack TrackForPhase(GamePhase p)
    {
        switch (p)
        {
            case GamePhase.Lobby: return BgmTrack.Lobby;
            case GamePhase.Betting: return BgmTrack.Betting;
            case GamePhase.Loadout: return BgmTrack.Betting;
            case GamePhase.Countdown: return BgmTrack.None;
            case GamePhase.Racing: return BgmTrack.Racing;
            case GamePhase.Settlement: return BgmTrack.Settlement;
            default: return BgmTrack.None;
        }
    }

    private void SwitchBgm(BgmTrack track)
    {
        if (track == currentTrack) return;
        currentTrack = track;

        var entry = library != null ? library.FindBgm(track) : null;
        AudioClip clip = entry != null ? entry.clip : null;

        // 채널 교대: front(새 곡 페이드 인) ↔ back(이전 곡 페이드 아웃)
        (bgmFront, bgmBack) = (bgmBack, bgmFront);

        bgmFront.entryVolume = entry != null ? entry.volume : 1f;
        bgmFront.weight = 0f;
        if (clip != null)
        {
            bgmFront.src.clip = clip;
            bgmFront.src.Play();
        }
        else
        {
            bgmFront.src.Stop();   // 클립 없음/None = 무음으로 페이드
            bgmFront.src.clip = null;
        }

        if (fadeCo != null) StopCoroutine(fadeCo);
        fadeCo = StartCoroutine(CrossfadeCo());
    }

    private IEnumerator CrossfadeCo()
    {
        float seconds = library != null ? Mathf.Max(0.01f, library.bgmCrossfadeSeconds) : 1f;
        float fromIn = bgmFront.weight;    // 보통 0
        float fromOut = bgmBack.weight;    // 보통 1
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / seconds;
            float k = Mathf.Clamp01(t);
            bgmFront.weight = Mathf.Lerp(fromIn, 1f, k);
            bgmBack.weight = Mathf.Lerp(fromOut, 0f, k);
            ApplyBgmVolumes();
            yield return null;
        }
        bgmBack.src.Stop();
        bgmBack.src.clip = null;
        fadeCo = null;
    }

    /// <summary>최종 음량 = 항목 볼륨 × 페이드 가중치 × BGM 슬라이더 (마스터는 AudioListener가 전역 처리).</summary>
    private void ApplyBgmVolumes()
    {
        float user = SettingsStore.BgmVolume;
        bgmFront.src.volume = bgmFront.entryVolume * bgmFront.weight * user;
        bgmBack.src.volume = bgmBack.entryVolume * bgmBack.weight * user;
    }

    private BgmChannel CreateBgmChannel(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 0f;   // BGM은 항상 2D
        src.volume = 0f;
        return new BgmChannel { src = src };
    }

    // ================= SFX =================

    private void BuildPool()
    {
        sfxPool = new AudioSource[Mathf.Max(1, sfxPoolSize)];
        for (int i = 0; i < sfxPool.Length; i++)
        {
            var go = new GameObject($"SFX_{i:00}");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = sfxMinDistance;
            sfxPool[i] = src;
        }
    }

    private void PlayInternal(SfxId id, Vector3? worldPos)
    {
        if (library == null) return;
        var entry = library.FindSfx(id);
        if (entry == null || entry.clips == null || entry.clips.Length == 0) return;

        var clip = entry.clips[Random.Range(0, entry.clips.Length)];
        if (clip == null) return;

        var src = NextSource();
        src.clip = clip;
        src.volume = entry.volume * SettingsStore.SfxVolume;
        src.pitch = Random.Range(entry.pitchMin, entry.pitchMax);
        if (worldPos.HasValue)
        {
            src.transform.position = worldPos.Value;
            src.spatialBlend = 1f;
            src.maxDistance = Mathf.Max(sfxMinDistance + 0.1f, entry.maxDistance);
        }
        else
        {
            src.spatialBlend = 0f;
        }
        src.Play();
    }

    private AudioSource NextSource()
    {
        // 안 쓰는 소스 우선, 전부 재생 중이면 커서 순환으로 가장 오래된 것을 뺏는다
        for (int i = 0; i < sfxPool.Length; i++)
        {
            int idx = (sfxCursor + i) % sfxPool.Length;
            if (!sfxPool[idx].isPlaying)
            {
                sfxCursor = (idx + 1) % sfxPool.Length;
                return sfxPool[idx];
            }
        }
        var stolen = sfxPool[sfxCursor];
        sfxCursor = (sfxCursor + 1) % sfxPool.Length;
        return stolen;
    }
}
