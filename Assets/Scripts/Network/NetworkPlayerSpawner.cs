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

    [Tooltip("전망대 스폰 위치 (대기 상태). 접속 순번으로 배정")]
    [SerializeField] private Transform[] spawnPoints;

    [Tooltip("지상 스폰 위치 — 매치 진행 중 합류/재접속자는 여기서 시작 (전망대에 갇힘 방지)")]
    [SerializeField] private Transform[] groundSpawnPoints;

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

        // 게스트는 호스트의 페이즈를 한 번 받고 나서 스폰 위치를 정한다 — 방송 전엔
        // 기본값 Lobby로 읽혀서 "매치 중인데 전망대에 스폰"되는 경쟁이 생김 (방송 주기 0.5초)
        if (online && !PhotonNetwork.IsMasterClient)
        {
            float syncTimeout = Time.time + 2f;
            while (!NetworkMatchSync.PhaseSynced && Time.time < syncTimeout) yield return null;
        }

        // 대기 상태 = 전망대, 그 외(매치 진행 중 합류/재접속) = 지상
        bool lobby = GameManager.Instance == null
                     || GameManager.Instance.CurrentPhase == GamePhase.Lobby;
        var points = (!lobby && groundSpawnPoints != null && groundSpawnPoints.Length > 0)
            ? groundSpawnPoints : spawnPoints;
        Debug.Log($"[Spawner] 스폰 지점: {(points == groundSpawnPoints ? "지상" : "전망대")} (페이즈 {GameManager.Instance?.CurrentPhase})");

        int idx = online
            ? (PhotonNetwork.LocalPlayer.ActorNumber - 1) % Mathf.Max(1, points.Length)
            : 0;

        Vector3 pos = points != null && points.Length > 0
            ? points[idx].position : Vector3.up;
        Quaternion rot = points != null && points.Length > 0
            ? points[idx].rotation : Quaternion.identity;

        if (online)
            PhotonNetwork.Instantiate(playerPrefab.name, pos, rot);
        else
            Instantiate(playerPrefab, pos, rot);   // 오프라인 — 셋업이 "내 것"으로 처리
    }
}
