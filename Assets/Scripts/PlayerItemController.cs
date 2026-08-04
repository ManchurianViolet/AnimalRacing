using UnityEngine;

/// <summary>
/// 로컬 플레이어의 아이템 슬롯 입력 전담 (Bootstrap에서 분리).
/// 1=빠따(휘두르기) / 2=부스트 주사기 / 3=감속 주사기 / 4=무전기(미구현·맨손).
/// 슬롯 "들기"는 전 페이즈 허용, 주사기 "사용"(조준 발사)은 레이싱 중에만 — 기존 규칙 유지.
/// 빠따 휘두르기는 현재 연출만 (판정·내구도는 피격 모션 확보 후).
/// HUD가 이 컴포넌트의 상태(HeldSlot, Selected, CountOf)를 읽어 표시한다.
/// </summary>
public class PlayerItemController : MonoBehaviour
{
    [SerializeField] private NetworkGateway gateway;
    [SerializeField] private ItemDefinition boostItem;
    [SerializeField] private ItemDefinition slowItem;

    public PlayerState Me { get; private set; }
    public ItemDefinition BoostItem => boostItem;
    public ItemDefinition SlowItem => slowItem;

    /// <summary>지금 손에 든 슬롯 (아바타 스폰 전에는 빠따로 간주).</summary>
    public int HeldSlot => PlayerEquipment.Local != null ? PlayerEquipment.Local.HeldSlot : PlayerEquipment.SlotBat;

    /// <summary>발사 가능한 선택 아이템 — 주사기 슬롯을 들고 있고 레이싱 중일 때만.</summary>
    public ItemDefinition Selected
    {
        get
        {
            if (GameManager.Instance == null || GameManager.Instance.CurrentPhase != GamePhase.Racing) return null;
            if (HeldSlot == PlayerEquipment.SlotBoost) return boostItem;
            if (HeldSlot == PlayerEquipment.SlotSlow) return slowItem;
            return null;
        }
    }

    public void Bind(PlayerState me) => Me = me;

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
    }

    public void SelectSlot(int slot)
    {
        PlayerEquipment.Local?.Select(slot);
    }

    private void Update()
    {
        // 커서가 풀려 있으면(베팅 패널 등 UI 조작 중) 슬롯/휘두르기 입력을 먹지 않는다
        if (Cursor.lockState != CursorLockMode.Locked) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(PlayerEquipment.SlotBat);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(PlayerEquipment.SlotBoost);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(PlayerEquipment.SlotSlow);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SelectSlot(PlayerEquipment.SlotRadio);

        if (!Input.GetMouseButtonDown(0)) return;

        if (HeldSlot == PlayerEquipment.SlotBat)
        {
            PlayerEquipment.Local?.Swing();   // 연출만 — 판정은 추후
            return;
        }

        // 주사기 발사 (레이싱 중 + 보유량 있을 때만)
        var item = Selected;
        if (item == null || Me == null || CountOf(item) <= 0) return;

        var ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out var hit) &&
            hit.collider.GetComponentInParent<Racer>() is Racer racer)
        {
            gateway.RequestUseItem(item, racer.RacerId);
        }
    }
}
