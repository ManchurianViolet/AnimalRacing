using Photon.Pun;
using UnityEngine;

/// <summary>
/// [멀티 2단계] 방 입장 완료 시 내 아바타를 네트워크 스폰.
/// PhotonNetwork.Instantiate = 방의 모든 컴퓨터에 같은 오브젝트 생성.
/// 프리팹은 반드시 Resources 폴더 안에 있어야 함 (Photon 규칙).
/// </summary>
public class NetworkPlayerSpawner : MonoBehaviourPunCallbacks
{
    [Tooltip("Resources 폴더 기준 프리팹 이름 (확장자 없이)")]
    [SerializeField] private string playerPrefabName = "NetPlayer";

    [Tooltip("스폰 위치들 (비우면 원점 주변 랜덤)")]
    [SerializeField] private Transform[] spawnPoints;

    public override void OnJoinedRoom()
    {
        // 내 접속 순번으로 스폰 지점 선택 (겹침 방지)
        int idx = (PhotonNetwork.LocalPlayer.ActorNumber - 1) % Mathf.Max(1, spawnPoints.Length);

        Vector3 pos = spawnPoints != null && spawnPoints.Length > 0
            ? spawnPoints[idx].position
            : new Vector3(Random.Range(-2f, 2f), 1f, Random.Range(-2f, 2f));

        Quaternion rot = spawnPoints != null && spawnPoints.Length > 0
            ? spawnPoints[idx].rotation : Quaternion.identity;

        PhotonNetwork.Instantiate(playerPrefabName, pos, rot);
        Debug.Log($"[NET] 내 아바타 스폰 완료 (지점 {idx})");
    }
}
