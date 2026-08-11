using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [설정] 타이틀 설정 팝업 — 볼륨/감도 슬라이더 + 해상도/화면 모드 ◀▶ + 언어 자리(잠금).
/// 값의 저장·전역 적용은 SettingsStore 담당, 여기는 UI 흐름만 (TitleMenu와 같은 철학).
/// ⚠ 해상도/화면 모드는 에디터 게임뷰가 고정이라 체감 불가 — 빌드에서 확인할 것.
/// 언어는 문자열 추출 대작업이라 잠가둠 (전 UI 하드코딩 한국어 — 별도 세션 예정, §7).
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("볼륨")]
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private TMP_Text volumeValue;

    [Header("배경음")]
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private TMP_Text bgmValue;

    [Header("효과음")]
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private TMP_Text sfxValue;

    [Header("감도")]
    [SerializeField] private Slider sensSlider;
    [SerializeField] private TMP_Text sensValue;

    [Header("시야각")]
    [SerializeField] private Slider fovSlider;
    [SerializeField] private TMP_Text fovValue;

    [Header("해상도")]
    [SerializeField] private Button btnResPrev;
    [SerializeField] private Button btnResNext;
    [SerializeField] private TMP_Text resValue;

    [Header("화면 모드")]
    [SerializeField] private Button btnModePrev;
    [SerializeField] private Button btnModeNext;
    [SerializeField] private TMP_Text modeValue;

    [Header("공통")]
    [SerializeField] private Button btnClose;
    [Tooltip("설정이 열려 있는 동안 숨길 것들 (메인 메뉴·타이틀 로고·서버바) — 커마 패널과 같은 패턴")]
    [SerializeField] private GameObject[] hideWhileOpen;

    private readonly List<Vector2Int> sizes = new();
    private int resIndex;
    private bool wantFullscreen;   // Screen.fullScreen은 적용이 한 프레임 늦어 로컬로 추적

    private void Awake()
    {
        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        volumeSlider.onValueChanged.AddListener(v => { SettingsStore.Volume = v; RefreshLabels(); });

        // 신규 슬라이더는 씬 배선 누락에 대비해 null 가드 (v8 단말기 복제 배선 누락 실사고 교훈)
        if (bgmSlider != null)
        {
            bgmSlider.minValue = 0f;
            bgmSlider.maxValue = 1f;
            bgmSlider.onValueChanged.AddListener(v => { SettingsStore.BgmVolume = v; RefreshLabels(); });
        }
        if (sfxSlider != null)
        {
            sfxSlider.minValue = 0f;
            sfxSlider.maxValue = 1f;
            sfxSlider.onValueChanged.AddListener(v => { SettingsStore.SfxVolume = v; RefreshLabels(); });
        }

        sensSlider.minValue = SettingsStore.SensMin;
        sensSlider.maxValue = SettingsStore.SensMax;
        sensSlider.onValueChanged.AddListener(v => { SettingsStore.Sensitivity = v; RefreshLabels(); });

        fovSlider.minValue = SettingsStore.FovMin;
        fovSlider.maxValue = SettingsStore.FovMax;
        fovSlider.wholeNumbers = true;   // 시야각은 1도 단위
        fovSlider.onValueChanged.AddListener(v => { SettingsStore.Fov = v; RefreshLabels(); });

        btnResPrev.onClick.AddListener(() => StepResolution(-1));
        btnResNext.onClick.AddListener(() => StepResolution(+1));
        btnModePrev.onClick.AddListener(ToggleMode);
        btnModeNext.onClick.AddListener(ToggleMode);
        btnClose.onClick.AddListener(Close);
    }

    /// <summary>설정 버튼의 영속 리스너가 호출 (커스터마이징 버튼과 같은 패턴).</summary>
    public void Open()
    {
        BuildSizes();
        wantFullscreen = Screen.fullScreen;
        gameObject.SetActive(true);
        SoundManager.PlaySfx(SfxId.PanelOpen);
        volumeSlider.SetValueWithoutNotify(SettingsStore.Volume);
        if (bgmSlider != null) bgmSlider.SetValueWithoutNotify(SettingsStore.BgmVolume);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(SettingsStore.SfxVolume);
        sensSlider.SetValueWithoutNotify(SettingsStore.Sensitivity);
        fovSlider.SetValueWithoutNotify(SettingsStore.Fov);
        RefreshLabels();

        foreach (var go in hideWhileOpen)
            if (go != null) go.SetActive(false);
    }

    public void Close()
    {
        foreach (var go in hideWhileOpen)
            if (go != null) go.SetActive(true);
        gameObject.SetActive(false);
        SoundManager.PlaySfx(SfxId.PanelClose);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    // ---- 해상도 ----

    private void BuildSizes()
    {
        sizes.Clear();
        foreach (var r in Screen.resolutions)
        {
            var s = new Vector2Int(r.width, r.height);
            if (!sizes.Contains(s)) sizes.Add(s);   // 주사율 중복 제거
        }
        if (sizes.Count == 0)   // 에디터 등 목록이 비면 통상 해상도로 폴백
        {
            sizes.Add(new Vector2Int(1280, 720));
            sizes.Add(new Vector2Int(1600, 900));
            sizes.Add(new Vector2Int(1920, 1080));
            sizes.Add(new Vector2Int(2560, 1440));
            sizes.Add(new Vector2Int(3840, 2160));
        }

        // 현재 해상도와 가장 가까운 항목을 시작점으로
        resIndex = 0;
        int best = int.MaxValue;
        for (int i = 0; i < sizes.Count; i++)
        {
            int diff = Mathf.Abs(sizes[i].x - Screen.width) + Mathf.Abs(sizes[i].y - Screen.height);
            if (diff < best) { best = diff; resIndex = i; }
        }
    }

    private void StepResolution(int dir)
    {
        if (sizes.Count == 0) return;
        resIndex = Mathf.Clamp(resIndex + dir, 0, sizes.Count - 1);
        Apply();
        RefreshLabels();
    }

    private void ToggleMode()
    {
        wantFullscreen = !wantFullscreen;
        Apply();
        RefreshLabels();
    }

    private void Apply()
    {
        var s = sizes[resIndex];
        // 유니티가 이 선택을 자체 저장(Screenmanager prefs)해서 다음 실행에도 유지된다
        Screen.SetResolution(s.x, s.y,
            wantFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
    }

    // ---- 표시 ----

    private void RefreshLabels()
    {
        Set(volumeValue, $"{Mathf.RoundToInt(SettingsStore.Volume * 100)}%");
        Set(bgmValue, $"{Mathf.RoundToInt(SettingsStore.BgmVolume * 100)}%");
        Set(sfxValue, $"{Mathf.RoundToInt(SettingsStore.SfxVolume * 100)}%");
        Set(sensValue, $"x{SettingsStore.Sensitivity:F2}");
        Set(fovValue, $"{Mathf.RoundToInt(SettingsStore.Fov)}°");
        if (sizes.Count > 0) Set(resValue, $"{sizes[resIndex].x} × {sizes[resIndex].y}");
        Set(modeValue, wantFullscreen ? "전체화면" : "창모드");
    }

    private static void Set(TMP_Text label, string text)
    {
        // 값이 같으면 TMP를 건드리지 않는다 (커마 패널과 같은 규칙)
        if (label != null && label.text != text) label.text = text;
    }
}
