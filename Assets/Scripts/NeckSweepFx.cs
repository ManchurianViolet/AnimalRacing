using UnityEngine;

/// <summary>
/// [기린] 목 휘두르기 연출 — 머리 본을 하늘로 쭉 뻗었다가(예열) 바닥 높이로 내리찍어
/// 몸 주변을 360도 한 바퀴 휩쓴다. 낮은 폴리 카툰이라 목뼈가 늘어나는 그림 자체가 개그.
/// v22: 웅크림(골반 내림) 폐기 — 몸은 계속 달리는 애니 그대로 두고 목만 휘두른다 (유저 결정:
/// 골반을 내리면 다리가 접혀 "쭈그려 앉아 미끄러지는" 그림이 됐었음). 훑기 원의 중심이
/// 매 프레임 기린의 현재 위치라 달리는 중에도 원이 몸을 따라온다.
/// 발동 감지 = 전 클라로 중계되는 스킬 사건(OnSkillEvent — NeckSweep + 내 RacerId). 통신 0.
/// 타이밍은 SkillTuning(예열/회전)이 단일 출처 — 호스트 기절 판정과 연출이 같은 시계를 쓴다.
/// ⚠ 본 위치는 += 금지 (§11) — 기준 localPosition에서 매 프레임 새로 계산 (RoarFx 패턴).
/// RaceManager가 기린(NeckSweep 스킬)에만 부착.
/// </summary>
public class NeckSweepFx : MonoBehaviour
{
    private Racer racer;
    private GameConfig config;
    private Transform head;
    private Vector3 headBaseScale = Vector3.one;
    private Vector3 headBaseLocalPos;
    private float timer = -1f;   // -1 = 대기

    // 목 체인 전체 (밑동 → ... → 머리). v22 재작성: 밑동 60% + 머리 100% 두 지점만 옮기던
    // 이전 방식은 중간 목 본들이 밑동에 강체로 붙어 따라가서 머리 직전에 남은 변위를
    // 한 번에 점프 = 목이 W자로 꺾였다 (유저 제보). 이제 체인 전 본을 "어깨→머리 목표점"
    // 직선 위에 거리 비례로 앉혀 목이 곧은 막대기 하나로 뻗는다.
    private Transform[] chain;
    private Vector3[] chainBaseLocalPos;
    private float[] chainFrac;      // 밑동 관절 = 0 ~ 머리 = 1 (바인드 자세 본 간 거리 비례)

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        this.config = config;

        head = FindHeadBone();
        if (head == null)
        {
            Debug.LogWarning($"[NeckSweepFx] {name}: 머리 본을 못 찾음 (연출 생략)");
            return;
        }
        headBaseScale = head.localScale;
        headBaseLocalPos = head.localPosition;

        // 목 체인 수집 — 머리에서 위로, 부모가 "자식 1개짜리 단일 체인"인 동안 올라간다.
        // 분기(자식 여럿 = 어깨/몸통)를 만나면 그 직전 본이 목 밑동 (§11 — 이름 대신 구조로)
        var list = new System.Collections.Generic.List<Transform> { head };
        var t = head;
        while (t.parent != null && t.parent.childCount == 1) { t = t.parent; list.Add(t); }
        list.Reverse();                                   // [0]=밑동 ... [n-1]=머리
        chain = list.ToArray();

        chainBaseLocalPos = new Vector3[chain.Length];
        chainFrac = new float[chain.Length];
        float total = 0f;
        for (int i = 0; i < chain.Length; i++)
        {
            chainBaseLocalPos[i] = chain[i].localPosition;
            if (i > 0) { total += chain[i].localPosition.magnitude; chainFrac[i] = total; }
        }
        for (int i = 1; i < chain.Length; i++)
            chainFrac[i] = total > 1e-5f ? chainFrac[i] / total : 1f;   // 정규화 (밑동 0 ~ 머리 1)
    }

    // 머리 본 탐색 — RoarFx와 같은 다단 전략 (이름 → 턱의 부모 → 최심부 spine)
    private Transform FindHeadBone()
    {
        var bones = GetComponentsInChildren<Transform>(true);
        foreach (var tr in bones)
        {
            string n = tr.name.ToLowerInvariant();
            if (n == "scull" || n == "skull" || n == "head") return tr;
        }
        foreach (var tr in bones)
            if (tr.name.ToLowerInvariant() == "jaw" && tr.parent != null) return tr.parent;

        Transform deepest = null;
        int bestIdx = -1;
        foreach (var tr in bones)
        {
            if (!tr.name.StartsWith("spine.")) continue;
            if (int.TryParse(tr.name.Substring(6), out int idx) && idx > bestIdx)
            { bestIdx = idx; deepest = tr; }
        }
        return deepest;
    }

    private void OnEnable() => GameEvents.OnSkillEvent += HandleSkillEvent;
    private void OnDisable() => GameEvents.OnSkillEvent -= HandleSkillEvent;

    private void HandleSkillEvent(SkillFeedEvent evt, int rid)
    {
        if (racer == null || head == null) return;
        if (evt == SkillFeedEvent.NeckSweep && rid == racer.RacerId) timer = 0f;
    }

    private void LateUpdate()
    {
        if (timer < 0f || head == null) return;

        // 탈락/완주 정리 — 누운 채 목이 돌면 기괴하다
        if (racer == null || racer.HasFinished) { ResetHead(); return; }

        timer += Time.deltaTime;
        float windup = SkillTuning.NeckSweepWindupSeconds;         // 뻗기 (기절 판정 직전까지)
        float spin = SkillTuning.NeckSweepSpinSeconds
                   * SkillTuning.NeckSweepSpinCount;               // 훑기 전체 (한 바퀴 × 회전 수 — v23: 2회전)
        float settle = 0.4f;                                       // 복귀
        float total = windup + spin + settle;
        if (timer >= total) { ResetHead(); return; }

        float raise = config != null ? config.neckRaiseHeight : 1.6f;
        float sweepH = config != null ? config.neckSweepHeight : 0.7f;
        float radius = SkillTuning.NeckSweepRadius * 0.9f;         // 연출 원 ≈ 판정 반경 (연출이 거짓말 X)

        // ⚠ 체인을 기준 자세로 먼저 리셋 — 지난 프레임에 옮겨둔 값이 남아 있으면(애니는 회전 커브만
        //    쓰므로 위치는 안 되돌려준다 §11) 아래 "제자리" 측정이 오염돼 변위가 눈덩이처럼 불어난다
        if (chain != null)
            for (int i = 0; i < chain.Length; i++)
                chain[i].localPosition = chainBaseLocalPos[i];

        // 머리 본의 "제자리" 월드 좌표 (애니 포즈 기준) — 오프셋은 여기서부터 계산
        Vector3 baseWorld = head.parent != null ? head.parent.TransformPoint(headBaseLocalPos)
                                                : headBaseLocalPos;
        Vector3 desired;
        float scaleK;

        if (timer < windup)
        {
            // ① 목을 하늘로 쭉 (예열) — 몸은 계속 달린다
            float k = Mathf.SmoothStep(0f, 1f, timer / windup);
            desired = baseWorld + Vector3.up * (raise * k);
            scaleK = Mathf.Lerp(1f, 1.15f, k);
        }
        else if (timer < windup + spin)
        {
            // ② 달리면서 목을 바닥 높이로 내리찍어 몸 주변 훑기(SpinCount 바퀴) —
            //    원 중심이 매 프레임 현재 위치라 달리는 몸을 원이 따라온다.
            //    각도 = 진행률 × 360 × 회전 수 — 회전 수가 정수라 끝 각도는 항상 전방(복귀 구간과 정합)
            float k = (timer - windup) / spin;
            float ang = k * 360f * SkillTuning.NeckSweepSpinCount * Mathf.Deg2Rad;
            Vector3 dir = racer.transform.rotation *
                new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            Vector3 center = racer.transform.position;
            desired = center + dir * radius + Vector3.up * sweepH;
            scaleK = 1.25f;
        }
        else
        {
            // ③ 스르륵 복귀
            float k = (timer - windup - spin) / settle;
            Vector3 lastDir = racer.transform.rotation * Vector3.forward;   // 한 바퀴 = 전방 복귀
            Vector3 sweepEnd = racer.transform.position + lastDir * radius + Vector3.up * sweepH;
            desired = Vector3.Lerp(sweepEnd, baseWorld, Mathf.SmoothStep(0f, 1f, k));
            scaleK = Mathf.Lerp(1.25f, 1f, k);
        }

        // 체인 전 본을 "어깨(밑동 관절) → 머리 목표점" 직선 위에 거리 비례로 앉힌다 —
        // 목이 한 덩어리 막대기로 뻗는다 (유저 그림 발주). 밑동(frac 0)은 제자리라 회전축이 어깨.
        // 머리(frac 1)는 정확히 목표점이므로 기절 판정 원과의 정합은 그대로다.
        // 적용은 밑동→머리 순서 — 부모를 먼저 확정해야 자식의 로컬 변환이 맞는다.
        if (chain != null && chain.Length >= 2)
        {
            Vector3 anchor = chain[0].position;            // 리셋 직후 = 애니 포즈의 밑동 관절
            for (int i = 1; i < chain.Length; i++)
            {
                Vector3 target = Vector3.Lerp(anchor, desired, chainFrac[i]);
                chain[i].localPosition = chain[i].parent != null
                    ? chain[i].parent.InverseTransformPoint(target)
                    : target;
            }
        }
        else
        {
            // 폴백 (체인이 머리뿐인 리그): 머리만 목표점으로
            head.localPosition = head.parent != null
                ? head.parent.InverseTransformPoint(desired)
                : desired;
        }
        head.localScale = headBaseScale * scaleK;
    }

    private void ResetHead()
    {
        timer = -1f;
        if (head != null) head.localScale = headBaseScale;
        if (chain != null)
            for (int i = 0; i < chain.Length; i++)
                chain[i].localPosition = chainBaseLocalPos[i];
        else if (head != null) head.localPosition = headBaseLocalPos;
    }
}
