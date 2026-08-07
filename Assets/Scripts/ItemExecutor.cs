using System.Collections;
using UnityEngine;

/// <summary>아이템 사용 단일 관문. [멀티] 호스트 전용 실행, 클라는 RPC 요청만.
/// 주사기(즉발) + 무전기 2종(5초 지연: 발동권=지정 스킬 강제 발동 / 처형권=그 시점 꼴등 탈락).</summary>
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

        switch (item.kind)
        {
            case ItemKind.Boost:
            case ItemKind.Slow:
            {
                var target = raceManager.GetRacer(targetRacerId);
                if (target == null || target.HasFinished)
                    return Reject(player, "유효하지 않은 타겟");

                var type = item.kind == ItemKind.Boost ? StatusEffectType.Boost : StatusEffectType.Slow;
                target.AddEffect(new StatusEffect(type, item.duration, item.magnitude));
                break;
            }

            case ItemKind.SkillTrigger:
            {
                var target = raceManager.GetRacer(targetRacerId);
                if (target == null || target.HasFinished)
                    return Reject(player, "유효하지 않은 타겟");
                // 패시브(말/개/펭귄)는 무전기 무반응 — 클라 조준 단계에서도 차단되지만 호스트가 최종 검증
                if (!SkillTuning.IsActive(target.Definition.skill))
                    return Reject(player, "사용 불가능한 동물");
                StartCoroutine(RadioSkillDelayed(target));
                break;
            }

            case ItemKind.Execute:
                // 대상은 발동 순간(5초 후)의 꼴등 — 지금은 예고만
                StartCoroutine(RadioExecDelayed());
                GameEvents.RaiseSkillProc("살벌한 무전이 울렸다... 꼴찌는 각오해라!");
                break;
        }

        player.ConsumeItem(item);
        player.StartCooldown(config.GetCooldownFor(GameManager.Instance.PlayerCount));
        GameEvents.RaiseItemUsed(player.PlayerId, item, targetRacerId);
        return true;
    }

    /// <summary>[발동 무전기] 지연 후 대상 스킬 강제 발동 — 1회 제한 무시 (레이스가 끝났으면 불발).</summary>
    private IEnumerator RadioSkillDelayed(Racer target)
    {
        yield return new WaitForSeconds(config.radioDelaySeconds);
        if (GameManager.Instance.CurrentPhase != GamePhase.Racing) yield break;
        if (target == null || target.HasFinished) yield break;
        target.ForceSkillByRadio();
    }

    /// <summary>[처형 무전기] 지연 후 그 시점의 꼴등 탈락 (레이스가 끝났으면 불발).</summary>
    private IEnumerator RadioExecDelayed()
    {
        yield return new WaitForSeconds(config.radioDelaySeconds);
        if (GameManager.Instance.CurrentPhase != GamePhase.Racing) yield break;
        raceManager.ExecuteLastPlace();
    }

    private bool Reject(PlayerState player, string reason)
    {
        GameEvents.RaiseItemRejected(player.PlayerId, reason);
        return false;
    }
}
