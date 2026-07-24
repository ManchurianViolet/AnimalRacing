using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// 스모크 테스트 부트스트랩: 플레이어 생성 + 매치 시작 + 로드아웃 배정.
/// 입력/선택은 PlayerItemController, 표시는 PlayerHUD로 이관됨.
/// 정산 OnGUI만 임시 유지 (정산 패널 제작 시 삭제 예정).
/// </summary>
public class PrototypeBootstrap : MonoBehaviour
{
    [Header("씬 레퍼런스")]
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private PlayerItemController itemController;
    [SerializeField] private BotController[] bots;

    [Header("아이템 SO (2종)")]
    [SerializeField] private ItemDefinition boostItem;
    [SerializeField] private ItemDefinition slowItem;

    [Header("네트워크")]
    [SerializeField] private NetworkGateway gateway;

    [Tooltip("매치 정원 (부족분은 봇으로 채움)")]
    [SerializeField] private int targetPlayerCount = 4;

    private void Start()
    {
        GameEvents.OnPhaseChanged += p => { if (p == GamePhase.Betting) AssignBetsAndLoadouts(); };
        StartCoroutine(StartWhenReady());
    }

    /// <summary>오프라인 전용: 기존 로컬 등록 (나 + 봇들).</summary>
    private void RegisterOfflinePlayers()
    {
        var me = new PlayerState(0, "나");
        matchManager.RegisterPlayer(me);
        itemController.Bind(me);

        for (int i = 0; i < bots.Length; i++)
        {
            var b = new PlayerState(i + 1, $"봇{(char)('A' + i)}", isBot: true);
            matchManager.RegisterPlayer(b);
            bots[i].Bind(b);
        }
    }

    /// <summary>
    /// 접속이 진행 중이면 방 입장까지 기다렸다가 시작 (타이밍 경쟁 방지).
    /// Photon 접속은 비동기라 Start 시점엔 아직 방 밖 — 그때 검사하면 전원이
    /// 자기를 오프라인 호스트로 착각하고 각자 게임을 돌리는 사고가 남.
    /// </summary>
    private System.Collections.IEnumerator StartWhenReady()
    {
        // 씬에 런처가 있으면 = 온라인 의도. 방 입장 확정 또는 접속 실패 확정까지 대기.
        // (타임아웃으로 어설프게 오프라인 시작하면, 뒤늦은 입장과 겹쳐
        //  로컬 매치 + 네트워크 매치가 공존하는 대참사가 남)
        bool onlineIntended = FindFirstObjectByType<NetworkLauncher>() != null;

        if (onlineIntended)
        {
            float timeout = Time.time + 30f;
            while (!PhotonNetwork.InRoom
                   && PhotonNetwork.NetworkClientState != Photon.Realtime.ClientState.Disconnected
                   && Time.time < timeout)
                yield return null;

            if (!PhotonNetwork.InRoom)
                Debug.LogWarning("[Bootstrap] 온라인 접속 실패 — 오프라인으로 시작합니다");
        }

        bool inRoom = PhotonNetwork.InRoom;
        Debug.Log($"[Bootstrap] 시작 준비 완료 — 방: {inRoom}, 호스트: {NetworkPlayers.IsAuthority}");

        if (!inRoom)
        {
            RegisterOfflinePlayers();
            matchManager.StartMatch();
        }
        else if (PhotonNetwork.IsMasterClient)
        {
            gateway.BuildHostRoster(targetPlayerCount, bots);
            matchManager.StartMatch();
        }
        // 클라: 로스터는 gateway RPC로, 페이즈는 NetworkMatchSync 방송으로 따라감
    }

    private void AssignBetsAndLoadouts()
    {
        if (!NetworkPlayers.IsAuthority) return;   // 로드아웃/봇 베팅은 호스트 소관

        if (boostItem == null || slowItem == null)
        {
            Debug.LogError("[Bootstrap] Boost Item / Slow Item 슬롯이 비어있습니다!");
            return;
        }

        var cfg = GameManager.Instance.Config;
        var loadout = new List<ItemDefinition>();
        for (int i = 0; i < cfg.boostCount; i++) loadout.Add(boostItem);
        for (int i = 0; i < cfg.slowCount; i++)  loadout.Add(slowItem);

        foreach (var p in matchManager.Players)
        {
            p.SetLoadout(loadout);
            // 봇: 관문(SubmitBet) 경유 — 사람과 동일하게 잔액에서 차감됨
            if (p.IsBot)
                matchManager.SubmitBet(p.PlayerId, RandomBet(p));
        }
    }

    private BetTicket RandomBet(PlayerState p)
    {
        var ids = Enumerable.Range(0, GameManager.Instance.Config.racerCount)
                            .OrderBy(_ => Random.value).Take(2).ToArray();
        // 봇 베팅 규모: 잔액의 5~25%씩 ($10 단위, 최소 $10)
        int a = To10(p.Money * Random.Range(0.05f, 0.25f));
        int b = To10(p.Money * Random.Range(0.05f, 0.25f));
        if (a + b > p.Money) { a = To10(p.Money / 2f); b = Mathf.Max(10, (p.Money - a) / 10 * 10); }
        return new BetTicket { firstId = ids[0], lastId = ids[1], firstAmount = a, lastAmount = b };
    }

    private static int To10(float v) => Mathf.Max(10, Mathf.FloorToInt(v / 10f) * 10);

}
