using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 화면 HUD (Screen Space Canvas): 지갑, 내 베팅(B키 토글), 크로스헤어,
/// 상호작용 프롬프트, 아이템 슬롯 초기화.
/// 게임 상태는 읽기만, 쓰기는 절대 안 함 (계율).
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    [Header("씬 레퍼런스")]
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private PlayerItemController itemController;
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private FirstPersonController playerController;

    /// <summary>[5-2] 스폰된 내 아바타 배선 (LocalPlayerBinder가 호출).</summary>
    public void BindLocalPlayer(FirstPersonController fpc, PlayerInteractor it)
    {
        playerController = fpc;
        interactor = it;
    }

    [Header("UI 레퍼런스")]
    [SerializeField] private TMP_Text walletText;        // 좌상단
    [SerializeField] private TMP_Text phaseTimerText;    // "베팅 중  42" (페이즈 + 남은 초)
    [SerializeField] private TMP_Text betText;           // 우상단 (B키 토글)
    [SerializeField] private TMP_Text crosshairText;     // 중앙 "+"
    [SerializeField] private TMP_Text promptText;        // 하단 중앙 "E - 베팅하기"
    [SerializeField] private ItemSlotView slotBoost;
    [SerializeField] private ItemSlotView slotSlow;

    private bool showBet = true;

    private void Start()
    {
        slotBoost.Init(itemController, itemController.BoostItem, "1");
        slotSlow.Init(itemController, itemController.SlowItem, "2");
    }

    private void UpdatePhaseTimer()
    {
        if (phaseTimerText == null) return;

        string lobbyLabel = "대기 중";
        if (Photon.Pun.PhotonNetwork.InRoom)
        {
            var room = Photon.Pun.PhotonNetwork.CurrentRoom;
            lobbyLabel = $"참가자 {room.PlayerCount}/{room.MaxPlayers} — 방장의 시작 대기 중";
        }

        string label = GameManager.Instance.CurrentPhase switch
        {
            GamePhase.Lobby      => lobbyLabel,
            GamePhase.Betting    => "베팅 중",
            GamePhase.Loadout    => "준비 중",
            GamePhase.Countdown  => "출발 준비",
            GamePhase.Racing     => "경기 중",
            GamePhase.Settlement => "결과 표시 중",
            _ => ""
        };
        float remain = matchManager != null ? matchManager.PhaseEndTime - Time.time : 0f;
        phaseTimerText.text = remain > 0f
            ? $"{label}  {Mathf.CeilToInt(remain)}"
            : label;
    }

    private void Update()
    {
        var me = itemController.Me;

        // 페이즈/타이머는 로스터 없이도 표시 (대기실에서 "참가자 N/M" 등)
        UpdatePhaseTimer();

        // 로스터 바인딩 전(대기실): 게임플레이 HUD 숨김
        bool bound = me != null;
        bool uiOpen = playerController != null && !playerController.ControlEnabled;
        bool showGameplay = bound && !uiOpen;

        if (slotBoost != null) slotBoost.gameObject.SetActive(showGameplay);
        if (slotSlow != null) slotSlow.gameObject.SetActive(showGameplay);
        if (crosshairText != null) crosshairText.gameObject.SetActive(!uiOpen);
        if (promptText != null) promptText.gameObject.SetActive(!uiOpen);

        // 상호작용 프롬프트는 로스터 없이도 (대기실 레버 "E - 게임 시작")
        if (promptText != null)
            promptText.text = interactor != null ? interactor.CurrentPrompt : "";

        if (!bound)
        {
            if (walletText != null) walletText.text = "";
            if (betText != null) betText.text = "";
            return;
        }

        if (Input.GetKeyDown(KeyCode.B)) showBet = !showBet;

        if (walletText != null)
            walletText.text = $"{me.Points:N0} P";

        if (betText != null)
            betText.text = BuildBetText(me);

        if (crosshairText != null)
            crosshairText.color = itemController.Selected != null ? Color.yellow : Color.white;

    }

    private string BuildBetText(PlayerState me)
    {
        if (!showBet) return "[B] 내 예측 보기";
        if (!me.Bet.IsValid(GameManager.Instance.Config.racerCount)) return "예측 미제출  [B] 숨기기";

        var sb = new StringBuilder();
        sb.Append("1등  ").Append(RacerName(me.Bet.firstId)).Append('\n');
        sb.Append("2등  ").Append(RacerName(me.Bet.secondId)).Append('\n');
        sb.Append("3등  ").Append(RacerName(me.Bet.thirdId)).Append('\n');
        sb.Append("[B] 숨기기");
        return sb.ToString();
    }

    private string RacerName(int id)
    {
        var r = raceManager.GetRacer(id);
        return r != null ? r.DisplayName : $"{id + 1}번";
    }
}
