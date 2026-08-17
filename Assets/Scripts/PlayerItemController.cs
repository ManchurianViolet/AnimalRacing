using UnityEngine;

/// <summary>
/// 로컬 플레이어의 아이템 슬롯 입력 전담 (Bootstrap에서 분리).
/// 1=빠따(휘두르기) / 2=부스트 주사기 / 3=감속 주사기 / 4=발동 무전기 / 5=처형 무전기.
/// 슬롯 "들기"는 전 페이즈 허용, 아이템 "사용"은 레이싱 중에만 — 기존 규칙 유지.
/// 발동 무전기는 주사기처럼 조준 발사(대상 지정), 처형 무전기는 조준 없이 클릭(대상은 5초 후의 꼴등).
/// HUD가 이 컴포넌트의 상태(HeldSlot, Selected, CountOf)를 읽어 표시한다.
/// </summary>
public class PlayerItemController : MonoBehaviour
{
    [SerializeField] private NetworkGateway gateway;
    [SerializeField] private ItemDefinition boostItem;
    [SerializeField] private ItemDefinition slowItem;
    [SerializeField] private ItemDefinition radioSkillItem;
    [SerializeField] private ItemDefinition radioExecItem;

    public PlayerState Me { get; private set; }
    public ItemDefinition BoostItem => boostItem;
    public ItemDefinition SlowItem => slowItem;
    public ItemDefinition RadioSkillItem => radioSkillItem;
    public ItemDefinition RadioExecItem => radioExecItem;

    /// <summary>지금 손에 든 슬롯 (아바타 스폰 전에는 빠따로 간주).</summary>
    public int HeldSlot => PlayerEquipment.Local != null ? PlayerEquipment.Local.HeldSlot : PlayerEquipment.SlotBat;

    /// <summary>조준 발사형 선택 아이템 — 주사기/발동 무전기를 들고 레이싱 중일 때만.</summary>
    public ItemDefinition Selected
    {
        get
        {
            if (GameManager.Instance == null || GameManager.Instance.CurrentPhase != GamePhase.Racing) return null;
            if (HeldSlot == PlayerEquipment.SlotBoost) return boostItem;
            if (HeldSlot == PlayerEquipment.SlotSlow) return slowItem;
            if (HeldSlot == PlayerEquipment.SlotRadioSkill) return radioSkillItem;
            return null;
        }
    }

    public void Bind(PlayerState me) => Me = me;

    /// <summary>조준 힌트 (HUD가 매 프레임 읽음): "OO에게 사용" / "사용 불가능한 동물입니다" / 빈 문자열.</summary>
    public string AimHint { get; private set; } = "";
    /// <summary>조준 힌트가 발사 불가(패시브 동물) 상태인가 — HUD 색 구분용.</summary>
    public bool AimBlocked { get; private set; }

    private Racer aimRacer;   // 이번 프레임 조준 중인 동물 — 힌트와 발사가 같은 판정을 공유

    public int CountOf(ItemDefinition item)
    {
        if (Me == null || item == null) return 0;
        int n = 0;
        foreach (var i in Me.Items) if (i == item) n++;
        return n;
    }

    /// <summary>HUD 슬롯 버튼 클릭 호환용.</summary>
    public void Select(ItemDefinition item)
    {
        if (item == boostItem) SelectSlot(PlayerEquipment.SlotBoost);
        else if (item == slowItem) SelectSlot(PlayerEquipment.SlotSlow);
        else if (item == radioSkillItem) SelectSlot(PlayerEquipment.SlotRadioSkill);
        else if (item == radioExecItem) SelectSlot(PlayerEquipment.SlotRadioExec);
    }

    public void SelectSlot(int slot)
    {
        // 휠 순환 커서 갱신 — 빈 슬롯을 들면 손은 빈손(HeldSlot=0)이 되므로 "지금 어느 칸인가"는 여기서 기억
        if (slot >= PlayerEquipment.SlotBat && slot <= PlayerEquipment.SlotRadioExec)
            wheelSlot = slot;

        // 다 쓴 슬롯은 들 수 없다 — 누르면 빈손이 된다.
        // (선택 시점에만 막으면 "쓰고 → 다른 슬롯 → 돌아오기"로 빈 소품이 되살아난다)
        if (!HasStockFor(slot)) slot = PlayerEquipment.SlotNone;

        var eq = PlayerEquipment.Local;
        int before = eq != null ? eq.HeldSlot : slot;
        eq?.Select(slot);

        // 손이 실제로 바뀐 프레임에만 (같은 슬롯 연타·빈 슬롯 재방문은 침묵). 내 조작이라 2D
        if (eq != null && eq.HeldSlot != before) SoundManager.PlaySfx(SfxId.SlotSwitch);
    }

    /// <summary>그 슬롯을 들 재고가 남았는가. 빠따는 내구도, 소모품은 개수, 맨손은 항상 참.</summary>
    public bool HasStockFor(int slot)
    {
        // 빠따 재고 = 내구도 (0이면 부서진 것 — 들 수 없고, 들고 있으면 자동 수납)
        if (slot == PlayerEquipment.SlotBat)
            return PlayerEquipment.Local == null || PlayerEquipment.Local.BatDurability > 0;
        var item = ItemForSlot(slot);
        return item == null || CountOf(item) > 0;
    }

    /// <summary>슬롯 번호 → 소모품 SO. 빠따는 아직 소모품이 아니라 null (내구도가 생기면 여기에 추가).</summary>
    public ItemDefinition ItemForSlot(int slot)
    {
        switch (slot)
        {
            case PlayerEquipment.SlotBoost:      return boostItem;
            case PlayerEquipment.SlotSlow:       return slowItem;
            case PlayerEquipment.SlotRadioSkill: return radioSkillItem;
            case PlayerEquipment.SlotRadioExec:  return radioExecItem;
            default:                             return null;
        }
    }

    /// <summary>
    /// 손에 든 것이 다 떨어졌으면 빈손으로 되돌린다.
    /// 개수는 호스트의 경제 방송(1초 주기)으로 갱신되므로 "쓴 직후"가 아니라 매 프레임 확인해야 한다.
    /// 단 무전기는 5초 지연 연출이 끝날 때까지 손에 남아야 하므로 RadioScreen이 끝나고 치운다.
    /// </summary>
    private void AutoStowEmpty()
    {
        var eq = PlayerEquipment.Local;
        if (eq == null || RadioScreen.AnyPlaying) return;
        if (eq.HeldSlot != PlayerEquipment.SlotNone && !HasStockFor(eq.HeldSlot))
            eq.Select(PlayerEquipment.SlotNone);
    }

    private void Update()
    {
        // 입력 가드보다 앞 — 커서가 풀렸거나 방 안이어도 재고가 떨어졌으면 손은 비워져 있어야 한다
        AutoStowEmpty();

        // 커서가 풀려 있으면(베팅 패널 등 UI 조작 중) 슬롯/휘두르기 입력을 먹지 않는다
        if (Cursor.lockState != CursorLockMode.Locked) { ClearAimHint(); return; }

        // 쓰러져 있는 동안엔 아이템/공격 입력 전면 차단 (기상 키는 PlayerKnockdown이 처리)
        var eqLocal = PlayerEquipment.Local;
        if (eqLocal != null && eqLocal.TryGetComponent<PlayerKnockdown>(out var kd) && kd.IsDown)
        { ClearAimHint(); return; }

        UpdateAimHint();

        // 베팅 방 조작 모드(내 방 안) — 슬롯 전환(1~5)도, 좌클릭도 전부 피규어 조작에 양보
        // (슬롯 키를 허용하면 숨겨둔 빠따/주사기가 다시 튀어나온다 — 방 안은 맨손이 규칙)
        if (FigurineBetting.PointerBusy) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(PlayerEquipment.SlotBat);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(PlayerEquipment.SlotBoost);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(PlayerEquipment.SlotSlow);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SelectSlot(PlayerEquipment.SlotRadioSkill);
        if (Input.GetKeyDown(KeyCode.Alpha5)) SelectSlot(PlayerEquipment.SlotRadioExec);

        // 마우스 휠 = 슬롯 순환 (1↔5 양방향 랩, 휠 아래 = 다음 슬롯)
        float wheel = Input.mouseScrollDelta.y;
        if (wheel != 0f) CycleSlot(wheel < 0f ? 1 : -1);

        if (!Input.GetMouseButtonDown(0)) return;

        if (HeldSlot == PlayerEquipment.SlotBat)
        {
            PlayerEquipment.Local?.Swing();
            return;
        }

        bool racing = GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.Racing;

        // 처형 무전기: 조준 불필요 — 대상은 발동 순간(5초 후)의 꼴등
        if (HeldSlot == PlayerEquipment.SlotRadioExec)
        {
            if (racing && Me != null && radioExecItem != null && CountOf(radioExecItem) > 0 && Me.IsCooldownReady)
                gateway.RequestUseItem(radioExecItem, -1);
            return;
        }

        // 주사기/발동 무전기: 조준 발사 (레이싱 중 + 보유량 있을 때만)
        // 판정은 이번 프레임의 조준 힌트와 동일한 결과(aimRacer)를 공유 — 화면 안내와 발사가 항상 일치
        var item = Selected;
        if (item == null || Me == null || CountOf(item) <= 0) return;
        if (aimRacer == null || AimBlocked) return;   // 발동 무전기 × 패시브 동물 = 발사 차단
        gateway.RequestUseItem(item, aimRacer.RacerId);
    }

    /// <summary>
    /// 휠 순환 커서 — HeldSlot과 별개인 이유: 빈 슬롯을 들면 손은 빈손(0)이 되지만
    /// 순환 위치는 그 칸에 남아 있어야 다음 휠이 이어서 돈다 (다섯 슬롯 전부 빌 수도 있음).
    /// </summary>
    private int wheelSlot = PlayerEquipment.SlotBat;

    /// <summary>
    /// 지금 커서가 올라가 있는 슬롯 번호 (1~5). HUD 하이라이트는 HeldSlot이 아니라 이걸 봐야 한다 —
    /// 재고가 떨어진 칸을 고르면 손은 빈손(0)이 되므로 HeldSlot 기준이면 테두리가 통째로 사라져
    /// 휠로 조작하는 사람이 "지금 몇 번인지"를 잃는다 (유저 지적).
    /// </summary>
    public int CursorSlot => wheelSlot;

    /// <summary>
    /// 휠 슬롯 순환 (1↔5 양방향 랩). 빈 슬롯도 그대로 방문한다 — 들면 관문이 빈손 처리.
    /// </summary>
    private void CycleSlot(int dir)
    {
        int slot = ((wheelSlot - 1 + dir) % 5 + 5) % 5 + 1;   // 1~5 순환 래핑
        SelectSlot(slot);
    }

    /// <summary>매 프레임 조준 판정: 조준 발사형 아이템을 들고 있을 때만 레이캐스트.</summary>
    private void UpdateAimHint()
    {
        ClearAimHint();

        var item = Selected;   // 주사기/발동 무전기 + 레이싱 중일 때만 non-null
        if (item == null || Me == null || CountOf(item) <= 0) return;

        var cam = Camera.main;
        if (cam == null) return;

        var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out var hit)) return;
        var racer = hit.collider.GetComponentInParent<Racer>();
        if (racer == null || racer.HasFinished) return;

        aimRacer = racer;
        // 발동 무전기는 액티브 스킬 동물에게만 — 패시브(말/개/펭귄)는 사용 불가 안내
        if (HeldSlot == PlayerEquipment.SlotRadioSkill && !SkillTuning.IsActive(racer.Definition.skill))
        {
            AimBlocked = true;
            AimHint = Loc.Get("aim.blocked");
        }
        else
        {
            AimHint = Loc.Format("aim.use", racer.DisplayName);
        }
    }

    private void ClearAimHint()
    {
        AimHint = "";
        AimBlocked = false;
        aimRacer = null;
    }
}
