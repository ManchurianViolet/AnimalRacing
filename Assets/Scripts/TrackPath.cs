using System.Linq;
using UnityEngine;

/// <summary>
/// 트랙 경로 v2 — 경계 직접 정의 방식.
/// 안쪽 라인(InnerLine)과 바깥 라인(OuterLine)을 각각 웨이포인트로 긋고,
/// 중심선/폭/한계는 전부 두 경계에서 역산한다. 추정(곡률 기반 접힘 감지 등) 전면 은퇴.
///
/// 규칙: 두 라인은 같은 개수의 점, 같은 순서(주행 방향), i번끼리 같은 단면의 쌍.
/// 폭은 구간마다 달라도 됨 (헤어핀은 넓게, 직선은 좁게 — 점 배치로 자유 조절).
/// </summary>
public class TrackPath : MonoBehaviour
{
    [Tooltip("안쪽 경계 라인의 부모 (자식들이 순서대로 점)")]
    [SerializeField] private Transform innerLine;
    [Tooltip("바깥 경계 라인의 부모 (자식들이 순서대로 점)")]
    [SerializeField] private Transform outerLine;

    private Vector3[] inner, outer;
    private Vector3[] pts;        // 중심선 = 쌍의 중점
    private float[] halfW;        // 단면별 반폭
    private float[] cumulative;
    private float[] yawAtPoint;

    public float TotalLength { get; private set; }
    /// <summary>출발 지점 반폭 (출발 그리드 정렬용).</summary>
    public float HalfWidth => halfW != null && halfW.Length > 0 ? halfW[0] : 5f;

    private void Awake() => Build();

    public void Build()
    {
        if (innerLine == null || outerLine == null)
        {
            Debug.LogError("[TrackPath] Inner/Outer Line이 비어있습니다 — 인스펙터에 두 라인 부모를 연결하세요");
            return;
        }

        inner = innerLine.Cast<Transform>().Select(t => t.position).ToArray();
        outer = outerLine.Cast<Transform>().Select(t => t.position).ToArray();

        if (inner.Length != outer.Length || inner.Length < 2)
        {
            Debug.LogError($"[TrackPath] 경계 점 개수 불일치/부족 — 안쪽 {inner.Length}개, 바깥 {outer.Length}개 " +
                           "(같은 개수·같은 순서·쌍 규칙 필수)");
            return;
        }

        int n = inner.Length;
        pts = new Vector3[n];
        halfW = new float[n];
        for (int i = 0; i < n; i++)
        {
            pts[i] = (inner[i] + outer[i]) * 0.5f;
            halfW[i] = Vector3.Distance(inner[i], outer[i]) * 0.5f;
        }

        cumulative = new float[n];
        for (int i = 1; i < n; i++)
            cumulative[i] = cumulative[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        TotalLength = cumulative[^1];

        // ---- 빌드 검진 ----
        Debug.Log($"[TrackPath] 빌드: 단면 {n}쌍, 총 길이 {TotalLength:F1}m, " +
                  $"폭 {halfW.Min() * 2f:F1}~{halfW.Max() * 2f:F1}m");
        for (int i = 1; i < n; i++)
        {
            float seg = cumulative[i] - cumulative[i - 1];
            if (seg < 0.05f)
                Debug.LogError($"[TrackPath] ⚠ 단면 {i - 1}↔{i} 겹침 ({seg:F3}m)");
            else if (seg > 40f)
                Debug.LogWarning($"[TrackPath] 단면 {i - 1}→{i} 구간 {seg:F0}m — 장거리 의심");
        }

        yawAtPoint = new float[n];
        for (int i = 1; i < n - 1; i++)
        {
            Vector3 inDir = (pts[i] - pts[i - 1]).normalized;
            Vector3 outDir = (pts[i + 1] - pts[i]).normalized;
            yawAtPoint[i] = Vector3.SignedAngle(inDir, outDir, Vector3.up);
        }
    }

    // ================= 진행도 =================

    public float GetProgress(Vector3 pos)
    {
        float bestSqr = float.MaxValue, bestProg = 0f;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector3 a = pts[i], ab = pts[i + 1] - a;
            float t = Mathf.Clamp01(Vector3.Dot(pos - a, ab) / ab.sqrMagnitude);
            Vector3 proj = a + ab * t;
            float d = (pos - proj).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                bestProg = cumulative[i] + ab.magnitude * t;
            }
        }
        return bestProg;
    }

    /// <summary>연속성 투영 — 직전 진행도 근처 구간에서만 검색 (반대편 변 포획 방지).</summary>
    public float GetProgressNear(Vector3 pos, float lastProgress, float forwardWindow = 15f, float backSlack = 5f)
    {
        float lo = lastProgress - backSlack;
        float hi = lastProgress + forwardWindow;

        float bestSqr = float.MaxValue, bestProg = lastProgress;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            if (cumulative[i + 1] < lo || cumulative[i] > hi) continue;

            Vector3 a = pts[i], ab = pts[i + 1] - a;
            float t = Mathf.Clamp01(Vector3.Dot(pos - a, ab) / ab.sqrMagnitude);
            Vector3 proj = a + ab * t;
            float d = (pos - proj).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                bestProg = cumulative[i] + ab.magnitude * t;
            }
        }

        if (bestSqr > 400f) return GetProgress(pos);   // 20m 이상 이탈 = 비정상 → 전체 검색 복구
        return bestProg;
    }

    // ================= 기하 =================

    public Vector3 GetPoint(float progress)
    {
        progress = Mathf.Clamp(progress, 0f, TotalLength);
        for (int i = 1; i < cumulative.Length; i++)
        {
            if (progress <= cumulative[i])
            {
                float segLen = cumulative[i] - cumulative[i - 1];
                float t = segLen < 1e-5f ? 0f : (progress - cumulative[i - 1]) / segLen;
                return Vector3.Lerp(pts[i - 1], pts[i], t);
            }
        }
        return pts[^1];
    }

    /// <summary>평활 접선 (앞뒤 2m를 잇는 방향) — 구간 경계에서 방향이 튀지 않음.</summary>
    public Vector3 GetTangent(float progress)
    {
        const float h = 2f;
        Vector3 a = GetPoint(progress - h);
        Vector3 b = GetPoint(progress + h);
        Vector3 d = b - a;
        if (d.sqrMagnitude < 1e-6f)
        {
            progress = Mathf.Clamp(progress, 0f, TotalLength - 0.01f);
            for (int i = 1; i < cumulative.Length; i++)
                if (progress <= cumulative[i])
                    return (pts[i] - pts[i - 1]).normalized;
            return (pts[^1] - pts[^2]).normalized;
        }
        return d.normalized;
    }

    public Vector3 GetNormal(float progress) =>
        Vector3.Cross(Vector3.up, GetTangent(progress)).normalized;

    public Vector3 GetPointAt(float progress, float lateral) =>
        GetPoint(progress) + GetNormal(progress) * lateral;

    public float GetLateralOffset(Vector3 pos)
    {
        float prog = GetProgress(pos);
        return Vector3.Dot(pos - GetPoint(prog), GetNormal(prog));
    }

    /// <summary>해당 진행도의 반폭 (단면 보간) — 폭이 구간마다 달라도 정확.</summary>
    public float GetHalfWidth(float progress)
    {
        progress = Mathf.Clamp(progress, 0f, TotalLength);
        for (int i = 1; i < cumulative.Length; i++)
        {
            if (progress <= cumulative[i])
            {
                float segLen = cumulative[i] - cumulative[i - 1];
                float t = segLen < 1e-5f ? 0f : (progress - cumulative[i - 1]) / segLen;
                return Mathf.Lerp(halfW[i - 1], halfW[i], t);
            }
        }
        return halfW[^1];
    }

    /// <summary>주행 가능 횡한계 = 그 지점의 반폭 (경계는 사람이 그었으니 추정 불필요).</summary>
    public float GetLateralLimit(float progress, float sign) => GetHalfWidth(progress);

    /// <summary>진행도 → (구간 인덱스, 구간 내 t). 단면 보간의 공통 재료.</summary>
    private void GetSection(float progress, out int i, out float t)
    {
        progress = Mathf.Clamp(progress, 0f, TotalLength);
        for (int k = 1; k < cumulative.Length; k++)
        {
            if (progress <= cumulative[k])
            {
                float segLen = cumulative[k] - cumulative[k - 1];
                i = k - 1;
                t = segLen < 1e-5f ? 0f : (progress - cumulative[k - 1]) / segLen;
                return;
            }
        }
        i = pts.Length - 2; t = 1f;
    }

    /// <summary>
    /// 목표점을 "안쪽 레일 ↔ 바깥 레일 사이의 비율"로 생성 (두 레일 사이 모델).
    /// 중심선+수직오프셋 방식은 안쪽 꼭짓점에서 목표가 제자리걸음하는 퇴화가 있었으나,
    /// 단면 보간은 목표가 경계 폴리라인을 따라 꼭짓점을 돌아 항상 전진한다.
    /// </summary>
    public Vector3 GetTargetOnSection(float progress, float lateral)
    {
        GetSection(progress, out int i, out float t);
        Vector3 pIn = Vector3.Lerp(inner[i], inner[i + 1], t);
        Vector3 pOut = Vector3.Lerp(outer[i], outer[i + 1], t);

        Vector3 c = (pIn + pOut) * 0.5f;
        Vector3 n = GetNormal(progress);
        float latIn = Vector3.Dot(pIn - c, n);
        float latOut = Vector3.Dot(pOut - c, n);
        if (Mathf.Abs(latOut - latIn) < 1e-4f) return c;

        float u = Mathf.Clamp01(Mathf.InverseLerp(latIn, latOut, lateral));
        return Vector3.Lerp(pIn, pOut, u);
    }

    public float GetSignedCurvatureAhead(float progress, float distance)
    {
        float end = Mathf.Min(progress + distance, TotalLength);
        float sum = 0f;
        for (int i = 1; i < pts.Length - 1; i++)
            if (cumulative[i] > progress && cumulative[i] <= end)
                sum += yawAtPoint[i];
        return sum / Mathf.Max(1f, distance);
    }

    // ================= 기즈모 =================

    private void OnDrawGizmos()
    {
        var inn = innerLine != null ? innerLine.Cast<Transform>().ToArray() : null;
        var outt = outerLine != null ? outerLine.Cast<Transform>().ToArray() : null;
        if (inn == null || outt == null || inn.Length < 2 || outt.Length < 2) return;

        // 안쪽 = 하늘색, 바깥 = 노랑
        Gizmos.color = Color.cyan;
        for (int i = 0; i < inn.Length - 1; i++)
            Gizmos.DrawLine(inn[i].position, inn[i + 1].position);

        Gizmos.color = Color.yellow;
        for (int i = 0; i < outt.Length - 1; i++)
            Gizmos.DrawLine(outt[i].position, outt[i + 1].position);

        // 쌍 단면 (사다리 가로대) — X로 꼬이면 쌍 순서가 어긋난 것
        int n = Mathf.Min(inn.Length, outt.Length);
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        for (int i = 0; i < n; i++)
            Gizmos.DrawLine(inn[i].position, outt[i].position);

#if UNITY_EDITOR
        for (int i = 0; i < inn.Length; i++)
            UnityEditor.Handles.Label(inn[i].position + Vector3.up * 1.2f, $"In {i}");
        for (int i = 0; i < outt.Length; i++)
            UnityEditor.Handles.Label(outt[i].position + Vector3.up * 1.2f, $"Out {i}");
#endif
    }
}
