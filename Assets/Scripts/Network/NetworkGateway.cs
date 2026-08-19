using System;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// [멀티 4단계] 요청/방송 통합 관문.
/// - 로스터: 호스트가 접속자+봇 명단을 만들어 방송, 클라는 거울(mirror) 목록 생성
/// - 요청 RPC: 베팅/대출/상환/아이템 — 클라 요청 → 호스트 검증·처리 → 결과 회신
/// - 경제 방송: 잔액/빚/아이템/제출 상태 주기 방송
/// - 정산 방송: 순위+베팅 공개+지급액 (비밀은 이 순간까지 호스트만 앎)
/// 오프라인에선 전부 로컬 직통 — 싱글 동작 완전 보존.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class NetworkGateway : MonoBehaviourPunCallbacks
{
    [Header("씬 레퍼런스")]
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private ItemExecutor itemExecutor;
    [SerializeField] private PlayerItemController itemController;

    [Header("아이템 SO (네트워크 직렬화용: 0=부스트, 1=감속, 2=발동 무전기, 3=처형 무전기)")]
    [SerializeField] private ItemDefinition boostItem;
    [SerializeField] private ItemDefinition slowItem;
    [SerializeField] private ItemDefinition radioSkillItem;
    [SerializeField] private ItemDefinition radioExecItem;

    [Header("봇 (이탈자 대타 전용 — 초기 충원 없음)")]
    [SerializeField] private BotController[] bots;

    [Tooltip("경제 상태 방송 주기 (초)")]
    [SerializeField] private float economyInterval = 1f;

    private float nextEconomy;
    private Action<bool> pendingBetCb;

    private bool IsHost => PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
    private bool Offline => !PhotonNetwork.InRoom;

    private void Awake()
    {
        if (boostItem == null || slowItem == null || radioSkillItem == null || radioExecItem == null)
            Debug.LogError("[NetworkGateway] 아이템 SO 슬롯(부스트/감속/무전기 2종)이 비어있습니다! " +
                "아이템 개수 방송이 0이 되어 게스트 아이템이 0개로 보입니다. " +
                "Bootstrap과 같은 SO를 연결하세요.");
    }

    private ItemDefinition ItemOf(int type) => type switch
    {
        0 => boostItem,
        1 => slowItem,
        2 => radioSkillItem,
        _ => radioExecItem,
    };

    private int TypeOf(ItemDefinition item)
    {
        if (item == boostItem) return 0;
        if (item == slowItem) return 1;
        if (item == radioSkillItem) return 2;
        return 3;
    }

    // ================= 로스터 =================

    /// <summary>
    /// [5-1b] 게임 시작 요청 — 레버가 호출.
    /// 오프라인: 즉시 시작(재경기). 호스트: 방 잠금 → 접속 인원만으로 로스터 → 시작.
    /// </summary>
    public void RequestStartMatch()
    {
        if (matchManager.IsMatchRunning) return;

        if (Offline)
        {
            matchManager.StartMatch();
            return;
        }
        if (!PhotonNetwork.IsMasterClient) return;

        PhotonNetwork.CurrentRoom.IsOpen = false;   // 진행 중 입장 잠금

        // 로스터 = 이 순간의 접속 인원 (봇 충원 없음 — 봇은 이탈 대타 전용)
        matchManager.ClearPlayers();
        foreach (var pl in PhotonNetwork.PlayerList.OrderBy(p => p.ActorNumber))
        {
            string name = string.IsNullOrEmpty(pl.NickName) ? $"P{pl.ActorNumber}" : pl.NickName;
            matchManager.RegisterPlayer(new PlayerState(pl.ActorNumber, name));
        }
        itemController.Bind(matchManager.GetPlayer(NetworkPlayers.LocalPlayerId));
        BroadcastRoster(RpcTarget.Others);

        int rounds = -1;
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("rounds", out var r))
            rounds = (int)r;
        matchManager.StartMatch(rounds);
    }

    private void BroadcastRoster(RpcTarget target)
    {
        var ps = matchManager.Players;
        photonView.RPC(nameof(RpcRoster), target,
            ps.Select(p => p.PlayerId).ToArray(),
            ps.Select(p => p.Nickname).ToArray(),
            ps.Select(p => p.IsBot).ToArray());
    }

    [PunRPC]
    private void RpcRoster(int[] ids, string[] names, bool[] isBot)
    {
        if (IsHost) return;
        matchManager.ClearPlayers();
        for (int i = 0; i < ids.Length; i++)
            matchManager.RegisterPlayer(new PlayerState(ids[i], names[i], isBot[i]));

        itemController.Bind(matchManager.GetPlayer(NetworkPlayers.LocalPlayerId));
        Debug.Log($"[NET] 로스터 수신: {ids.Length}명 (내 ID {NetworkPlayers.LocalPlayerId})");
    }

    /// <summary>
    /// [호스트] 매치 종료 → 대타 봇 전원 해제.
    /// v22: 방 재개방 폐기 — 매치 종료 = 시상식 후 방 해산이라 죽어가는 방에
    /// 새 사람이 들어오면 안 된다 (재대결 루프 자체가 사라짐 — 유저 결정).
    /// </summary>
    private void HandleMatchEnded()
    {
        if (!IsHost) return;
        if (bots != null)
            foreach (var b in bots) if (b != null) b.Bind(null);
    }

    // ================= 시상식 춤 중계 (v22) =================

    /// <summary>
    /// [시상식] 우승자 춤 변경 요청 — 순수 코스메틱이라 호스트 검증 없이 전 클라 직행.
    /// 수신 쪽(CeremonyDirector)이 "보낸 사람 == 우승자"를 각자 검증한다.
    /// </summary>
    public void RelayCeremonyDance(int danceIndex)
    {
        if (Offline)
        {
            GameEvents.RaiseCeremonyDance(NetworkPlayers.LocalPlayerId, danceIndex);
            return;
        }
        photonView.RPC(nameof(RpcCeremonyDance), RpcTarget.All, (byte)danceIndex);
    }

    [PunRPC]
    private void RpcCeremonyDance(byte danceIndex, PhotonMessageInfo info) =>
        GameEvents.RaiseCeremonyDance(info.Sender != null ? info.Sender.ActorNumber : -1, danceIndex);

    /// <summary>[호스트] 게스트 이탈 → 매치 중이면 그 자리에 봇 대타 투입.</summary>
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (!IsHost || !matchManager.IsMatchRunning) return;

        var state = matchManager.GetPlayer(otherPlayer.ActorNumber);
        if (state == null) return;

        // 이미 이 자리에 봇이 붙어있으면 스킵
        if (bots != null && bots.Any(b => b != null && b.BoundId == state.PlayerId)) return;

        var freeBot = bots?.FirstOrDefault(b => b != null && b.BoundId < 0);
        if (freeBot != null)
        {
            freeBot.Bind(state);
            Debug.Log($"[NET] {state.Nickname} 이탈 → 봇 대타 투입 (자리 보존, 복귀 가능)");
        }
        // 대타 봇이 없어도 타임아웃 자동베팅이 안전망으로 커버
    }

    /// <summary>[호스트] 입장/복귀: 매치 중 복귀자면 봇 해제 + 상태 재전송.</summary>
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!IsHost || !matchManager.IsMatchRunning) return;
        if (matchManager.GetPlayer(newPlayer.ActorNumber) == null) return;   // 로스터 밖(이론상 불가)

        // 대타 봇 해제 — 본인 복귀
        if (bots != null)
            foreach (var b in bots)
                if (b != null && b.BoundId == newPlayer.ActorNumber) b.Bind(null);

        // 복귀자에게 로스터 재전송 (거울 재건 → 경제/제출 방송이 다시 꽂히기 시작)
        BroadcastRoster(RpcTarget.Others);
        Debug.Log($"[NET] {newPlayer.NickName} 복귀 — 봇 해제 + 로스터 재전송");
    }

    // ================= 경제 방송 (호스트 → 클라) =================

    private void Update()
    {
        if (!IsHost || Time.time < nextEconomy) return;
        nextEconomy = Time.time + economyInterval;

        var ps = matchManager.Players;
        int n = ps.Count;
        var ids = new int[n]; var points = new int[n];
        var boost = new int[n]; var slow = new int[n];
        var radioA = new int[n]; var radioB = new int[n];

        for (int i = 0; i < n; i++)
        {
            var p = ps[i];
            ids[i] = p.PlayerId; points[i] = p.Points;
            boost[i]  = p.Items.Count(it => it == boostItem);
            slow[i]   = p.Items.Count(it => it == slowItem);
            radioA[i] = p.Items.Count(it => it == radioSkillItem);
            radioB[i] = p.Items.Count(it => it == radioExecItem);
        }

        photonView.RPC(nameof(RpcEconomy), RpcTarget.Others,
            ids, points, boost, slow, radioA, radioB, matchManager.GetSubmittedIds());

        // [진단] 아이템 개수 방송 내용 (변화 시에만 출력)
        string snap = string.Join(", ", System.Linq.Enumerable.Range(0, n)
            .Select(i => $"{ids[i]}:B{boost[i]}/S{slow[i]}/R{radioA[i]}{radioB[i]}"));
        if (snap != lastItemSnap)
        {
            Debug.Log($"[진단/호스트] 아이템 방송: {snap}");
            lastItemSnap = snap;
        }
    }

    private string lastItemSnap;

    [PunRPC]
    private void RpcEconomy(int[] ids, int[] points,
                            int[] boost, int[] slow, int[] radioA, int[] radioB, int[] submittedIds)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            var p = matchManager.GetPlayer(ids[i]);
            if (p == null) continue;
            p.ApplyNetworkEconomy(points[i]);
            p.ApplyNetworkItems(boost[i], slow[i], radioA[i], radioB[i],
                                boostItem, slowItem, radioSkillItem, radioExecItem);
        }
        matchManager.ApplyNetworkSubmitted(submittedIds);
    }

    // ================= 베팅 요청 =================

    public void RequestSubmitBet(BetTicket t, Action<bool> callback)
    {
        if (Offline || IsHost)
        {
            callback?.Invoke(matchManager.SubmitBet(NetworkPlayers.LocalPlayerId, t));
            return;
        }
        pendingBetCb = callback;
        photonView.RPC(nameof(RpcRequestBet), RpcTarget.MasterClient,
            t.firstId, t.secondId, t.thirdId);
    }

    [PunRPC]
    private void RpcRequestBet(int f, int s, int t, PhotonMessageInfo info)
    {
        var ticket = new BetTicket { firstId = f, secondId = s, thirdId = t };
        bool ok = matchManager.SubmitBet(info.Sender.ActorNumber, ticket);
        photonView.RPC(nameof(RpcBetResult), info.Sender, ok);
    }

    [PunRPC]
    private void RpcBetResult(bool ok)
    {
        var cb = pendingBetCb; pendingBetCb = null;
        cb?.Invoke(ok);
    }

    // ================= 아이템 요청/중계 =================

    public void RequestUseItem(ItemDefinition item, int racerId)
    {
        if (Offline || IsHost)
        {
            itemExecutor.TryUseItem(matchManager.GetPlayer(NetworkPlayers.LocalPlayerId), item, racerId);
            return;
        }

        // 클라: 낙관적 반영 (쿨다운/개수) — 진실은 1초 내 경제 방송이 정정
        // ⚠ 쿨다운 중엔 여기서 끊는다 — 낙관적 소비가 검증보다 먼저라, 광클을 그대로 통과시키면
        //    호스트가 거절할 요청에도 클릭마다 개수가 줄고(→0이면 자동 수납으로 손에서 사라짐)
        //    쿨다운 게이지가 계속 리셋된다. 호스트는 어차피 거절하므로 RPC 낭비이기도 함.
        var me = matchManager.GetPlayer(NetworkPlayers.LocalPlayerId);
        if (me != null && !me.IsCooldownReady) return;
        if (me != null)
        {
            me.StartCooldown(GameManager.Instance.Config.GetCooldownFor(matchManager.Players.Count));
            me.ConsumeItem(item);
        }
        photonView.RPC(nameof(RpcRequestItem), RpcTarget.MasterClient, TypeOf(item), racerId);
    }

    [PunRPC]
    private void RpcRequestItem(int itemType, int racerId, PhotonMessageInfo info)
    {
        var p = matchManager.GetPlayer(info.Sender.ActorNumber);
        if (p != null) itemExecutor.TryUseItem(p, ItemOf(itemType), racerId);
    }

    // 호스트에서 발생한 아이템 사용/거절을 클라로 중계 (타임라인/피드백)
    public override void OnEnable()
    {
        base.OnEnable();
        GameEvents.OnItemUsed     += HandleItemUsed;
        GameEvents.OnRacerFinished += HandleRacerFinished;
        GameEvents.OnSkillEvent   += HandleSkillEvent;
        GameEvents.OnItemRejected += HandleItemRejected;
        GameEvents.OnRaceSettled  += HandleSettled;
        GameEvents.OnBetAccepted  += HandleBetAccepted;
        GameEvents.OnMatchEnded   += HandleMatchEnded;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        GameEvents.OnItemUsed     -= HandleItemUsed;
        GameEvents.OnRacerFinished -= HandleRacerFinished;
        GameEvents.OnSkillEvent   -= HandleSkillEvent;
        GameEvents.OnItemRejected -= HandleItemRejected;
        GameEvents.OnRaceSettled  -= HandleSettled;
        GameEvents.OnBetAccepted  -= HandleBetAccepted;
        GameEvents.OnMatchEnded   -= HandleMatchEnded;
    }

    /// <summary>[호스트] 베팅 접수(수동/자동) 시 당사자에게만 영수증 회신 — 비밀 유지하며 본인 HUD 갱신.</summary>
    private void HandleBetAccepted(int pid, BetTicket t)
    {
        if (!IsHost) return;
        var target = PhotonNetwork.PlayerList.FirstOrDefault(pl => pl.ActorNumber == pid);
        if (target != null && !target.IsMasterClient)
            photonView.RPC(nameof(RpcYourBet), target, t.firstId, t.secondId, t.thirdId);
    }

    [PunRPC]
    private void RpcYourBet(int f, int s, int t)
    {
        var me = matchManager.GetPlayer(NetworkPlayers.LocalPlayerId);
        me?.SetBet(new BetTicket { firstId = f, secondId = s, thirdId = t });
    }

    private void HandleItemUsed(int pid, ItemDefinition item, int racerId)
    {
        if (!IsHost) return;
        photonView.RPC(nameof(RpcItemUsed), RpcTarget.Others, pid, TypeOf(item), racerId);
    }

    [PunRPC]
    private void RpcItemUsed(int pid, int itemType, int racerId) =>
        GameEvents.RaiseItemUsed(pid, ItemOf(itemType), racerId);

    /// <summary>[호스트] 결승선 통과/탈락 소식을 클라 타임라인으로 중계 (아이템 중계와 같은 패턴).</summary>
    private void HandleRacerFinished(int racerId, int rank, bool eliminated)
    {
        if (!IsHost) return;
        photonView.RPC(nameof(RpcRacerFinished), RpcTarget.Others, racerId, rank, eliminated);
    }

    [PunRPC]
    private void RpcRacerFinished(int racerId, int rank, bool eliminated)
    {
        if (eliminated) raceManager.GetRacer(racerId)?.ApplyNetworkEliminated();   // 클라 거울 상태
        GameEvents.RaiseRacerFinished(racerId, rank, eliminated);
    }

    // [로컬라이제이션] 문장 대신 (사건 byte + 동물 id) 중계 — 각 클라가 자기 언어로 조립.
    // ⚠ v16 대비 RPC 시그니처 변경 (RpcSkillProc(string) → RpcSkillEvent(byte,int)) = 스탠드얼론 전원 재빌드
    private void HandleSkillEvent(SkillFeedEvent evt, int rid)
    {
        if (!IsHost) return;
        photonView.RPC(nameof(RpcSkillEvent), RpcTarget.Others, (byte)evt, rid);
    }

    [PunRPC]
    private void RpcSkillEvent(byte evt, int rid) => GameEvents.RaiseSkillEvent((SkillFeedEvent)evt, rid);

    private void HandleItemRejected(int pid, RejectReason reason)
    {
        if (!IsHost) return;
        var target = PhotonNetwork.PlayerList.FirstOrDefault(pl => pl.ActorNumber == pid);
        if (target != null && !target.IsMasterClient)
            photonView.RPC(nameof(RpcItemRejected), target, (byte)reason);
    }

    [PunRPC]
    private void RpcItemRejected(byte reason) =>
        GameEvents.RaiseItemRejected(NetworkPlayers.LocalPlayerId, (RejectReason)reason);

    // ================= 정산 방송 (베팅 공개의 순간) =================

    private void HandleSettled(RaceResult r)
    {
        if (!IsHost) return;

        var ranked = raceManager.GetFinalRanking().Select(x => x.RacerId).ToArray();
        var ps = matchManager.Players;
        int n = ps.Count;
        var pids = new int[n];
        var p1 = new int[n]; var p2 = new int[n]; var p3 = new int[n];
        var gained = new int[n];

        for (int i = 0; i < n; i++)
        {
            var p = ps[i];
            pids[i] = p.PlayerId;
            p1[i] = p.Bet.firstId; p2[i] = p.Bet.secondId; p3[i] = p.Bet.thirdId;
            gained[i] = r.pointsGained.TryGetValue(p.PlayerId, out int v) ? v : 0;
        }

        photonView.RPC(nameof(RpcSettled), RpcTarget.Others,
            r.round, ranked, pids, p1, p2, p3, gained);
    }

    [PunRPC]
    private void RpcSettled(int round, int[] rankedIds,
                            int[] pids, int[] p1, int[] p2, int[] p3, int[] gained)
    {
        raceManager.ApplyNetworkRanking(rankedIds);

        var result = new RaceResult
        {
            round = round,
            firstId  = rankedIds.Length > 0 ? rankedIds[0] : -1,
            secondId = rankedIds.Length > 1 ? rankedIds[1] : -1,
            thirdId  = rankedIds.Length > 2 ? rankedIds[2] : -1
        };
        for (int i = 0; i < pids.Length; i++)
        {
            var p = matchManager.GetPlayer(pids[i]);
            if (p != null)
                p.SetBet(new BetTicket { firstId = p1[i], secondId = p2[i], thirdId = p3[i] });
            result.pointsGained[pids[i]] = gained[i];
        }

        GameEvents.RaiseRaceSettled(result);   // 클라 정산판이 그대로 렌더
    }
}
