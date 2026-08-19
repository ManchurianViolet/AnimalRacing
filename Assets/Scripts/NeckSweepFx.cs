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
    private Transform neckBase;             // 목 밑동 — 머리에서 단일 체인을 거슬러 몸통 분기 직전 본 (기린 = spine.010)
    private Vector3 headBaseScale = Vector3.one;
    private Vector3 headBaseLocalPos;
    private Vector3 neckBaseLocalPos;
    private float timer = -1f;   // -1 = 대기

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        this.config = config;

        head = FindHeadBone();
        if (head != null)
        {
            headBaseScale = head.localScale;
            headBaseLocalPos = head.localPosition;

            // 목 밑동 탐색 — 머리에서 위로, 부모가 "자식 1개짜리 단일 체인"인 동안 올라간다.
            // 분기(자식 여럿 = 어깨/몸통)를 만나면 그 직전 본이 목의 시작 (§11 — 이름 대신 구조로)
            var t = head;
            while (t.parent != null && t.parent.childCount == 1) t = t.parent;
            if (t != head) { neckBase = t; neckBaseLocalPos = t.localPosition; }
        }
        else Debug.LogWarning($"[NeckSweepFx] {name}: 머리 본을 못 찾음 (연출 생략)");
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
        float spin = SkillTuning.NeckSweepSpinSeconds;             // 360도 훑기
        float settle = 0.4f;                                       // 복귀
        float total = windup + spin + settle;
        if (timer >= total) { ResetHead(); return; }

        float raise = config != null ? config.neckRaiseHeight : 1.6f;
        float sweepH = config != null ? config.neckSweepHeight : 0.7f;
        float bendShare = config != null ? config.neckBendShare : 0.6f;
        float radius = SkillTuning.NeckSweepRadius * 0.9f;         // 연출 원 ≈ 판정 반경 (연출이 거짓말 X)

        // ⚠ 밑동을 기준 자세로 먼저 리셋 — 지난 프레임에 옮겨둔 값이 남아 있으면(애니는 회전 커브만
        //    쓰므로 위치는 안 되돌려준다 §11) 아래 "제자리" 측정이 오염돼 변위가 눈덩이처럼 불어난다
        if (neckBase != null) neckBase.localPosition = neckBaseLocalPos;

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
            // ② 달리면서 목을 바닥 높이로 내리찍어 몸 주변 360도 훑기 —
            //    원 중심이 매 프레임 현재 위치라 달리는 몸을 원이 따라온다
            float k = (timer - windup) / spin;
            float ang = k * 360f * Mathf.Deg2Rad;
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

        // 목 밑동을 변위의 일부만큼 먼저 옮긴다 — 꺾임점이 목 중간(머리 부모)이 아니라
        // 몸통 분기점(기린 = 어깨)으로 내려온다 (v22 유저 발주). 머리는 그 뒤에 정확한
        // 목표점으로 재계산하므로 판정 원과의 정합은 그대로다.
        if (neckBase != null)
        {
            Vector3 disp = (desired - baseWorld) * bendShare;
            neckBase.localPosition = neckBaseLocalPos + (neckBase.parent != null
                ? neckBase.parent.InverseTransformVector(disp)
                : disp);
        }

        head.localPosition = head.parent != null
            ? head.parent.InverseTransformPoint(desired)
            : desired;
        head.localScale = headBaseScale * scaleK;
    }

    private void ResetHead()
    {
        timer = -1f;
        if (head != null)
        {
            head.localPosition = headBaseLocalPos;
            head.localScale = headBaseScale;
        }
        if (neckBase != null) neckBase.localPosition = neckBaseLocalPos;
    }
}
