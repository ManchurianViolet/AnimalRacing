using UnityEngine;

/// <summary>아이템 사용 단일 관문. [멀티] 호스트 전용 실행, 클라는 RPC 요청만.</summary>
public class ItemExecutor : MonoBehaviour
{
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private GameConfig config;

    public bool TryUseItem(PlayerState player, ItemDefinition item, int targetRacerId)
    {
        if (GameManager.Instance.CurrentPhase != GamePhase.Racing)
            return Reject(player, "레이스 중이 아님");

        if (!player.IsCooldownReady)
            return Reject(player, $"쿨다운 {player.CooldownRemaining:F1}초");

        if (!player.HasItem(item))
            return Reject(player, "미보유 아이템");

        var target = raceManager.GetRacer(targetRacerId);
        if (target == null || target.HasFinished)
            return Reject(player, "유효하지 않은 타겟");

        var type = item.kind == ItemKind.Boost ? StatusEffectType.Boost : StatusEffectType.Slow;
        target.AddEffect(new StatusEffect(type, item.duration, item.magnitude));

        // [사슴] 근처(대상 포함) 경계 본능 트리거
        foreach (var r in raceManager.Racers)
        {
            if (r == null || r.HasFinished) continue;
            if (Mathf.Abs(r.Progress - target.Progress) <= SkillTuning.AlertRadius)
                r.TriggerAlert();
        }

        player.ConsumeItem(item);
        player.StartCooldown(config.GetCooldownFor(GameManager.Instance.PlayerCount));
        GameEvents.RaiseItemUsed(player.PlayerId, item, targetRacerId);
        return true;
    }

    private bool Reject(PlayerState player, string reason)
    {
        GameEvents.RaiseItemRejected(player.PlayerId, reason);
        return false;
    }
}
