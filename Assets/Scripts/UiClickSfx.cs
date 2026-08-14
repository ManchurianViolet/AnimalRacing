using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// [사운드] UI 클릭·호버음 — 버튼마다 onClick을 배선하지 않고 EventSystem 레이캐스트로 잡는다.
/// 이유: 방 목록·커마 패널·설정 행처럼 런타임에 만들어지는 버튼이 많아서, 씬에서 하나씩
/// 연결하면 새로 생기는 버튼이 조용히 빠진다 (그리고 버튼이 늘 때마다 배선을 잊게 된다).
/// SoundManager가 자동으로 얹으므로 씬 배선 불필요, DontDestroyOnLoad라 두 씬 모두 커버.
/// 커서가 잠긴 동안(1인칭 조작 중)은 UI를 안 쓰므로 통째로 건너뛴다.
/// </summary>
public class UiClickSfx : MonoBehaviour
{
    private readonly List<RaycastResult> hits = new();
    private PointerEventData pointer;
    private GameObject hovered;

    private void Update()
    {
        var es = EventSystem.current;
        if (es == null || Cursor.lockState == CursorLockMode.Locked) { hovered = null; return; }

        var top = TopSelectable(es);

        // 호버음은 다른 버튼으로 넘어간 순간에만 — 같은 버튼 위에 머무는 동안은 침묵
        if (top != hovered)
        {
            hovered = top;
            if (top != null) SoundManager.PlaySfx(SfxId.UiHover);
        }

        if (top != null && Input.GetMouseButtonDown(0)) SoundManager.PlaySfx(SfxId.UiClick);
    }

    /// <summary>커서 바로 아래의 누를 수 있는 UI (없으면 null).</summary>
    private GameObject TopSelectable(EventSystem es)
    {
        if (pointer == null) pointer = new PointerEventData(es);
        pointer.position = Input.mousePosition;

        hits.Clear();
        es.RaycastAll(pointer, hits);
        if (hits.Count == 0) return null;

        // 맨 위 히트만 본다 — 그 아래 버튼은 어차피 가려져서 못 누른다.
        // (버튼 위의 라벨/아이콘이 맞아도 GetComponentInParent가 버튼 본체를 찾아준다)
        var sel = hits[0].gameObject.GetComponentInParent<Selectable>();
        return sel != null && sel.IsInteractable() ? sel.gameObject : null;
    }
}
