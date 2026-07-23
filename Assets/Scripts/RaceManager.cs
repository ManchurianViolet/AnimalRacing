using System.Collections.Generic;
using System.Linq;
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
    private bool racing;

    public IReadOnlyList<Racer> Racers => racers;
    /// <summary>이번 라운드 출전 정의 목록 (인덱스 = racerId). 배당 계산용.</summary>
    public IReadOnlyList<AnimalDefinition> Lineup => lineup;
    public TrackPath Path => path;

    private void OnEnable()  => GameEvents.OnPhaseChanged += HandlePhase;
    private void OnDisable() => GameEvents.OnPhaseChanged -= HandlePhase;

    private void HandlePhase(GamePhase phase)
    {
        if (phase == GamePhase.Betting) SpawnRacers();

        racing = phase == GamePhase.Racing;
        foreach (var r in racers)
        {
            var motor = r != null ? r.GetComponent<RacerMotor>() : null;
            if (motor != null) motor.SimEnabled = racing;
        }
    }

    private void SpawnRacers()
    {
        foreach (var r in racers) if (r != null) Destroy(r.gameObject);
        racers.Clear();
        lineup.Clear();
        nextFinishRank = 1;

        var picks = Enumerable.Range(0, animalPool.Length).OrderBy(_ => Random.value).ToList();
        while (picks.Count < config.racerCount)
            picks.Add(Random.Range(0, animalPool.Length));
        picks = picks.Take(config.racerCount).OrderBy(_ => Random.value).ToList();

        for (int i = 0; i < config.racerCount; i++)
        {
            var def = animalPool[picks[i]];
            lineup.Add(def);
            var go = Instantiate(def.prefab, startSlots[i].position,
                                 Quaternion.LookRotation(path.GetTangent(0f)));

            StripAssetControllers(go);

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();

            // 몸통 콜라이더 없으면 렌더러 크기 기반 자동 캡슐 (수제 콜라이더 있으면 스킵)
            bool hasRealCollider = go.GetComponentsInChildren<Collider>()
                .Any(c => !(c is CharacterController));
            if (!hasRealCollider)
            {
                var rends = go.GetComponentsInChildren<Renderer>();
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);

                var cap = go.AddComponent<CapsuleCollider>();
                cap.direction = 2;
                cap.center = go.transform.InverseTransformPoint(b.center);
                cap.radius = Mathf.Min(b.extents.x, b.extents.y) * 0.9f;
                cap.height = b.size.z * 0.95f;
            }

            var racer = go.GetComponent<Racer>();
            if (racer == null) racer = go.AddComponent<Racer>();
            racer.Init(i, def, i + 1);

            var motor = go.GetComponent<RacerMotor>();
            if (motor == null) motor = go.AddComponent<RacerMotor>();
            motor.Init(racer, path, config);
            motor.SimEnabled = false;

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

        foreach (var r in racers)
        {
            r.SetProgress(path.GetProgress(r.transform.position));
            r.SimTick(dt);

            if (!r.HasFinished && r.Progress >= path.TotalLength - 0.1f)
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
        return lead == null ? 0f : Mathf.Clamp01(lead.Progress / path.TotalLength);
    }

    /// <summary>완주 순 최종 순위.</summary>
    public List<Racer> GetFinalRanking() =>
        racers.Where(r => r.HasFinished).OrderBy(r => r.FinishRank).ToList();
}
