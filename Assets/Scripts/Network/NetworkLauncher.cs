using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// [5단계] 접속 관리자 — 타이틀 씬 소속.
/// 시작 시 Photon 접속 + 로비 입장(방 목록 수신), TitleMenu의 요청으로 방 생성/입장.
/// 방 입장 성공 → 방장이 게임 씬 로드 (AutomaticallySyncScene으로 전원 따라옴).
/// 게임 씬에는 이 컴포넌트를 두지 않는다 (거기선 이미 방 안이니까).
/// </summary>
public class NetworkLauncher : MonoBehaviourPunCallbacks
{
    [SerializeField] private string gameVersion = "dev";
    [Tooltip("방 입장 후 로드할 게임(도박장) 씬 이름 — 타이틀 씬 이름 아님!")]
    [SerializeField] private string gameSceneName = "GameScene";

    /// <summary>방 목록 (방이름 → 정보). TitleMenu가 구독.</summary>
    public readonly Dictionary<string, RoomInfo> Rooms = new();
    public System.Action OnRoomsChanged;
    public System.Action<string> OnStatus;   // 상태/에러 문구

    /// <summary>로비 입장 완료 = 방 생성/입장 가능 상태.</summary>
    public bool Ready => PhotonNetwork.InLobby;

    private void Status(string msg)
    {
        if (!string.IsNullOrEmpty(msg)) Debug.Log($"[NET] {msg}");   // UI가 죽어도 Console엔 남게
        OnStatus?.Invoke(msg);
    }

    public const string PropPassword = "pw";
    public const string PropRounds = "rounds";

    // ---- 지역 선택 (v13 글로벌 대응) ----
    // ""(기본) = 자동(Best Region) — Photon 대시보드 허가 리스트(kr;us;eu) 안에서 핑 최저를 고른다.
    // 수동 선택은 대륙 간 친구 파티용: 지역이 다르면 서로 방 목록이 안 보이기 때문.
    public const string RegionPrefKey = "photonRegion";
    public static string SavedRegion => PlayerPrefs.GetString(RegionPrefKey, "");

    private bool switchingRegion;   // 지역 변경으로 인한 의도적 Disconnect 표시

    private void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.GameVersion = gameVersion;

        // [스팀] SteamID = Photon UserId — 창을 껐다 켜도 같은 사람으로 인식(재접속 복귀 성립).
        // MPPM 가상 플레이어는 MppmTestClient가 먼저 테스트 신원을 넣어두므로 덮지 않는다.
        if (SteamHub.IsAvailable && PhotonNetwork.AuthValues == null)
            PhotonNetwork.AuthValues = new AuthenticationValues(SteamHub.SteamId);

        if (!PhotonNetwork.IsConnected)
        {
            Connect();
        }
        else if (!PhotonNetwork.InLobby)
        {
            PhotonNetwork.JoinLobby();   // 게임에서 타이틀로 돌아온 경우
        }
    }

    private void Connect()
    {
        string region = SavedRegion;
        Status(string.IsNullOrEmpty(region)
            ? "서버 접속 중..."
            : $"서버 접속 중... ({PhotonRegions.Of(region)})");

        if (string.IsNullOrEmpty(region)) PhotonNetwork.ConnectUsingSettings();   // Best Region
        else PhotonNetwork.ConnectToRegion(region);
    }

    /// <summary>지역 변경 (RegionSelector가 호출). ""=자동. 저장 후 재접속한다.</summary>
    public void ChangeRegion(string code)
    {
        code ??= "";
        if (code == SavedRegion && PhotonNetwork.IsConnected) return;   // 변화 없음

        PlayerPrefs.SetString(RegionPrefKey, code);

        if (PhotonNetwork.IsConnected)
        {
            // 지역은 접속 단위 속성이라 끊고 다시 붙어야 한다 — OnDisconnected에서 재접속
            switchingRegion = true;
            Status("서버 변경 중...");
            PhotonNetwork.Disconnect();
        }
        else
        {
            Connect();
        }
    }

    public void SetNickname(string nick)
    {
        PhotonNetwork.NickName = nick;
        PlayerPrefs.SetString("nickname", nick);
    }

    // ---- 방 생성/입장 (TitleMenu가 호출) ----

    public void CreateRoom(int maxPlayers, int rounds, string password)
    {
        if (!Ready) { Status("아직 서버 접속 중입니다 — 잠시 후 다시 시도하세요"); return; }

        string roomName = $"{PhotonNetwork.NickName}의 방 #{Random.Range(100, 1000)}";
        var props = new ExitGames.Client.Photon.Hashtable
        {
            { PropPassword, password ?? "" },
            { PropRounds, rounds }
        };
        var options = new RoomOptions
        {
            MaxPlayers = maxPlayers,
            CustomRoomProperties = props,
            CustomRoomPropertiesForLobby = new[] { PropPassword, PropRounds },
            PlayerTtl = 60000   // 이탈자 자리 60초 보존 → 재접속 복귀 가능
        };
        Status("방 생성 중...");
        PhotonNetwork.CreateRoom(roomName, options);
    }

    public void JoinRoom(string roomName)
    {
        if (!Ready) { Status("아직 서버 접속 중입니다 — 잠시 후 다시 시도하세요"); return; }

        Status("입장 중...");
        PhotonNetwork.JoinRoom(roomName);
    }

    // ---- Photon 콜백 ----

    public override void OnConnectedToMaster()
    {
        Status($"접속 완료 (서버: {PhotonRegions.Of(PhotonNetwork.CloudRegion)})");
        PlayerLook.Publish();   // 저장된 커마 외형을 미리 올려둔다 (입장 시 남들이 바로 읽음)
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        Status("로비 입장 — 방 목록 수신 중");
        Rooms.Clear();
        OnRoomsChanged?.Invoke();
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        // Photon은 변경분만 보냄 — 캐시에 병합
        foreach (var info in roomList)
        {
            if (info.RemovedFromList) Rooms.Remove(info.Name);
            else Rooms[info.Name] = info;
        }
        OnRoomsChanged?.Invoke();
    }

    public override void OnJoinedRoom()
    {
        Status("입장 완료 — 게임 로드 중...");

        // [진단] 로드 가능 여부까지 출력 — canLoad=False면 빌드 씬 목록/체크박스 문제
        bool canLoad = Application.CanStreamedLevelBeLoaded(gameSceneName);
        Debug.Log($"[NET] LoadLevel 시도: '{gameSceneName}' | 방장={PhotonNetwork.IsMasterClient} | 로드가능={canLoad}");

        // 방장만 씬 로드 (AutomaticallySyncScene이 나머지를 끌고 감)
        if (PhotonNetwork.IsMasterClient)
            PhotonNetwork.LoadLevel(gameSceneName);
    }

    public override void OnCreateRoomFailed(short code, string msg) =>
        Status($"방 생성 실패: {msg}");

    public override void OnJoinRoomFailed(short code, string msg) =>
        Status(code == ErrorCode.GameFull ? "방이 가득 찼습니다"
             : code == ErrorCode.GameDoesNotExist ? "방이 사라졌습니다"
             : $"입장 실패: {msg}");

    public override void OnDisconnected(DisconnectCause cause)
    {
        if (switchingRegion)
        {
            switchingRegion = false;
            Connect();   // 새 지역으로 재접속
            return;
        }

        if (cause != DisconnectCause.DisconnectByClientLogic)
            Status($"접속 끊김: {cause}");
    }
}
