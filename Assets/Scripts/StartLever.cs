using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [5-1b] 게임 시작 버튼 (방장 전용) — 전망대에 설치.
/// 대기 상태(Lobby)에서 E → 정원 미달이면 확인창 → 시작 → 엘리베이터 하강(ElevatorRide가 페이즈로 감지).
/// 오프라인에선 매치 종료 후 재경기 버튼 역할.
/// 3D 오브젝트 루트에 Collider와 함께 부착.
/// ※ 대기실 벽 개폐는 전망대+엘리베이터 도입으로 폐기됨 (기획 확정).
/// </summary>
public class StartLever : MonoBehaviour, IInteractable
{
    [Header("씬 레퍼런스")]
    [SerializeField] private NetworkGateway gateway;
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private FirstPersonController playerController;

    /// <summary>[5-2] 스폰된 내 아바타 배선 (LocalPlayerBinder가 호출).</summary>
    public void BindLocalPlayer(FirstPersonController fpc) => playerController = fpc;

    [Header("인원 미달 확인창 (HUD 캔버스에)")]
    [SerializeField] private GameObject confirmPanel;
    [SerializeField] private TMP_Text confirmText;
    [SerializeField] private Button btnYes;
    [SerializeField] private Button btnNo;

    public string Prompt => Loc.Get("lever.prompt");

    private void Awake()
    {
        if (confirmPanel != null) confirmPanel.SetActive(false);
        if (btnYes != null) btnYes.onClick.AddListener(() => { CloseConfirm(); gateway.RequestStartMatch(); });
        if (btnNo != null) btnNo.onClick.AddListener(CloseConfirm);
    }

    public bool CanInteract() =>
        GameManager.Instance != null
        && GameManager.Instance.CurrentPhase == GamePhase.Lobby
        && !matchManager.IsMatchRunning
        && NetworkPlayers.IsAuthority
        && (confirmPanel == null || !confirmPanel.activeSelf);

    public void Interact()
    {
        if (PhotonNetwork.InRoom)
        {
            var room = PhotonNetwork.CurrentRoom;
            if (room.PlayerCount < room.MaxPlayers && confirmPanel != null)
            {
                confirmText.text = Loc.Format("lever.confirm", room.PlayerCount, room.MaxPlayers);
                confirmPanel.SetActive(true);
                if (playerController != null) playerController.SetControlEnabled(false);
                return;
            }
        }
        gateway.RequestStartMatch();
    }

    private void CloseConfirm()
    {
        if (confirmPanel != null) confirmPanel.SetActive(false);
        if (playerController != null) playerController.SetControlEnabled(true);
    }
}
