using UnityEngine;

/// <summary>
/// 베팅 방 하나 (피트스탑 차고 1동). 문 슬라이드·출입 차단·재실 감지 담당.
/// 문을 "열지 말지" 판단은 BettingRoomManager가 매 프레임 내려준다 (이 방은 연출만).
/// - 문: 에셋의 Door-closed 메시를 위로 슬라이드 (차고 셔터).
/// - 출입 차단: 문이 열려 있어도 주인 외에는 못 들어오게 하는 항시 배리어.
///   주인의 CC만 Physics.IgnoreCollision으로 통과 (BettingRoomManager가 배선).
/// - 재실 감지: 방 안 CharacterController 유무 — 배리어 덕에 "누가 있다 = 주인"이 보장돼
///   신원 조회 없이 로컬 계산만으로 전 클라가 같은 답을 얻는다.
/// </summary>
public class BettingRoom : MonoBehaviour
{
    [Header("문 (에셋 Door-closed 슬라이드)")]
    [SerializeField] private Transform doorClosed;
    [Tooltip("문이 열릴 때 위로 올라가는 높이 (m)")]
    [SerializeField] private float doorSlideHeight = 2.9f;

    [Header("출입 차단 (항시 켜짐 — 주인 CC만 무시 처리)")]
    [SerializeField] private Collider doorwayBarrier;

    // ⚠ 차고 실측: x 폭 6m / z 48.2~54.2 (문 48.4, 뒷벽 54.2).
    //    이 박스가 작으면 문 근처가 "방 밖"으로 잡혀 문이 열리고 무기가 돌아온다 (실사고)
    [Header("내부 (재실 판정 박스, 로컬 기준) — 문턱부터 뒷벽까지 덮어야 함")]
    [SerializeField] private Vector3 interiorCenter = new Vector3(0f, 1.5f, -0.1f);
    [SerializeField] private Vector3 interiorSize = new Vector3(5.4f, 3f, 5.6f);

    [Header("내용물")]
    [SerializeField] private Transform shelfAnchor;
    [SerializeField] private BetBox[] boxes;

    [Header("실내등 — 문이 닫히면 어둑, 열리면 바깥 햇빛과 함께 환해진다")]
    [SerializeField] private Light roomLight;
    [Tooltip("문이 완전히 닫혔을 때 밝기")]
    [SerializeField] private float lightClosed = 0.55f;
    [Tooltip("문이 완전히 열렸을 때 밝기")]
    [SerializeField] private float lightOpen = 2.2f;

    /// <summary>이 방이 내(로컬 플레이어) 방인가 — BettingRoomManager가 갱신.</summary>
    public bool IsLocalRoom { get; set; }

    public Transform ShelfAnchor => shelfAnchor;
    public BetBox[] Boxes => boxes;
    public Collider DoorwayBarrier => doorwayBarrier;

    private float doorBaseY;          // 닫힘 위치 (Awake에서 실측)
    private float doorT;              // 0=닫힘, 1=열림
    private bool doorOpenTarget;
    private bool doorMoving;          // 슬라이드 소리를 시작 프레임에 한 번만 내기 위한 상태
    private float slideSeconds = 0.6f;

    /// <summary>런타임 셋업용 (씬 조립 스크립트가 호출).</summary>
    public void Setup(Transform doorClosed, Collider barrier, Transform shelfAnchor, BetBox[] boxes)
    {
        this.doorClosed = doorClosed;
        this.doorwayBarrier = barrier;
        this.shelfAnchor = shelfAnchor;
        this.boxes = boxes;
    }

    public void SetRoomLight(Light l) => roomLight = l;

    private void Awake()
    {
        if (doorClosed != null) doorBaseY = doorClosed.localPosition.y;
        var cfg = GameManager.Instance != null ? GameManager.Instance.Config : null;
        if (cfg != null) slideSeconds = Mathf.Max(0.1f, cfg.roomDoorSlideSeconds);
        if (roomLight != null) roomLight.intensity = lightClosed;   // 시작은 닫힌 상태
    }

    /// <summary>문 목표 상태 (BettingRoomManager가 매 프레임 호출 — 중복 무해).</summary>
    public void SetDoorOpen(bool open) => doorOpenTarget = open;

    public bool IsDoorFullyOpen => doorT >= 0.99f;

    private void Update()
    {
        if (doorClosed == null) return;
        float target = doorOpenTarget ? 1f : 0f;
        if (Mathf.Approximately(doorT, target)) { doorMoving = false; return; }

        // 셔터가 움직이기 시작하는 프레임에 한 번 (열림·닫힘 공용). 문 자리에서 3D라
        // 남의 방이 열리는 소리도 가까이 있으면 들린다 — "누가 베팅을 끝냈다"는 신호
        if (!doorMoving)
        {
            doorMoving = true;
            SoundManager.PlaySfx(SfxId.DoorSlide, doorClosed.position);
        }

        doorT = Mathf.MoveTowards(doorT, target, Time.deltaTime / slideSeconds);
        var lp = doorClosed.localPosition;
        lp.y = doorBaseY + doorSlideHeight * doorT;
        doorClosed.localPosition = lp;

        // 문이 올라가는 만큼 실내가 밝아진다 (바깥 햇빛이 쏟아지는 느낌 보강)
        if (roomLight != null) roomLight.intensity = Mathf.Lerp(lightClosed, lightOpen, doorT);
    }

    /// <summary>
    /// 방 안에 플레이어(CC)가 있는가 — 배리어 덕에 있으면 곧 주인.
    /// ⚠ 콜라이더 겹침 스캔(OverlapBox)으로 하면 안 된다: 방 안엔 선반·상자 6개·피규어 9개·
    ///    차고 메시·터레인·가구까지 30개 넘게 겹쳐서 버퍼가 넘치고 정작 CC가 잘린다 (실사고 2회).
    ///    씬의 CC는 최대 4개뿐이니 그쪽을 직접 검사하는 게 정확하고 싸다.
    ///    (매 프레임 호출은 BettingRoomManager가 캐시된 목록으로 처리 — 이건 기즈모/단발 조회용)
    /// </summary>
    public bool IsAnyoneInside()
    {
        foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
            if (cc != null && ContainsPoint(cc.transform.position)) return true;
        return false;
    }

    /// <summary>월드 좌표가 방 안인가 (피규어 반출 방지 등).</summary>
    public bool ContainsPoint(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos) - interiorCenter;
        Vector3 half = interiorSize * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y + 1f && Mathf.Abs(local.z) <= half.z;
    }

    /// <summary>상자 3개가 전부 찼으면 티켓, 아니면 null.</summary>
    public BetTicket? BuildTicket()
    {
        int first = -1, second = -1, third = -1;
        foreach (var b in boxes)
        {
            if (b == null || b.Current == null) return null;
            if (b.Rank == 0) first = b.Current.RacerId;
            else if (b.Rank == 1) second = b.Current.RacerId;
            else third = b.Current.RacerId;
        }
        if (first < 0 || second < 0 || third < 0) return null;
        return new BetTicket { firstId = first, secondId = second, thirdId = third };
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.7f, 0.1f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(interiorCenter, interiorSize);
    }
}
