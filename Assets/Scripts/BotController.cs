using System.Linq;
using UnityEngine;

/// <summary>
/// 봇 (읽히는 행동 = 추리 단서):
///  1순위: 내 꼴등픽이 꼴찌가 아니면 → 감속으로 처박기 (티키토플 근본 플레이)
///  2순위: 내 1등픽이 선두가 아니면 → 부스트로 밀어주기
/// </summary>
public class BotController : MonoBehaviour
{
    [SerializeField] private ItemExecutor executor;
    [SerializeField] private RaceManager raceManager;
    [Range(0f, 1f)] public float actChancePerSecond = 0.5f;

    private PlayerState bot;

    public void Bind(PlayerState state) => bot = state;

    private void Update()
    {
        if (bot == null || GameManager.Instance.CurrentPhase != GamePhase.Racing) return;
        if (!bot.IsCooldownReady || bot.Items.Count == 0) return;
        if (Random.value > actChancePerSecond * Time.deltaTime) return;

        var running = raceManager.Racers.Where(r => !r.HasFinished)
                                        .OrderByDescending(r => r.Progress).ToList();
        if (running.Count < 2) return;

        var myFirst = raceManager.GetRacer(bot.Bet.firstId);
        var myLast  = raceManager.GetRacer(bot.Bet.lastId);
        var slow  = bot.Items.FirstOrDefault(i => i.kind == ItemKind.Slow);
        var boost = bot.Items.FirstOrDefault(i => i.kind == ItemKind.Boost);

        // 1순위: 꼴등픽 관리
        if (slow != null && myLast != null && !myLast.HasFinished &&
            running[^1].RacerId != bot.Bet.lastId)
        {
            executor.TryUseItem(bot, slow, bot.Bet.lastId);
            return;
        }

        // 2순위: 1등픽 밀어주기
        if (boost != null && myFirst != null && !myFirst.HasFinished &&
            running[0].RacerId != bot.Bet.firstId)
        {
            executor.TryUseItem(bot, boost, bot.Bet.firstId);
            return;
        }

        // 3순위: 남는 감속은 현재 선두 견제
        if (slow != null && running[0].RacerId != bot.Bet.firstId)
            executor.TryUseItem(bot, slow, running[0].RacerId);
    }
}
