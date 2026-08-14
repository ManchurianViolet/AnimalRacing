using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [5-3] 게임 씬 세션 파수꾼:
///  - 내 접속 끊김 → 자동 재접속+방 복귀 시도 (PlayerTTL 덕에 자리 보존)
///  - 매치 중 방장 이탈 → "방장이 나갔습니다" 안내 후 전원 타이틀로
///    (대기실에서의 방장 이탈은 허용 — 새 방장이 레버 권한을 자동 승계)
/// 매니저 오브젝트에 부착.
/// </summary>
public class NetworkSessionGuard : MonoBehaviourPunCallbacks
{
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private PlayerItemController itemController;
    [SerializeField] private string titleSceneName = "TitleScene";

    [Tooltip("화면 중앙 안내 문구 (HUD 캔버스 TMP, 선택)")]
    [SerializeField] private TMP_Text statusText;

    private bool reconnecting;
    private bool leavingToTitle;

    private void Start()
    {
        if (statusText != null) statusText.text = "";
    }

    // ---- 내 접속 끊김 → 복귀 시도 ----

    public override void OnDisconnected(DisconnectCause cause)
    {
        if (leavingToTitle || cause == DisconnectCause.DisconnectByClientLogic) return;

        if (!reconnecting)
        {
            reconnecting = true;
            SetStatus(Loc.Get("net.reconnecting"));
            Debug.LogWarning($"[SessionGuard] 접속 끊김({cause}) → ReconnectAndRejoin 시도");
            if (!PhotonNetwork.ReconnectAndRejoin())
                GoToTitle(Loc.Get("net.reconnectfail"));
        }
        else
        {
            GoToTitle("재접속 실패");   // 2차 실패 — 포기
        }
    }

    public override void OnJoinedRoom()
    {
        if (reconnecting)
        {
            reconnecting = false;
            SetStatus("");
            Debug.Log("[SessionGuard] 방 복귀 성공");
        }
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        if (reconnecting) GoToTitle(Loc.Get("net.rejoinfail"));
    }

    // ---- 방장 이탈 ----

    public override void OnMasterClientSwitched(Player newMaster)
    {
        // 매치 진행 중 방장(시뮬 담당) 이탈 → 매치만 중단하고 모임(방)은 유지:
        // 전원 대기실 복귀, 새 방장이 레버 승계, 방 재개방 → 목록에 3/4로 다시 뜸.
        // 대기실 중 교체면 아무 처리도 필요 없음 (레버 권한만 자동 승계)
        if (matchManager != null && matchManager.IsMatchRunning)
        {
            matchManager.AbortMatch();
            if (itemController != null) itemController.Bind(null);   // HUD 대기실 모드로

            if (PhotonNetwork.IsMasterClient)                        // 내가 새 방장이면
                PhotonNetwork.CurrentRoom.IsOpen = true;             // 방 재개방 (N/4 노출)

            SetStatus(Loc.Get("net.hostleft"));
            Invoke(nameof(ClearStatus), 4f);
            Debug.LogWarning($"[SessionGuard] 매치 중단 → 대기실 (새 방장: {newMaster.NickName})");
        }
        else
        {
            Debug.Log($"[SessionGuard] 대기실 방장 교체 → {newMaster.NickName}");
        }
    }

    private void ClearStatus() => SetStatus("");

    // ---- 공통 ----

    private void GoToTitle(string reason)
    {
        if (leavingToTitle) return;
        leavingToTitle = true;
        SetStatus(reason);
        Debug.LogWarning($"[SessionGuard] 타이틀로 이동: {reason}");

        if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
        else if (PhotonNetwork.IsConnected) PhotonNetwork.Disconnect();

        Invoke(nameof(LoadTitle), 1.5f);   // 안내 문구 잠깐 보여주고 이동
    }

    private void LoadTitle() => SceneManager.LoadScene(titleSceneName);

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }
}
