using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [타이틀 전용] 원형 트랙 위 동물 퍼레이드 연출.
/// - 트랙 링 자체는 씬에 구워진 오브젝트다 (TitleTrack 자식 Asphalt/LineOuter/LineInner —
///   유저가 에디터에서 보면서 주변을 직접 꾸밀 수 있게 v7에서 런타임 생성 → 베이크로 전환.
///   ⚠ 링을 옮기면 radius/트랜스폼과 어긋나 동물이 링 밖을 돈다 — 옮길 거면 radius도 맞출 것)
/// - 동물은 Resources의 레이스 프리팹을 빌려 쓰되, 게임플레이 컴포넌트
///   (Photon/Racer/RacerMotor/물리/번호판)를 전부 떼어 순수 장식으로 굴린다
///   — 타이틀 씬엔 RaceManager가 없어서 그대로 두면 NRE가 난다.
/// - 애니는 레이스와 같은 규약(Vert/State 플로트)으로 구동, 속도 편차로 자연스러운 추월 연출.
/// </summary>
public class TitleTrackShow : MonoBehaviour
{
    [Header("트랙 모양 (씬에 구워진 링과 맞아야 함)")]
    [Tooltip("트랙 중심 반지름 (m) — 동물이 도는 궤도")]
    [SerializeField] private float radius = 10f;
    [Tooltip("트랙 폭 (m) — 레인 산개 범위")]
    [SerializeField] private float width = 4f;

    [Header("동물 퍼레이드")]
    [Tooltip("동시에 도는 동물 수 (7종 중 무작위)")]
    [SerializeField] private int animalCount = 5;
    [Tooltip("주행 속도 범위 (m/s) — 편차가 추월 연출을 만든다")]
    [SerializeField] private float minSpeed = 4.5f;
    [SerializeField] private float maxSpeed = 7f;
    [Tooltip("동물 크기 배율 — 배경이라 멀어서 실물 크기면 잘 안 보임")]
    [SerializeField] private float runnerScale = 1.7f;

    [Header("부스트 쇼 — 랜덤 시간마다 랜덤 동물이 먼지 뿜으며 질주")]
    [Tooltip("먼지 튜닝 공급용 GameConfig 에셋 — 비우면 코드 기본값으로 동작")]
    [SerializeField] private GameConfig config;
    [Tooltip("부스트 발동 간격 (초, 이 범위에서 랜덤)")]
    [SerializeField] private float boostIntervalMin = 3f;
    [SerializeField] private float boostIntervalMax = 8f;
    [Tooltip("부스트 중 속도 배율")]
    [SerializeField] private float boostMultiplier = 1.9f;
    [Tooltip("부스트 지속 시간 (초)")]
    [SerializeField] private float boostDuration = 2.5f;

    private static readonly string[] PrefabNames =
        { "말프리팹", "디어프리팹", "호랑이프리팹", "고양이프리팹", "개프리팹", "치킨프리팹", "펭귄프리랩" };

    private class Runner
    {
        public Transform tf;     // 홀더 (우리가 링 위에서 움직임)
        public Transform body;   // 프리팹 루트 — 달리기 클립의 루트 모션이 여기 누적되므로 매 프레임 0으로 되돌림
        public Animator anim;
        public BoostDustFx fx;   // 부스트 먼지 (레이스와 같은 연출 재사용)
        public float angle;      // 현재 각도 (rad)
        public float speed;      // m/s
        public float lane;       // 중심 반지름 대비 오프셋 (안/바깥 레인)
        public float boostUntil; // 이 시각까지 부스트 배속
    }

    private readonly List<Runner> runners = new();
    private float nextBoostAt;

    private void Start()
    {
        if (config == null)   // 타이틀엔 GameManager가 없음 — 미배선이면 코드 기본값으로
            config = ScriptableObject.CreateInstance<GameConfig>();
        SpawnAnimals();
        nextBoostAt = Time.time + Random.Range(boostIntervalMin, boostIntervalMax);
    }

    private void Update()
    {
        // 랜덤 시간마다 랜덤 동물 부스트 — 먼지 뿜으며 질주
        if (Time.time >= nextBoostAt && runners.Count > 0)
        {
            var pick = runners[Random.Range(0, runners.Count)];
            pick.boostUntil = Time.time + boostDuration;
            if (pick.fx != null) pick.fx.Play(boostDuration);
            nextBoostAt = Time.time + Random.Range(boostIntervalMin, boostIntervalMax);
        }

        foreach (var r in runners)
        {
            bool boosted = Time.time < r.boostUntil;
            float speed = r.speed * (boosted ? boostMultiplier : 1f);

            float laneRadius = radius + r.lane;
            r.angle += (speed / laneRadius) * Time.deltaTime;

            Vector3 pos = transform.position
                + new Vector3(Mathf.Sin(r.angle), 0f, Mathf.Cos(r.angle)) * laneRadius;
            Vector3 fwd = new Vector3(Mathf.Cos(r.angle), 0f, -Mathf.Sin(r.angle));
            r.tf.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));

            if (r.anim != null)
            {
                r.anim.SetFloat("Vert", 1f);
                r.anim.SetFloat("State", 1f);
                r.anim.speed = Mathf.MoveTowards(r.anim.speed, boosted ? 1.6f : 1.1f, 2f * Time.deltaTime);
            }
        }
    }

    // 달리기 클립의 루트 모션이 프리팹 루트에 누적돼 뼈대가 홀더에서 이탈한다
    // (본게임에선 RacerMotor/TransformView가 매 프레임 위치를 다시 써서 상쇄되던 것).
    // 애니메이터가 본을 쓴 뒤(LateUpdate)에 로컬 오프셋을 0으로 되돌려 고삐를 채운다.
    private void LateUpdate()
    {
        foreach (var r in runners)
        {
            if (r.body == null) continue;
            r.body.localPosition = Vector3.zero;
            r.body.localRotation = Quaternion.identity;
        }
    }

    // ---------- 동물 ----------
    // (트랙 링 생성 코드는 v7에서 제거 — 링은 씬에 구워진 오브젝트, 헤더 주석 참조)

    private void SpawnAnimals()
    {
        // 이름 섞어서 앞에서 count개 (중복 없음)
        var names = new List<string>(PrefabNames);
        for (int i = 0; i < names.Count; i++)
        {
            int j = Random.Range(i, names.Count);
            (names[i], names[j]) = (names[j], names[i]);
        }

        int count = Mathf.Clamp(animalCount, 1, names.Count);
        for (int i = 0; i < count; i++)
        {
            var prefab = Resources.Load<GameObject>(names[i]);
            if (prefab == null) { Debug.LogWarning("[타이틀트랙] 프리팹 없음: " + names[i]); continue; }

            // 비활성 홀더 밑에서 생성 → Awake가 돌기 전에 게임플레이 컴포넌트를 떼어낸다
            var holder = new GameObject("Runner_" + names[i]);
            holder.transform.SetParent(transform, false);
            holder.transform.localScale = Vector3.one * runnerScale;
            holder.SetActive(false);

            var go = Instantiate(prefab, holder.transform);
            StripGameplay(go);
            holder.SetActive(true);

            var anim = go.GetComponentInChildren<Animator>(true);
            if (anim != null) { anim.applyRootMotion = false; anim.enabled = true; }

            // 부스트 먼지 — 레이스와 같은 컴포넌트 재사용 (racer 없이도 동작, Play는 우리가 직접 호출)
            var fx = go.AddComponent<BoostDustFx>();
            fx.Init(null, config);

            runners.Add(new Runner
            {
                tf = holder.transform,
                body = go.transform,
                anim = anim,
                fx = fx,
                angle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f),
                speed = Random.Range(minSpeed, maxSpeed),
                lane = Mathf.Lerp(-width * 0.28f, width * 0.28f, count <= 1 ? 0.5f : i / (float)(count - 1)),
            });
        }
    }

    /// <summary>레이스용 컴포넌트 제거 — 스크립트 먼저, 물리는 마지막 (RequireComponent 순서).</summary>
    private void StripGameplay(GameObject go)
    {
        RemoveAll<RacerMotor>(go);
        RemoveAll<Racer>(go);
        RemoveAll<NetworkRacerSetup>(go);
        RemoveAll<RacerNumberPlate>(go);
        RemoveAll<Photon.Pun.PhotonAnimatorView>(go);
        RemoveAll<Photon.Pun.PhotonTransformView>(go);
        RemoveAll<Photon.Pun.PhotonView>(go);
        RemoveAll<Rigidbody>(go);
        foreach (var c in go.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(c);
        // 번호판 큐브/텍스트 오브젝트도 정리 (이름 규약: Plate*)
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (t != null && t.name.StartsWith("Plate"))
                DestroyImmediate(t.gameObject);
    }

    private void RemoveAll<T>(GameObject go) where T : Component
    {
        foreach (var c in go.GetComponentsInChildren<T>(true))
            DestroyImmediate(c);
    }
}
