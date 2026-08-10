using UnityEngine;

/// <summary>
/// 플레이어에 부착. 화면 중앙 레이캐스트로 IInteractable을 찾아 E키 상호작용.
/// 프롬프트는 임시 OnGUI (추후 진짜 HUD로 이전).
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private float range = 3f;

    private IInteractable current;

    /// <summary>현재 조준 중인 상호작용 대상의 프롬프트. HUD가 표시.</summary>
    public string CurrentPrompt => current != null ? current.Prompt : "";

    private void Update()
    {
        current = null;

        // 커서가 풀려 있으면(ESC 메뉴 등 UI 사용 중) 상호작용 중단 — 아이템/피규어와 같은 규칙
        if (Cursor.lockState != CursorLockMode.Locked) return;

        var cam = Camera.main;
        if (cam == null) return;

        var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out var hit, range))
        {
            var it = hit.collider.GetComponentInParent<IInteractable>();
            if (it != null && it.CanInteract()) current = it;
        }

        if (current != null && Input.GetKeyDown(KeyCode.E))
            current.Interact();
    }

}
