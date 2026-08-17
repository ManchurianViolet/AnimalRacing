using System.Collections;
using UnityEngine;

/// <summary>
/// [사운드] 게임 사건 → 효과음 중계기. SoundManager가 자동으로 얹으므로 씬 배선이 필요 없다.
///
/// 한 곳에 모은 이유: 아이템 사용(ItemExecutor)·완주/탈락(Racer)·스킬 발동(Racer.SimTick)은
/// 전부 호스트에서만 도는 코드라, 그 자리에 소리를 넣으면 게스트 화면에서는 조용하다.
/// 반면 GameEvents는 게이트웨이가 전 클라로 중계하므로 이벤트를 구독해 각자 로컬 재생하면
/// 네트워크 추가 통신 0 (부스트 먼지·무전기 LCD와 같은 철학).
///
/// 반대로 "내 손"에서만 나는 소리(빠따 스윙·슬롯 전환·피규어 집기)는 각 호출부에 직접 넣었다 —
/// 그쪽은 애초에 로컬/RPC 재생 지점이라 여기로 끌어올 이유가 없다.
/// </summary>
public class SfxRelay : MonoBehaviour
{
    [Tooltip("완주 소리를 낼 상위 등수 — 9마리가 전부 삑삑거리면 시상대의 무게가 죽는다")]
    [SerializeField] private int finishSfxTopRanks = 3;

    // 씬마다 다른 오브젝트라 지연 탐색 (SoundManager는 씬을 넘어 살아남는다).
    // 씬이 바뀌면 옛 참조가 fake null이 되므로 == null 검사만으로 자동 재탐색된다.
    private RaceManager raceManager;
    private Coroutine countdownCo;

    private RaceManager Race
    {
        get
        {
            if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();
            return raceManager;
        }
    }

    private void OnEnable()
    {
        GameEvents.OnItemUsed += HandleItemUsed;
        GameEvents.OnRacerFinished += HandleRacerFinished;
        GameEvents.OnSkillEvent += HandleSkillEvent;
        GameEvents.OnPhaseChanged += HandlePhaseChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnItemUsed -= HandleItemUsed;
        GameEvents.OnRacerFinished -= HandleRacerFinished;
        GameEvents.OnSkillEvent -= HandleSkillEvent;
        GameEvents.OnPhaseChanged -= HandlePhaseChanged;
    }

    // ================= 아이템 =================

    private void HandleItemUsed(int pid, ItemDefinition item, int rid)
    {
        if (item == null) return;
        bool syringe = item.kind == ItemKind.Boost || item.kind == ItemKind.Slow;

        // 발사음은 쏜 본인 화면에서만 (2D) — 남이 12m 밖에서 주사기를 쏘는 소리까지 들릴 필요는 없다.
        // 이 이벤트는 호스트 검증을 통과한 사용에만 오므로 쿨다운·재고 거부 때는 소리가 안 난다.
        if (pid == NetworkPlayers.LocalPlayerId)
            SoundManager.PlaySfx(syringe ? SfxId.SyringeShot : SfxId.RadioUse);

        // 명중음은 맞은 동물 자리에서 (3D) — 누가 쐈든 근처에 있으면 들린다.
        // 무전기 2종은 효과가 5초 뒤에 나오므로 여기선 침묵 (그 자리는 LCD 타이핑이 맡는다).
        if (!syringe) return;
        if (TryRacerPos(rid, out var pos))
            SoundManager.PlaySfx(item.kind == ItemKind.Boost ? SfxId.SyringeHitBoost : SfxId.SyringeHitSlow, pos);
    }

    // ================= 완주 / 처형 =================

    private void HandleRacerFinished(int rid, int rank, bool eliminated)
    {
        if (eliminated)
        {
            // 처형은 판 전체의 사건 — 어디서 벌어졌든 똑같이 들려야 한다 (2D)
            SoundManager.PlaySfx(SfxId.Execution);
            return;
        }
        if (rank <= finishSfxTopRanks) SoundManager.PlaySfx(SfxId.RacerFinish);
    }

    // ================= 스킬 =================

    /// <summary>
    /// 스킬 발동 감지 — v16까지는 피드 문자열의 한국어 키워드 매칭이라 문구를 바꾸면 소리가
    /// 조용히 죽는 취약점이 있었다. 로컬라이제이션 개편으로 사건이 enum+동물 id로 오면서
    /// 매칭이 구조적으로 안전해짐 (문구는 이제 표시 전용 — 여기와 무관).
    /// </summary>
    private void HandleSkillEvent(SkillFeedEvent evt, int rid)
    {
        // [인간] 몽둥이 명중 — 지속음이 아니라 원샷 타격음 (PvP 빠따와 같은 소리, 유저 결정)
        if (evt == SkillFeedEvent.ClubHit)
        {
            var hitter = Race != null ? Race.GetRacer(rid) : null;
            if (hitter != null) SoundManager.PlaySfx(SfxId.BatHit, hitter.transform.position);
            else SoundManager.PlaySfx(SfxId.BatHit);
            return;
        }

        SfxId id;
        switch (evt)
        {
            case SkillFeedEvent.Roar:    id = SfxId.SkillRoar;    break;
            case SkillFeedEvent.Rudolph: id = SfxId.SkillRudolph; break;
            case SkillFeedEvent.Dash:    id = SfxId.SkillDash;    break;
            case SkillFeedEvent.CatWalk: id = SfxId.SkillCatWalk; break;
            case SkillFeedEvent.ClubRush: id = SfxId.SkillClubRush; break;
            default: return;   // 처형 예고·펭귄 무관심 등 나머지 사건은 소리 없음
        }

        // 발동 순간 1회만 재생 (유저 결정 — 지속 루프는 스킬이 겹치면 정신없다).
        // 발동한 동물 자리에서 3D. 못 찾으면 2D로 폴백.
        var racer = Race != null ? Race.GetRacer(rid) : null;
        if (racer != null) SoundManager.PlaySfx(id, racer.transform.position);
        else SoundManager.PlaySfx(id);
    }

    // ================= 페이즈 =================

    private void HandlePhaseChanged(GamePhase p)
    {
        if (countdownCo != null) { StopCoroutine(countdownCo); countdownCo = null; }

        if (p == GamePhase.Countdown) countdownCo = StartCoroutine(CountdownBeeps());
        else if (p == GamePhase.Racing) SoundManager.PlaySfx(SfxId.RaceStart);
    }

    /// <summary>카운트다운 1초 간격 비프. 게스트는 페이즈 방송(0.5초 주기)만큼 늦게 시작할 수 있다.</summary>
    private IEnumerator CountdownBeeps()
    {
        var cfg = GameManager.Instance != null ? GameManager.Instance.Config : null;
        int beeps = cfg != null ? Mathf.RoundToInt(cfg.countdownSeconds) : 3;
        for (int i = 0; i < beeps; i++)
        {
            SoundManager.PlaySfx(SfxId.CountdownBeep);
            yield return new WaitForSeconds(1f);
        }
        countdownCo = null;
    }

    // ================= 위치 조회 =================

    private bool TryRacerPos(int rid, out Vector3 pos)
    {
        pos = default;
        if (rid < 0 || Race == null) return false;
        var r = Race.GetRacer(rid);
        if (r == null) return false;
        pos = r.transform.position;
        return true;
    }

}
