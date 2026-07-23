using System.Linq;
using UnityEngine;

/// <summary>
/// 트랙 경로 (웨이포인트 꺾은선). 순위 판정의 유일한 기준.
/// - GetProgress(pos): 경로에 투영한 누적 거리 → 순위/꼴찌/결승 판정
/// - GetPoint / GetTangent / GetNormal: 조향 목표점 계산용
/// 웨이포인트를 촘촘히 박으면 시각적으로 곡선. 스플라인 교체는 이 클래스만 갈면 됨.
/// </summary>
public class TrackPath : MonoBehaviour
{
    [Tooltip("비워두면 자식 Transform들을 순서대로 사용")]
    [SerializeField] private Transform[] waypoints;
    [Tooltip("트랙 전체 폭 (레인 오프셋 한계 계산용)")]
    [SerializeField] private float width = 10f;

    private Vector3[] pts;
    private float[] cumulative;   // 각 웨이포인트까지의 누적 거리

    public float TotalLength { get; private set; }
    public float HalfWidth => width * 0.5f;

    private void Awake() => Build();

    public void Build()
    {
        if (waypoints == null || waypoints.Length < 2)
        {
            // 자식에서 자동 수집
            waypoints = GetComponentsInChildren<Transform>()
                        .Where(t => t != transform).ToArray();
        }

        pts = waypoints.Select(w => w.position).ToArray();
        cumulative = new float[pts.Length];
        for (int i = 1; i < pts.Length; i++)
            cumulative[i] = cumulative[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        TotalLength = cumulative[^1];
    }

    /// <summary>월드 위치를 경로에 투영한 누적 진행 거리.</summary>
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

    /// <summary>진행 거리 → 경로 위 지점.</summary>
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

    /// <summary>진행 방향 (해당 지점이 속한 선분의 방향).</summary>
    public Vector3 GetTangent(float progress)
    {
        progress = Mathf.Clamp(progress, 0f, TotalLength - 0.01f);
        for (int i = 1; i < cumulative.Length; i++)
            if (progress <= cumulative[i])
                return (pts[i] - pts[i - 1]).normalized;
        return (pts[^1] - pts[^2]).normalized;
    }

    /// <summary>경로의 오른쪽 법선. 레인 오프셋 방향.</summary>
    public Vector3 GetNormal(float progress) =>
        Vector3.Cross(Vector3.up, GetTangent(progress)).normalized;

    /// <summary>중심선 기준 좌우 오프셋 (부호 있음). 스폰 시 초기 레인 배정용.</summary>
    public float GetLateralOffset(Vector3 pos)
    {
        float prog = GetProgress(pos);
        return Vector3.Dot(pos - GetPoint(prog), GetNormal(prog));
    }

    private void OnDrawGizmos()
    {
        var wps = (waypoints != null && waypoints.Length >= 2)
            ? waypoints
            : GetComponentsInChildren<Transform>().Where(t => t != transform).ToArray();
        if (wps.Length < 2) return;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < wps.Length - 1; i++)
        {
            Gizmos.DrawLine(wps[i].position, wps[i + 1].position);
            // 트랙 폭 표시
            Vector3 dir = (wps[i + 1].position - wps[i].position).normalized;
            Vector3 n = Vector3.Cross(Vector3.up, dir) * (width * 0.5f);
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawLine(wps[i].position + n, wps[i + 1].position + n);
            Gizmos.DrawLine(wps[i].position - n, wps[i + 1].position - n);
            Gizmos.color = Color.yellow;
        }
    }
}
