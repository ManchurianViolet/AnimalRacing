using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [게임씬] ESC 일시정지 메뉴 — 왼쪽 다크 패널 + 돌아가기/설정/게임 종료.
/// 멀티게임이라 화면·시뮬은 계속 재생(timeScale 무수정), 내 입력만 잠근다:
/// SetControlEnabled(false) = 이동·시선 잠금 + 커서 해제 → 아이템/피규어/E는
/// 각자의 "커서 잠금 아닐 때 무시" 가드로 자동 차단된다.
/// 피격(넉다운)은 RPC라 메뉴 중에도 그대로 맞는다 — 쓰러지면 메뉴를 자동으로 닫아
/// 쓰러짐 카메라 연출(FPC LateUpdate — 조작 잠금 중엔 안 돎)이 정상 재생되게 한다.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [SerializeField] private GameObject panel;          // 왼쪽 다크 패널 루트
    [SerializeField] private Button btnResume;          // 돌아가기
    [SerializeField] private Button btnSettings;        // 설정
    [SerializeField] private Button btnQuit;            // 게임 종료
    [SerializeField] private SettingsPanel settingsPanel;

    /// <summary>메뉴 열림 상태 — 다른 시스템이 참조할 수 있게 공개.</summary>
    public static bool IsOpen { get; private set; }

    private void Awake()
    {
        btnResume.onClick.AddListener(Close);
        btnSettings.onClick.AddListener(() => { if (settingsPanel != null) settingsPanel.Open(); });
        btnQuit.onClick.AddListener(Application.Quit);
        panel.SetActive(false);
        IsOpen = false;
    }

    private void Update()
    {
        // 설정 카드가 열려 있는 동안 Esc는 SettingsPanel이 처리 (카드만 닫힘) — 여기는 침묵
        if (settingsPanel != null && settingsPanel.gameObject.activeInHierarchy) return;

        // 메뉴 중 피격당해 쓰러지면 자동 닫기 — 조작 잠금을 풀어야 쓰러짐 카메라가 재생된다
        if (IsOpen && IsLocalDown()) { Close(); return; }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (IsOpen) Close();
            else if (LocalFpc() != null && !IsLocalDown()) Open();
        }
    }

    public void Open()
    {
        IsOpen = true;
        panel.SetActive(true);
        SoundManager.PlaySfx(SfxId.PanelOpen);
        LocalFpc()?.SetControlEnabled(false);
    }

    public void Close()
    {
        IsOpen = false;
        panel.SetActive(false);
        SoundManager.PlaySfx(SfxId.PanelClose);
        if (settingsPanel != null && settingsPanel.gameObject.activeSelf) settingsPanel.Close();
        LocalFpc()?.SetControlEnabled(true);
    }

    private static FirstPersonController LocalFpc()
    {
        var eq = PlayerEquipment.Local;   // 내 아바타의 단일 출처 (v11 프로퍼티)
        return eq != null ? eq.GetComponent<FirstPersonController>() : null;
    }

    private static bool IsLocalDown()
    {
        var eq = PlayerEquipment.Local;
        var kd = eq != null ? eq.GetComponent<PlayerKnockdown>() : null;
        return kd != null && kd.IsDown;
    }
}
