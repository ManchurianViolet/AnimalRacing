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

    [Header("스킬 지속음")]
    [Tooltip("스킬음이 최대 음량까지 커지는 시간(초) — 툭 튀어나오지 않게")]
    [SerializeField] private float skillFadeIn = 0.5f;
    [Tooltip("스킬이 끝나기 이만큼(초) 전부터 서서히 잦아든다")]
    [SerializeField] private float skillFadeOut = 0.5f;

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
        GameEvents.OnSkillProc += HandleSkillProc;
        GameEvents.OnPhaseChanged += HandlePhaseChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnItemUsed -= HandleItemUsed;
        GameEvents.OnRacerFinished -= HandleRacerFinished;
        GameEvents.OnSkillProc -= HandleSkillProc;
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
    /// 스킬 발동 감지 — 전 클라로 중계되는 건 피드 문자열뿐이라 키워드로 가른다 (RoarFx와 같은 방식).
    /// ⚠ Racer / RacerMotor / RaceManager의 피드 문구를 고치면 여기 키워드도 같이 고쳐야 한다 —
    ///   안 그러면 에러 없이 소리만 조용히 사라진다.
    /// </summary>
    private void HandleSkillProc(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        SfxId id;
        float duration;   // 스킬이 실제로 지속되는 시간 — SkillTuning이 단일 출처
        if (line.Contains("포효")) { id = SfxId.SkillRoar; duration = SkillTuning.RoarDuration; }
        else if (line.Contains("루돌프")) { id = SfxId.SkillRudolph; duration = SkillTuning.RudolphFlightSeconds; }
        else if (line.Contains("냅다 달린다")) { id = SfxId.SkillDash; duration = SkillTuning.DashDuration; }
        else if (line.Contains("사뿐사뿐")) { id = SfxId.SkillCatWalk; duration = SkillTuning.CatWalkDuration; }
        else return;   // 처형 예고·펭귄 무관심 등 나머지 피드는 소리 없음

        // 발동한 동물을 따라다니며 3D 루프 — 달리는 중이라 위치를 고정하면 소리만 뒤에 남는다.
        // 문구 앞머리가 동물 이름이라 그걸로 찾는다. 못 찾으면 2D로 폴백.
        var racer = FindRacerByName(line);
        SoundManager.PlaySfxLoop(id, duration, racer != null ? racer.transform : null,
                                 skillFadeIn, skillFadeOut);
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

    /// <summary>피드 문구 앞머리의 이름으로 동물을 찾는다 (못 찾으면 null → 2D 폴백).</summary>
    private Racer FindRacerByName(string line)
    {
        if (Race == null) return null;
        foreach (var r in Race.Racers)
        {
            if (r == null || string.IsNullOrEmpty(r.DisplayName)) continue;
            if (line.StartsWith(r.DisplayName)) return r;
        }
        return null;
    }
}
