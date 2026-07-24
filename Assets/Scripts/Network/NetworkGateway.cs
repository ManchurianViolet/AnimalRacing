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

    [Header("아이템 SO (네트워크 직렬화용: 0=부스트, 1=감속)")]
    [SerializeField] private ItemDefinition boostItem;
    [SerializeField] private ItemDefinition slowItem;

    [Tooltip("경제 상태 방송 주기 (초)")]
    [SerializeField] private float economyInterval = 1f;

    private float nextEconomy;
    private BotController[] hostBots;
    private Action<bool> pendingBetCb;
    private Action<bool> pendingLoanCb;

    private bool IsHost => PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
    private bool Offline => !PhotonNetwork.InRoom;

    private void Awake()
    {
        if (boostItem == null || slowItem == null)
            Debug.LogError("[NetworkGateway] Boost Item / Slow Item 슬롯이 비어있습니다! " +
                "아이템 개수 방송이 전부 0이 되어 게스트 아이템이 0개로 보입니다. " +
                "Bootstrap과 같은 SO를 연결하세요.");
    }

    private ItemDefinition ItemOf(int type) => type == 0 ? boostItem : slowItem;
    private int TypeOf(ItemDefinition item) => item == boostItem ? 0 : 1;

    // ================= 로스터 =================

    /// <summary>[호스트] 접속 인원 + 봇으로 명단 구성 후 방송. Bootstrap이 호출.</summary>
    public void BuildHostRoster(int targetCount, BotController[] bots)
    {
        hostBots = bots;
        matchManager.ClearPlayers();

        foreach (var pl in PhotonNetwork.PlayerList.OrderBy(p => p.ActorNumber))
        {
            string name = string.IsNullOrEmpty(pl.NickName) ? $"P{pl.ActorNumber}" : pl.NickName;
            matchManager.RegisterPlayer(new PlayerState(pl.ActorNumber, name));
        }

        int botCount = Mathf.Clamp(targetCount - PhotonNetwork.PlayerList.Length, 0, bots.Length);
        for (int i = 0; i < botCount; i++)
        {
            var b = new PlayerState(NetworkPlayers.BotIdBase + i, $"봇{(char)('A' + i)}", isBot: true);
            matchManager.RegisterPlayer(b);
            bots[i].Bind(b);
        }

        itemController.Bind(matchManager.GetPlayer(NetworkPlayers.LocalPlayerId));
        BroadcastRoster(RpcTarget.Others);
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

    /// <summary>[호스트] 중간 입장자 등록: 봇 하나가 자리를 양보하고 사람으로 교체.</summary>
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!IsHost) return;
        if (matchManager.GetPlayer(newPlayer.ActorNumber) != null) return;

        // 봇 자리 양보 (ID 큰 봇부터 은퇴)
        var bot = matchManager.Players
            .Where(x => x.IsBot).OrderByDescending(x => x.PlayerId).FirstOrDefault();
        if (bot != null)
        {
            matchManager.RemovePlayer(bot.PlayerId);
            if (hostBots != null)
                foreach (var bc in hostBots)
                    if (bc != null && bc.BoundId == bot.PlayerId) bc.enabled = false;
        }

        string name = string.IsNullOrEmpty(newPlayer.NickName) ? $"P{newPlayer.ActorNumber}" : newPlayer.NickName;
        var p = new PlayerState(newPlayer.ActorNumber, name);
        p.ResetEconomy(GameManager.Instance.Config.startMoney);

        // 중간 입장자 로드아웃 지급 (라운드 시작 일괄 배정을 놓쳤으므로 여기서 직접)
        var cfg = GameManager.Instance.Config;
        var loadout = new System.Collections.Generic.List<ItemDefinition>();
        for (int i = 0; i < cfg.boostCount; i++) loadout.Add(boostItem);
        for (int i = 0; i < cfg.slowCount; i++)  loadout.Add(slowItem);
        p.SetLoadout(loadout);

        matchManager.RegisterPlayer(p);
        BroadcastRoster(RpcTarget.Others);
    }

    // ================= 경제 방송 (호스트 → 클라) =================

    private void Update()
    {
        if (!IsHost || Time.time < nextEconomy) return;
        nextEconomy = Time.time + economyInterval;

        var ps = matchManager.Players;
        int n = ps.Count;
        var ids = new int[n]; var money = new int[n]; var debt = new int[n];
        var borrowed = new bool[n]; var boost = new int[n]; var slow = new int[n];

        for (int i = 0; i < n; i++)
        {
            var p = ps[i];
            ids[i] = p.PlayerId; money[i] = p.Money; debt[i] = p.Debt;
            borrowed[i] = p.BorrowedThisRound;
            boost[i] = p.Items.Count(it => it == boostItem);
            slow[i]  = p.Items.Count(it => it == slowItem);
        }

        photonView.RPC(nameof(RpcEconomy), RpcTarget.Others,
            ids, money, debt, borrowed, boost, slow, matchManager.GetSubmittedIds());

        // [진단] 아이템 개수 방송 내용 (변화 시에만 출력)
        string snap = string.Join(", ", System.Linq.Enumerable.Range(0, n)
            .Select(i => $"{ids[i]}:B{boost[i]}/S{slow[i]}"));
        if (snap != lastItemSnap)
        {
            Debug.Log($"[진단/호스트] 아이템 방송: {snap}");
            lastItemSnap = snap;
        }
    }

    private string lastItemSnap;

    [PunRPC]
    private void RpcEconomy(int[] ids, int[] money, int[] debt, bool[] borrowed,
                            int[] boost, int[] slow, int[] submittedIds)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            var p = matchManager.GetPlayer(ids[i]);
            if (p == null)
            {
                Debug.LogWarning($"[진단/클라] 경제 방송의 ID {ids[i]}가 내 거울 명단에 없음!");
                continue;
            }
            p.ApplyNetworkEconomy(money[i], debt[i], borrowed[i]);
            p.ApplyNetworkItems(boost[i], slow[i], boostItem, slowItem);
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
            t.firstId, t.lastId, t.firstAmount, t.lastAmount);
    }

    [PunRPC]
    private void RpcRequestBet(int f, int l, int fa, int la, PhotonMessageInfo info)
    {
        var ticket = new BetTicket { firstId = f, lastId = l, firstAmount = fa, lastAmount = la };
        bool ok = matchManager.SubmitBet(info.Sender.ActorNumber, ticket);
        photonView.RPC(nameof(RpcBetResult), info.Sender, ok);
    }

    [PunRPC]
    private void RpcBetResult(bool ok)
    {
        var cb = pendingBetCb; pendingBetCb = null;
        cb?.Invoke(ok);
    }

    // ================= 은행 요청 =================

    public void RequestLoan(int amount, Action<bool> callback)
    {
        if (Offline || IsHost)
        {
            callback?.Invoke(matchManager.TryAtmLoan(NetworkPlayers.LocalPlayerId, amount));
            return;
        }
        pendingLoanCb = callback;
        photonView.RPC(nameof(RpcRequestLoan), RpcTarget.MasterClient, amount);
    }

    [PunRPC]
    private void RpcRequestLoan(int amount, PhotonMessageInfo info)
    {
        bool ok = matchManager.TryAtmLoan(info.Sender.ActorNumber, amount);
        photonView.RPC(nameof(RpcLoanResult), info.Sender, ok);
    }

    [PunRPC]
    private void RpcLoanResult(bool ok)
    {
        var cb = pendingLoanCb; pendingLoanCb = null;
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
        var me = matchManager.GetPlayer(NetworkPlayers.LocalPlayerId);
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
        GameEvents.OnItemRejected += HandleItemRejected;
        GameEvents.OnRaceSettled  += HandleSettled;
        GameEvents.OnBetAccepted  += HandleBetAccepted;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        GameEvents.OnItemUsed     -= HandleItemUsed;
        GameEvents.OnItemRejected -= HandleItemRejected;
        GameEvents.OnRaceSettled  -= HandleSettled;
        GameEvents.OnBetAccepted  -= HandleBetAccepted;
    }

    /// <summary>[호스트] 베팅 접수(수동/자동) 시 당사자에게만 영수증 회신 — 비밀 유지하며 본인 HUD 갱신.</summary>
    private void HandleBetAccepted(int pid, BetTicket t)
    {
        if (!IsHost) return;
        var target = PhotonNetwork.PlayerList.FirstOrDefault(pl => pl.ActorNumber == pid);
        if (target != null && !target.IsMasterClient)
            photonView.RPC(nameof(RpcYourBet), target,
                t.firstId, t.lastId, t.firstAmount, t.lastAmount);
    }

    [PunRPC]
    private void RpcYourBet(int f, int l, int fa, int la)
    {
        var me = matchManager.GetPlayer(NetworkPlayers.LocalPlayerId);
        me?.SetBet(new BetTicket { firstId = f, lastId = l, firstAmount = fa, lastAmount = la });
    }

    private void HandleItemUsed(int pid, ItemDefinition item, int racerId)
    {
        if (!IsHost) return;
        photonView.RPC(nameof(RpcItemUsed), RpcTarget.Others, pid, TypeOf(item), racerId);
    }

    [PunRPC]
    private void RpcItemUsed(int pid, int itemType, int racerId) =>
        GameEvents.RaiseItemUsed(pid, ItemOf(itemType), racerId);

    private void HandleItemRejected(int pid, string reason)
    {
        if (!IsHost) return;
        var target = PhotonNetwork.PlayerList.FirstOrDefault(pl => pl.ActorNumber == pid);
        if (target != null && !target.IsMasterClient)
            photonView.RPC(nameof(RpcItemRejected), target, reason);
    }

    [PunRPC]
    private void RpcItemRejected(string reason) =>
        GameEvents.RaiseItemRejected(NetworkPlayers.LocalPlayerId, reason);

    // ================= 정산 방송 (베팅 공개의 순간) =================

    private void HandleSettled(RaceResult r)
    {
        if (!IsHost) return;

        var ranked = raceManager.GetFinalRanking().Select(x => x.RacerId).ToArray();
        var ps = matchManager.Players;
        int n = ps.Count;
        var pids = new int[n]; var bf = new int[n]; var bl = new int[n];
        var af = new int[n]; var al = new int[n]; var pay = new int[n];

        for (int i = 0; i < n; i++)
        {
            var p = ps[i];
            pids[i] = p.PlayerId;
            bf[i] = p.Bet.firstId; bl[i] = p.Bet.lastId;
            af[i] = p.Bet.firstAmount; al[i] = p.Bet.lastAmount;
            pay[i] = r.payouts.TryGetValue(p.PlayerId, out int v) ? v : 0;
        }

        photonView.RPC(nameof(RpcSettled), RpcTarget.Others,
            r.round, r.firstId, r.lastId, ranked, pids, bf, bl, af, al, pay);
    }

    [PunRPC]
    private void RpcSettled(int round, int firstId, int lastId, int[] rankedIds,
                            int[] pids, int[] bf, int[] bl, int[] af, int[] al, int[] pay)
    {
        raceManager.ApplyNetworkRanking(rankedIds);

        var result = new RaceResult { round = round, firstId = firstId, lastId = lastId };
        for (int i = 0; i < pids.Length; i++)
        {
            var p = matchManager.GetPlayer(pids[i]);
            if (p != null)
                p.SetBet(new BetTicket { firstId = bf[i], lastId = bl[i], firstAmount = af[i], lastAmount = al[i] });
            result.payouts[pids[i]] = pay[i];
        }

        GameEvents.RaiseRaceSettled(result);   // 클라 정산판이 그대로 렌더
    }
}
