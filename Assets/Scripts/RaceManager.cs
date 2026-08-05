using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// 레이스 총괄 (리뉴얼). 동물끼리 충돌 없음 — 스폰 시 전 쌍 IgnoreCollision.
/// 탈락 없음, 스킬 없음. 순위 = TrackPath 투영 진행도.
/// </summary>
public class RaceManager : MonoBehaviour
{
    [SerializeField] private GameConfig config;
    [SerializeField] private TrackPath path;
    [SerializeField] private AnimalDefinition[] animalPool;
    [SerializeField] private Transform[] startSlots;

    private readonly List<Racer> racers = new();
    private readonly List<AnimalDefinition> lineup = new();
    private int nextFinishRank = 1;
    private int eliminatedCount;   // 처형 무전기 탈락자 수 (순위를 최하위부터 배정)
    private bool racing;

    /// <summary>완주 거리 = 트랙 길이 × 랩 수 (진행도는 랩 누적).</summary>
    public float RaceLength => path.TotalLength * Mathf.Max(1, config.lapCount);

    public IReadOnlyList<Racer> Racers => racers;
    /// <summary>이번 라운드 출전 정의 목록 (인덱스 = racerId). 배당 계산용.</summary>
    public IReadOnlyList<AnimalDefinition> Lineup => lineup;
    public TrackPath Path => path;

    private void OnEnable()  => GameEvents.OnPhaseChanged += HandlePhase;
    private void OnDisable() => GameEvents.OnPhaseChanged -= HandlePhase;

    /// <summary>내가 시뮬 권위자인가 (오프라인 또는 방장).</summary>
    private bool IsAuthority => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;

    private void HandlePhase(GamePhase phase)
    {
        if (phase == GamePhase.Betting)
        {
            if (IsAuthority) SpawnRacers();
            else
            {
                // 클라: 스폰/파괴는 네트워크가 해줌. 여기선 파괴된 잔재만 정리.
                // (전체 Clear 금지 — 페이즈 방송이 스폰 메시지보다 늦게 오면
                //  방금 등록된 새 동물들까지 지워버리는 경쟁이 생김)
                racers.RemoveAll(r => r == null);
                lineup.Clear();
                nextFinishRank = 1;
                eliminatedCount = 0;
            }
        }

        racing = phase == GamePhase.Racing && IsAuthority;
        // 정산 중에도 모터는 살려둔다 — 완주자는 모터의 완주 분기(산개/정지 연출)만 탄다.
        // 여기서 꺼버리면 마지막 완주자가 감속을 못 받고 무마찰로 영원히 미끄러진다.
        bool motorsAlive = IsAuthority &&
            (phase == GamePhase.Racing || phase == GamePhase.Settlement);
        foreach (var r in racers)
        {
            var motor = r != null ? r.GetComponent<RacerMotor>() : null;
            if (motor != null) motor.SimEnabled = motorsAlive;
        }
    }

    private void SpawnRacers()
    {
        foreach (var r in racers)
        {
            if (r == null) continue;
            if (PhotonNetwork.InRoom && r.GetComponent<PhotonView>() != null)
                PhotonNetwork.Destroy(r.gameObject);   // 전 컴퓨터에서 제거
            else
                Destroy(r.gameObject);
        }
        racers.Clear();
        lineup.Clear();
        nextFinishRank = 1;
        eliminatedCount = 0;

        var picks = Enumerable.Range(0, animalPool.Length).OrderBy(_ => Random.value).ToList();
        while (picks.Count < config.racerCount)
            picks.Add(Random.Range(0, animalPool.Length));
        picks = picks.Take(config.racerCount).OrderBy(_ => Random.value).ToList();

        for (int i = 0; i < config.racerCount; i++)
        {
            var def = animalPool[picks[i]];
            lineup.Add(def);

            Vector3 pos = startSlots[i].position;
            Quaternion rot = Quaternion.LookRotation(path.GetTangent(0f));

            // 네트워크: 방의 모든 컴퓨터에 생성 + 정체(레이서ID/동물/번호)를 데이터로 동봉
            GameObject go = PhotonNetwork.InRoom
                ? PhotonNetwork.Instantiate(def.prefab.name, pos, rot, 0,
                      new object[] { i, picks[i], i + 1 })
                : Instantiate(def.prefab, pos, rot);

            StripAssetControllers(go);

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();

            EnsureBodyCollider(go);

            var racer = go.GetComponent<Racer>();
            if (racer == null) racer = go.AddComponent<Racer>();
            racer.Init(i, def, i + 1);
            racer.SetTrackLength(RaceLength);   // 진행률(스킬 발동 지점)은 랩 누적 기준
            ApplyFrictionless(go);
            go.GetComponentInChildren<RacerNumberPlate>()?.Apply(i + 1);

            var motor = go.GetComponent<RacerMotor>();
            if (motor == null) motor = go.AddComponent<RacerMotor>();
            motor.Init(racer, path, config, this);
            motor.SimEnabled = false;

            EnsureDustFx(go, racer);

            racers.Add(racer);
        }

        // 동물끼리 충돌 전면 비활성 (리뉴얼 규칙: 몸싸움 없음)
        // 1차: "Racer" 레이어가 있으면 레이어 자체를 상호 무시 (가장 확실)
        int racerLayer = LayerMask.NameToLayer("Racer");
        if (racerLayer >= 0)
        {
            Physics.IgnoreLayerCollision(racerLayer, racerLayer, true);
            foreach (var r in racers)
                SetLayerRecursive(r.gameObject, racerLayer);
        }
        else
        {
            Debug.LogWarning("[RaceManager] 'Racer' 레이어가 없습니다. " +
                "Project Settings > Tags and Layers에서 레이어를 추가하면 충돌 무시가 더 안정적입니다. " +
                "임시로 쌍별 IgnoreCollision을 사용합니다.");
            StartCoroutine(IgnorePairsNextFrame());
        }

        // 플레이어와 동물 충돌 전면 무시 (기획 확정: 유령 통과)
        IgnorePlayerCollisions();
    }

    private void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    // 폴백: CharacterController 등이 프레임 끝에 제거된 "다음 프레임"에 쌍별 등록
    private System.Collections.IEnumerator IgnorePairsNextFrame()
    {
        yield return null;
        for (int a = 0; a < racers.Count; a++)
            for (int b = a + 1; b < racers.Count; b++)
            {
                if (racers[a] == null || racers[b] == null) continue;
                foreach (var ca in racers[a].GetComponentsInChildren<Collider>())
                    foreach (var cb in racers[b].GetComponentsInChildren<Collider>())
                        if (ca != null && cb != null)
                            Physics.IgnoreCollision(ca, cb, true);
            }
    }

    /// <summary>플레이어 아바타와 동물 충돌 전면 무시 (기획 확정: 유령 통과).
    /// 동물 스폰 직후와 아바타 스폰 시(매치 중 재접속 복귀 포함) 양쪽에서 호출된다. 중복 호출 무해.</summary>
    public void IgnorePlayerCollisions()
    {
        StartCoroutine(IgnorePlayersNextFrame());
    }

    // 다음 프레임 등록: 에셋 잔여 컴포넌트가 프레임 끝에 제거되는 타이밍 회피 (쌍별 폴백과 동일)
    private System.Collections.IEnumerator IgnorePlayersNextFrame()
    {
        yield return null;
        foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
            foreach (var racer in racers)
            {
                if (racer == null) continue;
                foreach (var col in racer.GetComponentsInChildren<Collider>())
                    if (col != null && col != cc) Physics.IgnoreCollision(cc, col, true);
            }
    }

    private void StripAssetControllers(GameObject go)
    {
        string[] byName = { "CreatureMover", "MovePlayerInput", "CharacterMover" };
        foreach (var n in byName)
        {
            var c = go.GetComponent(n);
            if (c != null) Destroy(c);
        }
        var cc = go.GetComponent<CharacterController>();
        if (cc != null) Destroy(cc);
    }

    private void Update()
    {
        if (!racing) return;
        float dt = Time.deltaTime;

        UpdateSkillContext();

        foreach (var r in racers)
        {
            // 랩 누적 진행도 (이음새 자동 이월) — 순위/완주/스킬 진행률 전부 이 값 기준
            float newProg = path.GetDistanceNear(r.transform.position, r.Progress);
            if (config.debugProgressLog)
            {
                if (float.IsNaN(newProg))
                    Debug.LogError($"[투영] {r.DisplayName} 진행도 NaN! pos={r.transform.position}");
                else if (Mathf.Abs(newProg - r.Progress) > 8f)
                    Debug.LogWarning($"[투영점프] {r.DisplayName} {r.Progress:F1}m → {newProg:F1}m");
            }
            r.SetProgress(newProg);
            r.SimTick(dt);

            if (!r.HasFinished && r.Progress >= RaceLength - 0.1f)
                r.MarkFinished(nextFinishRank++);
        }

        if (racers.All(r => r.HasFinished))
        {
            racing = false;
            GameManager.Instance.Settle();
        }
    }

    // ---- 조회 ----
    public Racer GetRacer(int id) => racers.FirstOrDefault(r => r.RacerId == id);

    public Racer GetLastPlaceRacer() =>
        racers.Where(r => !r.HasFinished).OrderBy(r => r.Progress).FirstOrDefault();

    public float GetLeaderProgressRatio()
    {
        var lead = racers.OrderByDescending(r => r.Progress).FirstOrDefault();
        return lead == null ? 0f : Mathf.Clamp01(lead.Progress / RaceLength);
    }

    /// <summary>[호스트/처형 무전기] 그 순간의 꼴등을 탈락시킨다. 순위는 최하위부터 배정.</summary>
    public bool ExecuteLastPlace()
    {
        var victim = GetLastPlaceRacer();
        if (victim == null) return false;

        int rank = racers.Count - eliminatedCount;   // 첫 탈락 = 꼴찌 확정
        eliminatedCount++;
        victim.Eliminate(rank);
        GameEvents.RaiseSkillProc($"무전 한 방! {victim.DisplayName}이(가) 레이스에서 끌려 나갔다!");
        return true;
    }

    // ================= 네트워크 (클라이언트 측 등록) =================

    /// <summary>
    /// [클라 전용] 네트워크로 스폰된 동물을 표시용으로 등록.
    /// 시뮬 없음: 위치/애니는 Photon이 받아쓰기, 여기선 정체(Definition)와 외형 정리만.
    /// NetworkRacerSetup이 호출.
    /// </summary>
    public void RegisterNetworkRacer(GameObject go, int racerId, int animalIdx, int postNumber)
    {
        // 지난 라운드 잔재/중복 정리 (같은 ID의 죽은 참조 제거)
        racers.RemoveAll(r => r == null || r.RacerId == racerId);

        StripAssetControllers(go);
        EnsureBodyCollider(go);   // 아이템 조준(레이캐스트)에 필요 — 클라에도 필수

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;   // 물리 계산 금지 — 받아쓰기 전용

        var motor = go.GetComponent<RacerMotor>();
        if (motor != null) motor.enabled = false;

        var racer = go.GetComponent<Racer>();
        if (racer == null) racer = go.AddComponent<Racer>();
        racer.Init(racerId, animalPool[animalIdx], postNumber);
        racer.SetTrackLength(RaceLength);
        ApplyFrictionless(go);
        go.GetComponentInChildren<RacerNumberPlate>()?.Apply(postNumber);
        EnsureDustFx(go, racer);   // 먼지는 순수 로컬 연출 — 클라도 자기 화면에서 직접 재생

        int racerLayer = LayerMask.NameToLayer("Racer");
        if (racerLayer >= 0) SetLayerRecursive(go, racerLayer);

        racers.Add(racer);
        racers.Sort((a, b) => a.RacerId.CompareTo(b.RacerId));

        // 클라에도 플레이어-동물 충돌 무시 필요 (미러 동물은 kinematic이지만 CC가 밀려남)
        IgnorePlayerCollisions();
    }

    /// <summary>부스트 먼지구름 연출 부착 (호스트·클라 공용, 순수 로컬 재생).</summary>
    private void EnsureDustFx(GameObject go, Racer racer)
    {
        var fx = go.GetComponent<BoostDustFx>();
        if (fx == null) fx = go.AddComponent<BoostDustFx>();
        fx.Init(racer, config);
    }

    /// <summary>
    /// 몸통 콜라이더 보장: 없으면 렌더러 크기 기반 자동 캡슐 — 바닥을 발끝에 정렬 (수제 콜라이더 있으면 스킵).
    /// 호스트(물리/조준)와 클라(조준) 공용.
    /// </summary>
    private static void EnsureBodyCollider(GameObject go)
    {
        // 꺼진 콜라이더는 없는 셈 (장식 오브젝트의 콜라이더는 체크 해제로 무시 가능)
        bool hasRealCollider = go.GetComponentsInChildren<Collider>()
            .Any(c => c.enabled && !(c is CharacterController));
        if (hasRealCollider) return;

        // 몸통 크기는 동물 본체(SkinnedMesh)만으로 산출 — 번호판 큐브나
        // 월드 TMP(렉트가 거대함) 같은 부속이 끼면 캡슐이 왜곡돼 바닥을 뚫음
        var all = go.GetComponentsInChildren<Renderer>();
        var rends = all.Where(r => r is SkinnedMeshRenderer).ToArray();
        if (rends.Length == 0)
            rends = all.Where(r => r.GetComponent<TMPro.TMP_Text>() == null).ToArray();
        if (rends.Length == 0) return;

        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);

        // 바닥이 평평한 박스는 도로 타일 이음새의 유령 모서리 충돌에 걸려 급정지한다.
        // 캡슐의 둥근 바닥은 이음새를 썰매처럼 타넘음 (수제 캡슐인 사슴으로 검증됨).
        // 반지름=몸 반높이: 캡슐 바닥이 발끝과 일치(다리 파묻힘 방지)하고 조준 표적도 몸통까지.
        // 바닥은 루트 아래 0.05까지만 인정 — 바인드 포즈 메시가 루트 밑으로 뻗은 모델(펭귄 -0.3)이
        // 그대로면 그만큼 공중에 떠 보인다.
        float rootY = go.transform.position.y;
        float bottom = Mathf.Max(b.min.y, rootY - 0.05f);
        float radius = Mathf.Min(b.extents.y, (b.max.y - bottom) * 0.5f) * 0.9f;
        var capCenter = b.center;
        capCenter.y = bottom + radius;

        // 콜라이더 radius/height는 로컬 단위 — 루트가 스케일된 프리팹(치킨/고양이 1.5배)은
        // 월드 값을 그대로 넣으면 스케일이 겹으로 곱해져 캡슐이 커지고 그만큼 몸이 떠오른다
        var s = go.transform.lossyScale;
        var cap = go.AddComponent<CapsuleCollider>();
        cap.direction = 2;
        cap.center = go.transform.InverseTransformPoint(capCenter);
        cap.radius = radius / Mathf.Max(1e-4f, Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y)));
        cap.height = b.size.z * 0.95f / Mathf.Max(1e-4f, Mathf.Abs(s.z));
    }

    private static PhysicsMaterial frictionlessMat;

    /// <summary>
    /// 동물끼리 밀착 시 마찰 쐐기(교착)로 박히지 않게 — 무마찰 재질 적용.
    /// 부딪히되 미끄러져 분리되는 몸싸움.
    /// </summary>
    private static void ApplyFrictionless(GameObject go)
    {
        if (frictionlessMat == null)
        {
            frictionlessMat = new PhysicsMaterial("RacerSlick")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };
        }
        foreach (var c in go.GetComponentsInChildren<Collider>())
            if (c.enabled) c.material = frictionlessMat;
    }

    /// <summary>
    /// 전역 시야가 필요한 스킬 처리 (매 시뮬 틱, 호스트 전용):
    /// [개] 꼴등 판정 세팅, [호랑이] 습격 발동 (최근접 탐색 + 스턴).
    /// </summary>
    private void UpdateSkillContext()
    {
        Racer last = null;
        foreach (var r in racers)
        {
            if (r == null || r.HasFinished) continue;
            if (last == null || r.Progress < last.Progress) last = r;
        }
        foreach (var r in racers)
        {
            if (r == null) continue;
            r.SetLastPlace(r == last);
        }

        foreach (var tiger in racers)
        {
            if (tiger == null || tiger.HasFinished) continue;
            if (!tiger.TryConsumeAmbush()) continue;

            // 사거리 무제한 — 가장 가까운 주자를 무조건 문다
            Racer prey = null;
            float best = float.MaxValue;
            foreach (var other in racers)
            {
                if (other == null || other == tiger || other.HasFinished) continue;
                float d = Mathf.Abs(other.Progress - tiger.Progress);
                if (d < best) { best = d; prey = other; }
            }

            if (prey != null)
            {
                prey.AddEffect(new StatusEffect(StatusEffectType.Stun, SkillTuning.AmbushStun, 0f));
                GameEvents.RaiseSkillProc(prey.IsStunned
                    ? $"{tiger.DisplayName}이(가) {prey.DisplayName}을(를) 덮쳤다!"
                    : $"{tiger.DisplayName}이(가) {prey.DisplayName}을(를) 덮쳤지만... 꿈쩍도 안 한다!");
            }
        }
    }

    /// <summary>[클라] 호스트가 방송한 완주 순위 일괄 반영 (정산판용).</summary>
    public void ApplyNetworkRanking(int[] orderedRacerIds)
    {
        for (int i = 0; i < orderedRacerIds.Length; i++)
        {
            var r = GetRacer(orderedRacerIds[i]);
            if (r != null) r.ApplyNetworkFinish(i + 1);
        }
    }

    /// <summary>완주 순 최종 순위.</summary>
    public List<Racer> GetFinalRanking() =>
        racers.Where(r => r.HasFinished).OrderBy(r => r.FinishRank).ToList();
}
