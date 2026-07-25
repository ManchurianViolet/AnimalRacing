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

    private void Start()
    {
        int idx = PhotonNetwork.InRoom
            ? (PhotonNetwork.LocalPlayer.ActorNumber - 1) % Mathf.Max(1, spawnPoints.Length)
            : 0;

        Vector3 pos = spawnPoints != null && spawnPoints.Length > 0
            ? spawnPoints[idx].position : Vector3.up;
        Quaternion rot = spawnPoints != null && spawnPoints.Length > 0
            ? spawnPoints[idx].rotation : Quaternion.identity;

        if (PhotonNetwork.InRoom)
            PhotonNetwork.Instantiate(playerPrefab.name, pos, rot);
        else
            Instantiate(playerPrefab, pos, rot);   // 오프라인 — IsMine이 true라 동일 흐름
    }
}
