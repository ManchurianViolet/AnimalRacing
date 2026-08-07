using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 1등/꼴등 예상 슬롯.
/// 드롭되면 레인번호칸(번호 TMP 포함) 복제본이 Slot 위치에 전시됨.
/// 클릭 = 선택 취소.
/// </summary>
public class BetDropZone : MonoBehaviour, IDropHandler
{
    [Tooltip("전시 복제본이 놓일 위치. 비우면 존 정중앙")]
    [SerializeField] private Transform slot;

    [Tooltip("보조 라벨 (선택). 비었을 땐 안내문구, 채워지면 동물 이름")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private string emptyText = "여기에 드롭";

    public int SelectedId { get; private set; } = -1;
    public string SelectedName { get; private set; }

    public System.Action onChanged;

    private RectTransform displayClone;

    private Transform Slot => slot != null ? slot : transform;

    private void Awake() => UpdateVisual();

    public void OnDrop(PointerEventData e)
    {
        var icon = e.pointerDrag != null ? e.pointerDrag.GetComponent<DraggableBetIcon>() : null;
        if (icon == null) return;
        Set(icon.RacerId, icon.DisplayName, icon.GhostTemplate);
    }

    public void Set(int id, string name, RectTransform template)
    {
        SelectedId = id;
        SelectedName = name;

        if (displayClone != null) Destroy(displayClone.gameObject);
        if (template != null)
        {
            displayClone = Instantiate(template, Slot);
            displayClone.sizeDelta = template.rect.size;
            displayClone.anchorMin = displayClone.anchorMax = new Vector2(0.5f, 0.5f);
            displayClone.anchoredPosition = Vector2.zero;

            var cg = displayClone.GetComponent<CanvasGroup>();
            if (cg == null) cg = displayClone.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
        }

        UpdateVisual();
        onChanged?.Invoke();
    }

    public void Clear(bool notify = true)
    {
        SelectedId = -1;
        SelectedName = null;
        if (displayClone != null) { Destroy(displayClone.gameObject); displayClone = null; }
        UpdateVisual();
        if (notify) onChanged?.Invoke();
    }

    private void UpdateVisual()
    {
        if (label != null)
            label.text = SelectedId >= 0 ? SelectedName : emptyText;
    }
}
