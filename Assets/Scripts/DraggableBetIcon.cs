using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 드래그 핸들 (동물아이콘Image에 부착).
/// 고스트는 Panel(원본과 같은 좌표계) 소속으로 생성되어 크기가 원본과 동일하고,
/// 위치는 로컬 평면 좌표로 이동시켜 캔버스 평면에 딱 붙어 다닌다.
/// </summary>
public class DraggableBetIcon : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int RacerId { get; private set; }
    public string DisplayName { get; private set; }
    public RectTransform GhostTemplate => ghostTemplate;

    private Canvas rootCanvas;
    private RectTransform ghostTemplate;
    private RectTransform ghost;
    private RectTransform ghostParent;   // 고스트가 움직일 평면 (패널)

    public void Init(int racerId, string displayName, Canvas root, RectTransform ghostTemplate)
    {
        RacerId = racerId;
        DisplayName = displayName;
        rootCanvas = root;
        this.ghostTemplate = ghostTemplate;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (ghostTemplate == null || rootCanvas == null) return;

        // 고스트의 부모 = 원본의 조상 중 패널 (같은 좌표계 → 스케일 계보 보존)
        // BettingPanel이 붙은 오브젝트를 찾고, 없으면 캔버스로 폴백
        var panel = GetComponentInParent<BettingPanel>();
        ghostParent = panel != null ? (RectTransform)panel.transform
                                    : (RectTransform)rootCanvas.transform;

        ghost = Instantiate(ghostTemplate, ghostParent);
        ghost.SetAsLastSibling();                        // 패널 내 최상단에 그리기
        ghost.localScale = GetRelativeScale(ghostTemplate, ghostParent);
        ghost.sizeDelta = ghostTemplate.rect.size;
        ghost.localRotation = Quaternion.identity;

        var cg = ghost.GetComponent<CanvasGroup>();
        if (cg == null) cg = ghost.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0.55f;
        cg.blocksRaycasts = false;

        MoveGhost(e);
    }

    public void OnDrag(PointerEventData e) => MoveGhost(e);

    public void OnEndDrag(PointerEventData e)
    {
        if (ghost != null) Destroy(ghost.gameObject);
    }

    private void MoveGhost(PointerEventData e)
    {
        if (ghost == null) return;
        // 부모 평면의 "로컬" 좌표로 변환 → 깊이 어긋남 없이 평면에 밀착
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                ghostParent, e.position, e.pressEventCamera, out var localPoint))
            ghost.anchoredPosition = localPoint;
    }

    /// <summary>원본이 최종적으로 보이던 크기를 새 부모 아래에서 재현하는 로컬 스케일.</summary>
    private static Vector3 GetRelativeScale(RectTransform source, RectTransform newParent)
    {
        Vector3 world = source.lossyScale;
        Vector3 parent = newParent.lossyScale;
        return new Vector3(
            parent.x != 0f ? world.x / parent.x : 1f,
            parent.y != 0f ? world.y / parent.y : 1f,
            parent.z != 0f ? world.z / parent.z : 1f);
    }
}
