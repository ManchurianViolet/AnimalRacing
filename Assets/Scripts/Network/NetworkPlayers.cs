/// <summary>
/// [멀티 4단계] 플레이어 식별 헬퍼.
/// 오프라인: 내 ID = 0 (기존 유지). 온라인: 내 ID = Photon ActorNumber (1부터).
/// 봇 ID는 100번대 (충돌 방지).
/// </summary>
public static class NetworkPlayers
{
    public const int BotIdBase = 100;

    public static int LocalPlayerId =>
        Photon.Pun.PhotonNetwork.InRoom ? Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber : 0;

    /// <summary>내가 시뮬/경제 권위자인가 (오프라인 또는 방장).</summary>
    public static bool IsAuthority =>
        !Photon.Pun.PhotonNetwork.InRoom || Photon.Pun.PhotonNetwork.IsMasterClient;
}
