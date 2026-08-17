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
    [SerializeField] private TMP_Text statusText;
    [Tooltip("게임 로고 — 팝업(방 만들기/참가/비밀번호)이 열리면 가려지지 않게 숨긴다. " +
             "커마·설정 패널 쪽은 각 패널의 hideWhileOpen이 담당")]
    [SerializeField] private GameObject logo;

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
    private TMP_Text createWarn;     // 방 만들기 경고 문구 — 코드로 만든다 (씬 배선 0)

    private void Awake()
    {
        // 게임 씬에서 돌아오면 커서가 1인칭용(잠김·숨김)으로 남아 있다 —
        // ESC 메뉴의 "메인 메뉴로"든 호스트 이탈이든 경로와 무관하게 여기서 한 번 되돌린다.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        btnOpenCreate.onClick.AddListener(() => ShowPanel(createPanel));
        btnOpenJoin.onClick.AddListener(() => ShowPanel(joinPanel));
        btnQuit.onClick.AddListener(Application.Quit);
        btnCreate.onClick.AddListener(HandleCreate);
        btnCreateBack.onClick.AddListener(() => ShowPanel(mainPanel));
        btnJoinBack.onClick.AddListener(() => ShowPanel(mainPanel));
        btnPwConfirm.onClick.AddListener(HandlePwConfirm);
        btnPwCancel.onClick.AddListener(() => SetPwPrompt(false));

        // 다시 입력하기 시작하면 경고를 치운다 (고친 뒤에도 빨간 글씨가 남아 있으면 아직 틀린 줄 안다)
        if (maxPlayersInput != null) maxPlayersInput.onValueChanged.AddListener(_ => ClearCreateWarn());
        if (roundsInput != null) roundsInput.onValueChanged.AddListener(_ => ClearCreateWarn());
    }

    private void Start()
    {
        ApplyNickname();

        launcher.OnStatus += s => { if (statusText != null) statusText.text = s; };
        launcher.OnRoomsChanged += RefreshRoomList;

        ShowPanel(mainPanel);
        SetPwPrompt(false);
        RefreshRoomList();
    }

    private void ApplyNickname()
    {
        // 닉네임 입력칸은 v13에서 제거됨 — 스팀 닉네임 자동이 기본.
        // 스팀 미실행 폴백 = 저장값, 그것도 없으면 랜덤 (SetNickname이 저장하므로 이후 고정).
        string nick = SteamHub.IsAvailable ? SteamHub.PersonaName
            : PlayerPrefs.GetString("nickname", "");
        if (string.IsNullOrWhiteSpace(nick)) nick = Loc.Format("title.player", Random.Range(100, 1000));
        launcher.SetNickname(nick);
    }

    private void ShowPanel(GameObject panel)
    {
        mainPanel.SetActive(panel == mainPanel);
        createPanel.SetActive(panel == createPanel);
        joinPanel.SetActive(panel == joinPanel);
        pwPromptPanel.SetActive(false);
        ClearCreateWarn();
        RefreshLogo();
    }

    /// <summary>비밀번호 팝업 토글 — 로고 갱신을 빠뜨리지 않게 한 곳으로 모은다.</summary>
    private void SetPwPrompt(bool on)
    {
        pwPromptPanel.SetActive(on);
        RefreshLogo();
    }

    /// <summary>팝업이 하나라도 떠 있으면 로고를 숨긴다 (로고가 화면 중앙까지 내려와 팝업을 덮는다).</summary>
    private void RefreshLogo()
    {
        if (logo == null) return;
        bool popup = createPanel.activeSelf || joinPanel.activeSelf || pwPromptPanel.activeSelf;
        logo.SetActive(!popup);
    }

    // ---- 방 만들기 ----

    private void HandleCreate()
    {
        // 인원 수·라운드는 필수 — 예전엔 빈칸이면 조용히 4인/3라운드로 방이 만들어져서
        // "아무것도 안 넣었는데 방이 생기는" 상태였다. 비밀번호만 선택(비우면 공개방).
        if (!TryReadCount(maxPlayersInput, 2, 4, out int maxP))
        { WarnCreate("create.needplayers", maxPlayersInput); return; }

        if (!TryReadCount(roundsInput, 1, 9, out int rounds))
        { WarnCreate("create.needrounds", roundsInput); return; }

        ClearCreateWarn();
        ApplyNickname();
        launcher.CreateRoom(maxP, rounds, passwordInput.text.Trim());
    }

    /// <summary>빈칸·숫자 아님·범위 밖이면 전부 false. 통과한 값만 그대로 쓴다 (조용한 보정 없음).</summary>
    private static bool TryReadCount(TMP_InputField field, int min, int max, out int value)
    {
        value = 0;
        if (field == null) return false;
        // TMP 입력칸은 비어 있을 때 폭 0짜리 문자를 남길 수 있어 같이 걷어낸다
        string raw = (field.text ?? "").Replace("\u200B", "").Trim();
        if (raw.Length == 0) return false;
        if (!int.TryParse(raw, out value)) return false;
        return value >= min && value <= max;
    }

    private void WarnCreate(string key, TMP_InputField focus)
    {
        var t = EnsureCreateWarn();
        if (t != null) t.text = Loc.Get(key);
        if (focus != null) focus.ActivateInputField();   // 어느 칸이 문제인지 커서로도 알려준다
    }

    private void ClearCreateWarn()
    {
        if (createWarn != null) createWarn.text = "";   // 아직 안 만들었으면 만들 필요도 없다
    }

    /// <summary>경고 줄을 팝업 안에 직접 만든다 — 비밀번호 칸 아래·버튼 위의 빈 자리.</summary>
    private TMP_Text EnsureCreateWarn()
    {
        if (createWarn != null) return createWarn;
        if (createPanel == null) return null;

        var go = new GameObject("CreateWarn", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(createPanel.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(460f, 30f);
        rt.anchoredPosition = new Vector2(0f, -108f);   // 비밀번호 칸 아래(-83) ~ 버튼 위(-134) 사이

        var t = go.AddComponent<TextMeshProUGUI>();
        var src = btnCreate != null ? btnCreate.GetComponentInChildren<TMP_Text>() : null;
        if (src != null) t.font = src.font;             // 팝업의 다른 글씨와 같은 폰트로
        t.fontSize = 20f;
        t.alignment = TextAlignmentOptions.Center;
        t.color = new Color(0.95f, 0.36f, 0.30f, 1f);
        t.raycastTarget = false;
        t.text = "";

        createWarn = t;
        return t;
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
        SetPwPrompt(true);
    }

    private void HandlePwConfirm()
    {
        if (pwInput.text == pendingRoomPw)
        {
            SetPwPrompt(false);
            launcher.JoinRoom(pendingRoom);
        }
        else if (statusText != null)
        {
            statusText.text = Loc.Get("title.wrongpw");
        }
    }
}
