using System.Linq;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [5단계] 타이틀 화면: 메인(만들기/참가/종료) → 만들기 패널 / 참가 패널(방 목록 + 비번 확인).
/// 접속/방 처리는 NetworkLauncher에 위임, 여기는 UI 흐름만.
/// </summary>
public class TitleMenu : MonoBehaviour
{
    [SerializeField] private NetworkLauncher launcher;

    [Header("공통")]
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private TMP_Text statusText;

    [Header("패널")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject createPanel;
    [SerializeField] private GameObject joinPanel;
    [SerializeField] private GameObject pwPromptPanel;

    [Header("메인")]
    [SerializeField] private Button btnOpenCreate;
    [SerializeField] private Button btnOpenJoin;
    [SerializeField] private Button btnQuit;

    [Header("방 만들기")]
    [SerializeField] private TMP_InputField maxPlayersInput;   // 2~4
    [SerializeField] private TMP_InputField roundsInput;       // 1~9
    [SerializeField] private TMP_InputField passwordInput;     // 비우면 오픈방
    [SerializeField] private Button btnCreate;
    [SerializeField] private Button btnCreateBack;

    [Header("방 참가")]
    [SerializeField] private Transform roomListParent;
    [SerializeField] private RoomListItem roomItemPrefab;
    [SerializeField] private TMP_Text emptyListText;
    [SerializeField] private Button btnJoinBack;

    [Header("비밀번호 확인")]
    [SerializeField] private TMP_InputField pwInput;
    [SerializeField] private Button btnPwConfirm;
    [SerializeField] private Button btnPwCancel;

    private string pendingRoom;      // 비번 확인 대기 중인 방
    private string pendingRoomPw;

    private void Awake()
    {
        btnOpenCreate.onClick.AddListener(() => ShowPanel(createPanel));
        btnOpenJoin.onClick.AddListener(() => ShowPanel(joinPanel));
        btnQuit.onClick.AddListener(Application.Quit);
        btnCreate.onClick.AddListener(HandleCreate);
        btnCreateBack.onClick.AddListener(() => ShowPanel(mainPanel));
        btnJoinBack.onClick.AddListener(() => ShowPanel(mainPanel));
        btnPwConfirm.onClick.AddListener(HandlePwConfirm);
        btnPwCancel.onClick.AddListener(() => pwPromptPanel.SetActive(false));
    }

    private void Start()
    {
        nicknameInput.text = PlayerPrefs.GetString("nickname", $"플레이어{Random.Range(100, 1000)}");
        ApplyNickname();
        nicknameInput.onEndEdit.AddListener(_ => ApplyNickname());

        launcher.OnStatus += s => { if (statusText != null) statusText.text = s; };
        launcher.OnRoomsChanged += RefreshRoomList;

        ShowPanel(mainPanel);
        pwPromptPanel.SetActive(false);
        RefreshRoomList();
    }

    private void ApplyNickname()
    {
        string nick = string.IsNullOrWhiteSpace(nicknameInput.text)
            ? $"플레이어{Random.Range(100, 1000)}" : nicknameInput.text.Trim();
        launcher.SetNickname(nick);
    }

    private void ShowPanel(GameObject panel)
    {
        mainPanel.SetActive(panel == mainPanel);
        createPanel.SetActive(panel == createPanel);
        joinPanel.SetActive(panel == joinPanel);
        pwPromptPanel.SetActive(false);
    }

    // ---- 방 만들기 ----

    private void HandleCreate()
    {
        ApplyNickname();
        int.TryParse(maxPlayersInput.text, out int maxP);
        int.TryParse(roundsInput.text, out int rounds);
        maxP = Mathf.Clamp(maxP == 0 ? 4 : maxP, 2, 4);
        rounds = Mathf.Clamp(rounds == 0 ? 3 : rounds, 1, 9);

        launcher.CreateRoom(maxP, rounds, passwordInput.text.Trim());
    }

    // ---- 방 참가 ----

    private void RefreshRoomList()
    {
        if (roomListParent == null) return;

        foreach (Transform child in roomListParent)
            if (child.GetComponent<RoomListItem>() != null) Destroy(child.gameObject);

        var rooms = launcher.Rooms.Values
            .Where(r => r.IsVisible && r.PlayerCount > 0)
            .OrderBy(r => r.Name).ToList();

        if (emptyListText != null)
            emptyListText.gameObject.SetActive(rooms.Count == 0);

        foreach (var room in rooms)
        {
            var item = Instantiate(roomItemPrefab, roomListParent);
            var captured = room;
            item.Bind(captured, () => TryJoin(captured));
        }
    }

    private void TryJoin(Photon.Realtime.RoomInfo room)
    {
        ApplyNickname();
        string pw = room.CustomProperties.TryGetValue(NetworkLauncher.PropPassword, out var p)
            ? (string)p : "";

        if (string.IsNullOrEmpty(pw))
        {
            launcher.JoinRoom(room.Name);
            return;
        }

        // 비밀방: 비번 확인 후 입장
        pendingRoom = room.Name;
        pendingRoomPw = pw;
        pwInput.text = "";
        pwPromptPanel.SetActive(true);
    }

    private void HandlePwConfirm()
    {
        if (pwInput.text == pendingRoomPw)
        {
            pwPromptPanel.SetActive(false);
            launcher.JoinRoom(pendingRoom);
        }
        else if (statusText != null)
        {
            statusText.text = "비밀번호가 틀렸습니다";
        }
    }
}
