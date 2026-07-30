using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 레이서: 정체성 + 상태이상 + 진행도 + 애니메이터 + 속도 리롤.
/// 속도는 리롤 주기마다 범위 내 랜덤으로 갱신되고, 급변 없이 부드럽게 수렴.
/// </summary>
public class Racer : MonoBehaviour
{
    public int RacerId { get; private set; }
    public string DisplayName { get; private set; }
    public AnimalDefinition Definition { get; private set; }

    public float Progress { get; private set; }
    public bool HasFinished { get; private set; }
    public int FinishRank { get; private set; } = -1;

    [Header("애니메이터 (ithappy 규약)")]
    [SerializeField] private string vertID = "Vert";
    [SerializeField] private string stateID = "State";

    private Animator animator;
    private Rigidbody rb;
    private float animVert;

    // ---- 속도 리롤 상태 ----
    private float rolledSpeed;      // 이번 구간의 목표 속도 (범위 내 랜덤)
    private float smoothedSpeed;    // 실제 적용 속도 (목표로 부드럽게 수렴)
    private float rerollTimer;

    private readonly List<StatusEffect> effects = new();

    // ---- 스킬 상태 ----
    private float trackLength = 1f;     // RaceManager가 세팅 (진행률 계산용)
    private float raceTime;             // 이번 레이스 경과 (치킨)
    private bool isLastPlace;           // RaceManager가 매 틱 세팅 (개)
    private float activeTriggerRatio;   // 액티브 발동 진행률 (호랑이/고양이)
    private bool activeConsumed;
    private float alertPending = -1f;   // 사슴: 발동 대기 타이머

    public float ProgressRatio => Progress / Mathf.Max(1f, trackLength);
    public bool IsStunned
    {
        get { foreach (var e in effects) if (e.type == StatusEffectType.Stun) return true; return false; }
    }

    public void SetTrackLength(float len) => trackLength = Mathf.Max(1f, len);
    public void SetLastPlace(bool last) => isLastPlace = last;

    /// <summary>[호랑이] 액티브 발동 시점 도달 & 미사용이면 소비하고 true.</summary>
    public bool TryConsumeAmbush()
    {
        if (Definition.skill != AnimalSkill.Ambush || activeConsumed) return false;
        if (ProgressRatio < activeTriggerRatio) return false;
        activeConsumed = true;
        return true;
    }

    /// <summary>[사슴] 근처 아이템 폭발 감지 — 지연 후 도주 가속.</summary>
    public void TriggerAlert()
    {
        if (Definition.skill != AnimalSkill.Alert || HasFinished) return;
        if (alertPending >= 0f) return;   // 이미 대기 중
        alertPending = SkillTuning.AlertDelay;
    }

    /// <summary>현재 유효 최고속도 = 리롤 속도 × 아이템 배율 × 스킬 배율 (스턴 = 0).</summary>
    public float CurrentMaxSpeed
    {
        get
        {
            float m = 1f;
            foreach (var e in effects)
            {
                if (e.type == StatusEffectType.Stun) return 0f;
                if (e.type == StatusEffectType.Boost) m *= e.magnitude;
                if (e.type == StatusEffectType.Slow)  m *= e.magnitude;
            }
            return smoothedSpeed * m * SkillMultiplier();
        }
    }

    /// <summary>자기 완결형 패시브 배율 (말/개/치킨).</summary>
    private float SkillMultiplier()
    {
        switch (Definition.skill)
        {
            case AnimalSkill.FinalSprint:
                return ProgressRatio >= SkillTuning.FinalSprintZone ? SkillTuning.FinalSprintMult : 1f;
            case AnimalSkill.Loyalty:
                return isLastPlace ? SkillTuning.LoyaltyMult : 1f;
            case AnimalSkill.Dash:
                if (raceTime < SkillTuning.DashTime) return SkillTuning.DashMult;
                if (raceTime < SkillTuning.DashTime + SkillTuning.DashFatigueTime) return SkillTuning.DashFatigueMult;
                return 1f;
            default: return 1f;
        }
    }

    public void Init(int id, AnimalDefinition def, int postNumber)
    {
        RacerId = id;
        Definition = def;
        DisplayName = $"{postNumber}번 {def.displayName}";
        animator = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody>();

        if (animator != null)
        {
            if (!animator.enabled) animator.enabled = true;
            animator.applyRootMotion = false;
        }

        RollSpeed();
        smoothedSpeed = rolledSpeed;   // 시작은 즉시 적용

        // 스킬 상태 초기화
        raceTime = 0f;
        isLastPlace = false;
        activeConsumed = false;
        alertPending = -1f;
        activeTriggerRatio = Random.Range(SkillTuning.ActiveMinRatio, SkillTuning.ActiveMaxRatio);
    }

    private void RollSpeed()
    {
        rolledSpeed = Random.Range(Definition.MinSpeedMs, Definition.MaxSpeedMs);
        rerollTimer = Definition.speedRerollInterval;
    }

    public void SetProgress(float p) => Progress = p;

    public void SimTick(float dt)
    {
        raceTime += dt;

        // [고양이] 액티브: 발동 지점 도달 시 ±30% 셀프 효과
        if (Definition.skill == AnimalSkill.Whim && !activeConsumed
            && ProgressRatio >= activeTriggerRatio && !HasFinished)
        {
            activeConsumed = true;
            bool up = Random.value < 0.5f;
            effects.Add(new StatusEffect(
                up ? StatusEffectType.Boost : StatusEffectType.Slow,
                SkillTuning.WhimDuration,
                up ? SkillTuning.WhimUp : SkillTuning.WhimDown));
            GameEvents.RaiseSkillProc($"{DisplayName}의 변덕! {(up ? "폭주한다!" : "드러누웠다...")}");
        }

        // [사슴] 경계 발동 대기 → 도주 가속
        if (alertPending >= 0f)
        {
            alertPending -= dt;
            if (alertPending < 0f)
            {
                effects.Add(new StatusEffect(StatusEffectType.Boost,
                    SkillTuning.AlertDuration, SkillTuning.AlertMult));
                GameEvents.RaiseSkillProc($"{DisplayName}이(가) 놀라서 내달린다!");
            }
        }

        // 상태이상 갱신
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            effects[i].remaining -= dt;
            if (effects[i].remaining <= 0f) effects.RemoveAt(i);
        }

        // 속도 리롤: 주기마다 새 목표, 실제 속도는 1.5초 정도에 걸쳐 수렴
        if (!HasFinished)
        {
            rerollTimer -= dt;
            if (rerollTimer <= 0f) RollSpeed();
            smoothedSpeed = Mathf.MoveTowards(smoothedSpeed, rolledSpeed,
                (Definition.MaxSpeedMs - Definition.MinSpeedMs) * dt / 1.5f);
        }

        DriveAnimator(dt);
    }

    private void DriveAnimator(float dt)
    {
        if (animator == null || rb == null) return;
        Vector3 v = rb.linearVelocity; v.y = 0f;
        float target = HasFinished ? 0f
            : Mathf.Clamp01(v.magnitude / Mathf.Max(0.1f, Definition.MaxSpeedMs));
        animVert = Mathf.MoveTowards(animVert, target, 4.5f * dt);
        animator.SetFloat(vertID, animVert);
        animator.SetFloat(stateID, 1f);
        animator.speed = Mathf.Lerp(1.0f, 1.8f, animVert);
    }

    public void AddEffect(StatusEffect effect)
    {
        // [펭귄] 무관심: 모든 외부 효과 면역 (이로운 것 포함)
        if (Definition != null && Definition.skill == AnimalSkill.Apathy) return;
        effects.Add(effect);
    }

    /// <summary>[클라] 호스트가 방송한 완주 순위 반영 (이벤트 없이 조용히).</summary>
    public void ApplyNetworkFinish(int rank)
    {
        HasFinished = true;
        FinishRank = rank;
    }

    public void MarkFinished(int rank)
    {
        HasFinished = true;
        FinishRank = rank;
        GameEvents.RaiseRacerFinished(RacerId, rank);
    }
}
