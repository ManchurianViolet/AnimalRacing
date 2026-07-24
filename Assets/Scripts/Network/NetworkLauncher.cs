using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// [멀티 1단계] 접속 검증용 런처.
/// 실행 → Photon 클라우드 접속 → 방 입장(없으면 생성) → Console 로그로 확인.
/// 이후 단계에서 로비 UI로 대체될 임시 부품.
/// </summary>
public class NetworkLauncher : MonoBehaviourPunCallbacks
{
    [Tooltip("같은 버전끼리만 만나게 하는 구분자")]
    [SerializeField] private string gameVersion = "dev";

    [Tooltip("방 이름 (전원이 같은 이름으로 입장 — 개발용 고정 방)")]
    [SerializeField] private string roomName = "jebi-dev";

    [SerializeField] private byte maxPlayers = 4;

    private void Start()
    {
        // 마스터(방장)가 씬을 바꾸면 전원 따라오게 하는 설정 (나중에 필수)
        PhotonNetwork.AutomaticallySyncScene = true;

        PhotonNetwork.GameVersion = gameVersion;
        PhotonNetwork.ConnectUsingSettings();   // PhotonServerSettings의 App ID 사용
        Debug.Log("[NET] Photon 접속 시도...");
    }

    // ---- 접속 흐름 콜백 ----

    public override void OnConnectedToMaster()
    {
        Debug.Log($"[NET] 마스터 서버 접속 성공 (지역: {PhotonNetwork.CloudRegion})");
        // 고정 이름 방에 입장 (없으면 생성) — 랜덤 매칭의 "각자 방 만드는" 경쟁 원천 차단
        PhotonNetwork.JoinOrCreateRoom(roomName,
            new RoomOptions { MaxPlayers = maxPlayers }, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"[NET] 방 입장 완료! 인원 {PhotonNetwork.CurrentRoom.PlayerCount}/{maxPlayers}" +
                  $" | 내가 방장인가: {PhotonNetwork.IsMasterClient}");
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"[NET] 새 플레이어 입장: {newPlayer.NickName} " +
                  $"(현재 {PhotonNetwork.CurrentRoom.PlayerCount}명)");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"[NET] 플레이어 퇴장 (현재 {PhotonNetwork.CurrentRoom.PlayerCount}명)");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"[NET] 접속 끊김: {cause}");
    }
}
