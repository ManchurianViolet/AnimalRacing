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
    [SerializeField] private TMP_Text aimHintText;       // 크로스헤어 아래 "OO에게 사용" / "사용 불가능한 동물입니다"
    [SerializeField] private ItemSlotView slotBat;
    [SerializeField] private ItemSlotView slotBoost;
    [SerializeField] private ItemSlotView slotSlow;
    [SerializeField] private ItemSlotView slotRadioSkill;
    [SerializeField] private ItemSlotView slotRadioExec;

    [Header("베팅 방 — 손 칸 (방 안에선 무기 5칸 대신 이것만)")]
    [SerializeField] private GameObject handSlot;
    [Tooltip("든 피규어의 동물 아이콘 (icon 미배정 동물은 이름 텍스트로 폴백)")]
    [SerializeField] private UnityEngine.UI.Image handIcon;
    [SerializeField] private TMP_Text handNameLabel;

    private bool showBet = true;

    private void Start()
    {
        if (slotBat != null) slotBat.Init(itemController, PlayerEquipment.SlotBat, null, "빠따", "1");
        slotBoost.Init(itemController, PlayerEquipment.SlotBoost, itemController.BoostItem, null, "2");
        slotSlow.Init(itemController, PlayerEquipment.SlotSlow, itemController.SlowItem, null, "3");
        if (slotRadioSkill != null)
            slotRadioSkill.Init(itemController, PlayerEquipment.SlotRadioSkill, itemController.RadioSkillItem, "발동 무전기", "4");
        if (slotRadioExec != null)
            slotRadioExec.Init(itemController, PlayerEquipment.SlotRadioExec, itemController.RadioExecItem, "처형 무전기", "5");
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
        // 정산 중엔 결과판이 화면을 덮으므로 아이템 슬롯 숨김 (겹침 방지)
        bool settlement = GameManager.Instance != null &&
                          GameManager.Instance.CurrentPhase == GamePhase.Settlement;
        bool showGameplay = bound && !uiOpen && !settlement;

        // 베팅 방 조작 모드(내 방 안): 무기 5칸 대신 "손" 1칸 — 든 피규어가 담긴다
        bool roomMode = FigurineBetting.PointerBusy;
        bool showSlots = showGameplay && !roomMode;

        if (slotBat != null) slotBat.gameObject.SetActive(showSlots);
        if (slotBoost != null) slotBoost.gameObject.SetActive(showSlots);
        if (slotSlow != null) slotSlow.gameObject.SetActive(showSlots);
        if (slotRadioSkill != null) slotRadioSkill.gameObject.SetActive(showSlots);
        if (slotRadioExec != null) slotRadioExec.gameObject.SetActive(showSlots);

        if (handSlot != null)
        {
            handSlot.SetActive(!uiOpen && roomMode);   // 로비(로스터 전)에도 방 안이면 표시
            if (roomMode)
            {
                var fig = FigurineBetting.HeldFigurine;
                bool hasIcon = fig != null && fig.Def != null && fig.Def.icon != null;
                if (handIcon != null)
                {
                    handIcon.enabled = hasIcon;
                    if (hasIcon) handIcon.sprite = fig.Def.icon;
                }
                if (handNameLabel != null)
                    handNameLabel.text = fig == null ? "손" : (hasIcon ? "" : fig.HoverName);
            }
        }

        if (crosshairText != null) crosshairText.gameObject.SetActive(!uiOpen);
        if (promptText != null) promptText.gameObject.SetActive(!uiOpen);

        // 조준 힌트는 로스터 바인딩 전(로비)에도 필요 — 피규어("4번 펭귄") > 아이템 > 방 안내
        if (aimHintText != null)
        {
            if (!string.IsNullOrEmpty(FigurineBetting.Hint))
            {
                aimHintText.text = FigurineBetting.Hint;
                aimHintText.color = new Color(1f, 0.95f, 0.6f);
            }
            else if (showGameplay && !string.IsNullOrEmpty(itemController.AimHint))
            {
                aimHintText.text = itemController.AimHint;
                aimHintText.color = itemController.AimBlocked
                    ? new Color(1f, 0.45f, 0.4f)     // 사용 불가 — 붉게
                    : new Color(1f, 0.95f, 0.6f);    // 사용 가능 — 크로스헤어 노랑 계열
            }
            else
            {
                // "자기 방에 들어가 베팅하세요!" — 베팅 중 방 밖에 있을 때
                aimHintText.text = BettingRoomManager.Guidance;
                aimHintText.color = new Color(0.96f, 0.65f, 0.14f);   // 앰버 강조
            }
        }

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
        // (조준 힌트는 위에서 처리 — 로스터 바인딩 전에도 표시해야 해서 bound 가드 앞으로 이동)
    }

    private string BuildBetText(PlayerState me)
    {
        if (!showBet) return "[B] 내 예측 보기";
        if (!me.Bet.IsValid(GameManager.Instance.Config.racerCount)) return "예측 미제출  [B] 숨기기";

        var sb = new StringBuilder();
        sb.Append("1등   ").Append(RacerName(me.Bet.firstId)).Append('\n');
        sb.Append("2등↑ ").Append(RacerName(me.Bet.secondId)).Append('\n');
        sb.Append("3등↑ ").Append(RacerName(me.Bet.thirdId)).Append('\n');
        sb.Append("[B] 숨기기");
        return sb.ToString();
    }

    private string RacerName(int id)
    {
        var r = raceManager.GetRacer(id);
        return r != null ? r.DisplayName : $"{id + 1}번";
    }
}
