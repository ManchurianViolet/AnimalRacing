using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 배당 기반 정산: 지급 = 베팅액 × 배당 (원금 포함). 빗나가면 베팅액 소멸(제출 시 이미 차감됨).
/// </summary>
public class OddsSettlement : MonoBehaviour
{
    [SerializeField] private RaceManager raceManager;

    public void Settle(IReadOnlyList<PlayerState> players,
                       OddsCalculator.AnimalOdds[] odds, int round)
    {
        var ranking = raceManager.GetFinalRanking();
        if (ranking.Count == 0) return;

        int firstId = ranking[0].RacerId;
        int lastId  = ranking[^1].RacerId;

        var result = new RaceResult { round = round, firstId = firstId, lastId = lastId };

        foreach (var p in players)
        {
            int payout = 0;
            if (p.Bet.firstId == firstId && p.Bet.firstAmount > 0)
                payout += Mathf.FloorToInt(p.Bet.firstAmount * odds[firstId].winOdds);
            if (p.Bet.lastId == lastId && p.Bet.lastAmount > 0)
                payout += Mathf.FloorToInt(p.Bet.lastAmount * odds[lastId].lastOdds);

            p.AddMoney(payout);
            result.payouts[p.PlayerId] = payout;
        }

        GameEvents.RaiseRaceSettled(result);
    }
}

/// <summary>정산 결과.</summary>
public class RaceResult
{
    public int round;
    public int firstId, lastId;
    public Dictionary<int, int> payouts = new();          // playerId → 수령액 (원금 포함)
    public List<string> carriedPots = new();              // (팟 제도 폐기 — 호환용 빈 목록)
}
