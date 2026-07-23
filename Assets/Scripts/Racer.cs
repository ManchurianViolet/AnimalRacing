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

    /// <summary>현재 유효 최고속도 = 리롤된 속도 × 아이템 배율.</summary>
    public float CurrentMaxSpeed
    {
        get
        {
            float m = 1f;
            foreach (var e in effects)
            {
                if (e.type == StatusEffectType.Boost) m *= e.magnitude;
                if (e.type == StatusEffectType.Slow)  m *= e.magnitude;
            }
            return smoothedSpeed * m;
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
    }

    private void RollSpeed()
    {
        rolledSpeed = Random.Range(Definition.MinSpeedMs, Definition.MaxSpeedMs);
        rerollTimer = Definition.speedRerollInterval;
    }

    public void SetProgress(float p) => Progress = p;

    public void SimTick(float dt)
    {
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

    public void AddEffect(StatusEffect effect) => effects.Add(effect);

    public void MarkFinished(int rank)
    {
        HasFinished = true;
        FinishRank = rank;
        GameEvents.RaiseRacerFinished(RacerId, rank);
    }
}
