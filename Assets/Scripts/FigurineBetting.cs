using UnityEngine;

/// <summary>
/// 피규어 집기/놓기 (로컬 플레이어 전용, 씬에 1개).
/// 내 방 안에서: 크로스헤어로 피규어 조준 → "4번 펭귄" → 좌클릭 집기(손에 들림)
/// → 상자 조준 → "1등에 예측하기" → 좌클릭 넣기. 빈 곳 클릭 = 선반 복귀.
/// 상자 속 피규어를 다시 조준·클릭하면 도로 꺼내 든다.
/// 방 안에 있는 동안 PointerBusy=true — PlayerItemController가 좌클릭(빠따 등)을 양보한다.
/// </summary>
public class FigurineBetting : MonoBehaviour
{
    /// <summary>조준 안내문 (PlayerHUD가 아이템 힌트보다 우선 표시).</summary>
    public static string Hint { get; private set; } = "";

    /// <summary>베팅 방 조작 모드 — true면 좌클릭이 전투/아이템으로 넘어가지 않는다.</summary>
    public static bool PointerBusy { get; private set; }

    /// <summary>지금 보고 있거나 들고 있는 동물 (모니터 상세 표시용, 없으면 -1).</summary>
    public static int FocusRacerId { get; private set; } = -1;

    /// <summary>손에 든 피규어 (HUD 손 칸 표시용, 없으면 null).</summary>
    public static BetFigurine HeldFigurine => instance != null ? instance.held : null;

    private static FigurineBetting instance;

    [Header("씬 레퍼런스")]
    [SerializeField] private BettingRoomManager roomManager;

    [Header("조작")]
    [Tooltip("집기/놓기 레이 거리 (m)")]
    [SerializeField] private float reach = 3.5f;
    [Tooltip("든 피규어의 손 본 기준 위치 (주사기 소품과 같은 규약)")]
    [SerializeField] private Vector3 holdLocalPos = new Vector3(0.04f, 0.05f, 0.01f);
    [Tooltip("든 피규어의 손 본 기준 회전 (도)")]
    [SerializeField] private Vector3 holdLocalEuler = Vector3.zero;
    [Tooltip("손에 쥔 동안의 크기 — 선반 크기(0.33) 그대로면 눈앞 0.22m에서 화면을 다 덮는다. " +
             "팔이 짧아 손을 더 멀리 못 보내므로 쥘 때만 줄인다")]
    [SerializeField] private float heldScale = 0.14f;

    private BetFigurine held;
    private bool roomModeActive;   // 무기 소품 숨김 상태 추적 (진입/이탈 엣지에서만 전환)

    private void Awake()
    {
        instance = this;
        if (roomManager == null) roomManager = FindFirstObjectByType<BettingRoomManager>();
    }

    /// <summary>
    /// 자기 아바타를 제외한 첫 히트. 카메라가 머리 본에 물려 있어(v5) 선반을 내려다보면
    /// 레이가 자기 몸통을 지나다 CC 캡슐에 먼저 맞는다 (실사고 — 힌트가 안 뜨던 원인).
    /// </summary>
    private bool RaycastIgnoringSelf(Ray ray, out RaycastHit best)
    {
        best = default;
        float min = float.MaxValue;
        Transform selfRoot = PlayerEquipment.Local != null ? PlayerEquipment.Local.transform.root : null;
        foreach (var h in Physics.RaycastAll(ray, reach))
        {
            if (selfRoot != null && h.collider.transform.root == selfRoot) continue;
            if (h.distance < min) { min = h.distance; best = h; }
        }
        return min < float.MaxValue;
    }

    /// <summary>방 조작 모드 진입/이탈 — 무기 소품/상체 레이어를 로컬로 숨긴다 (문 닫힌 방은 남이 못 봄).</summary>
    private void SetRoomMode(bool active)
    {
        if (active == roomModeActive) return;
        roomModeActive = active;
        var eq = PlayerEquipment.Local;
        if (eq == null) return;
        bool down = eq.TryGetComponent<PlayerKnockdown>(out var kd) && kd.IsDown;
        if (active) eq.SuppressForKnockdown();
        else if (!down) eq.RestoreAfterKnockdown();   // 쓰러진 채면 기상 로직이 복원
    }

    private void OnDisable() { Hint = ""; PointerBusy = false; FocusRacerId = -1; }

    /// <summary>든 피규어 강제 반납 (피규어 재생성/쓰러짐 등 외부 사정).</summary>
    public static void ForceDrop()
    {
        if (instance != null && instance.held != null)
        {
            instance.held.ReturnHome();
            instance.held = null;
        }
    }

    private void Update()
    {
        Hint = "";
        PointerBusy = false;
        FocusRacerId = -1;

        if (!InteractionAllowed()) { ForceDrop(); SetRoomMode(false); return; }
        SetRoomMode(true);

        var room = roomManager.LocalRoom;

        // 방 밖으로 들고 나가기 방지 (문이 열린 틈에 반출)
        if (held != null && PlayerEquipment.Local != null
            && !room.ContainsPoint(PlayerEquipment.Local.transform.position))
        {
            ForceDrop();
            return;
        }

        // 방 안 = 베팅 조작 모드 (빠따 스윙 등 좌클릭 양보)
        PointerBusy = true;

        var cam = Camera.main;
        if (cam == null) return;
        var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        bool hitSomething = RaycastIgnoringSelf(ray, out var hit);

        if (held == null)
        {
            if (!hitSomething) return;
            var fig = hit.collider.GetComponentInParent<BetFigurine>();
            if (fig == null)
            {
                // 상자/전시대 껍데기(반투명)를 맞혀도 안의 피규어를 조준한 것으로 — 꺼내 들 수 있게
                var hitBox = hit.collider.GetComponentInParent<BetBox>();
                if (hitBox != null) fig = hitBox.Current;
                if (fig == null)
                {
                    var hitStand = hit.collider.GetComponentInParent<InspectStand>();
                    if (hitStand != null) fig = hitStand.Current;
                }
            }
            if (fig == null) return;

            Hint = fig.HoverName;                       // "4번 펭귄"
            FocusRacerId = fig.RacerId;
            if (Input.GetMouseButtonDown(0)) PickUp(fig);
            return;
        }

        // 들고 있는 중
        FocusRacerId = held.RacerId;
        BetBox box = hitSomething ? hit.collider.GetComponentInParent<BetBox>() : null;
        InspectStand stand = hitSomething && box == null
            ? hit.collider.GetComponentInParent<InspectStand>() : null;

        if (box != null)
        {
            Hint = box.PlaceHint;                       // "1등에 예측하기"
            if (Input.GetMouseButtonDown(0))
            {
                box.Place(held);
                held = null;
            }
        }
        else if (stand != null)
        {
            Hint = stand.PlaceHint;                     // "여기에 올려 살펴보기"
            if (Input.GetMouseButtonDown(0))
            {
                stand.Place(held);
                held = null;
            }
        }
        else
        {
            Hint = held.HoverName;
            if (Input.GetMouseButtonDown(0)) ForceDrop();   // 빈 곳 클릭 = 선반 복귀
        }
    }

    /// <summary>
    /// 오른손 본에 쥔다 — 주사기/무전기 소품과 같은 규약.
    /// 팔은 PlayerAimPose의 손 IK가 시선을 따라 들어올린다 (카메라에 붙이면 손과 따로 놀았다).
    /// </summary>
    public void PickUp(BetFigurine fig)
    {
        var eq = PlayerEquipment.Local;
        Transform hand = eq != null ? eq.RightHandBone : null;
        if (hand == null) return;

        if (fig.InBox != null) fig.InBox.Take();
        if (fig.InStand != null) fig.InStand.Take();    // 전시대에서 집으면 달리기 정지
        held = fig;
        fig.PickCollider.enabled = false;               // 든 것이 레이를 가리지 않게
        fig.transform.SetParent(hand, false);
        fig.transform.localPosition = holdLocalPos;
        fig.transform.localRotation = Quaternion.Euler(holdLocalEuler);
        fig.transform.localScale = Vector3.one * heldScale;
    }

    private bool InteractionAllowed()
    {
        if (roomManager == null || roomManager.LocalRoom == null) return false;
        if (Cursor.lockState != CursorLockMode.Locked) return false;   // UI 조작 중

        var gm = GameManager.Instance;
        if (gm == null) return false;
        var phase = gm.CurrentPhase;
        if (phase != GamePhase.Lobby && phase != GamePhase.Betting) return false;
        if (phase == GamePhase.Betting && roomManager.LocalSubmitted) return false;   // 확정 후 잠금

        var eq = PlayerEquipment.Local;
        if (eq == null) return false;
        if (eq.TryGetComponent<PlayerKnockdown>(out var kd) && kd.IsDown) return false;

        // 내 방 안에 있을 때만 (문 밖에서 벽 너머로 집기 방지)
        return roomManager.LocalRoom.ContainsPoint(eq.transform.position);
    }
}
