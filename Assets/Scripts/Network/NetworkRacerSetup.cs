using Photon.Pun;
using UnityEngine;

/// <summary>
/// [멀티 3단계] 동물 프리팹용: 네트워크 스폰 직후 자기 정체를 읽어 등록.
/// 호스트: 아무것도 안 함 (RaceManager가 직접 셋업).
/// 클라: 동봉된 데이터(레이서ID/동물/번호)로 RaceManager에 표시용 등록.
/// </summary>
public class NetworkRacerSetup : MonoBehaviourPun, IPunInstantiateMagicCallback
{
    public void OnPhotonInstantiate(PhotonMessageInfo info)
    {
        if (photonView.IsMine) return;   // 호스트 측 인스턴스는 RaceManager 담당

        var data = photonView.InstantiationData;
        if (data == null || data.Length < 3)
        {
            Debug.LogError("[NetworkRacerSetup] 스폰 데이터 누락");
            return;
        }

        int racerId   = (int)data[0];
        int animalIdx = (int)data[1];
        int postNum   = (int)data[2];

        var rm = FindFirstObjectByType<RaceManager>();
        if (rm != null)
            rm.RegisterNetworkRacer(gameObject, racerId, animalIdx, postNum);
    }
}
