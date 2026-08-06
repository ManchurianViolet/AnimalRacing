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
        PlayerEquipment.Local?.Select(slot);
    }

    private void Update()
    {
        // 커서가 풀려 있으면(베팅 패널 등 UI 조작 중) 슬롯/휘두르기 입력을 먹지 않는다
        if (Cursor.lockState != CursorLockMode.Locked) { ClearAimHint(); return; }

        // 쓰러져 있는 동안엔 아이템/공격 입력 전면 차단 (기상 키는 PlayerKnockdown이 처리)
        var eqLocal = PlayerEquipment.Local;
        if (eqLocal != null && eqLocal.TryGetComponent<PlayerKnockdown>(out var kd) && kd.IsDown)
        { ClearAimHint(); return; }

        UpdateAimHint();

        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(PlayerEquipment.SlotBat);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(PlayerEquipment.SlotBoost);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(PlayerEquipment.SlotSlow);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SelectSlot(PlayerEquipment.SlotRadioSkill);
        if (Input.GetKeyDown(KeyCode.Alpha5)) SelectSlot(PlayerEquipment.SlotRadioExec);

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
            AimHint = "사용 불가능한 동물입니다";
        }
        else
        {
            AimHint = $"{racer.DisplayName}에게 사용";
        }
    }

    private void ClearAimHint()
    {
        AimHint = "";
        AimBlocked = false;
        aimRacer = null;
    }
}
