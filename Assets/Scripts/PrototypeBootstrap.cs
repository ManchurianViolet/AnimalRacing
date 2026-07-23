using System.Collections.Generic;
using System.Linq;
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

    private PlayerState me;

    private void Start()
    {
        me = new PlayerState(0, "나");
        matchManager.RegisterPlayer(me);
        itemController.Bind(me);

        for (int i = 0; i < bots.Length; i++)
        {
            var b = new PlayerState(i + 1, $"봇{(char)('A' + i)}", isBot: true);
            matchManager.RegisterPlayer(b);
            bots[i].Bind(b);
        }

        GameEvents.OnPhaseChanged += p => { if (p == GamePhase.Betting) AssignBetsAndLoadouts(); };

        matchManager.StartMatch();
    }

    private void AssignBetsAndLoadouts()
    {
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
