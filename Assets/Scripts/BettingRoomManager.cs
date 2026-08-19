using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// 베팅 방 시스템 총괄 (씬에 1개).
/// - 방↔플레이어 배정: 매치 중 = MatchManager 로스터 순번, 로비 = 접속 순번(액터 정렬).
///   Photon 방장은 항상 최저 액터라 로비에서 방장 = 방 1 — 시작 레버는 방 1에만 있으면 된다.
/// - 문 상태머신 (전 클라 로컬 계산 — 추가 통신 0):
///   제출 여부는 경제 방송(submitted 미러), 재실은 미러 위치 기반 OverlapBox라 전 클라 동일.
///     로비: 비면 열림, 들어오면 닫힘 (대기실) / 베팅: 제출 전 = 로비와 동일(들어와 갇힘),
///     제출 후 = 나갈 때까지 열림 / 그 외: 안에 남은 사람만 내보내고 닫힘.
///     봇 방: 베팅 시작 +N초에 열렸다가 잠시 후 닫힘 ("나갔다" 연기 — 실제 베팅은 자동 처리).
/// - 피규어: 내 방에만 로컬 생성 (비밀 유지 + 통신 0). 라인업/라운드가 바뀌면 재생성.
/// </summary>
public class BettingRoomManager : MonoBehaviour
{
    /// <summary>HUD 안내문 ("자기 방에 들어가 베팅하세요") — PlayerHUD가 읽음.</summary>
    public static string Guidance { get; private set; } = "";

    [Header("씬 레퍼런스")]
    [SerializeField] private BettingRoom[] rooms;   // 로스터 순번대로 4개
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private MatchManager matchManager;

    [Header("피규어")]
    [Tooltip("선반 위 피규어 간격 (m)")]
    [SerializeField] private float shelfSpacing = 0.55f;

    /// <summary>내 방 (없으면 null — 매치 중 합류자 등).</summary>
    public BettingRoom LocalRoom { get; private set; }

    /// <summary>내가 내 방 안에 있는가.</summary>
    public bool LocalInsideRoom =>
        LocalRoom != null && PlayerEquipment.Local != null
        && LocalRoom.ContainsPoint(PlayerEquipment.Local.transform.position);

    /// <summary>내가 이번 라운드 베팅을 제출했는가 (클라 미러 포함).</summary>
    public bool LocalSubmitted =>
        matchManager != null && matchManager.HasSubmitted(NetworkPlayers.LocalPlayerId);

    // 방별 주인 정보 (RefreshOwners가 갱신)
    private readonly int[] ownerIds = { -1, -1, -1, -1 };
    private readonly bool[] ownerIsBot = new bool[4];
    private readonly bool[] hasOwner = new bool[4];

    // 씬에 아바타가 실존하는 플레이어들 — "초대(문 열림)"는 아바타가 밖에 있을 때만.
    // 이게 없으면 스폰 전 잠깐을 "빈 방"으로 오판해 게임 시작 순간 문이 열렸다 닫힌다 (실사고)
    private readonly HashSet<int> avatarIds = new();
    private readonly List<CharacterController> avatarCCs = new();   // 재실 판정 (씬 CC는 최대 4개)

    private float bettingStartedAt = -999f;
    private float nextOwnerRefresh;
    private CharacterController localCC;         // 배리어 무시 배선용
    private int barrierWiredRoom = -1;
    private int builtLineupHash;                 // 피규어 재생성 판단
    private readonly List<GameObject> figurines = new();

    private void Awake()
    {
        // 씬 배선 누락 대비 자동 탐색 (v8 법칙: 복제/조립 실수 방어)
        if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();
        if (matchManager == null) matchManager = FindFirstObjectByType<MatchManager>();
    }

    private void OnEnable() => GameEvents.OnPhaseChanged += HandlePhase;
    private void OnDisable() { GameEvents.OnPhaseChanged -= HandlePhase; Guidance = ""; }

    private void HandlePhase(GamePhase p)
    {
        if (p == GamePhase.Betting)
        {
            bettingStartedAt = Time.time;
            builtLineupHash = 0;   // 라운드마다 피규어 리셋 (배치 초기화)
            TeleportLocalToMyRoom();   // 대기실 → 자기 방 순간이동 (v21 유저 결정 — 매 라운드)
        }
    }

    /// <summary>
    /// 베팅 시작 순간 내 아바타를 내 방 스폰 지점으로 순간이동 (v21 유저 결정 —
    /// 대기실이 트랙 건너편으로 옮겨져 걸어가면 베팅 시간을 까먹는다).
    /// [멀티] 각 클라가 자기 아바타만 옮긴다 — 원격 위치는 TransformView가 이미 미러. 통신 0.
    /// </summary>
    private void TeleportLocalToMyRoom()
    {
        RefreshOwners();   // 페이즈 방송이 0.5초 주기 배정보다 먼저 도착할 수 있어 즉시 갱신

        if (LocalRoom == null) return;   // 매치 중 합류자 등 — 방 없으면 그대로
        var avatar = PlayerEquipment.Local;
        if (avatar == null) return;

        // 방 안 스폰 지점 (스포너가 쓰던 RoomSpawn 재사용, 없으면 방 중앙 폴백)
        Transform spawn = null;
        foreach (Transform child in LocalRoom.transform)
            if (child.name.StartsWith("RoomSpawn")) { spawn = child; break; }
        Vector3 pos = spawn != null ? spawn.position
                                    : LocalRoom.transform.TransformPoint(new Vector3(0f, 0.1f, -1.5f));
        Quaternion rot = spawn != null ? spawn.rotation : LocalRoom.transform.rotation;

        // CC는 켜진 채 position 대입이 씹힌다 — 끄고 옮기고 켠다
        var cc = avatar.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        avatar.transform.SetPositionAndRotation(pos, rot);
        if (cc != null) cc.enabled = true;
    }

    private void Update()
    {
        var gm = GameManager.Instance;
        GamePhase phase = gm != null ? gm.CurrentPhase : GamePhase.Lobby;

        if (Time.time >= nextOwnerRefresh)
        {
            nextOwnerRefresh = Time.time + 0.5f;
            RefreshOwners();
            RefreshAvatars();
        }

        WireLocalBarrier();
        EnsureFigurines(phase);
        DriveDoors(phase);
        UpdateGuidance(phase);
    }

    // ---------- 방 배정 ----------

    private void RefreshOwners()
    {
        int myId = NetworkPlayers.LocalPlayerId;
        int myRoom = -1;

        if (matchManager != null && matchManager.Players.Count > 0)
        {
            // 매치 로스터 순번 = 방 번호 (봇 대타가 붙어도 PlayerState가 유지돼 방이 안 바뀜)
            var players = matchManager.Players;
            for (int i = 0; i < rooms.Length; i++)
            {
                hasOwner[i] = i < players.Count;
                ownerIds[i] = hasOwner[i] ? players[i].PlayerId : -1;
                ownerIsBot[i] = hasOwner[i] && players[i].IsBot;
                if (hasOwner[i] && ownerIds[i] == myId) myRoom = i;
            }
        }
        else if (PhotonNetwork.InRoom)
        {
            // 로비: 액터 정렬 순번 (PlayerList는 액터번호 오름차순)
            var list = PhotonNetwork.PlayerList;
            for (int i = 0; i < rooms.Length; i++)
            {
                hasOwner[i] = i < list.Length;
                ownerIds[i] = hasOwner[i] ? list[i].ActorNumber : -1;
                ownerIsBot[i] = false;
                if (hasOwner[i] && list[i].IsLocal) myRoom = i;
            }
        }
        else
        {
            // 오프라인 로비: 나 혼자 방 1
            for (int i = 0; i < rooms.Length; i++)
            {
                hasOwner[i] = i == 0;
                ownerIds[i] = i == 0 ? myId : -1;
                ownerIsBot[i] = false;
            }
            myRoom = 0;
        }

        var newLocal = myRoom >= 0 && myRoom < rooms.Length ? rooms[myRoom] : null;
        if (newLocal != LocalRoom)
        {
            if (LocalRoom != null) LocalRoom.IsLocalRoom = false;
            LocalRoom = newLocal;
            if (LocalRoom != null) LocalRoom.IsLocalRoom = true;
            barrierWiredRoom = -1;   // 배리어 무시 재배선
        }
    }

    /// <summary>씬의 아바타(CC) 수집 — 문 초대 판단(존재)과 재실 판정(위치)에 함께 쓴다.</summary>
    private void RefreshAvatars()
    {
        avatarIds.Clear();
        avatarCCs.Clear();
        foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
        {
            avatarCCs.Add(cc);
            var pv = cc.GetComponent<Photon.Pun.PhotonView>();
            if (pv != null && pv.Owner != null) avatarIds.Add(pv.Owner.ActorNumber);
        }
        // 오프라인(미접속)은 PhotonView Owner가 없다 — 내 아바타는 명시 등록
        if (PlayerEquipment.Local != null) avatarIds.Add(NetworkPlayers.LocalPlayerId);
    }

    /// <summary>캐시된 CC 목록으로 재실 판정 (위치는 매 프레임 최신값을 읽는다).</summary>
    private bool AnyoneIn(BettingRoom room)
    {
        foreach (var cc in avatarCCs)
            if (cc != null && room.ContainsPoint(cc.transform.position)) return true;
        return false;
    }

    // ---------- 출입 차단 배선 ----------

    private void WireLocalBarrier()
    {
        if (LocalRoom == null) return;
        if (localCC == null)
        {
            // 내 아바타의 CC — PlayerEquipment.Local이 "내 것" 판정의 단일 출처
            if (PlayerEquipment.Local != null)
                localCC = PlayerEquipment.Local.GetComponent<CharacterController>();
            if (localCC == null) return;
            barrierWiredRoom = -1;
        }

        int idx = System.Array.IndexOf(rooms, LocalRoom);
        if (idx == barrierWiredRoom) return;

        // 내 방 배리어만 통과, 나머지는 막힘 (남의 방 출입 불가 확정)
        for (int i = 0; i < rooms.Length; i++)
            if (rooms[i] != null && rooms[i].DoorwayBarrier != null)
                Physics.IgnoreCollision(localCC, rooms[i].DoorwayBarrier, rooms[i] == LocalRoom);
        barrierWiredRoom = idx;
    }

    // ---------- 문 상태머신 ----------

    private void DriveDoors(GamePhase phase)
    {
        var cfg = GameManager.Instance != null ? GameManager.Instance.Config : null;
        float botDelay = cfg != null ? cfg.roomBotDoorDelay : 20f;
        float botLinger = cfg != null ? cfg.roomBotDoorLinger : 5f;
        float sinceBetting = Time.time - bettingStartedAt;

        for (int i = 0; i < rooms.Length; i++)
        {
            var room = rooms[i];
            if (room == null) continue;

            bool open;
            if (!hasOwner[i])
                open = false;                                          // 빈 방: 항상 닫힘
            else if (ownerIsBot[i])
                open = phase == GamePhase.Betting
                       && sinceBetting >= botDelay
                       && sinceBetting < botDelay + botLinger;          // 봇: "나갔다" 연기
            else
            {
                bool inside = AnyoneIn(room);
                bool submitted = matchManager != null && matchManager.HasSubmitted(ownerIds[i]);
                // "초대(열림)"는 주인 아바타가 씬에 실존하고 밖에 있을 때만 —
                // 스폰 전(게임 시작 순간)은 빈 방처럼 보여도 문이 닫혀 있어야 한다
                bool invite = avatarIds.Contains(ownerIds[i]) && !inside;
                if (phase == GamePhase.Betting)
                    open = submitted ? inside : invite;                // 제출 전: 들어오면 갇힘 / 후: 나갈 때까지 열림
                else if (phase == GamePhase.Lobby)
                    open = invite;                                     // 대기실: 밖에 있는 주인만 초대
                else
                    open = inside;                                     // 그 외: 남은 사람만 내보냄
            }
            room.SetDoorOpen(open);
        }
    }

    // ---------- 피규어 (내 방 전용, 로컬) ----------

    private void EnsureFigurines(GamePhase phase)
    {
        if (LocalRoom == null || raceManager == null) return;
        var racers = raceManager.Racers;
        if (racers == null || racers.Count == 0) return;
        if (phase != GamePhase.Lobby && phase != GamePhase.Betting) return;

        // 라인업 지문: 라인업이 바뀌거나 라운드 리셋(builtLineupHash=0) 시 재생성
        int hash = 17;
        foreach (var r in racers) hash = hash * 31 + r.RacerId;
        hash = hash * 31 + (System.Array.IndexOf(rooms, LocalRoom) + 1);
        if (hash == builtLineupHash) return;
        builtLineupHash = hash;

        BuildFigurines(racers);
    }

    private void BuildFigurines(IReadOnlyList<Racer> racers)
    {
        FigurineBetting.ForceDrop();   // 손에 든 채 재생성되면 유령 피규어가 남는다

        foreach (var go in figurines) if (go != null) Destroy(go);
        figurines.Clear();
        foreach (var b in LocalRoom.Boxes) if (b != null) b.Current = null;

        var cfg = GameManager.Instance != null ? GameManager.Instance.Config : null;
        float scale = cfg != null ? cfg.figurineScale : 0.33f;
        var shelf = LocalRoom.ShelfAnchor;
        if (shelf == null) { Debug.LogWarning("[베팅방] 선반 앵커 없음 — 피규어 생략"); return; }

        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            var def = racer.Definition;
            if (def == null || def.prefab == null) continue;

            // 선반 슬롯 (출발 순서대로 한 줄)
            var slot = new GameObject($"FigSlot_{racer.RacerId}").transform;
            slot.SetParent(shelf, false);
            slot.localPosition = new Vector3((i - (racers.Count - 1) * 0.5f) * shelfSpacing, 0f, 0f);
            // 피규어가 옆모습(보는 사람 기준 오른쪽)을 보게 — 옆구리 번호판이 보이는 각 (유저 결정).
            //    ⚠ 회전은 홀더가 아니라 슬롯에 준다: ReturnHome()이 피규어 localRotation을
            //    identity로 되돌리므로 홀더에 주면 복귀할 때 풀린다.
            slot.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // 홀더 비활성 상태에서 생성 → Awake 전에 게임플레이 컴포넌트 제거 (TitleTrackShow 패턴)
            var holder = new GameObject($"Figurine_{racer.RacerId + 1}");
            holder.transform.SetParent(slot, false);
            holder.transform.localScale = Vector3.one * scale;
            holder.SetActive(false);

            var body = Instantiate(def.prefab, holder.transform);
            // ⚠ Instantiate(prefab, parent)는 프리팹 에셋에 저장된 position을 로컬 위치로 쓴다 —
            //    동물 프리팹들은 (141.9, 0, 149.6)으로 저장돼 있어 리셋 없이는 몸이 47m 밖에 생긴다 (실사고)
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            StripGameplay(body);

            holder.SetActive(true);

            // 번호판 (양 옆구리 — 레이스와 동일 규약) + 받침 원반
            var plate = body.GetComponentInChildren<RacerNumberPlate>(true);
            if (plate != null) plate.Apply(racer.RacerId + 1);
            BuildBaseDisc(holder.transform, racer.RacerId + 1, scale);

            // 집기 콜라이더 — 렌더러 실측 바운즈 기반 (홀더 로컬 단위 = 월드/scale, §11 콜라이더 로컬 단위 법칙)
            var col = holder.AddComponent<BoxCollider>();
            var bounds = MeasureBounds(holder.transform);
            col.center = holder.transform.InverseTransformPoint(bounds.center);
            col.size = bounds.size / Mathf.Max(0.0001f, scale);

            var fig = holder.AddComponent<BetFigurine>();
            fig.Init(racer.RacerId, racer.RacerId + 1, def, slot, col);
            figurines.Add(holder);
            figurines.Add(slot.gameObject);
        }
        Debug.Log($"[베팅방] 피규어 {racers.Count}마리 생성 (내 방)");
    }

    private static void BuildBaseDisc(Transform holder, int postNumber, float scale)
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.name = "Base";
        disc.transform.SetParent(holder, false);
        disc.transform.localPosition = new Vector3(0f, 0.04f, 0f);
        disc.transform.localScale = new Vector3(1.5f, 0.04f, 1.5f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = RacerColors.Of(postNumber);
        disc.GetComponent<Renderer>().sharedMaterial = mat;
    }

    private static Bounds MeasureBounds(Transform root)
    {
        var rs = root.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(root.position, Vector3.one * 0.3f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>
    /// 레이스용 컴포넌트 제거 — 번호판(RacerNumberPlate/Plate*)은 유지 (TitleTrackShow와의 차이).
    /// ⚠ Animator는 남긴다 (전시대에서 달리기를 재생해야 함). BetFigurine이 Init에서 꺼두고,
    ///   전시대에 올릴 때만 켠다. 예전에 여기서 지웠던 이유는 Animator.Update()를 강제 호출해
    ///   정지 포즈를 구우려다 본이 100m 밖으로 날아간 사고였는데, 그 강제 호출 쪽을 없앴다.
    /// public인 이유: FigurineThumbs(HUD 썸네일)가 같은 스트립 규약을 재사용한다.
    /// </summary>
    public static void StripGameplay(GameObject go)
    {
        RemoveAll<RacerMotor>(go);
        RemoveAll<Racer>(go);
        RemoveAll<NetworkRacerSetup>(go);
        RemoveAll<Photon.Pun.PhotonAnimatorView>(go);
        RemoveAll<Photon.Pun.PhotonTransformView>(go);
        RemoveAll<Photon.Pun.PhotonView>(go);
        RemoveAll<Rigidbody>(go);
        foreach (var c in go.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(c);
    }

    private static void RemoveAll<T>(GameObject go) where T : Component
    {
        foreach (var c in go.GetComponentsInChildren<T>(true))
            DestroyImmediate(c);
    }

    // ---------- HUD 안내 ----------

    private void UpdateGuidance(GamePhase phase)
    {
        bool need = phase == GamePhase.Betting
                    && LocalRoom != null
                    && !LocalSubmitted
                    && !LocalInsideRoom;
        Guidance = need ? Loc.Get("room.guidance") : "";
    }
}
