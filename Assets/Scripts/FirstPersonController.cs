using UnityEngine;

/// <summary>
/// 1인칭 플레이어 컨트롤러.
/// SetControlEnabled(false): 단말기 사용 등 UI 조작 중 이동/시점 잠금 + 커서 해제.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("이동")]
    [SerializeField] private float walkSpeed = 3.5f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -20f;

    [Header("시점")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float pitchMin = -80f;
    [SerializeField] private float pitchMax = 80f;

    [Header("애니메이터 (에셋 규약)")]
    [SerializeField] private Animator animator;
    [SerializeField] private string horID = "Hor";
    [SerializeField] private string vertID = "Vert";
    [SerializeField] private string stateID = "State";
    [SerializeField] private string jumpID = "IsJump";

    private CharacterController controller;
    private float pitch;
    private float verticalVel;
    private bool controlEnabled = true;

    /// <summary>현재 조작 가능 상태 (false = 단말기/ATM 등 UI 사용 중). HUD가 참조.</summary>
    public bool ControlEnabled => controlEnabled;

    private Vector2 animAxis;
    private float animState;
    private const float AnimFlow = 4.5f;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        ApplyCursor();
    }

    /// <summary>UI 사용 중 조작 잠금/해제. 커서 상태도 함께 전환.</summary>
    public void SetControlEnabled(bool enabled)
    {
        controlEnabled = enabled;
        ApplyCursor();
        if (!enabled) AnimateUpdate(Vector2.zero, false, false);   // 제자리 idle
    }

    private void ApplyCursor()
    {
        Cursor.lockState = controlEnabled ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !controlEnabled;
    }

    private void Update()
    {
        if (!controlEnabled) return;
        LookUpdate();
        MoveUpdate();
    }

    private void LookUpdate()
    {
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up, mx);
        pitch = Mathf.Clamp(pitch - my, pitchMin, pitchMax);
        if (cameraPivot != null)
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void MoveUpdate()
    {
        Vector2 axis = new(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        axis = Vector2.ClampMagnitude(axis, 1f);
        bool isRun = Input.GetKey(KeyCode.LeftShift);
        bool grounded = controller.isGrounded;

        if (grounded && verticalVel < 0f) verticalVel = -2f;
        if (grounded && Input.GetButtonDown("Jump"))
            verticalVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
        verticalVel += gravity * Time.deltaTime;

        Vector3 move = (transform.right * axis.x + transform.forward * axis.y)
                     * (isRun ? runSpeed : walkSpeed);
        move.y = verticalVel;
        controller.Move(move * Time.deltaTime);

        AnimateUpdate(axis, isRun, !grounded);
    }

    private void AnimateUpdate(Vector2 axis, bool isRun, bool isAir)
    {
        if (animator == null) return;

        animAxis = Vector2.MoveTowards(animAxis, axis, AnimFlow * Time.deltaTime);
        animState = Mathf.MoveTowards(animState, isRun ? 1f : 0f, AnimFlow * Time.deltaTime);

        animator.SetFloat(horID, animAxis.x);
        animator.SetFloat(vertID, animAxis.y);
        animator.SetFloat(stateID, animState);
        animator.SetBool(jumpID, isAir);
    }
}
