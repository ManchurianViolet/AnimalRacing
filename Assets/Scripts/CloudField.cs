using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// 구름 컨베이어 — Clouds 자식 구름들을 한 방향으로 흘려보내고,
/// 배치 영역을 벗어난 구름은 반대편 가장자리에서 모습(횡위치/높이/크기/회전/속도)을
/// 새로 뽑아 재등장시킨다. 보기엔 "생성됐다 사라지는" 절차적 하늘이지만
/// 실제론 재활용이라 스폰/GC 비용 0.
/// 진행 방향(+/-)은 방 이름 해시로 결정 — 방 파질 때 확정, 전 클라이언트 동일 (동기화 통신 불필요).
/// 오프라인은 판마다 랜덤. 순수 장식이라 게임 로직과 무관.
/// </summary>
public class CloudField : MonoBehaviour
{
    [Header("이동")]
    [Tooltip("표류 축 (기본: 월드 Z) — 실제 방향(+/-)은 방마다 결정됨")]
    public Vector3 driftAxis = Vector3.forward;
    [Tooltip("기본 속도 (m/s)")] public float speed = 4f;
    [Tooltip("구름별 속도 편차 비율 (0.35 = ±35%) — 시차감 연출")]
    [Range(0f, 0.9f)] public float speedJitter = 0.35f;

    [Header("재등장")]
    [Tooltip("배치 영역 가장자리에서 이만큼 더 나가면(시야 밖) 반대편 재등장")]
    public float margin = 60f;
    [Tooltip("재등장 시 원본 대비 크기 배율 범위")]
    public Vector2 scaleRange = new Vector2(0.7f, 1.4f);
    [Tooltip("재등장 시 높이 흔들림 (±m)")] public float heightJitter = 6f;

    private class Cloud
    {
        public Transform t;
        public float speedMul;
        public Vector3 baseScale;
        public float baseY;
    }

    private readonly List<Cloud> clouds = new();
    private Vector3 dir;        // 부호 포함 정규화 진행 방향
    private Vector3 lateralAxis;
    private float minAlong, maxAlong, minLat, maxLat;

    private void Start()
    {
        dir = driftAxis.normalized * ChooseSign();

        // 진행축과 수직인 횡축 (수직 표류로 바꿔도 퇴화하지 않게 폴백)
        lateralAxis = Vector3.Cross(Vector3.up, dir);
        if (lateralAxis.sqrMagnitude < 1e-4f) lateralAxis = Vector3.Cross(Vector3.forward, dir);
        lateralAxis.Normalize();

        // 현재 배치에서 영역 역산 — 씬에 깔린 구름들이 곧 스폰 영역 정의
        minAlong = minLat = float.MaxValue;
        maxAlong = maxLat = float.MinValue;
        foreach (Transform child in transform)
        {
            float along = Vector3.Dot(child.position, dir);
            float lat = Vector3.Dot(child.position, lateralAxis);
            minAlong = Mathf.Min(minAlong, along); maxAlong = Mathf.Max(maxAlong, along);
            minLat = Mathf.Min(minLat, lat); maxLat = Mathf.Max(maxLat, lat);

            clouds.Add(new Cloud
            {
                t = child,
                speedMul = 1f + Random.Range(-speedJitter, speedJitter),
                baseScale = child.localScale,
                baseY = child.position.y,
            });
        }
    }

    /// <summary>방 이름 해시 → 방향 부호. string.GetHashCode는 런타임별로 달라질 수 있어 수제 해시.</summary>
    private float ChooseSign()
    {
        if (PhotonNetwork.InRoom && !string.IsNullOrEmpty(PhotonNetwork.CurrentRoom.Name))
        {
            int h = 0;
            foreach (char c in PhotonNetwork.CurrentRoom.Name) h = h * 31 + c;
            return (h & 1) == 0 ? 1f : -1f;
        }
        return Random.value < 0.5f ? 1f : -1f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        foreach (var c in clouds)
        {
            c.t.position += dir * (speed * c.speedMul * dt);
            if (Vector3.Dot(c.t.position, dir) > maxAlong + margin) Respawn(c);
        }
    }

    /// <summary>시야 밖으로 나간 구름을 반대편 가장자리 너머에서 새 모습으로 재등장.</summary>
    private void Respawn(Cloud c)
    {
        float along = minAlong - margin * Random.Range(0.5f, 1f);   // 진입 시점 분산 — 줄지어 들어오는 것 방지
        float lat = Random.Range(minLat, maxLat);
        float y = c.baseY + Random.Range(-heightJitter, heightJitter);

        Vector3 pos = dir * along + lateralAxis * lat;
        pos.y = y;
        c.t.position = pos;

        c.t.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        c.t.localScale = c.baseScale * Random.Range(scaleRange.x, scaleRange.y);
        c.speedMul = 1f + Random.Range(-speedJitter, speedJitter);
    }
}
