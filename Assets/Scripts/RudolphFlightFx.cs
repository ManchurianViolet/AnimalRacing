using UnityEngine;

/// <summary>
/// [사슴] 루돌프 비행 중 달리기 애니메이션 대폭 배속 — 공중에서 다리를 미친 듯이 젓는 코믹 연출.
/// (본 직접 회전(풍차)은 기괴해서 기각 — 애니 배속으로 교체, 기획 결정)
/// 발동 판정은 "지면 위 높이" 기반: 높이는 TransformView가 미러하므로 클라에서도 통신 0으로
/// 동작 (BoostDustFx와 같은 로컬 연출 철학). LateUpdate라 호스트 DriveAnimator(1.0~1.8 배속)와
/// 탈락 애니 정지(speed=0)보다 나중에 적용 — 단 완주/탈락 후엔 손대지 않는다.
/// RaceManager가 사슴에만 부착.
/// </summary>
public class RudolphFlightFx : MonoBehaviour
{
    private Racer racer;
    private GameConfig config;
    private Animator animator;
    private TrailRenderer trail;   // 꼬리 리본 (비행 중에만 배출)
    private float weight;          // 0=지상 ~ 1=순항 고도
    private bool overriding;
    private float savedSpeed = 1f; // 덮어쓰기 직전 배속 (클라=1, 호스트=DriveAnimator 값)

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        this.config = config;
        animator = GetComponentInChildren<Animator>();
        BuildTrail();
    }

    private void LateUpdate()
    {
        if (racer == null || animator == null || config == null) return;
        if (racer.HasFinished)
        {
            EndOverride();   // 탈락 애니 정지와 싸우지 않기
            if (trail != null) trail.emitting = false;
            return;
        }

        float h = HeightAboveGround();
        float target = Mathf.Clamp01((h - config.rudolphLiftStart)
            / Mathf.Max(0.1f, config.rudolphLiftFull - config.rudolphLiftStart));
        weight = Mathf.MoveTowards(weight, target, 3f * Time.deltaTime);

        if (trail != null) trail.emitting = weight > 0.05f;

        if (weight > 0.02f)
        {
            if (!overriding) { overriding = true; savedSpeed = animator.speed; }
            animator.speed = Mathf.Lerp(Mathf.Max(savedSpeed, 1f), config.rudolphFlightAnimSpeed, weight);
        }
        else EndOverride();
    }

    // ---- 꼬리 트레일: 꼬리 본에 리본 렌더러 부착 (머티리얼/색은 코드 생성, 전 사슴 공용) ----
    private static Material trailMat;

    private void BuildTrail()
    {
        // 사슴 리그의 꼬리 끝 본 이름은 "spine" (spine.003→spine.002→spine.001→spine 체인의 말단)
        Transform tail = null;
        foreach (var tr in GetComponentsInChildren<Transform>(true))
            if (tr.name == "spine") { tail = tr; break; }
        if (tail == null)
        {
            // 폴백: 루트 뒤쪽 고정 앵커 (본을 못 찾아도 연출은 나가야 한다)
            var anchor = new GameObject("RudolphTrailAnchor");
            anchor.transform.SetParent(transform, false);
            anchor.transform.localPosition = new Vector3(0f, 0.7f, -0.7f);
            tail = anchor.transform;
        }

        if (trailMat == null)
        {
            Shader sh = config.boostDustMaterial != null ? config.boostDustMaterial.shader
                : Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) return;
            trailMat = new Material(sh) { name = "RudolphTrail (runtime)" };
        }

        var go = new GameObject("RudolphTrail");
        go.transform.SetParent(tail, false);
        trail = go.AddComponent<TrailRenderer>();
        trail.material = trailMat;
        trail.time = config.rudolphTrailTime;
        trail.minVertexDistance = 0.08f;
        trail.startWidth = config.rudolphTrailWidth;
        trail.endWidth = 0f;
        trail.numCapVertices = 4;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;

        // 색: 꼬리 쪽(머리 방향)은 루돌프 빨강 → 끝으로 갈수록 금색으로 반짝이다 사라짐
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(config.rudolphTrailColorA, 0f),
                new GradientColorKey(config.rudolphTrailColorB, 0.55f),
                new GradientColorKey(config.rudolphTrailColorB, 1f),
            },
            new[]
            {
                new GradientAlphaKey(0.9f, 0f),
                new GradientAlphaKey(0.55f, 0.6f),
                new GradientAlphaKey(0f, 1f),
            });
        trail.colorGradient = g;
    }

    private void EndOverride()
    {
        if (!overriding) return;
        overriding = false;
        animator.speed = savedSpeed;   // 호스트는 다음 틱에 DriveAnimator가 어차피 재설정
    }

    /// <summary>발밑 지면까지 높이 (자기/다른 동물 콜라이더 제외).</summary>
    private float HeightAboveGround()
    {
        Vector3 origin = racer.transform.position + Vector3.up * 0.5f;
        var hits = Physics.RaycastAll(origin, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<Racer>() != null) continue;
            if (hit.normal.y < 0.4f) continue;
            if (hit.distance < best) best = hit.distance;
        }
        return best == float.MaxValue ? 0f : Mathf.Max(0f, best - 0.5f);
    }
}
