using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아이템 슬롯 하나 (4칸 공용: 빠따/부스트/감속/무전기).
/// 표시: 이름, 개수(아이템 슬롯만), 쿨다운 게이지(Filled Image), "들고 있음" 하이라이트.
/// 클릭으로도 선택 가능 (Button 연결 시).
/// </summary>
public class ItemSlotView : MonoBehaviour
{
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text countLabel;
    [Tooltip("Image Type=Filled 로 설정. 쿨다운 남은 비율만큼 채워짐")]
    [SerializeField] private Image cooldownFill;
    [Tooltip("들고 있을 때 켜지는 테두리/배경 오브젝트")]
    [SerializeField] private GameObject selectedFrame;
    [SerializeField] private TMP_Text hotkeyLabel;   // "1"~"4" (선택)

    private PlayerItemController controller;
    private ItemDefinition item;     // 주사기 슬롯만 사용, 빠따/무전기는 null
    private int slotIndex;

    /// <summary>slot: PlayerEquipment.Slot* 번호. item은 소모형 슬롯(주사기)만, 나머지는 null.</summary>
    public void Init(PlayerItemController controller, int slot, ItemDefinition item, string displayName, string hotkey)
    {
        this.controller = controller;
        this.item = item;
        slotIndex = slot;
        if (nameLabel != null) nameLabel.text = item != null ? item.itemName : displayName;
        if (hotkeyLabel != null) hotkeyLabel.text = hotkey;

        var btn = GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(() => controller.SelectSlot(slot));
    }

    private void Update()
    {
        if (controller == null) return;

        if (countLabel != null)
            countLabel.text = item != null ? $"×{controller.CountOf(item)}" : "";

        if (cooldownFill != null)
            cooldownFill.fillAmount = (item != null && controller.Me != null) ? controller.Me.CooldownRatio : 0f;

        if (selectedFrame != null)
            selectedFrame.SetActive(controller.HeldSlot == slotIndex);

        // 소모형 슬롯은 다 쓰면 흐리게 (빠따/무전기는 항상 또렷)
        var cg = GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = (item != null && controller.CountOf(item) <= 0) ? 0.35f : 1f;
    }
}
