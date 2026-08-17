using UnityEngine;

/// <summary>
/// [인간 레이서] 매판 랜덤 복장 — "누군지 모를 인간이 뛴다"가 재미 포인트 (유저 결정).
/// [멀티] 시드 = 방 이름 해시 (CloudField와 같은 수제 31곱 누적) + 고정 소금 —
/// 방 파질 때 확정·전 클라 동일이라 모두 같은 옷을 본다. 통신 0. 오프라인은 판마다 랜덤.
/// 인간 프리팹(커마 슬롯 구조 + CharacterCustomization, loadSavedOnAwake=끔)에 부착.
/// </summary>
[RequireComponent(typeof(CharacterCustomization))]
public class HumanLookRandomizer : MonoBehaviour
{
    private void Start()
    {
        var custom = GetComponent<CharacterCustomization>();
        if (custom == null) return;

        var prev = Random.state;   // 전역 랜덤 오염 방지 (레이스 리롤이 같은 스트림을 쓴다)
        Random.InitState(ComputeSeed());
        custom.Randomize();        // 선택만 굴린다 — 저장(SaveToPrefs) 없음 = 플레이어 옷장 무오염
        custom.ApplyAll();
        Random.state = prev;
    }

    private static int ComputeSeed()
    {
        string room = Photon.Pun.PhotonNetwork.InRoom
            ? Photon.Pun.PhotonNetwork.CurrentRoom.Name : null;
        if (string.IsNullOrEmpty(room))
            return Random.Range(int.MinValue, int.MaxValue);   // 오프라인: 판마다 랜덤

        int h = 17;
        foreach (char c in room) h = h * 31 + c;
        return h * 31 + 7919;   // 구름(CloudField) 해시와 다른 소금 — 같은 방이어도 다른 스트림
    }
}
