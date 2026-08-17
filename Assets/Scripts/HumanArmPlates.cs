using UnityEngine;

/// <summary>
/// [인간 레이서] 번호판을 양팔 바깥면에 앉힌다 (옆구리에 두면 팔에 가려 안 보인다 — 유저 지적).
///
/// 왜 런타임 실측인가: 인간은 매판 랜덤 복장(HumanLookRandomizer)이라 소매 두께가 제각각이다.
/// 실측값 — 맨팔 반지름 0.049~0.060 / 상의 소매 최대 0.091(상의 3). 고정 오프셋을 쓰면
/// 맨팔에선 4cm 붕 뜨고, 얇게 잡으면 두꺼운 소매에 통째로 파묻힌다.
/// 그래서 스폰 직후 "실제로 입고 있는 메시"를 한 번 재서 그 바로 바깥에 놓는다.
/// 판은 팔 본의 자식이라 한 번 자리를 잡으면 이후엔 공짜로 따라다닌다 (매 프레임 계산 없음).
///
/// 바깥 방향은 월드 좌우가 아니라 "몸통 → 팔"의 반지름 방향으로 잡는다.
/// 그래야 T포즈(팔이 옆으로 뻗음)든 달리는 자세(팔이 내려옴)든 같은 면을 가리킨다 —
/// 첫 프레임의 애니 포즈에 결과가 휘둘리지 않는다.
///
/// [멀티] 순수 로컬 배치 — 통신 0 (모두 같은 옷을 보므로 각자 계산해도 결과가 같다).
/// </summary>
public class HumanArmPlates : MonoBehaviour
{
    [Header("판 묶음 (팔 본의 자식)")]
    [Tooltip("왼팔 번호판 홀더 — 이 오브젝트의 +X가 팔 바깥을 보게 배치된다")]
    [SerializeField] private Transform leftPlate;
    [SerializeField] private Transform rightPlate;

    [Header("붙는 위치")]
    [Tooltip("어깨(0) → 팔꿈치(1) 사이 어디에 놓을지")]
    [SerializeField, Range(0.2f, 0.9f)] private float alongArm = 0.55f;
    [Tooltip("옷 표면에서 띄우는 여유 (m) — 팔이 굽을 때 살짝 파고드는 것 방지")]
    [SerializeField] private float surfaceGap = 0.012f;
    [Tooltip("판 두께의 절반 (m) — 판 큐브의 두께와 맞출 것")]
    [SerializeField] private float plateHalfThickness = 0.005f;
    [Tooltip("굵기를 재는 팔 구간 (어깨→팔꿈치 비율). 판이 놓일 자리 주변만 본다")]
    [SerializeField] private Vector2 measureBand = new Vector2(0.35f, 0.8f);

    [Header("바깥 방향 (팔 본 로컬)")]
    [Tooltip("달리기 한 사이클의 평균 '팔 바깥' 축을 본 로컬로 구운 값. " +
             "비워두면(0) 몸통→팔 반지름으로 계산 — 그 경우 달릴 때 판이 30~50° 돌아간다")]
    [SerializeField] private Vector3 leftOutwardLocal;
    [SerializeField] private Vector3 rightOutwardLocal;

    private bool fitted;

    // Start가 아니라 첫 LateUpdate에 맞춘다 — HumanLookRandomizer도 Start에서 옷을 입히는데
    // Start끼리는 순서가 보장되지 않는다. LateUpdate면 그 프레임의 모든 Start가 끝난 뒤다.
    private void LateUpdate()
    {
        if (fitted) return;
        fitted = true;

        Fit(leftPlate, leftOutwardLocal);
        Fit(rightPlate, rightOutwardLocal);
        enabled = false;   // 한 번이면 끝 — 이후엔 팔 본이 알아서 데리고 다닌다
    }

    /// <summary>옷을 갈아입었을 때 다시 재고 싶으면 호출 (현재 호출처 없음 — 복장은 스폰 때 한 번 정해진다).</summary>
    public void Refit()
    {
        fitted = false;
        enabled = true;
    }

    private void Fit(Transform plate, Vector3 outwardLocal)
    {
        if (plate == null) return;

        Transform arm = plate.parent;                       // 위팔 본
        if (arm == null) return;
        Transform elbow = FindChildBone(arm, plate);        // 팔꿈치(아래팔 본)
        Transform chest = arm.parent != null ? arm.parent.parent : null;   // 어깨의 부모 = 가슴
        if (elbow == null || chest == null) return;

        // ⚠ 배치 계산은 전부 "팔 본 로컬"에서 한다. 월드에서 계산해 대입하면 그 순간의 애니 포즈가
        // 결과에 섞여 들어가, 스폰 때 아이들이었다가 달리기 시작하면 판이 40°씩 돌아간다 (실사고).
        // 팔꿈치의 localPosition은 애니와 무관한 뼈대 상수라 이 계산은 포즈에 안 휘둘린다.
        Vector3 axisLocal = elbow.localPosition;
        float armLength = axisLocal.magnitude;
        if (armLength < 0.0001f) return;
        axisLocal /= armLength;

        // 바깥 방향: 구워둔 값(달리기 한 사이클 평균)이 있으면 그것, 없으면 몸통→팔 반지름으로 폴백
        Vector3 outLocal;
        if (outwardLocal.sqrMagnitude > 1e-4f)
        {
            outLocal = outwardLocal.normalized;
        }
        else
        {
            Vector3 center = arm.position + (arm.rotation * axisLocal) * (armLength * alongArm);
            outLocal = Quaternion.Inverse(arm.rotation) * (center - chest.position);
        }

        outLocal -= axisLocal * Vector3.Dot(outLocal, axisLocal);   // 팔 축 성분 제거 = 단면의 바깥쪽
        if (outLocal.sqrMagnitude < 1e-6f) return;
        outLocal.Normalize();

        Vector3 armPos = arm.position;
        Vector3 axisWorld = arm.rotation * axisLocal;
        float radius = MeasureRadius(armPos, axisWorld, armLength, arm.rotation * outLocal);
        if (radius <= 0f) radius = 0.06f;                   // 못 쟀을 때의 안전값 (맨팔 굵기)

        // 판의 세로 = 팔을 거슬러 올라가는 방향(팔꿈치→어깨). 숫자가 팔을 따라 바로 선다.
        // (팔 축과 바깥에 모두 수직인 축을 쓰면 숫자가 90° 누워버린다 — 실사고)
        Vector3 upLocal = -axisLocal;

        plate.localPosition = axisLocal * (armLength * alongArm)
                            + outLocal * (radius + surfaceGap + plateHalfThickness);
        // 홀더의 +X = 바깥. Unity에서 right = Cross(up, forward)이므로 forward = Cross(바깥, up)
        plate.localRotation = Quaternion.LookRotation(Vector3.Cross(outLocal, upLocal), upLocal);
    }

    /// <summary>입고 있는 스킨드메시 전부를 훑어 팔 구간의 바깥쪽 최대 돌출을 잰다 (소매 포함).</summary>
    private float MeasureRadius(Vector3 armPos, Vector3 axis, float armLength, Vector3 outward)
    {
        var baked = new Mesh();
        float best = 0f;

        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            // 비활성 슬롯은 정점 4개짜리 빈 껍데기 (§3-8) — 잴 게 없다
            if (smr.sharedMesh == null || smr.sharedMesh.vertexCount < 8) continue;

            smr.BakeMesh(baked, true);
            var m = smr.transform.localToWorldMatrix;
            var verts = baked.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 w = m.MultiplyPoint3x4(verts[i]);
                Vector3 rel = w - armPos;
                float along = Vector3.Dot(rel, axis);
                if (along < armLength * measureBand.x || along > armLength * measureBand.y) continue;

                Vector3 radial = rel - axis * along;
                if (radial.sqrMagnitude > 0.04f) continue;   // 0.2m 밖 = 몸통·반대쪽 팔 정점
                float d = Vector3.Dot(radial, outward);
                if (d > best) best = d;
            }
        }

        Destroy(baked);
        return best;
    }

    /// <summary>위팔 본의 자식 중 아래팔(가장 멀리 뻗은 자식)을 고른다 — 리그별 이름 차이를 안 탄다.</summary>
    private static Transform FindChildBone(Transform arm, Transform exclude)
    {
        Transform best = null;
        float far = -1f;
        foreach (Transform c in arm)
        {
            if (c == exclude) continue;   // 판 홀더도 팔의 자식이다
            float d = c.localPosition.sqrMagnitude;
            if (d > far) { far = d; best = c; }
        }
        return best;
    }
}
