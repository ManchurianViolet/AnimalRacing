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

    private void Update()
    {
        var me = itemController.Me;
        if (me == null) return;

        // 단말기/ATM 등 UI 사용 중엔 게임플레이 HUD(슬롯/크로스헤어/프롬프트) 숨김
        bool uiOpen = playerController != null && !playerController.ControlEnabled;
        if (slotBoost != null) slotBoost.gameObject.SetActive(!uiOpen);
        if (slotSlow != null) slotSlow.gameObject.SetActive(!uiOpen);
        if (crosshairText != null) crosshairText.gameObject.SetActive(!uiOpen);
        if (promptText != null) promptText.gameObject.SetActive(!uiOpen);

        if (Input.GetKeyDown(KeyCode.B)) showBet = !showBet;

        if (phaseTimerText != null)
        {
            string label = GameManager.Instance.CurrentPhase switch
            {
                GamePhase.Lobby      => "대기 중",
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

        if (walletText != null)
        {
            walletText.text = me.Debt > 0
                ? $"$ {me.Money:N0}   <color=#FF6B6B>빚 -${me.Debt:N0}</color>"
                : $"$ {me.Money:N0}";
        }

        if (betText != null)
            betText.text = BuildBetText(me);

        if (crosshairText != null)
            crosshairText.color = itemController.Selected != null ? Color.yellow : Color.white;

        if (promptText != null)
            promptText.text = interactor != null ? interactor.CurrentPrompt : "";
    }

    private string BuildBetText(PlayerState me)
    {
        if (!showBet) return "[B] 내 베팅 보기";
        if (!me.Bet.IsValid(GameManager.Instance.Config.racerCount)) return "베팅 미제출  [B] 숨기기";

        var sb = new StringBuilder();
        sb.Append("1등  ").Append(RacerName(me.Bet.firstId)).Append('\n');
        sb.Append("꼴등  ").Append(RacerName(me.Bet.lastId)).Append('\n');
        sb.Append("[B] 숨기기");
        return sb.ToString();
    }

    private string RacerName(int id)
    {
        var r = raceManager.GetRacer(id);
        return r != null ? r.DisplayName : $"{id + 1}번";
    }
}
