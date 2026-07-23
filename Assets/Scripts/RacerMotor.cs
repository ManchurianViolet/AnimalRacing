using UnityEngine;

/// <summary>
/// 주행 (고정 레인 추종 + 어시스트 거버너).
/// 스탯 번역: acceleration = 수렴 게인(감속 아이템 회복 속도), maxSpeed = 속도 상한.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class RacerMotor : MonoBehaviour
{
    public bool SimEnabled { get; set; }

    private Rigidbody rb;
    private Racer racer;
    private TrackPath path;
    private GameConfig cfg;
    private float laneOffset;    // 스폰 위치 기반 고정 레인

    public void Init(Racer racer, TrackPath path, GameConfig cfg)
    {
        this.racer = racer;
        this.path = path;
        this.cfg = cfg;

        rb = GetComponent<Rigidbody>();
        // 회전은 전부 물리에서 몰수 (Y축 열어두면 접지 마찰 반작용 토크로 팽이 됨)
        // FreezeRotation이어도 MoveRotation에 의한 코드 회전은 정상 작동
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.angularVelocity = Vector3.zero;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        laneOffset = Mathf.Clamp(path.GetLateralOffset(transform.position),
                                 -path.HalfWidth + 0.5f, path.HalfWidth - 0.5f);
    }

    private void FixedUpdate()
    {
        if (!SimEnabled || racer == null) return;
        float dt = Time.fixedDeltaTime;

        if (racer.HasFinished)
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, 3f * dt);
            return;
        }

        // 목표점: 내 레인 위 lookAhead 앞
        float aheadProg = racer.Progress + cfg.lookAhead;
        Vector3 target = path.GetPoint(aheadProg) + path.GetNormal(aheadProg) * laneOffset;
        Vector3 to = target - rb.position; to.y = 0f;

        Vector3 flatVel = rb.linearVelocity; flatVel.y = 0f;

        // 어시스트 (속도 거버너): 스탯 속도로 수렴, 상한으로 관성 유지
        Vector3 desiredVel = to.normalized * racer.CurrentMaxSpeed;
        Vector3 assist = (desiredVel - flatVel) * racer.Definition.AccelGain;
        assist = Vector3.ClampMagnitude(assist, cfg.maxAssistAccel);
        rb.AddForce(assist, ForceMode.Acceleration);

        // 진행 방향 바라보기
        if (to.sqrMagnitude > 0.01f)
            rb.MoveRotation(Quaternion.Slerp(rb.rotation,
                Quaternion.LookRotation(to.normalized),
                racer.Definition.AccelGain * 1.6f * dt));   // 회전 반응도 가속 게인에 연동
    }
}
