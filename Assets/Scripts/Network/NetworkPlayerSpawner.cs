using Photon.Pun;
using UnityEngine;

/// <summary>
/// [5-2] 게임 씬 진입 시 내 아바타 스폰 (씬 배치 플레이어 대체).
/// 온라인: PhotonNetwork.Instantiate (전 컴퓨터에 생성).
/// 오프라인: 일반 Instantiate (싱글도 같은 프리팹/흐름 — 동작 통일).
/// </summary>
public class NetworkPlayerSpawner : MonoBehaviour
{
    [Tooltip("Resources 폴더의 프리팹 이름 (오프라인용 직접 참조도 겸함)")]
    [SerializeField] private GameObject playerPrefab;

    [Tooltip("스폰 위치들 (대기실 안). 접속 순번으로 배정")]
    [SerializeField] private Transform[] spawnPoints;

    private System.Collections.IEnumerator Start()
    {
        // 접속 진행 중이면 방 입장 확정까지 대기 — 씬 전환 직후엔 InRoom이
        // 아직 false일 수 있어서, 그대로 진행하면 오프라인 분기(로컬 전용 아바타)를
        // 잘못 타는 경쟁이 생김 (Bootstrap과 동일한 패턴의 방어)
        float timeout = Time.time + 30f;
        while (PhotonNetwork.IsConnected && !PhotonNetwork.InRoom && Time.time < timeout)
            yield return null;

        bool online = PhotonNetwork.InRoom;
        Debug.Log($"[Spawner] 스폰 분기: {(online ? "온라인(네트워크)" : "오프라인(로컬)")}");

        int idx = online
            ? (PhotonNetwork.LocalPlayer.ActorNumber - 1) % Mathf.Max(1, spawnPoints.Length)
            : 0;

        Vector3 pos = spawnPoints != null && spawnPoints.Length > 0
            ? spawnPoints[idx].position : Vector3.up;
        Quaternion rot = spawnPoints != null && spawnPoints.Length > 0
            ? spawnPoints[idx].rotation : Quaternion.identity;

        if (online)
            PhotonNetwork.Instantiate(playerPrefab.name, pos, rot);
        else
            Instantiate(playerPrefab, pos, rot);   // 오프라인 — 셋업이 "내 것"으로 처리
    }
}
