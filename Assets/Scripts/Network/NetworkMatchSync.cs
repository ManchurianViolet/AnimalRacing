using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// [멀티 3단계] 매치 상태 방송국.
/// 호스트: 페이즈/타이머/라운드를 주기적으로, 배당을 계산 시점에 방송.
/// 클라: 받아서 GameManager/MatchManager에 반영 (표시 계층이 알아서 따라옴).
/// 매니저 오브젝트에 PhotonView와 함께 부착.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class NetworkMatchSync : MonoBehaviourPunCallbacks
{
    [SerializeField] private MatchManager matchManager;

    [Tooltip("상태 방송 주기 (초)")]
    [SerializeField] private float broadcastInterval = 0.5f;

    private float nextBroadcast;

    private bool IsHost => PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;

    public override void OnEnable()
    {
        base.OnEnable();
        GameEvents.OnOddsReady += HandleOddsReady;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        GameEvents.OnOddsReady -= HandleOddsReady;
    }

    /// <summary>[호스트] 라운드 중간 입장자에게 이번 라운드 배당 재전송.</summary>
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!IsHost || matchManager.CurrentOdds == null) return;
        var odds = matchManager.CurrentOdds;
        int n = odds.Length;
        float[] win = new float[n], last = new float[n];
        for (int i = 0; i < n; i++) { win[i] = odds[i].winOdds; last[i] = odds[i].lastOdds; }
        photonView.RPC(nameof(RpcOdds), newPlayer, win, last);
    }

    // ---- 호스트: 주기 방송 ----

    private void Update()
    {
        if (!IsHost || Time.time < nextBroadcast) return;
        nextBroadcast = Time.time + broadcastInterval;

        float remaining = Mathf.Max(0f, matchManager.PhaseEndTime - Time.time);
        photonView.RPC(nameof(RpcMatchState), RpcTarget.Others,
            (int)GameManager.Instance.CurrentPhase, remaining, matchManager.CurrentRound);
    }

    private void HandleOddsReady(OddsCalculator.AnimalOdds[] odds)
    {
        if (!IsHost || odds == null) return;

        int n = odds.Length;
        float[] win = new float[n], last = new float[n];
        for (int i = 0; i < n; i++) { win[i] = odds[i].winOdds; last[i] = odds[i].lastOdds; }
        photonView.RPC(nameof(RpcOdds), RpcTarget.Others, win, last);
    }

    // ---- 클라: 수신 반영 ----

    [PunRPC]
    private void RpcMatchState(int phase, float remaining, int round)
    {
        var gm = GameManager.Instance;
        var p = (GamePhase)phase;

        if (round != matchManager.CurrentRound)
        {
            matchManager.ApplyNetworkRound(round);
            GameEvents.RaiseRoundChanged(round, matchManager.TotalRounds);

            // 새 라운드 = 클라 거울도 지난 베팅 초기화 (호스트의 ClearBet과 대칭)
            foreach (var pl in matchManager.Players) pl.ClearBet();
        }

        if (gm.CurrentPhase != p)
            gm.SetPhase(p);   // 이벤트 연쇄로 UI/정리 로직이 따라옴

        matchManager.ApplyNetworkPhaseTimer(remaining);
    }

    [PunRPC]
    private void RpcOdds(float[] win, float[] last)
    {
        int n = win.Length;
        var odds = new OddsCalculator.AnimalOdds[n];
        for (int i = 0; i < n; i++)
        {
            odds[i] = new OddsCalculator.AnimalOdds
            {
                winOdds = win[i],
                lastOdds = last[i],
                winProbability = 0.9f / Mathf.Max(1.1f, win[i]),
                lastProbability = 0.9f / Mathf.Max(1.1f, last[i])
            };
        }
        matchManager.ApplyNetworkOdds(odds);
        GameEvents.RaiseOddsReady(odds);
    }
}
