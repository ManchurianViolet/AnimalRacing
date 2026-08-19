using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 화면 HUD (Screen Space Canvas): 지갑, 내 베팅, 크로스헤어,
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
    [SerializeField] private TMP_Text betText;           // 우상단 (옛 한 줄 방식 폴백)
    [SerializeField] private TMP_Text crosshairText;     // 중앙 "+"
    [SerializeField] private TMP_Text promptText;        // 하단 중앙 "E - 베팅하기"
    [SerializeField] private TMP_Text aimHintText;       // 크로스헤어 아래 "OO에게 사용" / "사용 불가능한 동물입니다"
    [SerializeField] private ItemSlotView slotBat;
    [SerializeField] private ItemSlotView slotBoost;
    [SerializeField] private ItemSlotView slotSlow;
    [SerializeField] private ItemSlotView slotRadioSkill;
    [SerializeField] private ItemSlotView slotRadioExec;

    [Header("HUD 프레임 패널 (테두리) — 비워두면 옛 텍스트 방식으로 폴백")]
    [Tooltip("좌상단 포인트 패널 루트. 로스터 바인딩 전(로비)엔 통째로 숨긴다")]
    [SerializeField] private GameObject walletPanel;
    [Tooltip("우상단 내 예측 패널 루트. 비면 betText 한 줄 방식")]
    [SerializeField] private GameObject betPanel;
    [Tooltip("패널 하단 안내 — 미제출일 때만 뜨고, 제출되면 꺼져서 패널이 그만큼 줄어든다")]
    [SerializeField] private TMP_Text betFooter;
    [Tooltip("예측 3행 (1등 / 2등↑ / 3등↑)")]
    [SerializeField] private BetRow[] betRows = new BetRow[3];

    /// <summary>
    /// 우상단 예측 패널의 한 행 = [메달(+↑)] + [번호 배지 · 동물 이름을 묶은 박스].
    /// 번호 배지는 전광판과 같은 단일 출처(RacerColors)를 쓴다.
    /// </summary>
    [System.Serializable]
    public class BetRow
    {
        public GameObject root;
        [Tooltip("금/은/동 메달 (스프라이트는 씬에서 고정 배정)")]
        public UnityEngine.UI.Image medal;
        [Tooltip("\"이상\" 표시 화살표 — 1등 행은 꺼둔다")]
        public GameObject upArrow;
        [Tooltip("레인 번호 배지 배경 (RacerColors.Of)")]
        public UnityEngine.UI.Image badge;
        [Tooltip("배지 숫자 (RacerColors.TextOn)")]
        public TMP_Text badgeText;
        public TMP_Text nameLabel;
    }

    [Header("베팅 방 — 손 칸 (방 안에선 무기 5칸 대신 이것만)")]
    [SerializeField] private GameObject handSlot;
    [Tooltip("든 피규어의 동물 아이콘 (icon 미배정 동물은 이름 텍스트로 폴백)")]
    [SerializeField] private UnityEngine.UI.Image handIcon;
    [SerializeField] private TMP_Text handNameLabel;

    private void Start()
    {
        // 이름은 키로 넘긴다 — ItemSlotView가 언어 전환 때 스스로 다시 조회 (완성 문자열 금지)
        if (slotBat != null) slotBat.Init(itemController, PlayerEquipment.SlotBat, null, "item.bat", "1");
        slotBoost.Init(itemController, PlayerEquipment.SlotBoost, itemController.BoostItem, null, "2");
        slotSlow.Init(itemController, PlayerEquipment.SlotSlow, itemController.SlowItem, null, "3");
        if (slotRadioSkill != null)
            slotRadioSkill.Init(itemController, PlayerEquipment.SlotRadioSkill, itemController.RadioSkillItem, "item.radioskill", "4");
        if (slotRadioExec != null)
            slotRadioExec.Init(itemController, PlayerEquipment.SlotRadioExec, itemController.RadioExecItem, "item.radioexec", "5");
    }

    private void UpdatePhaseTimer()
    {
        if (phaseTimerText == null) return;

        string lobbyLabel = Loc.Get("phase.lobby");
        if (Photon.Pun.PhotonNetwork.InRoom)
        {
            var room = Photon.Pun.PhotonNetwork.CurrentRoom;
            lobbyLabel = Loc.Format("hud.lobbyplayers", room.PlayerCount, room.MaxPlayers);
        }

        string label = GameManager.Instance.CurrentPhase switch
        {
            GamePhase.Lobby      => lobbyLabel,
            GamePhase.Betting    => Loc.Get("phase.betting"),
            GamePhase.Loadout    => Loc.Get("phase.loadout"),
            GamePhase.Countdown  => Loc.Get("phase.countdown"),
            GamePhase.Racing     => Loc.Get("phase.racing"),
            GamePhase.Settlement => Loc.Get("phase.settlement"),
            GamePhase.Ceremony   => Loc.Get("phase.ceremony"),
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
        // 정산 중엔 결과판이 화면을 덮으므로 아이템 슬롯 숨김 (겹침 방지). 시상식도 동일 (연출 화면)
        bool settlement = GameManager.Instance != null &&
                          (GameManager.Instance.CurrentPhase == GamePhase.Settlement ||
                           GameManager.Instance.CurrentPhase == GamePhase.Ceremony);
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
                // 수제 초상화(icon)가 있으면 그것을, 없으면 런타임 썸네일(피규어 모양 자동 렌더)을 쓴다
                Sprite figSprite = null;
                if (fig != null && fig.Def != null)
                    figSprite = fig.Def.icon != null ? fig.Def.icon : FigurineThumbs.Get(fig.Def, fig.PostNumber);
                bool hasIcon = figSprite != null;
                if (handIcon != null)
                {
                    handIcon.enabled = hasIcon;
                    if (hasIcon) { handIcon.sprite = figSprite; handIcon.preserveAspect = true; }
                }
                if (handNameLabel != null)
                    handNameLabel.text = fig == null ? Loc.Get("hud.hand") : fig.HoverName;   // 아이콘 위에도 "#6 펭귄" 유지
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
            // 로비: 빈 프레임만 남지 않게 패널째 숨긴다
            if (walletPanel != null) walletPanel.SetActive(false);
            if (betPanel != null) betPanel.SetActive(false);
            if (walletText != null) walletText.text = "";
            if (betText != null) betText.text = "";
            return;
        }

        if (walletPanel != null) walletPanel.SetActive(true);
        if (betPanel != null) betPanel.SetActive(true);

        if (walletText != null)
            walletText.text = $"{me.Points:N0} <color=#9A9AA2>P</color>";

        UpdateBetPanel(me);

        if (crosshairText != null)
            crosshairText.color = itemController.Selected != null ? Color.yellow : Color.white;
        // (조준 힌트는 위에서 처리 — 로스터 바인딩 전에도 표시해야 해서 bound 가드 앞으로 이동)
    }

    // 실시간 미리보기용 — 내 베팅 방의 상자 3개 (로컬 전용이라 비밀 유지·통신 0)
    private BettingRoom localRoom;

    /// <summary>
    /// 우상단 예측 패널 갱신. betPanel이 없으면 옛 betText 한 줄 방식으로 폴백한다.
    /// 표시 우선순위: ① 확정 티켓(제출 후) ② 베팅 중 = 내 방 상자 실시간 미리보기(빈칸 포함)
    /// ③ 그 외 페이즈에 미제출이면 행 접음. 상자는 내 방에만 로컬 생성이라 비밀 유지 그대로.
    /// </summary>
    private void UpdateBetPanel(PlayerState me)
    {
        if (betPanel == null)
        {
            if (betText != null) betText.text = BuildBetText(me);
            return;
        }

        bool valid = me.Bet.IsValid(GameManager.Instance.Config.racerCount);
        bool betting = GameManager.Instance.CurrentPhase == GamePhase.Betting;
        bool live = !valid && betting;                  // 제출 전 + 베팅 중 = 상자 미리보기
        bool showRows = valid || live;

        if (live && (localRoom == null || !localRoom.IsLocalRoom))
        {
            localRoom = null;
            foreach (var r in FindObjectsByType<BettingRoom>(FindObjectsSortMode.None))
                if (r.IsLocalRoom) { localRoom = r; break; }
        }

        for (int i = 0; i < betRows.Length; i++)
        {
            var row = betRows[i];
            if (row == null || row.root == null) continue;

            row.root.SetActive(showRows);
            if (!showRows) continue;

            if (valid)
            {
                int id = i switch
                {
                    0 => me.Bet.firstId,
                    1 => me.Bet.secondId,
                    _ => me.Bet.thirdId
                };
                FillRow(row, id + 1, AnimalName(id));   // 등번호 = RacerId + 1 (전광판과 같은 규칙)
            }
            else
            {
                // 내 방 상자(rank i)에 놓인 피규어 — 없으면 빈칸
                BetFigurine fig = null;
                if (localRoom != null && localRoom.Boxes != null)
                    foreach (var box in localRoom.Boxes)
                        if (box != null && box.Rank == i) { fig = box.Current; break; }

                if (fig != null) FillRow(row, fig.PostNumber, fig.AnimalName);
                else FillRow(row, -1, "");              // 빈칸 (메달·화살표만 남김)
            }
        }

        // 제출 후엔 안내가 필요 없다 — 끄면 ContentSizeFitter가 패널을 그만큼 줄인다
        if (betFooter != null)
        {
            betFooter.gameObject.SetActive(!valid);
            if (!valid) betFooter.text = Loc.Get("hud.betnone");
        }
    }

    /// <summary>행 하나 채우기. postNumber < 1 = 빈칸 (배지 숨김 + 이름 비움).</summary>
    private void FillRow(BetRow row, int postNumber, string name)
    {
        bool has = postNumber >= 1;
        if (row.badge != null)
        {
            row.badge.gameObject.SetActive(has);
            if (has) row.badge.color = RacerColors.Of(postNumber);
        }
        if (row.badgeText != null && has)
        {
            row.badgeText.text = postNumber.ToString();
            row.badgeText.color = RacerColors.TextOn(postNumber);
        }
        if (row.nameLabel != null) row.nameLabel.text = name;
    }

    private string BuildBetText(PlayerState me)
    {
        if (!me.Bet.IsValid(GameManager.Instance.Config.racerCount)) return Loc.Get("hud.betnone");

        var sb = new StringBuilder();
        sb.Append(Loc.Get("bet.slot1")).Append("   ").Append(RacerName(me.Bet.firstId)).Append('\n');
        sb.Append(Loc.Get("bet.slot2")).Append(' ').Append(RacerName(me.Bet.secondId)).Append('\n');
        sb.Append(Loc.Get("bet.slot3")).Append(' ').Append(RacerName(me.Bet.thirdId));
        return sb.ToString();
    }

    /// <summary>번호를 뺀 순수 동물 이름 — 번호는 옆 배지가 담당한다 (전광판과 같은 방식).</summary>
    private string AnimalName(int id)
    {
        var r = raceManager.GetRacer(id);
        if (r == null) return "";
        return r.Definition != null ? r.Definition.LocalizedName : r.DisplayName;
    }

    private string RacerName(int id)
    {
        var r = raceManager.GetRacer(id);
        return r != null ? r.DisplayName : Loc.Format("racer.fallback", id + 1);
    }
}
