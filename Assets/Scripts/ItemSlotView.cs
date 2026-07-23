using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아이템 슬롯 하나 (부스트/감속 공용 프리팹).
/// 표시: 이름, 개수, 쿨다운 게이지(Filled Image), 선택 하이라이트.
/// 클릭으로도 선택 가능 (Button 연결 시).
/// </summary>
public class ItemSlotView : MonoBehaviour
{
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text countLabel;
    [Tooltip("Image Type=Filled 로 설정. 쿨다운 남은 비율만큼 채워짐")]
    [SerializeField] private Image cooldownFill;
    [Tooltip("선택 중일 때 켜지는 테두리/배경 오브젝트")]
    [SerializeField] private GameObject selectedFrame;
    [SerializeField] private TMP_Text hotkeyLabel;   // "1" / "2" (선택)

    private PlayerItemController controller;
    private ItemDefinition item;

    public void Init(PlayerItemController controller, ItemDefinition item, string hotkey)
    {
        this.controller = controller;
        this.item = item;
        if (nameLabel != null) nameLabel.text = item.itemName;
        if (hotkeyLabel != null) hotkeyLabel.text = hotkey;

        var btn = GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(() => controller.Select(item));
    }

    private void Update()
    {
        if (controller == null || controller.Me == null || item == null) return;

        int count = controller.CountOf(item);
        if (countLabel != null) countLabel.text = $"×{count}";

        if (cooldownFill != null)
            cooldownFill.fillAmount = controller.Me.CooldownRatio;

        if (selectedFrame != null)
            selectedFrame.SetActive(controller.Selected == item);

        // 소진되면 흐리게
        var cg = GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = count > 0 ? 1f : 0.35f;
    }
}
