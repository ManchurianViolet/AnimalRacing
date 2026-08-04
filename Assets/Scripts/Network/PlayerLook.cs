using Photon.Pun;
using UnityEngine;

/// <summary>
/// 내 캐릭터 외형 코드를 방에 알리고, 남의 외형 코드를 읽는다.
/// 코드는 CharacterCustomization.Encode() 결과 문자열("0,2,1,-1,...") — 30바이트 남짓이라
/// Photon 플레이어 커스텀 속성에 얹어도 부담이 없다.
/// 커스텀 속성은 방 밖에서 걸어둬도 PUN이 캐시했다가 입장 때 함께 보낸다 (타이틀에서 확정 → 그대로 반영).
/// </summary>
public static class PlayerLook
{
    public const string PropKey = "look";

    /// <summary>
    /// [테스트 전용] 값이 있으면 저장값 대신 이걸 쓴다.
    /// 멀티플레이어 플레이 모드의 가상 플레이어는 PlayerPrefs를 본체와 공유해서
    /// 그냥 두면 전부 같은 옷을 입는다 — 그때 창마다 다른 외형을 주려고 쓴다.
    /// </summary>
    public static string Override;

    /// <summary>이 컴퓨터에 저장된 내 외형 (타이틀 커마에서 확정한 값).</summary>
    public static string Local =>
        !string.IsNullOrEmpty(Override) ? Override
                                        : PlayerPrefs.GetString(CharacterCustomization.PrefsKey, "");

    /// <summary>내 외형을 방에 방송. 접속 전이면 조용히 무시된다.</summary>
    public static void Publish()
    {
        if (!PhotonNetwork.IsConnected) return;
        PhotonNetwork.LocalPlayer.SetCustomProperties(
            new ExitGames.Client.Photon.Hashtable { { PropKey, Local } });
    }

    /// <summary>해당 플레이어의 외형 코드 (없으면 빈 문자열 = 기본 차림).</summary>
    public static string Of(Photon.Realtime.Player p)
    {
        if (p != null && p.CustomProperties != null &&
            p.CustomProperties.TryGetValue(PropKey, out var v) && v is string s)
            return s;
        return "";
    }
}
