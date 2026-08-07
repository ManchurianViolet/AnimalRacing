using UnityEngine;

/// <summary>
/// 주행 — "진짜 레이스" 모델:
///  · 레인은 출발 그리드일 뿐, 주행 중엔 자유 횡위치 (lateral)
///  · 코너 전방 곡률을 읽어 인코스로 수렴 (레이싱 라인)
///  · 전방의 느린 주자는 빈 쪽으로 비켜 추월, 양쪽이 막히면 감속 추종 (자리다툼)
///  · 코너 감속: 전방 곡률만큼 속도 상한을 깎음 — 제동은 전원 동일(강한 고정 게인),
///    탈출에서 상한을 되찾는 속도만 가속 스탯(AccelGain) 소관
/// 속도/스킬/진행도는 Racer.SimTick 소관 — 여기는 "조향 + 코너 속도 상한"만.
/// 호스트 전용 시뮬 (클라 동물은 TransformView 받아쓰기).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class RacerMotor : MonoBehaviour
{
    public bool SimEnabled { get; set; }

    private Rigidbody rb;
    private Racer racer;
    private TrackPath path;
    private GameConfig cfg;
    private RaceManager raceManager;

    private float lateral;        // 현재 횡위치 (중심선 기준, + = 오른쪽)
    private float lateralVel;     // SmoothDamp 상태
    private float personalMargin; // 개인 라인 취향 (전원이 같은 인코스 픽셀을 노리는 것 방지)

    private float stuckTimer;     // 교착 감시견

    // ---- 완주 연출 상태 ----
    private bool restPicked;      // 휴식 지점을 이미 뽑았나
    private Vector3 restPoint;    // 결승선 너머의 개인 휴식 지점

    // ---- 탈락(처형) 연출 상태 ----
    private bool elimStarted;
    private Quaternion elimBaseRot;   // 쓰러지기 시작한 순간의 자세
    private float elimRoll;           // 옆으로 넘어간 각도 (0→90)
    private Vector3 groundUp = Vector3.up;   // 평활화된 지면 법선 (경사 정렬용)

    // ---- [사슴] 루돌프 비행 상태 (스크립트 궤적 — 물리 몰수, 클라는 TransformView 미러) ----
    private bool flightActive;
    private Vector3 flightStartPos, flightEndPos;
    private float flightStartProg, flightEndProg;   // 진행도는 비행 중 투영 대신 직접 기입 (현이 코너를 깊게 질러 투영이 튐)
    private float flightT;            // 0→1 정규화 진행
    private float flightDur;          // 비행 시간
    private float flightSpeed;        // 발동 순간의 주행 속도 (착지 후 이 속도로 복귀)

    // ---- 디버그 계기판 상태 (기즈모용 스냅샷) ----
    private Vector3 dbgTarget;
    private float dbgCurv, dbgCornerT, dbgDesiredLat;
    private float dbgSpeed, dbgCap;
    private bool dbgBlocked;

    public float Lateral => lateral;

    public void Init(Racer racer, TrackPath path, GameConfig cfg, RaceManager raceManager)
    {
        this.racer = racer;
        this.path = path;
        this.cfg = cfg;
        this.raceManager = raceManager;

        rb = GetComponent<Rigidbody>();
        // 회전은 전부 물리에서 몰수 (Y축 열어두면 접지 마찰 반작용 토크로 팽이 됨)
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.angularVelocity = Vector3.zero;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // 출발 그리드: 스폰 위치의 횡좌표가 초기 lateral (레인의 마지막 임무)
        lateral = Mathf.Clamp(path.GetLateralOffset(transform.position),
                              -MaxLat(), MaxLat());
        lateralVel = 0f;
        personalMargin = Random.Range(0f, 1.0f);   // 인코스 목표를 저마다 조금씩 다르게
    }

    private float MaxLat() =>
        path.GetHalfWidth(racer != null ? racer.Progress : 0f) - cfg.roadMargin;

    private void FixedUpdate()
    {
        if (!SimEnabled || racer == null) return;
        float dt = Time.fixedDeltaTime;

        if (racer.HasFinished)
        {
            if (flightActive) AbortFlight();   // 비행 중 처형/완주 확정 — 공중 상태 정리 후 연출로
            if (racer.IsEliminated) EliminatedCollapse(dt);
            else FinishCoast(dt);
            return;
        }

        // ---- [사슴] 루돌프: 비행 중이면 스크립트 궤적이 전부, 요청이 오면 개시 ----
        if (flightActive) { FlightTick(dt); return; }
        if (racer.ConsumeFlightRequest()) { BeginFlight(); return; }

        float myProg = racer.Progress;
        float baseCap = racer.CurrentMaxSpeed;
        Vector3 flatVel = rb.linearVelocity; flatVel.y = 0f;

        // ---- 1) 레이싱 라인: 전방 곡률 → 인코스 목표 ----
        float curv = path.GetSignedCurvatureAhead(myProg, cfg.racingLineLookAhead);
        float cornerT = Mathf.Clamp01(Mathf.Abs(curv) / Mathf.Max(0.1f, cfg.curvatureSaturation));

        // ---- 1.5) 코너 감속: 상한 = 최고속 × (1 − 코너강도 × 감속률) ----
        // 감지 창(6m)은 레이싱 라인(9m)보다 짧게 — 코너를 거의 다 돌았을 때
        // 상한이 먼저 회복되기 시작해 "탈출 가속"이 코너 끝에서 터진다.
        // [고양이] 사뿐한 발놀림: 지속 중엔 코너 감속 자체를 무시 (풀스피드 코너링)
        float cornerFactor = 1f;
        if (cfg.cornerDecelEnabled && !racer.CornerIgnoreActive)
        {
            float senseCurv = path.GetSignedCurvatureAhead(myProg, cfg.cornerSenseAhead);
            float senseT = Mathf.Clamp01(Mathf.Abs(senseCurv)
                                         / Mathf.Max(0.1f, cfg.curvatureSaturation));
            cornerFactor = 1f - senseT * cfg.cornerDecelRate;
        }
        float speedCap = baseCap * cornerFactor;

        // 안쪽 한계 = 빌드 때 구운 접힘 클리핑 표 (전방 구간의 최솟값 — 미리 좁힘)
        float sign = Mathf.Sign(curv);
        float insideRoom = MaxLat();
        for (float probe = 0f; probe <= cfg.lookAhead + 2f; probe += 2f)
            insideRoom = Mathf.Min(insideRoom,
                path.GetLateralLimit(myProg + probe, sign) - cfg.roadMargin);
        insideRoom = Mathf.Max(0.5f, insideRoom);

        float insideLat = Mathf.Sign(curv) * Mathf.Max(0.5f, insideRoom - personalMargin);
        // 직선(cornerT≈0)에선 현재 위치 유지 성향, 코너일수록 인코스로
        float desiredLat = Mathf.Lerp(lateral, insideLat, cornerT * cfg.insideBiasStrength);

        // ---- 2) 회피/자리다툼: 전방 느린 주자 검사 ----
        bool blockedCenter = false, leftOccupied = false, rightOccupied = false;
        float blockerSpeed = float.MaxValue;
        float sideRepel = 0f;   // 나란한 이웃과의 횡간격 유지력

        foreach (var other in raceManager.Racers)
        {
            if (other == null || other == racer || other.HasFinished) continue;
            if (other.IsFlying) continue;   // 하늘 위의 사슴은 장애물이 아니다

            float dp = other.Progress - myProg;
            // 이웃 횡좌표: 전체 투영은 교차/근접 구간에서 오염되므로 이웃 모터의 상태값 사용
            var otherMotor = other.GetComponent<RacerMotor>();
            float otherLat = otherMotor != null ? otherMotor.Lateral
                             : path.GetLateralOffset(other.transform.position);
            float dLat = otherLat - lateral;

            // 나란히 달리는 이웃: 겹친 만큼 반대쪽으로 밀려남 (간격 스프링)
            if (Mathf.Abs(dp) <= cfg.sideBySideRange)
            {
                float overlap = cfg.bodyClearance - Mathf.Abs(dLat);
                if (overlap > 0f)
                {
                    float dir = dLat > 0f ? -1f
                              : dLat < 0f ? 1f
                              : (racer.RacerId % 2 == 0 ? 1f : -1f);   // 완전 포개짐 — 번호로 갈라섬
                    sideRepel += dir * overlap;
                }
            }

            if (dp < 0.1f || dp > cfg.avoidLookAhead) continue;          // 이하: 내 전방 근접만
            if (other.CurrentMaxSpeed > baseCap * 1.02f) continue;      // 더 빠른 놈은 곧 사라짐

            if (Mathf.Abs(dLat) < cfg.bodyClearance)
            {
                blockedCenter = true;
                blockerSpeed = Mathf.Min(blockerSpeed, other.CurrentMaxSpeed);
            }
            else if (dLat > 0f && dLat < cfg.bodyClearance + cfg.overtakeShift)
                rightOccupied = true;
            else if (dLat < 0f && dLat > -(cfg.bodyClearance + cfg.overtakeShift))
                leftOccupied = true;
        }

        if (blockedCenter)
        {
            bool canRight = !rightOccupied && lateral + cfg.overtakeShift <= MaxLat();
            bool canLeft  = !leftOccupied  && lateral - cfg.overtakeShift >= -MaxLat();

            if (canRight && canLeft)
            {
                // 양쪽 다 열림 → 인코스 쪽으로 추월 (레이싱 라인과 한 방향)
                bool insideIsRight = insideLat >= lateral;
                desiredLat = lateral + (insideIsRight ? cfg.overtakeShift : -cfg.overtakeShift);
            }
            else if (canRight) desiredLat = lateral + cfg.overtakeShift;
            else if (canLeft)  desiredLat = lateral - cfg.overtakeShift;
            else
            {
                // 갇힘: 앞 주자 꽁무니 추종 (레이스의 정체 구간)
                speedCap = Mathf.Min(speedCap, blockerSpeed * cfg.blockedSpeedFactor);
            }
        }

        // ---- 교착 감시견: 막힌 채 사실상 정지가 지속되면 비상 차선 변경 ----
        if (blockedCenter && flatVel.magnitude < 0.6f && !racer.IsStunned)
            stuckTimer += dt;
        else
            stuckTimer = Mathf.Max(0f, stuckTimer - dt * 2f);

        if (stuckTimer > 0.8f)
        {
            float escape = (!rightOccupied && leftOccupied) ? 1f
                         : (rightOccupied && !leftOccupied) ? -1f
                         : (racer.RacerId % 2 == 0 ? 1f : -1f);   // 양쪽 동일 — 번호로 갈라섬
            desiredLat = lateral + escape * cfg.overtakeShift * 1.5f;
        }

        // ---- 3) 횡 이동 (감쇠 스프링) + 도로 클램프 ----
        desiredLat += sideRepel;   // 나란한 이웃과 간격 벌리기 (인코스 수렴과 균형)
        desiredLat = Mathf.Clamp(desiredLat, -MaxLat(), MaxLat());

        // 코너 안쪽 방향으로는 반경 한계 이상 못 파고듦 (접힘 방지 캡)
        if (Mathf.Sign(desiredLat) == Mathf.Sign(curv) && Mathf.Abs(desiredLat) > insideRoom)
            desiredLat = Mathf.Sign(curv) * insideRoom;
        lateral = Mathf.SmoothDamp(lateral, desiredLat, ref lateralVel,
                                   cfg.lateralSmoothTime, cfg.lateralMaxSpeed, dt);

        // ---- 4) 조향/가속 (기존 어시스트 거버너 유지) ----
        // 목표점의 횡좌표는 "전방 지점의 한계"로 한 번 더 클램프 — 몸이 순간적으로
        // 깊은 자리에 있어도 목표는 항상 유효 구역 안 → 스스로 빠져나오는 구조
        float aheadProg = myProg + cfg.lookAhead;
        float aheadLimit = path.GetLateralLimit(aheadProg, Mathf.Sign(lateral)) - cfg.roadMargin * 0.5f;
        float targetLat = Mathf.Sign(lateral) * Mathf.Min(Mathf.Abs(lateral), Mathf.Max(0.3f, aheadLimit));
        Vector3 target = path.GetTargetOnSection(aheadProg, targetLat);   // 두 레일 사이 보간

        // 계기판 스냅샷
        dbgTarget = target; dbgCurv = curv; dbgCornerT = cornerT;
        dbgDesiredLat = desiredLat; dbgBlocked = blockedCenter;
        dbgSpeed = flatVel.magnitude; dbgCap = speedCap;
        Vector3 to = target - rb.position; to.y = 0f;

        // 제동/가속 게인 분리: 상한 초과(코너 진입·스턴·Slow 피격)는 전원 동일한
        // 강한 제동. 대칭 거버너면 "굼뜬 가속 = 굼뜬 제동"이라 코너 진입에서 번
        // 거리가 탈출 손해와 정확히 상쇄되어 가속 스탯이 도로 무의미해진다.
        float gain = racer.Definition.AccelGain;
        if (flatVel.magnitude > speedCap + 0.15f)
            gain = Mathf.Max(cfg.cornerBrakeGain, gain);

        Vector3 desiredVel = to.normalized * speedCap;
        Vector3 assist = (desiredVel - flatVel) * gain;
        assist = Vector3.ClampMagnitude(assist, cfg.maxAssistAccel);
        rb.AddForce(assist, ForceMode.Acceleration);

        // 회전: FreezeRotation이 MoveRotation까지 막으므로 (Unity 6) transform 직접 회전.
        // 물리에서 회전을 완전 몰수한 축이라 충돌 없음.
        RotateToward(to, racer.Definition.AccelGain * 1.6f, dt);
    }

    // ================= [사슴] 루돌프 비행 =================
    // "현재 속도 × 5초" 앞의 트랙 지점까지 직선(현)으로 포물선 비행.
    // 이득 = 트랙 곡선거리 − 직선거리 → 코너 구간에서 발동할수록 크다.
    // 비행 중 물리 몰수(kinematic) — 진행도는 RaceManager의 연속성 투영이 자연 추적.

    private void BeginFlight()
    {
        Vector3 flatVel = rb.linearVelocity; flatVel.y = 0f;
        float runSpeed = Mathf.Max(3f, flatVel.magnitude, racer.CurrentMaxSpeed);
        flightSpeed = runSpeed;   // 착지 후 복귀 주행 속도

        // 목표 = 12초 앞 지점을 5초 만에 — 비행 시간이 리드 시간과 같으면 이득 0이라 무의미 (기획)
        float fullLead = runSpeed * SkillTuning.RudolphLeadSeconds;
        float targetProg = Mathf.Min(racer.Progress + fullLead, raceManager.RaceLength - 1f);   // 공중 완주 방지
        if (targetProg <= racer.Progress + 1f)
        {
            racer.EndFlight();   // 결승선 코앞 — 날 거리가 없다 (조용히 불발)
            return;
        }

        flightActive = true;
        flightT = 0f;
        flightStartPos = rb.position;
        flightEndPos = path.GetTargetOnSection(path.WrapProgress(targetProg), 0f);
        flightStartProg = racer.Progress;
        flightEndProg = targetProg;
        // 결승선 클램프로 리드가 짧아졌으면 비행 시간도 비례 축소 (슬로모 활공 방지)
        flightDur = Mathf.Max(0.5f,
            SkillTuning.RudolphFlightSeconds * (targetProg - racer.Progress) / fullLead);
        rb.isKinematic = true;

        GameEvents.RaiseSkillProc($"{racer.DisplayName}이(가) 하늘로 날아올랐다! 루돌프다!");
    }

    private void FlightTick(float dt)
    {
        flightT += dt / flightDur;
        float t = Mathf.Clamp01(flightT);

        // 등변사다리꼴 고도: 사면 상승 → 수평 순항 → 사면 하강 (용수철식 포물선 기각 — 기획)
        float climb = SkillTuning.RudolphClimbRatio;
        float hNorm = t < climb ? t / climb
                    : t > 1f - climb ? (1f - t) / climb
                    : 1f;
        Vector3 pos = Vector3.Lerp(flightStartPos, flightEndPos, t);
        pos.y += SkillTuning.RudolphPeakHeight * hNorm;
        // ⚠ MovePosition 금지: 아래 transform.rotation 쓰기가 트랜스폼→물리 재동기화를 일으켜
        // 예약된 MovePosition 목표를 옛 위치로 덮어쓴다 (상승 구간 통째 실종 실사고).
        // kinematic 비행은 위치/회전 모두 transform 단일 작성자로.
        transform.position = pos;
        // 진행도 직접 기입 — RaceManager는 비행 중 투영을 건너뛴다 ([투영점프] 방지)
        racer.SetProgress(Mathf.Lerp(flightStartProg, flightEndProg, t));

        Vector3 dir = flightEndPos - flightStartPos; dir.y = 0f;
        if (dir.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir.normalized, Vector3.up), 6f * dt);

        if (flightT >= 1f) LandFlight();
    }

    private void LandFlight()
    {
        flightActive = false;
        racer.EndFlight();
        rb.isKinematic = false;

        Vector3 dir = flightEndPos - flightStartPos; dir.y = 0f;
        if (dir.sqrMagnitude > 1e-4f)
            rb.linearVelocity = dir.normalized * flightSpeed;   // 활공 관성 그대로 착지 주행

        // 착지점 기준으로 조향 상태 재정렬 (비행 전 lateral은 무효)
        lateral = Mathf.Clamp(path.GetLateralOffset(rb.position), -MaxLat(), MaxLat());
        lateralVel = 0f;
    }

    /// <summary>비행 중 처형/완주 등 외부 확정 — 공중 상태만 정리 (물리 복원).</summary>
    private void AbortFlight()
    {
        flightActive = false;
        racer.EndFlight();
        rb.isKinematic = false;
    }

    // ---- 탈락 연출: 급제동 + 옆으로 픽 쓰러짐 (회전은 TransformView로 클라에도 미러) ----
    private void EliminatedCollapse(float dt)
    {
        if (!elimStarted)
        {
            elimStarted = true;
            elimBaseRot = transform.rotation;
            elimRoll = 0f;
        }

        Vector3 v = rb.linearVelocity;
        v.x = Mathf.Lerp(v.x, 0f, 8f * dt);
        v.z = Mathf.Lerp(v.z, 0f, 8f * dt);   // y는 중력에 맡김 (공중에서 안 멈추게)
        rb.linearVelocity = v;

        // 진행 방향 축으로 90도 굴러 넘어짐 — 번호 홀짝으로 좌/우 갈라 단조로움 회피
        float dir = racer.RacerId % 2 == 0 ? 1f : -1f;
        elimRoll = Mathf.MoveTowards(elimRoll, 90f, 200f * dt);
        transform.rotation = elimBaseRot * Quaternion.Euler(0f, 0f, elimRoll * dir);
    }

    // ---- 완주 연출: 결승선을 지나 저마다 랜덤 거리를 더 달리다 좌우로 흩어져 멈춤 ----
    private void FinishCoast(float dt)
    {
        if (!restPicked)
        {
            restPicked = true;
            float extra = Random.Range(cfg.finishCoastMin, cfg.finishCoastMax);
            float lat = Random.Range(-cfg.finishSpread, cfg.finishSpread);
            restPoint = path.GetTargetOnSection((racer.Progress + extra) % path.TotalLength, lat);
        }

        Vector3 toRest = restPoint - rb.position; toRest.y = 0f;
        float dist = toRest.magnitude;
        if (dist < 0.5f)
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, 6f * dt);
            return;
        }

        // 남은 거리에 비례해 잦아드는 속도 — 전력질주가 서서히 걸음으로
        float coast = Mathf.Min(Mathf.Max(rb.linearVelocity.magnitude, 2f), Mathf.Max(1.2f, dist * 1.2f));
        Vector3 dv = toRest.normalized * coast - rb.linearVelocity; dv.y = 0f;
        rb.AddForce(Vector3.ClampMagnitude(dv * 3f, cfg.maxAssistAccel), ForceMode.Acceleration);
        RotateToward(toRest, 4f, dt);
    }

    // ---- 회전 공통: 발밑 지면 법선에 몸을 정렬 — 다리/경사로에서 수평 부양 방지 ----
    private void RotateToward(Vector3 dir, float gain, float dt)
    {
        groundUp = Vector3.Slerp(groundUp, SampleGroundUp(), 10f * dt);   // 이음새에서 법선 튐 완화
        Vector3 fwd = Vector3.ProjectOnPlane(dir, groundUp);
        if (fwd.sqrMagnitude < 1e-4f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(fwd.normalized, groundUp), gain * dt);
    }

    private Vector3 SampleGroundUp()
    {
        var hits = Physics.RaycastAll(rb.position + Vector3.up * 1.5f, Vector3.down, 4f,
                                      ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 up = Vector3.up;
        foreach (var h in hits)
        {
            // 동물(자신 포함)은 지면이 아님. 벽면(법선이 눕는 면)도 제외
            if (h.collider.GetComponentInParent<Racer>() != null) continue;
            if (h.normal.y < 0.4f) continue;
            if (h.distance < best) { best = h.distance; up = h.normal; }
        }
        return up;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!SimEnabled || racer == null || cfg == null || !cfg.debugMotorGizmos) return;

        // 조향 목표선 + 목표점
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.3f, dbgTarget + Vector3.up * 0.3f);
        Gizmos.DrawWireSphere(dbgTarget + Vector3.up * 0.3f, 0.25f);

        // 상태 라벨
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2.2f,
            $"#{racer.RacerId} prog {racer.Progress:F1}\n" +
            $"lat {lateral:F1}→{dbgDesiredLat:F1}\n" +
            $"v {dbgSpeed:F1}/{dbgCap:F1}  curv {dbgCurv:F1} T{dbgCornerT:F2}{(dbgBlocked ? " 막힘" : "")}");
    }
#endif
}
