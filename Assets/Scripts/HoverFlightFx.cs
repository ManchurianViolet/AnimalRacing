using UnityEngine;

/// <summary>
/// [비둘기] 이동 = 비행 호버 — 달리는 동안 몸(스켈레톤)을 공중에 띄운다.
/// 컨트롤러의 Walk/Run 자리에는 Fly 클립이 구워져 있는데(AnimalControllerBaker.FlyMovers)
/// 클립의 루트 높이가 0이라 그냥 두면 땅에 붙어 날갯짓만 한다 — 높이는 여기서 만든다.
///
/// [멀티] 위치 변화만 보고 판단하므로(PlayerFootsteps와 같은 철학) 호스트/클라가
/// 같은 코드로 돌고 네트워크 추가 통신 0 — 원격 미러 위치도 TransformView가 이미 옮겨준다.
///
/// ⚠ 애니메이터가 매 프레임 스켈레톤 localPosition을 다시 쓰므로(§11) LateUpdate에서
/// "기준 + 오프셋"으로 덮는다. += 누적 금지 (§11 포효 머리 50m 이탈 사고).
/// 콜라이더/판정은 루트(지면) 그대로 — 순수 시각 연출이라 조준·물리 무영향.
/// RaceManager가 AnimalDefinition.hoverFlight 동물에만 부착. 튜닝은 GameConfig "연출 — 비행 호버".
/// </summary>
public class HoverFlightFx : MonoBehaviour
{
    private Racer racer;
    private GameConfig config;
    private Transform visual;        // 스켈레톤 루트 — 스킨드메시·번호판(본 부착)이 전부 따라 뜬다
    private Vector3 baseLocal;       // Init 시점의 스켈레톤 로컬 위치 (애니 커브 기준값과 동일 = 0 근방)

    private Vector3 lastPos;
    private bool hasLast;
    private float speedSmoothed;     // 수평 속도 (저역 통과 — 한 프레임 떨림에 이착륙 안 하게)
    private float hover;             // 현재 호버 높이 (월드 m)
    private float hoverVel;

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        this.config = config;

        // 스켈레톤 루트 탐색 — 이름 우선, 없으면 스킨드메시 rootBone에서 역추적 (§11 "이름은 리그마다 다르다")
        foreach (Transform child in transform)
            if (child.name.StartsWith("Skeleton")) { visual = child; break; }
        if (visual == null)
        {
            var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null && smr.rootBone != null)
            {
                Transform t = smr.rootBone;
                while (t.parent != null && t.parent != transform) t = t.parent;
                if (t.parent == transform) visual = t;
            }
        }
        if (visual == null)
        {
            Debug.LogWarning($"[비행호버] {name} — 스켈레톤 루트를 못 찾아 연출 생략");
            enabled = false;
            return;
        }

        baseLocal = visual.localPosition;
        hasLast = false;
        BuildAimProxy();
    }

    /// <summary>
    /// 조준 전용 트리거 캡슐 — 물리 캡슐(EnsureBodyCollider)은 지면에 남아 접지를 담당하고,
    /// 이건 스켈레톤 자식이라 호버를 따라 떠오른다. 주사기/발동 무전기 레이캐스트가
    /// "보이는 새"를 맞히게 하는 장치 (GetComponentInParent&lt;Racer&gt;로 식별되므로 배선 0).
    /// isTrigger = 물리 무간섭. 지면/모터 레이는 전부 QueryTriggerInteraction.Ignore 확인됨.
    /// 새가 작아 판정을 35% 부풀린다 (조준 관대함 — 서 있을 때도 유효).
    /// </summary>
    private void BuildAimProxy()
    {
        var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null) return;
        Bounds b = smr.bounds;   // 월드 (스폰 시점 자세)

        var go = new GameObject("AimProxy");
        go.layer = gameObject.layer;
        go.transform.SetParent(visual, false);
        go.transform.position = b.center;
        go.transform.rotation = transform.rotation;

        var cap = go.AddComponent<CapsuleCollider>();
        cap.isTrigger = true;
        cap.direction = 2;   // 몸 길이 = z축
        // 콜라이더 값은 로컬 단위 — 스케일된 프리팹(비둘기 2.2배)은 나눠 넣는다 (§11)
        // 반지름은 몸높이 기준 — 바인드 포즈 날개폭(extents.x)을 쓰면 반경 1m짜리가 되어
        // 옆 레인 동물의 조준까지 가로챈다 (실측 후 교정)
        float s = Mathf.Max(1e-4f, go.transform.lossyScale.y);
        cap.radius = b.extents.y * 1.35f / s;
        cap.height = b.size.z * 1.15f / s;
    }

    private void LateUpdate()
    {
        if (visual == null) return;

        // 수평 속도 실측 (스폰 첫 프레임·순간이동 직후는 계측 생략)
        Vector3 p = transform.position;
        float dt = Time.deltaTime;
        if (hasLast && dt > 0.0001f)
        {
            Vector3 d = p - lastPos;
            d.y = 0f;
            float v = d.magnitude / dt;
            speedSmoothed = Mathf.Lerp(speedSmoothed, v, 1f - Mathf.Exp(-8f * dt));
        }
        lastPos = p;
        hasLast = true;

        float minSpeed = config != null ? config.hoverFlightMinSpeed : 1.5f;
        float height = config != null ? config.hoverFlightHeight : 0.55f;
        float blend = Mathf.Max(0.05f, config != null ? config.hoverFlightBlendSeconds : 0.5f);

        // 완주(관성 정지)·탈락(옆으로 눕기)은 속도가 0으로 가므로 자동 착지 — 별도 상태 검사 불요.
        // 단 탈락 애니 정지(animFrozen) 중에도 이 LateUpdate는 살아 있어 착지가 끝까지 재생된다.
        float target = speedSmoothed > minSpeed ? height : 0f;
        hover = Mathf.SmoothDamp(hover, target, ref hoverVel, blend);

        // 월드 m → 부모 로컬 단위 (비둘기 2.2배 스케일 자동 보정 — §11 콜라이더 로컬 단위 법칙과 같은 함정)
        float scale = Mathf.Max(0.0001f, transform.lossyScale.y);
        visual.localPosition = baseLocal + Vector3.up * (hover / scale);
    }
}
