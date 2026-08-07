using UnityEngine;

/// <summary>
/// 조준형 아이템(주사기 2종 / 무전기 2종)을 들었을 때 오른팔을 들어올려
/// 1인칭 시야 안에 소품이 보이게 한다 (NetPlayer 루트 = Animator와 같은 오브젝트에 부착).
///
/// 방식: 휴머노이드 손 IK. 애니 클립을 새로 구하지 않고 "손을 눈 앞 어디에 둘지"를 좌표로 지정한다.
/// - 장점: 조준 상하각(pitch)을 따라 손이 같이 움직인다 — 위를 보면 주사기도 같이 올라감.
/// - 기준점은 카메라가 아니라 "머리 본 + 눈 오프셋"이라 원격 아바타(카메라 없음)도 같은 계산으로 재생된다.
///   내 pitch = FirstPersonController, 남의 pitch = PlayerHeadAim이 동기화한 수신값.
///
/// ⚠ 컨트롤러(PlayerMovement) Movement Layer의 IK Pass가 켜져 있어야 OnAnimatorIK가 호출된다.
/// ⚠ 이 캐릭터는 팔이 짧다(상완 0.292 + 전완 0.230 = 0.52m). 어깨~눈이 이미 0.37m라
///   손 목표를 멀리 두면 팔이 닿지 않아 쭉 뻗은 막대기가 된다 — reachSafety로 어깨 기준 거리를 클램프한다.
/// </summary>
public class PlayerAimPose : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private FirstPersonController fpc;
    [SerializeField] private PlayerEquipment equipment;
    [SerializeField] private PlayerHeadAim headAim;
    [SerializeField] private PlayerKnockdown knockdown;

    [Header("손 위치 — 눈 기준, 조준 방향축 (x=오른쪽 / y=위 / z=앞)")]
    [Tooltip("z를 키우면 팔이 앞으로 뻗고(=화면에서 소품이 작아지고), y를 올리면 화면 위로 올라온다. " +
             "어깨가 눈 뒤 0.37m라 z 0.45쯤까지는 팔이 여유 있게 닿는다")]
    [SerializeField] private Vector3 handOffset = new Vector3(0.17f, -0.14f, 0.42f);
    [Tooltip("베팅 방 피규어를 쥘 때의 손 위치 — 시야 중앙을 비우도록 더 아래·오른쪽")]
    [SerializeField] private Vector3 handOffsetFigurine = new Vector3(0.13f, -0.21f, 0.20f);

    [Header("소품 기울기 — 0=수직으로 세움 / 90=조준 방향으로 눕힘")]
    [Tooltip("주사기: 조준해서 쏘는 물건이라 앞으로 겨누게 눕힌다")]
    [SerializeField] private float propPitch = 70f;
    [Tooltip("무전기: 겨누는 물건이 아니라 세워 든다")]
    [SerializeField] private float propPitchRadio = 20f;
    [Tooltip("베팅 방 피규어: 들여다보는 물건이라 똑바로 세워 든다")]
    [SerializeField] private float propPitchFigurine = 5f;
    [Tooltip("손 회전 미세 조정 (도) — 위 값으로 안 잡히는 손목 각도만")]
    [SerializeField] private Vector3 handEulerTweak = Vector3.zero;

    [Header("팔꿈치")]
    [Tooltip("팔꿈치를 끌어당길 지점 (눈 기준, 조준 방향축) — 아래·바깥이어야 겨드랑이가 안 뜬다")]
    [SerializeField] private Vector3 elbowOffset = new Vector3(0.42f, -0.45f, 0.02f);
    [Range(0f, 1f)]
    [SerializeField] private float elbowWeight = 0.6f;

    [Header("블렌딩")]
    [Tooltip("슬롯 전환 시 팔이 올라가고 내려가는 속도 (1/초)")]
    [SerializeField] private float blendSpeed = 7f;
    [Tooltip("어깨 기준 도달 거리를 팔 길이의 이 비율로 제한 (1.0=완전히 쭉 폄)")]
    [Range(0.5f, 1f)]
    [SerializeField] private float reachSafety = 0.94f;

    private Transform headBone, rightUpperArm, handBone;
    private float armLength;
    private float weight;   // 현재 IK 가중치 (0=애니 그대로, 1=완전히 IK 자세)

    // 휴머노이드 IK의 "회전 목표"는 손 본의 회전과 리그마다 다른 고정량만큼 어긋나 있다
    // (본 로컬축 정의 차이). 처음 팔을 들 때 1프레임만 무회전을 넣어보고 그 어긋난 양을 실측해 둔다.
    private Quaternion goalCalib = Quaternion.identity;
    private bool calibrated, calibPending;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (fpc == null) fpc = GetComponent<FirstPersonController>();
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();
        if (headAim == null) headAim = GetComponent<PlayerHeadAim>();
        if (knockdown == null) knockdown = GetComponent<PlayerKnockdown>();

        if (animator == null || !animator.isHuman) { enabled = false; return; }

        headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        handBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        if (rightUpperArm != null && lower != null && handBone != null)
            armLength = Vector3.Distance(rightUpperArm.position, lower.position)
                      + Vector3.Distance(lower.position, handBone.position);
    }

    /// <summary>캘리브레이션 프레임의 결과 읽기 — 애니메이터 평가가 끝난 뒤여야 하므로 LateUpdate.</summary>
    private void LateUpdate()
    {
        if (!calibPending || handBone == null) return;
        goalCalib = handBone.rotation;   // 목표에 무회전을 넣었으므로 결과가 곧 어긋난 양
        calibrated = true;
        calibPending = false;
    }

    /// <summary>지금 팔을 들어야 하는 상태인가 — 조준형 슬롯(주사기 2종/무전기 2종) 또는 베팅 방 피규어.</summary>
    private bool WantsRaise
    {
        get
        {
            if (equipment == null) return false;
            if (knockdown != null && knockdown.IsDown) return false;   // 누워 있으면 소품도 꺼져 있다

            // 베팅 방 안에선 무기가 숨겨져 있다 — 피규어를 쥐었을 때만 팔을 든다
            // (HeldSlot은 그대로라, 이 가드가 없으면 빈손으로 주사기 자세를 잡는다)
            if (FigurineBetting.PointerBusy) return FigurineBetting.HeldFigurine != null;

            int s = equipment.HeldSlot;
            return s == PlayerEquipment.SlotBoost || s == PlayerEquipment.SlotSlow ||
                   s == PlayerEquipment.SlotRadioSkill || s == PlayerEquipment.SlotRadioExec;
        }
    }

    /// <summary>조준 회전 = 몸통 yaw × 시선 pitch (내 것은 FPC, 남의 것은 동기화 수신값).</summary>
    private Quaternion AimRotation
    {
        get
        {
            float pitch = 0f;
            if (fpc != null && fpc.isActiveAndEnabled) pitch = fpc.Pitch;
            else if (headAim != null) pitch = headAim.CurrentPitch;
            return transform.rotation * Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || animator == null || headBone == null) return;

        // 최초 1프레임 캘리브레이션: 팔은 그대로 둔 채(위치 가중치 0) 회전 목표만 무회전으로 넣어 본다
        if (!calibrated && WantsRaise && handBone != null)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f);
            animator.SetIKRotation(AvatarIKGoal.RightHand, Quaternion.identity);
            calibPending = true;
            return;
        }

        weight = Mathf.MoveTowards(weight, WantsRaise ? 1f : 0f, blendSpeed * Time.deltaTime);
        if (weight <= 0.001f)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
            return;
        }

        Quaternion aim = AimRotation;
        Vector3 eye = headBone.position + transform.rotation * (fpc != null ? fpc.EyeOffset : new Vector3(0f, 0.05f, 0.25f));

        // 손 목표 — 팔이 닿는 거리로 클램프 (팔 길이 0.52m, 어깨~눈 0.37m라 금방 한계에 걸린다)
        bool holdingFigurine = FigurineBetting.HeldFigurine != null;
        Vector3 target = eye + aim * (holdingFigurine ? handOffsetFigurine : handOffset);
        if (rightUpperArm != null && armLength > 0f)
        {
            Vector3 fromShoulder = target - rightUpperArm.position;
            float max = armLength * reachSafety;
            if (fromShoulder.magnitude > max)
                target = rightUpperArm.position + fromShoulder.normalized * max;
        }

        // 손 회전 — 소품은 손 로컬 +Y로 뻗으므로, 그 축을 조준 기준 "위~앞" 사이로 눕힌다
        bool radio = equipment != null && (equipment.HeldSlot == PlayerEquipment.SlotRadioSkill ||
                                           equipment.HeldSlot == PlayerEquipment.SlotRadioExec);
        float r = (holdingFigurine ? propPitchFigurine : radio ? propPitchRadio : propPitch) * Mathf.Deg2Rad;
        Vector3 propUp = aim * new Vector3(0f, Mathf.Cos(r), Mathf.Sin(r));
        Quaternion rot = Quaternion.LookRotation(aim * Vector3.right, propUp) * Quaternion.Euler(handEulerTweak);

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, weight);
        animator.SetIKPosition(AvatarIKGoal.RightHand, target);
        animator.SetIKRotation(AvatarIKGoal.RightHand, rot * Quaternion.Inverse(goalCalib));

        animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, weight * elbowWeight);
        animator.SetIKHintPosition(AvatarIKHint.RightElbow, eye + aim * elbowOffset);
    }
}
