using UnityEngine;

/// <summary>
/// 베팅 방의 동물 피규어 (내 방에만 로컬 생성 — 비밀 유지 + 네트워크 0).
/// 출전 동물 하나를 대표하며 선반 ↔ 손 ↔ 상자를 오간다.
/// 생성/스트립은 BettingRoomManager가, 집기/놓기는 FigurineBetting이 담당.
/// </summary>
public class BetFigurine : MonoBehaviour
{
    public int RacerId { get; private set; } = -1;
    public int PostNumber { get; private set; }          // 출전 번호 (1부터)

    /// <summary>종명 — 캐시하지 않고 그때그때 조회 (언어 전환이 즉시 먹게).</summary>
    public string AnimalName => Def != null ? Def.LocalizedName : "?";

    /// <summary>원본 동물 정의 — HUD 손 칸 아이콘/모니터 상세가 참조.</summary>
    public AnimalDefinition Def { get; private set; }

    /// <summary>조준 안내문: "4번 펭귄" — 동물 표기 이름과 같은 서식(racer.name) 공유.</summary>
    public string HoverName => Loc.Format("racer.name", PostNumber, AnimalName);

    /// <summary>선반 위 제자리 (BettingRoomManager가 만든 슬롯 앵커).</summary>
    public Transform HomeSlot { get; private set; }

    /// <summary>선반/상자에 놓였을 때의 크기 — 손에 쥘 땐 더 작게 줄였다가 이 값으로 되돌린다.</summary>
    public float ShelfScale { get; private set; } = 1f;

    /// <summary>
    /// 스케일 1 기준의 가장 긴 치수(m). 손에 쥘 때 동물마다 다른 덩치를 화면에서 같은 크기로
    /// 맞추는 데 쓴다 (말과 치킨이 같은 배율이면 하나는 넘치고 하나는 점만 해진다).
    /// </summary>
    public float BaseSize { get; private set; } = 1f;

    /// <summary>
    /// 스케일 1 기준, 루트 pivot에서 몸통 중심까지의 로컬 오프셋.
    /// pivot이 발밑이라 손에 쥘 때 화면 중앙에 맞추려면 이만큼 보정해야 하는데,
    /// 매 프레임 renderer.bounds를 다시 읽으면 안 된다 — 그 값은 transform 변경을 한 프레임 늦게
    /// 따라오므로, 보정 결과가 다시 입력이 되어 위치가 엉뚱한 곳으로 밀려난다(실사고).
    /// </summary>
    public Vector3 BaseCenter { get; private set; }

    /// <summary>지금 들어가 있는 상자 (없으면 null).</summary>
    public BetBox InBox { get; set; }

    /// <summary>지금 올라가 있는 관찰 전시대 (없으면 null).</summary>
    public InspectStand InStand { get; set; }

    /// <summary>집기 레이캐스트용 콜라이더 — 손에 들리면 끈다.</summary>
    public Collider PickCollider { get; private set; }

    public void Init(int racerId, int postNumber, AnimalDefinition def, Transform homeSlot, Collider pickCollider)
    {
        RacerId = racerId;
        PostNumber = postNumber;
        Def = def;
        HomeSlot = homeSlot;
        PickCollider = pickCollider;
        ShelfScale = transform.localScale.x;
        BaseSize = MeasureBaseSize();
        // 선반에 놓인 지금 자세에서 한 번만 잰다 (InverseTransformPoint가 스케일도 걷어낸다)
        BaseCenter = TryGetWorldBounds(out var initBounds)
            ? transform.InverseTransformPoint(initBounds.center)
            : Vector3.zero;

        // 달리기 연출용 — 평소엔 꺼둔 채(정지 포즈) 전시대에서만 켠다
        anim = GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            body = anim.transform;
            anim.applyRootMotion = false;
            anim.enabled = false;
        }
    }

    private Animator anim;
    private Transform body;      // 프리팹 루트 — 루트 모션 누적을 매 프레임 되돌린다
    private bool running;

    private Renderer[] cachedRenderers;

    /// <summary>
    /// 크기·중심을 잴 렌더러 = 동물 본체(SkinnedMeshRenderer)만.
    /// 받침대까지 넣으면 작은 동물일수록 받침대가 최장변으로 잡혀(개·고양이 = 받침대 1.5m)
    /// 정작 동물이 쪼그라든다. 번호판도 몸 밖으로 튀어나와 바운즈를 부풀리므로 함께 제외된다.
    /// </summary>
    private Renderer[] BodyRenderers()
    {
        if (cachedRenderers == null)
        {
            var skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            cachedRenderers = skinned.Length > 0
                ? (Renderer[])skinned
                : GetComponentsInChildren<Renderer>(true);
        }
        return cachedRenderers;
    }

    private Renderer[] cachedBase;

    /// <summary>
    /// 손에 쥐었는지 알린다 — 쥐는 동안은 받침대를 숨긴다.
    /// 받침대는 동물 덩치와 무관하게 고정 크기라, 본체 기준으로 확대하면 작은 동물일수록
    /// 받침대가 화면을 뒤덮는다 (치킨 본체 0.32 vs 받침대 0.63).
    /// (v12에 "받침대 비례 축소 유지"를 시도했다가 유저가 되돌림 — 숨김이 확정)
    /// </summary>
    public void SetHeld(bool held)
    {
        if (cachedBase == null)
        {
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (Transform c in transform)          // 루트 직속 = 받침대. 번호판은 rig 안쪽이라 안 걸린다
            {
                var r = c.GetComponent<MeshRenderer>();
                if (r != null && c.GetComponent<SkinnedMeshRenderer>() == null) list.Add(r);
            }
            cachedBase = list.ToArray();
        }
        for (int i = 0; i < cachedBase.Length; i++)
            if (cachedBase[i] != null) cachedBase[i].enabled = !held;
    }

    /// <summary>동물 본체가 차지하는 실제 범위 — pivot이 발밑이라, 손에 쥘 때 중심을 맞추는 데 쓴다.</summary>
    public bool TryGetWorldBounds(out Bounds bounds)
    {
        cachedRenderers = BodyRenderers();
        bounds = default;
        bool any = false;
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            var r = cachedRenderers[i];
            if (r == null || !r.enabled) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    /// <summary>렌더러 바운즈에서 현재 스케일을 걷어내 "원본 크기"를 구한다.</summary>
    private float MeasureBaseSize()
    {
        if (!TryGetWorldBounds(out var b)) return 1f;
        float scale = Mathf.Max(0.0001f, transform.lossyScale.x);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        return Mathf.Max(0.0001f, longest / scale);
    }

    /// <summary>전시대 위에서 달리게 한다 (레이스와 같은 애니 규약: Vert/State).</summary>
    public void SetRunning(bool on)
    {
        running = on;
        if (anim == null) return;
        anim.enabled = on;
        if (!on) return;
        anim.applyRootMotion = false;
        anim.SetFloat("Vert", 1f);
        anim.SetFloat("State", 1f);
        anim.speed = 1.1f;
    }

    // 달리기 클립의 루트 모션이 프리팹 루트에 누적돼 뼈대가 전시대 밖으로 달려나간다
    // (레이스에선 RacerMotor가 매 프레임 위치를 다시 써서 상쇄되던 것 — §11 법칙)
    private void LateUpdate()
    {
        if (!running || body == null) return;
        body.localPosition = Vector3.zero;
        body.localRotation = Quaternion.identity;
    }

    /// <summary>선반 제자리로 복귀 (상자/손 어디에 있었든).</summary>
    public void ReturnHome()
    {
        if (InBox != null && InBox.Current == this) InBox.Current = null;
        InBox = null;
        if (InStand != null) InStand.Take();
        SetRunning(false);
        transform.SetParent(HomeSlot, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * ShelfScale;   // 손에서 줄여둔 크기 원복
        SetHeld(false);                                    // 받침대 복원
        if (PickCollider != null) PickCollider.enabled = true;
    }
}
