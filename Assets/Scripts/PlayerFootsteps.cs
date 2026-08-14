using UnityEngine;

/// <summary>
/// [사운드] 플레이어 발소리 — 지면 재질에 따라 다른 소리를 낸다.
///
/// [멀티] 네트워크 추가 통신 0. 판단 근거가 "이 아바타의 위치가 얼마나 움직였나"뿐이고,
/// 원격 아바타의 위치는 PhotonTransformView가 이미 미러하고 있으므로 각 클라가 로컬로 재생한다
/// (부스트 먼지·무전기 LCD와 같은 철학). 그래서 남이 뒤에서 다가오는 발소리가 3D로 들린다.
///
/// ⚠ FirstPersonController의 입력을 안 본다 — 원격 아바타는 FPC가 꺼져 있어서 입력이 없기 때문.
///    위치 변화만 보면 내 것/남의 것을 같은 코드로 처리할 수 있다.
///
/// 재질 판정은 레이어/태그가 아니라 "콜라이더 종류 + 부모 체인 이름"으로 한다 —
/// 씬의 지면이 전부 Default 레이어 + Untagged라 다른 방법이 없다 (씬 수정 0이 목표).
/// </summary>
public class PlayerFootsteps : MonoBehaviour
{
    [Header("보폭 (이만큼 걸을 때마다 한 발)")]
    [Tooltip("걷기 보폭(m) — 짧을수록 발소리가 잦다")]
    [SerializeField] private float strideWalk = 2.0f;
    [Tooltip("달리기 보폭(m) — 보통 걷기보다 넓다(성큼성큼)")]
    [SerializeField] private float strideRun = 2.8f;
    [Tooltip("이 속도(m/s)를 넘으면 달리는 것으로 보고 달리기 보폭을 쓴다")]
    [SerializeField] private float runSpeedThreshold = 4.0f;
    [Tooltip("이 속도(m/s) 아래면 멈춘 것으로 보고 발소리를 내지 않는다")]
    [SerializeField] private float minSpeed = 0.6f;

    [Header("지면 판정")]
    [Tooltip("발밑을 훑는 레이캐스트 길이(m)")]
    [SerializeField] private float groundProbeDistance = 1.6f;
    [Tooltip("이 단어가 오브젝트 이름이나 부모 이름에 있으면 아스팔트로 본다")]
    [SerializeField] private string[] asphaltKeywords = { "Road" };
    [Tooltip("이 단어가 있으면 콘크리트(실내·피트)로 본다. 터레인은 항상 흙")]
    [SerializeField] private string[] concreteKeywords = { "Pit", "Garage", "Floor", "베팅방" };

    private CharacterController cc;
    private PlayerKnockdown knockdown;
    private Photon.Pun.PhotonView view;

    private Vector3 lastPos;
    private float distanceAccum;
    private bool isLocalAvatar;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        knockdown = GetComponent<PlayerKnockdown>();
        view = GetComponent<Photon.Pun.PhotonView>();
        lastPos = transform.position;
    }

    private void Start()
    {
        // 오프라인(미접속)이면 PhotonView.IsMine이 false라 "미접속 = 내 것" 규칙을 명시 적용 (§11)
        isLocalAvatar = view == null
                     || !Photon.Pun.PhotonNetwork.IsConnected
                     || view.IsMine;
    }

    private void Update()
    {
        Vector3 now = transform.position;
        Vector3 delta = now - lastPos;
        lastPos = now;

        // 수평 이동만 센다 — 엘리베이터 하강이나 낙하로는 발소리가 나면 안 된다
        delta.y = 0f;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float speed = delta.magnitude / dt;

        // 쓰러져 있거나(끌려가는 중일 수도) 너무 느리면 누적 자체를 하지 않는다
        if (speed < minSpeed || (knockdown != null && knockdown.IsDown))
        {
            distanceAccum = 0f;
            return;
        }

        // 공중이면 발이 땅에 없다. 단 isGrounded는 서 있어도 깜빡이므로(§11)
        // 원격 아바타에는 쓸 수 없다 — 아래 재질 판정의 레이캐스트가 접지 확인을 겸한다.

        distanceAccum += delta.magnitude;

        bool running = speed >= runSpeedThreshold;
        float stride = running ? strideRun : strideWalk;

        if (distanceAccum < stride) return;
        distanceAccum -= stride;

        PlayStep(running);
    }

    private void PlayStep(bool running)
    {
        // 발밑을 훑어 지면을 확인 — 못 맞으면 공중이므로 소리 없음 (접지 판정 겸용)
        Vector3 origin = transform.position + Vector3.up * 0.3f;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                             groundProbeDistance, ~0, QueryTriggerInteraction.Ignore))
            return;

        // 자기 자신(CharacterController)에 맞은 건 무시
        if (cc != null && hit.collider == (Collider)cc) return;

        SfxId id = ClassifyGround(hit.collider);

        if (isLocalAvatar)
        {
            // 내 발소리는 2D — 거리 감쇠 없이 음량이 일정해야 안정적이다
            SoundManager.PlaySfx(id);
        }
        else
        {
            // 남의 발소리는 3D — "뒤에서 누가 온다"가 게임플레이 정보가 된다
            SoundManager.PlaySfx(id, hit.point);
        }
    }

    /// <summary>콜라이더 종류 + 부모 체인 이름으로 재질을 가른다.</summary>
    private SfxId ClassifyGround(Collider col)
    {
        // 터레인은 타입만으로 확실히 판정된다 — 잔디·흙
        if (col is TerrainCollider) return SfxId.FootstepDirt;

        // 부모를 타고 올라가며 키워드를 찾는다 (도로 조각은 "트랙/Road/Road_..." 밑에 있다)
        Transform t = col.transform;
        while (t != null)
        {
            string n = t.name;
            for (int i = 0; i < asphaltKeywords.Length; i++)
                if (!string.IsNullOrEmpty(asphaltKeywords[i]) && n.Contains(asphaltKeywords[i]))
                    return SfxId.FootstepAsphalt;

            for (int i = 0; i < concreteKeywords.Length; i++)
                if (!string.IsNullOrEmpty(concreteKeywords[i]) && n.Contains(concreteKeywords[i]))
                    return SfxId.FootstepConcrete;

            t = t.parent;
        }

        return SfxId.FootstepConcrete;   // 정체불명은 무난한 콘크리트로
    }
}
