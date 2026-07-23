using UnityEngine;

/// <summary>
/// 로컬 플레이어의 아이템 선택/사용 입력 전담 (Bootstrap에서 분리).
/// 1=부스트, 2=감속 선택 → 화면 중앙 레이캐스트로 동물 클릭 사용.
/// HUD가 이 컴포넌트의 상태(Selected, CountOf)를 읽어 표시한다.
/// </summary>
public class PlayerItemController : MonoBehaviour
{
    [SerializeField] private ItemExecutor executor;
    [SerializeField] private ItemDefinition boostItem;
    [SerializeField] private ItemDefinition slowItem;

    public PlayerState Me { get; private set; }
    public ItemDefinition Selected { get; private set; }
    public ItemDefinition BoostItem => boostItem;
    public ItemDefinition SlowItem => slowItem;

    public void Bind(PlayerState me) => Me = me;

    public int CountOf(ItemDefinition item)
    {
        if (Me == null || item == null) return 0;
        int n = 0;
        foreach (var i in Me.Items) if (i == item) n++;
        return n;
    }

    public void Select(ItemDefinition item)
    {
        if (Me == null || CountOf(item) <= 0) return;
        Selected = item;
    }

    private void Update()
    {
        if (Me == null) return;

        if (GameManager.Instance.CurrentPhase != GamePhase.Racing)
        {
            Selected = null;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Alpha1)) Select(boostItem);
        if (Input.GetKeyDown(KeyCode.Alpha2)) Select(slowItem);

        if (Selected != null && Input.GetMouseButtonDown(0))
        {
            var ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out var hit) &&
                hit.collider.GetComponentInParent<Racer>() is Racer racer)
            {
                executor.TryUseItem(Me, Selected, racer.RacerId);
                Selected = null;
            }
        }
    }
}
