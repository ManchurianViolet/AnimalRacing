using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [원숭이] 바나나 뿌리기 연출 — 발동하면 달리면서 뒤로 껍질 5개를 흩뿌리고,
/// 껍질은 트랙에 계속 남는다 (유저 확정). 밟혀서 소멸할 때만 치운다.
/// [멀티] 발동 감지 = 전 클라로 중계되는 스킬 사건(OnSkillEvent) 구독 — 통신 0.
/// 투척 시각·오프셋은 SkillTuning의 결정적 함수라 호스트 판정 좌표(RaceManager)와
/// 같은 그림이 나온다 (호스트는 완전 일치, 클라는 TransformView 미러 오차 수십 cm 이내).
/// 밟힘 소멸은 BananaSlip 사건(racerId=희생자)을 받아 희생자 근처 껍질을 제거.
/// 모델: GameConfig.bananaPeelPrefab (GltfBaker 산출물) — 비면 노란 캡슐 폴백.
/// RaceManager가 원숭이(Banana 스킬)에만 부착.
/// </summary>
public class BananaTrailFx : MonoBehaviour
{
    private Racer racer;
    private GameConfig config;
    private RaceManager race;

    private Transform container;        // 씬 루트의 껍질 보관함 (몸에 붙이면 껍질이 따라와 버림)
    private readonly List<Transform> peels = new();
    private int dropRemaining;          // 남은 투척 수 (0 = 대기)
    private int dropIndex;              // 투척 순번 (결정적 오프셋 인덱스)
    private float dropTimer;

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        this.config = config;
        race = FindFirstObjectByType<RaceManager>();
    }

    private void OnEnable() => GameEvents.OnSkillEvent += HandleSkillEvent;
    private void OnDisable() => GameEvents.OnSkillEvent -= HandleSkillEvent;

    private void HandleSkillEvent(SkillFeedEvent evt, int rid)
    {
        if (racer == null) return;

        if (evt == SkillFeedEvent.Banana && rid == racer.RacerId)
        {
            dropRemaining = SkillTuning.BananaCount;
            dropIndex = 0;
            dropTimer = 0f;   // 첫 개는 즉시 (호스트 판정 스케줄과 동일)
        }
        else if (evt == SkillFeedEvent.BananaSlip)
        {
            // 희생자에게 가장 가까운 시각 껍질 제거 — 판정(호스트)과 시각(로컬)의 좌표가
            // 약간 어긋날 수 있어 좌표 일치가 아니라 최근접 탐색 (반경 상한 2.5m)
            var victim = race != null ? race.GetRacer(rid) : null;
            if (victim == null) return;
            int best = -1; float bestSq = 2.5f * 2.5f;
            for (int i = 0; i < peels.Count; i++)
            {
                if (peels[i] == null) continue;
                Vector3 d = peels[i].position - victim.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude < bestSq) { bestSq = d.sqrMagnitude; best = i; }
            }
            if (best >= 0)
            {
                Destroy(peels[best].gameObject);
                peels.RemoveAt(best);
            }
        }
    }

    private void Update()
    {
        if (dropRemaining <= 0) return;
        if (racer == null || racer.HasFinished) { dropRemaining = 0; return; }

        dropTimer -= Time.deltaTime;
        if (dropTimer > 0f) return;
        dropTimer += SkillTuning.BananaSpreadSeconds / SkillTuning.BananaCount;

        SpawnPeel(dropIndex++);
        dropRemaining--;
    }

    private void SpawnPeel(int index)
    {
        Vector3 pos = transform.TransformPoint(SkillTuning.BananaLocalOffset(index));

        // 지면 스냅 — 자기 몸(콜라이더)을 맞으면 안 되므로 전체 히트에서 지면만 고른다
        var hits = Physics.RaycastAll(pos + Vector3.up * 2f, Vector3.down, 8f,
            ~0, QueryTriggerInteraction.Ignore);
        float bestY = float.NegativeInfinity; bool found = false;
        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<Racer>() != null) continue;   // 동물 몸 제외
            if (h.collider is CharacterController) continue;                  // 플레이어 제외
            if (h.point.y > bestY) { bestY = h.point.y; found = true; }
        }
        if (found) pos.y = bestY + 0.02f;

        GameObject peel;
        if (config != null && config.bananaPeelPrefab != null)
        {
            peel = Instantiate(config.bananaPeelPrefab);
            foreach (var c in peel.GetComponentsInChildren<Collider>())
                Destroy(c);   // 소품 콜라이더 제거 (§11 — CC·동물과 부딪히면 안 됨)

            // 크기 정규화 — 원본 스케일이 제각각이라 "목표 길이" 방식 (§11 glTF 법칙)
            float target = config.bananaVisualSize;
            var rends = peel.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0 && target > 0.001f)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                float longest = Mathf.Max(b.size.x, b.size.z, 0.001f);
                peel.transform.localScale *= target / longest;
            }
        }
        else
        {
            // 폴백: 납작한 노란 캡슐
            peel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(peel.GetComponent<Collider>());
            peel.transform.localScale = new Vector3(0.4f, 0.05f, 0.25f);
            var mr = peel.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(1f, 0.85f, 0.2f);
            mr.sharedMaterial = mat;
        }

        peel.name = "바나나껍질";
        peel.transform.SetParent(EnsureContainer(), true);
        peel.transform.position = pos;
        peel.transform.rotation = Quaternion.Euler(0f, index * 73f, 0f);   // 결정적 회전 (전 클라 동일)

        // 바닥 정렬 — 피벗이 메시 바닥과 어긋난 모델이라 피벗 기준으로 놓으면 공중에 뜬다.
        // 배치 후 실제 렌더러 바운즈의 최하단을 지면에 맞춰 내린다 (모델이 바뀌어도 자동)
        if (found)
        {
            var prs = peel.GetComponentsInChildren<Renderer>();
            if (prs.Length > 0)
            {
                float bottom = float.PositiveInfinity;
                foreach (var r in prs) bottom = Mathf.Min(bottom, r.bounds.min.y);
                peel.transform.position += Vector3.up * (pos.y - 0.01f - bottom);
            }
        }
        peels.Add(peel.transform);
    }

    private Transform EnsureContainer()
    {
        if (container == null)
            container = new GameObject("바나나껍질(연출)").transform;
        return container;
    }

    // 원숭이가 사라질 때(새 라운드 스폰 정리) 남은 껍질도 함께 정리 —
    // "레이스 끝까지 잔존"은 이 소멸 시점이 라운드 교체라서 자연 성립한다
    private void OnDestroy()
    {
        if (container != null) Destroy(container.gameObject);
    }
}
