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

    [Header("시점 — 눈 위치를 머리에 물리기")]
    [Tooltip("눈이 될 머리 본. 비우면 이름으로 자동 탐색(Head). " +
             "루트에 고정하면 달리기 애니가 상체를 숙일 때 카메라가 몸 안으로 들어가 몸통이 뚫려 보인다")]
    [SerializeField] private Transform headBone;
    [Tooltip("머리 본 기준 눈 위치 (몸 기준축: z=앞, y=위). z는 얼굴 앞면보다 앞이어야 머리 속이 안 보인다")]
    [SerializeField] private Vector3 eyeOffset = new Vector3(0f, 0.05f, 0.25f);

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

    /// <summary>현재 시선 상하각 (위=음수). PlayerHeadAim이 아바타 머리 본에 반영.</summary>
    public float Pitch => pitch;

    /// <summary>쓰러짐 등 강제 입력 잠금 — SetControlEnabled와 달리 커서는 잠긴 채 유지.</summary>
    public bool InputLocked { get; set; }

    /// <summary>카메라 피벗 (PlayerKnockdown이 쓰러짐 연출에 사용).</summary>
    public Transform CameraPivot => cameraPivot;

    /// <summary>
    /// 쓰러짐 연출용 (PlayerKnockdown이 프레임마다 굴림). 0=평소(눈이 몸 앞),
    /// 1=누움(눈이 머리 위 하늘 쪽) — 쓰러지는 동안 카메라가 가슴/몸통을 관통하지 않게 한다.
    /// </summary>
    public float LieEyeBlend { get; set; }

    private Vector3 eyeOffsetHeadLocal;   // 머리 본 로컬 기준 눈 오프셋 (서 있는 첫 프레임에 역산)
    private bool eyeLocalCaptured;
    private const float GroundEyeClearance = 0.10f;   // 쓰러지며 바닥 볼 때 카메라 땅 뚫기 방지

    private Vector2 animAxis;
    private float animState;
    private const float AnimFlow = 4.5f;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (headBone == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Head") { headBone = t; break; }
        }
        ApplyCursor();
    }

    /// <summary>
    /// 눈을 머리 본에 따라붙인다. 애니메이터가 본을 갱신한 뒤여야 하므로 LateUpdate.
    /// 회전은 건드리지 않는다 — 시점은 마우스(pitch)와 몸통(yaw)이 결정하고,
    /// 머리 본에서는 위치만 빌려온다 (애니메이션 고갯짓이 화면을 흔들면 멀미남).
    /// </summary>
    private void LateUpdate()
    {
        if (cameraPivot == null || headBone == null) return;

        if (!eyeLocalCaptured)
        {
            // 서 있는 첫 프레임 기준으로 "머리 본 로컬 눈 오프셋"을 역산해 둔다 (쓰러짐 눈 앵커용)
            eyeOffsetHeadLocal = Quaternion.Inverse(headBone.rotation) * (transform.rotation * eyeOffset);
            eyeLocalCaptured = true;
        }

        Vector3 pos = headBone.position + transform.rotation * eyeOffset;
        if (LieEyeBlend > 0f)
        {
            // 쓰러짐~누움: 눈을 "얼굴 앞"에 앵커 — 위치·회전이 모두 얼굴을 따라가는 진짜 눈 시점.
            // (월드 위쪽 고정 오프셋은 몸이 뒤로 넘어가는 중간에 뒤통수 뒤가 되어 자기 머리가 보였음 — v7 픽스)
            Vector3 eyePos = headBone.position + headBone.rotation * eyeOffsetHeadLocal;
            eyePos.y = Mathf.Max(eyePos.y, transform.position.y + GroundEyeClearance);
            pos = Vector3.Lerp(pos, eyePos, LieEyeBlend);
        }
        cameraPivot.position = pos;
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
        if (!controlEnabled || InputLocked) return;
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
