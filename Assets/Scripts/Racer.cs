using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 레이서: 정체성 + 상태이상 + 진행도 + 애니메이터 + 속도 리롤.
/// 속도는 리롤 주기마다 범위 내 랜덤으로 갱신되고, 급변 없이 부드럽게 수렴.
/// </summary>
public class Racer : MonoBehaviour
{
    public int RacerId { get; private set; }
    public int PostNumber { get; private set; }   // 등번호 (1부터) — 번호판·전광판과 같은 값

    /// <summary>표기 이름 ("4번 호랑이" / "#4 Tiger"). [로컬라이제이션] 캐시하지 않고
    /// 그때그때 조립 — 캐시하면 인게임 언어 전환이 이름에 안 먹는다. (종명은 5단계에서 키 전환 예정)</summary>
    public string DisplayName => Definition != null
        ? Loc.Format("racer.name", PostNumber, Definition.LocalizedName)
        : Loc.Format("racer.fallback", PostNumber);
    public AnimalDefinition Definition { get; private set; }

    /// <summary>누적 주행거리 (랩 포함, 음수 = 출발선 뒤 그리드). 경로 좌표가 필요하면 TrackPath.WrapProgress 경유.</summary>
    public float Progress { get; private set; }
    public bool HasFinished { get; private set; }
    /// <summary>처형 무전기로 탈락됨 (HasFinished도 함께 true — 순위는 최하위부터 배정).</summary>
    public bool IsEliminated { get; private set; }
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
    private float elimFreezeAt = -1f;   // 처형 후 애니 정지 예정 시각 (호스트/클라 각자 로컬)
    private bool animFrozen;
    private bool isLastPlace;           // RaceManager가 매 틱 세팅 (개)
    private bool isLeader;              // RaceManager가 매 틱 세팅 (비둘기 — 1등이면 무임승차 대기)
    private float activeTriggerRatio;   // 액티브 자동 발동 진행률 (호랑이/사슴/고양이/치킨)
    private bool activeConsumed;
    private bool flightRequested;       // 사슴: 모터에게 비행 개시 요청 (다음 FixedUpdate)
    private bool freeRideRequested;     // 비둘기: 모터에게 무임승차 비행 개시 요청 (다음 FixedUpdate)
    private float catWalkRemaining;     // 고양이: 코너 감속 무시 잔여 (초)
    private float clubRushRemaining;    // 인간: 몽둥이 질주 잔여 (초)
    private float camouflageRemaining;  // 얼룩말: 위장 잔여 (초)
    private float sweepGhostRemaining;  // 기린: 목 휘두르기 시전 중 유체화 잔여 (초) — 재운 시체 더미에 갇히지 않게
    private float colaDrinkRemaining;   // 북극곰: 콜라 들이키는 연출 잔여 (초) — 0이 되는 순간 부스트 개시

    /// <summary>[사슴] 루돌프 비행 중 — 이동은 모터의 스크립트 궤적, 모든 효과 면역.</summary>
    public bool IsFlying { get; private set; }
    /// <summary>[고양이] 사뿐한 발놀림 지속 중 — 모터가 코너 감속을 건너뛴다.</summary>
    public bool CornerIgnoreActive => catWalkRemaining > 0f;

    /// <summary>[인간] 몽둥이 질주 지속 중 — RaceManager가 접촉 스턴을 판정한다.</summary>
    public bool ClubRushActive => clubRushRemaining > 0f;

    /// <summary>[얼룩말 위장 / 기린 목 휘두르기] 유체화 중 — 몸싸움 스프링/회피/몽둥이 스턴에서 제외 (서로 통과).
    /// 물리 충돌은 원래 전면 오프(§3-6)라 실질 의미는 모터 몸싸움 로직 제외다. 조준(콜라이더)은 유지.
    /// 기린은 시전 창 동안만 — 주변 전원을 재우면 쓰러진 몸들이 벽이 돼 기린이 갇혀 멈추던 실사고(v22).</summary>
    public bool IsGhost => camouflageRemaining > 0f || sweepGhostRemaining > 0f;

    /// <summary>[기린] 목 휘두르기 시전 유체화 개시 — RaceManager가 발동 순간 호출.</summary>
    public void BeginSweepGhost(float seconds) => sweepGhostRemaining = seconds;

    public float ProgressRatio => Progress / Mathf.Max(1f, trackLength);
    public bool IsStunned
    {
        get { foreach (var e in effects) if (e.type == StatusEffectType.Stun) return true; return false; }
    }

    public void SetTrackLength(float len) => trackLength = Mathf.Max(1f, len);
    public void SetLastPlace(bool last) => isLastPlace = last;
    public void SetLeader(bool lead) => isLeader = lead;

    /// <summary>액티브 자동 발동 시점 도달 & 미사용이면 소비하고 true.
    /// 전역 시야가 필요한 스킬(호랑이 포효)용 — RaceManager가 매 틱 호출.</summary>
    public bool TryConsumeActive(AnimalSkill s)
    {
        if (Definition.skill != s || activeConsumed || HasFinished) return false;
        if (ProgressRatio < activeTriggerRatio) return false;
        activeConsumed = true;
        return true;
    }

    /// <summary>
    /// [발동 무전기] 액티브 스킬 강제 발동 — 1회 제한 무시(기획 확정: 무전기만이 "두 번째 발동"을
    /// 만들 수 있는 유일한 수단). 패시브는 조준 단계에서 차단되므로 여기는 최후 관문(무반응).
    /// </summary>
    public void ForceSkillByRadio()
    {
        if (HasFinished) return;
        if (!SkillTuning.IsActive(Definition.skill)) return;
        activeConsumed = false;
        activeTriggerRatio = -1f;   // 다음 시뮬 틱에 즉시 발동 (자동 발동 경로 그대로)
    }

    /// <summary>[처형 무전기] 탈락 — 즉시 경기 종료 취급, 순위는 최하위부터 (RaceManager가 배정).</summary>
    public void Eliminate(int rank)
    {
        if (HasFinished) return;
        HasFinished = true;
        IsEliminated = true;
        FinishRank = rank;
        effects.Clear();   // 죽은 몸에 이펙트 잔류 방지
        BeginElimFreeze();
        GameEvents.RaiseRacerFinished(RacerId, rank, eliminated: true);
    }

    /// <summary>쓰러진 뒤 잠시 재생하다 완전 정지 예약 — "죽었는데 아이들 재생"의 어색함 방지.</summary>
    private void BeginElimFreeze()
    {
        float delay = GameManager.Instance != null
            ? GameManager.Instance.Config.elimAnimFreezeSeconds : 5f;
        elimFreezeAt = Time.time + delay;
    }

    // 애니 정지는 호스트(SimTick)와 클라(AnimatorView 미러) 모두 각자 로컬로 걸어야 해서 Update 사용
    private void Update()
    {
        if (animFrozen || elimFreezeAt < 0f || animator == null) return;
        if (Time.time < elimFreezeAt) return;
        animFrozen = true;
        elimFreezeAt = -1f;
        animator.speed = 0f;   // 그 자세 그대로 레이스 끝까지 (새 라운드는 새 스폰이라 자동 초기화)
    }

    /// <summary>[사슴→모터] 비행 요청 인수 — 모터가 FixedUpdate에서 소비하고 비행 개시.</summary>
    public bool ConsumeFlightRequest()
    {
        if (!flightRequested) return false;
        flightRequested = false;
        IsFlying = true;
        return true;
    }

    /// <summary>[비둘기→모터] 무임승차 비행 요청 인수 — 모터가 FixedUpdate에서 소비하고 비행 개시.</summary>
    public bool ConsumeFreeRideRequest()
    {
        if (!freeRideRequested) return false;
        freeRideRequested = false;
        IsFlying = true;
        return true;
    }

    /// <summary>[모터] 착지/비행 중단 통지.</summary>
    public void EndFlight() => IsFlying = false;

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

    /// <summary>자기 완결형 패시브 배율 (말/개).</summary>
    private float SkillMultiplier()
    {
        switch (Definition.skill)
        {
            case AnimalSkill.FinalSprint:
                return ProgressRatio >= SkillTuning.FinalSprintZone ? SkillTuning.FinalSprintMult : 1f;
            case AnimalSkill.Loyalty:
                return isLastPlace ? SkillTuning.LoyaltyMult : 1f;
            default: return 1f;
        }
    }

    public void Init(int id, AnimalDefinition def, int postNumber)
    {
        RacerId = id;
        Definition = def;
        PostNumber = postNumber;
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
        IsEliminated = false;
        isLastPlace = false;
        isLeader = false;
        activeConsumed = false;
        flightRequested = false;
        freeRideRequested = false;
        IsFlying = false;
        catWalkRemaining = 0f;
        clubRushRemaining = 0f;
        camouflageRemaining = 0f;
        sweepGhostRemaining = 0f;
        colaDrinkRemaining = 0f;
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
        // 자기 완결형 액티브 자동 발동 (포효는 전역 시야가 필요해 RaceManager가 TryConsumeActive로 처리)
        if (!HasFinished && !activeConsumed && ProgressRatio >= activeTriggerRatio)
        {
            switch (Definition.skill)
            {
                case AnimalSkill.Rudolph:
                    activeConsumed = true;
                    flightRequested = true;   // 실제 개시(연출/피드)는 모터가 다음 FixedUpdate에
                    break;

                case AnimalSkill.CatWalk:
                    activeConsumed = true;
                    catWalkRemaining = SkillTuning.CatWalkDuration;
                    GameEvents.RaiseSkillEvent(SkillFeedEvent.CatWalk, RacerId);
                    break;

                case AnimalSkill.Dash:
                    activeConsumed = true;
                    // 셀프 효과라 AddEffect 관문(펭귄/비행 면역)을 거치지 않고 직접 추가
                    effects.Add(new StatusEffect(StatusEffectType.Boost,
                        SkillTuning.DashDuration, SkillTuning.DashMult));
                    GameEvents.RaiseSkillEvent(SkillFeedEvent.Dash, RacerId);
                    break;

                case AnimalSkill.ClubRush:
                    activeConsumed = true;
                    clubRushRemaining = SkillTuning.ClubRushDuration;
                    effects.Add(new StatusEffect(StatusEffectType.Boost,
                        SkillTuning.ClubRushDuration, SkillTuning.ClubRushMult));
                    GameEvents.RaiseSkillEvent(SkillFeedEvent.ClubRush, RacerId);
                    break;

                case AnimalSkill.Camouflage:
                    activeConsumed = true;
                    camouflageRemaining = SkillTuning.CamouflageDuration;
                    effects.Add(new StatusEffect(StatusEffectType.Boost,
                        SkillTuning.CamouflageDuration, SkillTuning.CamouflageMult));
                    GameEvents.RaiseSkillEvent(SkillFeedEvent.Camouflage, RacerId);
                    break;

                case AnimalSkill.Cola:
                    activeConsumed = true;
                    // 부스트는 들이키는 연출(ColaDrinkSeconds)이 끝나는 순간 — 연출-판정 타이밍 일치
                    colaDrinkRemaining = SkillTuning.ColaDrinkSeconds;
                    GameEvents.RaiseSkillEvent(SkillFeedEvent.Cola, RacerId);
                    break;

                case AnimalSkill.FreeRide:
                    // 자기가 1등이면 발동하지 않고 대기 — 1등이 아니게 되는 순간 발동 (유저 확정).
                    // activeConsumed를 안 건드려서 매 틱 재시도 (무전기 재발동의 5초 지연 중 1등이 된 경우도 커버)
                    if (isLeader) break;
                    activeConsumed = true;
                    freeRideRequested = true;   // 실제 개시(연출/피드)는 모터가 다음 FixedUpdate에 (루돌프와 동일)
                    break;
            }
        }

        // [고양이] 사뿐한 발놀림 잔여 시간
        if (catWalkRemaining > 0f) catWalkRemaining -= dt;

        // [인간] 몽둥이 질주 잔여 시간 — 접촉 스턴 판정은 RaceManager(전역 시야)가 이 플래그로 처리
        if (clubRushRemaining > 0f) clubRushRemaining -= dt;

        // [얼룩말] 위장 잔여 시간 — 유체화(IsGhost)는 모터/몽둥이 판정이 이 플래그로 처리
        if (camouflageRemaining > 0f) camouflageRemaining -= dt;

        // [기린] 시전 유체화 잔여 시간 — 재운 동물들이 일어날 때까지 시체 벽을 통과해 계속 달린다
        if (sweepGhostRemaining > 0f) sweepGhostRemaining -= dt;

        // [북극곰] 콜라 들이키기 — 연출이 끝나는 순간 부스트 개시 (셀프라 AddEffect 관문 안 거침, 치킨과 동일)
        if (colaDrinkRemaining > 0f)
        {
            colaDrinkRemaining -= dt;
            if (colaDrinkRemaining <= 0f && !HasFinished)
                effects.Add(new StatusEffect(StatusEffectType.Boost,
                    SkillTuning.ColaDuration, SkillTuning.ColaMult));
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
        if (animFrozen) return;   // 처형 정지 후엔 속도/파라미터 안 건드림
        if (animator == null || rb == null) return;
        Vector3 v = rb.linearVelocity; v.y = 0f;
        float target = HasFinished ? 0f
            : IsFlying ? 1f   // 비행 중엔 전력 질주 자세 (kinematic이라 rb 속도가 0으로 읽힘)
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
        // [사슴] 루돌프 비행 중: 하늘 위라 아무것도 닿지 않는다 (포효 포함)
        if (IsFlying) return;
        effects.Add(effect);
    }

    /// <summary>[클라] 호스트가 방송한 완주 순위 반영 (이벤트 없이 조용히).</summary>
    public void ApplyNetworkFinish(int rank)
    {
        HasFinished = true;
        FinishRank = rank;
    }

    /// <summary>[클라] 호스트가 중계한 탈락 반영 (자세는 TransformView 받아쓰기, 애니 정지는 로컬 예약).</summary>
    public void ApplyNetworkEliminated()
    {
        if (IsEliminated) return;
        HasFinished = true;
        IsEliminated = true;
        BeginElimFreeze();
    }

    public void MarkFinished(int rank)
    {
        HasFinished = true;
        FinishRank = rank;
        GameEvents.RaiseRacerFinished(RacerId, rank);
    }
}
