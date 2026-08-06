using UnityEngine;

/// <summary>
/// [개막 연출] 전망대 ↔ 지상 엘리베이터.
/// 대기(Lobby) = 전망대에 정박 → 방장이 시작 버튼을 누르면(= 페이즈가 Lobby를 벗어나면)
/// 전원을 케이지 위로 스냅한 뒤 지상으로 하강. 매치가 끝나 Lobby로 돌아오면 다시 올라간다.
///
/// 네트워크: 트리거가 "페이즈 방송"이라 각 클라가 자기 로컬에서 같은 연출을 재생한다 —
/// 추가 통신 0 (전광판·부스트 먼지와 같은 로컬 연출 철학). 탑승자 운반도 각자 자기
/// 아바타만 옮기고, 남의 화면엔 PhotonTransformView가 그대로 미러한다.
/// </summary>
/// ⚠ 실행 순서 -100: 케이지 이동+탑승자 운반이 FirstPersonController보다 먼저 끝나야 한다.
/// 반대면 프레임의 마지막 이동이 "허공으로 하강"이 되어 CharacterController.isGrounded가
/// 깜빡이고 낙하 애니메이션이 섞여 나온다 (실사고).
[DefaultExecutionOrder(-100)]
public class ElevatorRide : MonoBehaviour
{
    [Header("구성")]
    [Tooltip("실제로 오르내리는 케이지 (바닥판 + 난간의 부모)")]
    [SerializeField] private Transform cage;
    [Tooltip("탑승 위치 (케이지 자식). 접속 순번으로 배정 — 서로 겹치지 않게")]
    [SerializeField] private Transform[] rideSlots;

    [Tooltip("케이지 바닥 반폭 (m) — 탑승 판정 범위")]
    [SerializeField] private float cageHalfExtent = 2.5f;

    [Header("높이")]
    [Tooltip("전망대에 정박했을 때의 케이지 y")]
    [SerializeField] private float topY = 22f;
    [Tooltip("지상에 도착했을 때의 케이지 y")]
    [SerializeField] private float bottomY = 0.1f;
    [Tooltip("이동 속도 (m/s)")]
    [SerializeField] private float speed = 5f;
    [Tooltip("출발 전 뜸 들이는 시간 (초) — 문 닫히는 느낌")]
    [SerializeField] private float departDelay = 0.8f;

    private float targetY;
    private float waitTimer;
    private bool moving;
    private float clearGrace;   // 정지 직후 탑승 플래그를 조금 더 유지 (착지 프레임의 접지 깜빡임 흡수)

    /// <summary>케이지가 아래(지상)에 있나 — 스폰 위치 판단에 쓰인다.</summary>
    public bool AtBottom => Mathf.Abs(cage.position.y - bottomY) < 0.05f;

    private void Awake()
    {
        if (cage == null) { Debug.LogError("[ElevatorRide] cage 미배선"); enabled = false; return; }
        targetY = cage.position.y;
    }

    private void OnEnable()  => GameEvents.OnPhaseChanged += HandlePhase;
    private void OnDisable() => GameEvents.OnPhaseChanged -= HandlePhase;

    private void HandlePhase(GamePhase phase)
    {
        bool lobby = phase == GamePhase.Lobby;
        float want = lobby ? topY : bottomY;
        if (Mathf.Approximately(targetY, want)) return;

        // 출발 직전 전원을 케이지 위로 — "누가 안 탔는데 출발" 문제를 연출로 흡수 (기획 확정)
        SnapRidersOntoCage();
        targetY = want;
        waitTimer = departDelay;
        moving = true;
    }

    private void Update()
    {
        if (!moving)
        {
            if (clearGrace > 0f)
            {
                clearGrace -= Time.deltaTime;
                if (clearGrace <= 0f) ClearRidingFlags();
            }
            return;
        }

        if (waitTimer > 0f) { waitTimer -= Time.deltaTime; return; }

        Vector3 p = cage.position;
        float next = Mathf.MoveTowards(p.y, targetY, speed * Time.deltaTime);
        float delta = next - p.y;
        if (Mathf.Approximately(delta, 0f)) { moving = false; clearGrace = 0.3f; return; }

        cage.position = new Vector3(p.x, next, p.z);
        CarryRiders(delta);
    }

    /// <summary>케이지 위에 선 CharacterController들을 같은 양만큼 함께 이동 (미끄러짐/튐 방지).</summary>
    private void CarryRiders(float deltaY)
    {
        foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
        {
            bool onboard = IsOnCage(cc.transform.position);
            if (cc.TryGetComponent<FirstPersonController>(out var fpc))
                fpc.OnMovingPlatform = onboard;   // 탑승 중엔 낙하 애니/낙하 가속 억제
            if (onboard) cc.Move(new Vector3(0f, deltaY, 0f));
        }
    }

    /// <summary>정지 시 탑승 상태 해제 — 안 풀면 지상에서 내려도 낙하 애니가 영영 안 나온다.</summary>
    private void ClearRidingFlags()
    {
        foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
            fpc.OnMovingPlatform = false;
    }

    private bool IsOnCage(Vector3 pos)
    {
        Vector3 d = pos - cage.position;
        float half = cageHalfExtent + 0.3f;   // 발이 가장자리에 걸쳐도 탑승으로 인정
        return Mathf.Abs(d.x) <= half && Mathf.Abs(d.z) <= half
            && d.y > -0.6f && d.y < 2.5f;
    }

    /// <summary>내 아바타를 탑승 슬롯으로 순간이동 (CharacterController는 끄고 옮겨야 먹힌다).</summary>
    private void SnapRidersOntoCage()
    {
        if (rideSlots == null || rideSlots.Length == 0) return;

        int i = 0;
        foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            // 내 아바타만 옮긴다 — 남의 것은 각자 클라가 옮기고 TransformView가 미러
            var view = fpc.GetComponent<Photon.Pun.PhotonView>();
            bool mine = view == null || !Photon.Pun.PhotonNetwork.InRoom || view.IsMine;
            if (!mine) continue;

            var slot = rideSlots[i % rideSlots.Length];
            i++;

            var cc = fpc.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;          // CC가 켜져 있으면 transform 이동이 되돌려진다
            fpc.transform.SetPositionAndRotation(slot.position, slot.rotation);
            if (cc != null) cc.enabled = true;
        }
    }

    /// <summary>스폰용: 매치 중 합류/재접속자는 지상에서 시작해야 한다 (전망대에 갇힘 방지).</summary>
    public Vector3 GroundLevelY => new Vector3(0f, bottomY, 0f);
}
