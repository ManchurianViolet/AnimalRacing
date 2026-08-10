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
    [SerializeField] private Vector3 handOffset = new Vector3(0.08f, -0.03f, 1.2f);
    [Tooltip("베팅 방 피규어를 쥘 때의 손 위치 (닿는 데까지 자동 클램프). " +
             "화면 중앙에 두면 팔 한계 거리의 거대한 손이 시야·조준을 다 가린다 — 우하단 구석이 정답")]
    [SerializeField] private Vector3 handOffsetFigurine = new Vector3(0.22f, -0.22f, 0.42f);

    [Header("주사기 찌르기 연출")]
    [Tooltip("발사 순간 손을 당기는 거리(m) — 팔이 평소 최대 신장이라 '당겼다 스냅'이 찌르기로 읽힌다")]
    [SerializeField] private float thrustPullDistance = 0.22f;
    [Tooltip("찌르기 전체 시간(초) — 앞 35%가 당김, 뒤 65%가 앞으로 스냅")]
    [SerializeField] private float thrustSeconds = 0.28f;


    [Header("베팅 방 피규어 — 내 화면 전용 배치")]
    [Tooltip("팔을 안 드는 모드(raiseArmForFigurine=꺼짐)에서만 쓰는 눈 기준 위치 (조준축: x=오른쪽 / y=위 / z=앞). " +
             "팔을 드는 기본 모드에선 아래 figurineHandOffset(손 본 기준)이 쓰인다")]
    [SerializeField] private Vector3 figurineViewOffset = new Vector3(0.045f, -0.035f, 0.18f);
    [Tooltip("팔을 든 채 쥘 때, IK가 끝난 손 본 기준 피규어 위치 (조준축: x=오른쪽 / y=위 / z=앞). " +
             "z를 음수로 두면 손보다 카메라 쪽이라 손바닥에 가려지지 않는다. " +
             "눈 기준 고정이 아니라 손 기준이어야 하늘/땅을 볼 때(팔 길이 클램프) 손과 안 갈라진다")]
    [SerializeField] private Vector3 figurineHandOffset = new Vector3(-0.015f, 0.10f, 0.06f);
    [Tooltip("피규어의 가장 긴 변을 이 길이(m)로 맞춘다 — 말이든 치킨이든 화면에서 같은 크기로 보인다. " +
             "손 바로 앞(약 0.15m)이라 작아도 크게 보인다")]
    [SerializeField] private float figurineViewSize = 0.18f;
    [Tooltip("피규어 회전 (조준 기준). 동물은 +z가 전진 방향이라 0이면 엉덩이만 보인다 — " +
             "180이 정면, 140쯤이면 비스듬한 3/4 앞모습이라 어떤 동물인지 가장 잘 읽힌다")]
    [SerializeField] private Vector3 figurineViewEuler = new Vector3(0f, 140f, 0f);

    [Tooltip("피규어를 쥘 때도 팔을 들어올릴지. 끄면 팔을 내린 채 피규어만 허공에 보인다")]
    [SerializeField] private bool raiseArmForFigurine = true;

    [Header("소품 기울기 — 0=수직으로 세움 / 90=조준 방향으로 눕힘")]
    [Tooltip("주사기: 조준해서 쏘는 물건이라 앞으로 겨누게 눕힌다")]
    [SerializeField] private float propPitch = 70f;
    [Tooltip("무전기: 겨누는 물건이 아니라 세워 든다")]
    [SerializeField] private float propPitchRadio = 20f;
    [Tooltip("베팅 방 피규어: 손바닥이 위를 보게 눕혀 받침대로 만든다 (0이면 손바닥이 카메라를 정면으로 봐서 화면을 가림)")]
    [SerializeField] private float propPitchFigurine = 70f;
    [Tooltip("손 회전 미세 조정 (도) — 위 값으로 안 잡히는 손목 각도만")]
    [SerializeField] private Vector3 handEulerTweak = Vector3.zero;

    [Header("팔꿈치")]
    [Tooltip("팔꿈치를 끌어당길 지점 (눈 기준, 조준 방향축) — 아래·바깥이어야 겨드랑이가 안 뜬다")]
    [SerializeField] private Vector3 elbowOffset = new Vector3(0.42f, -0.45f, 0.02f);
    [Range(0f, 1f)]
    [SerializeField] private float elbowWeight = 0.6f;

    [Header("무전기 손 모양")]
    [Tooltip("무전기를 들면 손가락을 펴 손바닥에 얹은 모양으로 만든다. " +
             "주사기는 가늘어서 쥔 포즈가 자연스럽지만, 무전기는 두꺼워 손가락이 몸통을 파고든다")]
    [SerializeField] private bool openHandForRadio = true;
    [Tooltip("펴는 정도 (0=애니 그립 그대로, 1=완전히 편 기본 포즈)")]
    [Range(0f, 1f)]
    [SerializeField] private float openHandAmount = 1f;
    [Tooltip("쥔 손 ↔ 편 손 전환 속도 (1/초)")]
    [SerializeField] private float openHandSpeed = 12f;
    [Tooltip("엄지만 접은 채로 둬서 기기를 감싸 쥐게 한다 (1인칭에서 화면 맨 오른쪽에 오는 손가락이 엄지다)")]
    [SerializeField] private bool curlThumbOnRadio = true;
    [Tooltip("무전기 파지 때 엄지 마디마다 추가로 얹는 굽힘(본 로컬 오일러, 도) — 버튼을 꾹 누르는 모양. 이 리그는 X축이 굽힘 축 (실측)")]
    [SerializeField] private Vector3 radioThumbCurl = new Vector3(62f, 0f, 0f);

    [Header("블렌딩")]
    [Tooltip("슬롯 전환 시 팔이 올라가고 내려가는 속도 (1/초)")]
    [SerializeField] private float blendSpeed = 7f;
    [Tooltip("어깨 기준 도달 거리를 팔 길이의 이 비율로 제한 (1.0=완전히 쭉 폄)")]
    [Range(0.5f, 1f)]
    [SerializeField] private float reachSafety = 0.99f;

    private Transform headBone, rightUpperArm, handBone;
    private float armLength;
    private float weight;   // 현재 IK 가중치 (0=애니 그대로, 1=완전히 IK 자세)

    // 휴머노이드 IK의 "회전 목표"는 손 본의 회전과 리그마다 다른 고정량만큼 어긋나 있다
    // (본 로컬축 정의 차이). 처음 팔을 들 때 1프레임만 무회전을 넣어보고 그 어긋난 양을 실측해 둔다.
    private Quaternion goalCalib = Quaternion.identity;
    private bool calibrated, calibPending;

    // 오른손 손가락 본과 그 "펴진" 기준 회전 — 프리팹에 저장된 포즈가 곧 편 손이다.
    // ⚠ 반드시 Awake에서 읽어야 한다. Animator의 첫 평가는 그 다음 Update 사이클이라
    //    Awake 시점의 본 회전만이 애니가 안 섞인 원본 포즈다.
    private Transform[] fingerBones;
    private Quaternion[] fingerRest;
    private bool[] fingerIsThumb;
    private float openHandWeight;   // 현재 펴짐 정도 (0=쥠, 1=폄)

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (fpc == null) fpc = GetComponent<FirstPersonController>();
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();
        if (headAim == null) headAim = GetComponent<PlayerHeadAim>();
        if (knockdown == null) knockdown = GetComponent<PlayerKnockdown>();

        if (animator == null || !animator.isHuman) { enabled = false; return; }

        // ⚠ 1인칭은 카메라가 머리 본 안에 있어 자기 몸이 화면에 안 잡힌다. 그 상태로 컬링이 걸리면
        // Unity가 애니메이터의 IK 패스를 통째로 건너뛰어 OnAnimatorIK가 아예 호출되지 않는다
        // (증상: 아래를 내려다볼 때만 팔이 올라가고, 정면을 보면 슬그머니 내려간다).
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        handBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        if (rightUpperArm != null && lower != null && handBone != null)
            armLength = Vector3.Distance(rightUpperArm.position, lower.position)
                      + Vector3.Distance(lower.position, handBone.position);

        CacheFingerRest();
    }

    /// <summary>오른손 손가락 본과 그 원본(편) 회전을 캐시한다. 이 리그는 손가락당 2본이라 10개다.</summary>
    private void CacheFingerRest()
    {
        var bones = new System.Collections.Generic.List<Transform>();
        var rest = new System.Collections.Generic.List<Quaternion>();
        var thumb = new System.Collections.Generic.List<bool>();

        for (int i = (int)HumanBodyBones.RightThumbProximal; i <= (int)HumanBodyBones.RightLittleDistal; i++)
        {
            var hb = (HumanBodyBones)i;
            var t = animator.GetBoneTransform(hb);
            if (t == null) continue;      // Distal이 없는 리그가 많다 — 있는 것만 쓴다
            bones.Add(t);
            rest.Add(t.localRotation);
            thumb.Add(hb == HumanBodyBones.RightThumbProximal ||
                      hb == HumanBodyBones.RightThumbIntermediate ||
                      hb == HumanBodyBones.RightThumbDistal);
        }

        fingerBones = bones.ToArray();
        fingerRest = rest.ToArray();
        fingerIsThumb = thumb.ToArray();
    }

    /// <summary>캘리브레이션 결과 읽기 + 피규어 배치 — 둘 다 애니메이터 평가 후여야 하므로 LateUpdate.</summary>
    private void LateUpdate()
    {
        if (calibPending && handBone != null)
        {
            goalCalib = handBone.rotation;   // 목표에 무회전을 넣었으므로 결과가 곧 어긋난 양
            calibrated = true;
            calibPending = false;
        }
        ApplyOpenHand();
        PlaceHeldFigurine();
    }

    /// <summary>
    /// 무전기를 든 동안 오른손 손가락을 편다.
    /// HoldRight 레이어(CombatIdle1H01)는 가는 무기를 쥐는 그립이라 손가락이 66도 굽는데,
    /// 무전기는 몸통이 두꺼워서 그 손가락이 그대로 메시를 파고든다.
    /// 애니 레이어를 늘리는 대신 손가락 본만 원본 포즈로 되돌리는 쪽이 싸고 부작용이 없다.
    /// LateUpdate라 애니메이터 평가 뒤에 덮어쓴다 — 원격 아바타도 같은 경로로 재생된다.
    /// </summary>
    private void ApplyOpenHand()
    {
        if (fingerBones == null || fingerBones.Length == 0) return;

        bool down = knockdown != null && knockdown.IsDown;
        // 피규어를 쥐면 엄지까지 완전히 편다 — 손바닥이 피규어 받침대가 되게.
        // (피규어는 내 아바타에만 존재하므로 Local 가드 — 원격 아바타 손이 따라 펴지면 안 된다)
        bool holdingFig = !down && equipment != null && PlayerEquipment.Local == equipment &&
                          FigurineBetting.HeldFigurine != null;
        bool radio = !down && openHandForRadio && equipment != null &&
                     (equipment.HeldSlot == PlayerEquipment.SlotRadioSkill ||
                      equipment.HeldSlot == PlayerEquipment.SlotRadioExec);
        bool want = radio || holdingFig;

        openHandWeight = Mathf.MoveTowards(openHandWeight, want ? 1f : 0f, openHandSpeed * Time.deltaTime);
        if (openHandWeight <= 0.001f) return;

        float t = openHandWeight * openHandAmount;
        for (int i = 0; i < fingerBones.Length; i++)
        {
            if (fingerBones[i] == null) continue;
            // 무전기는 엄지를 애니 그립 포즈 위에 추가로 굽힌다 — 버튼(PTT)을 엄지로 꾹 누르는 모양.
            // 애니가 매 프레임 회전을 다시 쓰므로 곱셈이 누적되지 않는다 (§11 본 오프셋 법칙의 회전판)
            if (!holdingFig && curlThumbOnRadio && fingerIsThumb[i])
            {
                fingerBones[i].localRotation *= Quaternion.Euler(radioThumbCurl * openHandWeight);
                continue;
            }
            fingerBones[i].localRotation =
                Quaternion.Slerp(fingerBones[i].localRotation, fingerRest[i], t);
        }
    }

    /// <summary>
    /// 쥔 피규어를 "IK가 끝난 실제 손 본 + 오프셋"에 그린다 (크기만 화면 기준 정규화).
    /// - 위치를 손 본에서 가져오는 이유: 손 IK 목표는 팔 길이로 클램프되므로, 피규어를 눈 기준
    ///   고정 위치에 두면 하늘/땅을 볼 때 손만 뒤처져 피규어와 갈라진다(실사고). 손 본은 클램프
    ///   결과까지 반영된 최종 위치라 팔이 어디서 멈추든 피규어가 손에 붙어 있다.
    /// - 크기는 배율 고정이 아니라 "가장 긴 변 = figurineViewSize" — 덩치가 제각각(호랑이 3.0m /
    ///   치킨 0.9m)이라 배율로는 화면 크기가 안 맞는다.
    /// - 오프셋 z를 음수로 둬 손보다 카메라 쪽에 그린다 — 손바닥에 가려지지 않게.
    /// 피규어는 내 방에만 로컬 생성되어 남의 화면엔 존재하지 않으므로 이렇게 해도 안전하다.
    /// </summary>
    private void PlaceHeldFigurine()
    {
        // 내 아바타에서만 — 원격 아바타도 이 컴포넌트를 갖고 있어 가드가 없으면 남이 내 피규어를 끌어간다
        if (equipment == null || PlayerEquipment.Local != equipment) return;

        var fig = FigurineBetting.HeldFigurine;
        if (fig == null || headBone == null) return;

        Quaternion aim = AimRotation;
        Quaternion rot = aim * Quaternion.Euler(figurineViewEuler);
        fig.transform.rotation = rot;

        // 덩치가 제각각이라 배율이 아니라 "화면에서의 실제 크기"로 맞춘다
        float world = figurineViewSize / Mathf.Max(0.0001f, fig.BaseSize);
        float parent = fig.transform.parent != null ? Mathf.Max(0.0001f, fig.transform.parent.lossyScale.x) : 1f;
        fig.transform.localScale = Vector3.one * (world / parent);

        Vector3 goal;
        if (raiseArmForFigurine && handBone != null)
            goal = handBone.position + aim * figurineHandOffset;
        else
        {
            // 팔을 안 드는 모드에선 손이 허벅지 옆이라 기준으로 못 쓴다 — 눈 앞 고정 위치로 폴백
            Vector3 eye = headBone.position + transform.rotation * (fpc != null ? fpc.EyeOffset : new Vector3(0f, 0.05f, 0.25f));
            goal = eye + aim * figurineViewOffset;
        }

        // pivot이 발밑이라 그대로 놓으면 몸통이 화면 위로 치우친다.
        // 보정량은 Init 때 한 번 잰 고정값(BaseCenter)으로 계산한다 — 매 프레임 bounds를 다시 읽으면
        // 직전 프레임의 보정 결과가 입력으로 되먹여져 위치가 화면 밖으로 밀려난다.
        fig.transform.position = goal - rot * (fig.BaseCenter * world);
    }

    /// <summary>지금 팔을 들어야 하는 상태인가 — 조준형 슬롯(주사기 2종/무전기 2종) 또는 베팅 방 피규어.</summary>
    private bool WantsRaise
    {
        get
        {
            if (equipment == null) return false;
            if (knockdown != null && knockdown.IsDown) return false;   // 누워 있으면 소품도 꺼져 있다

            // 베팅 방 안에선 무기가 숨겨져 있다 — 여기서 팔을 들 이유는 피규어뿐이다
            // (HeldSlot은 그대로라, 이 가드가 없으면 빈손으로 주사기 자세를 잡는다)
            // 단 피규어는 팔보다 멀리 그려지므로, 팔을 들면 손이 앞을 가린다 — 기본은 들지 않는다
            if (FigurineBetting.PointerBusy)
                return raiseArmForFigurine && FigurineBetting.HeldFigurine != null;

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

    private float thrustElapsed = float.MaxValue;   // 찌르기 경과 시간 (MaxValue = 휴면)

    private void OnEnable() => GameEvents.OnItemUsed += HandleItemUsed;
    private void OnDisable() => GameEvents.OnItemUsed -= HandleItemUsed;

    /// <summary>
    /// 주사기(부스트/감속) 발사 순간 찌르기 시작. OnItemUsed는 게이트웨이가 전 클라로 중계하므로
    /// 원격 아바타도 자기 주인의 발사에 맞춰 각자 로컬 재생 — 네트워크 추가 통신 0 (부스트 먼지 철학).
    /// </summary>
    private void HandleItemUsed(int playerId, ItemDefinition item, int racerId)
    {
        if (item == null || (item.kind != ItemKind.Boost && item.kind != ItemKind.Slow)) return;
        if (!IsAvatarOf(playerId)) return;
        thrustElapsed = 0f;
    }

    /// <summary>이 아바타가 그 플레이어의 것인가 — 온라인은 포톤 소유자, 오프라인은 나뿐 (봇은 아바타 없음).</summary>
    private bool IsAvatarOf(int playerId)
    {
        var pv = GetComponent<Photon.Pun.PhotonView>();
        if (pv != null && Photon.Pun.PhotonNetwork.IsConnected) return pv.OwnerActorNr == playerId;
        return playerId == NetworkPlayers.LocalPlayerId;
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

        // [주사기 찌르기] 발사 순간 당겼다(35%) 앞으로 스냅(65%).
        // ⚠ 반드시 클램프 뒤에 적용 — handOffset(z 1.2)은 팔 한계 너머라 클램프가 항상 작동 중이어서,
        //   클램프 앞에서 당기면 "한계 밖에서 한계 밖으로" 이동일 뿐 잘린 결과가 그대로다 (실측 1.5cm — 실사고)
        if (thrustElapsed < thrustSeconds)
        {
            thrustElapsed += Time.deltaTime;
            float p = Mathf.Clamp01(thrustElapsed / thrustSeconds);
            float pull = p < 0.35f
                ? Mathf.Sin(p / 0.35f * Mathf.PI * 0.5f)              // 빠르게 당김 (0→1)
                : Mathf.Cos((p - 0.35f) / 0.65f * Mathf.PI * 0.5f);   // 앞으로 스냅 (1→0)
            target -= aim * Vector3.forward * (thrustPullDistance * pull);
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
