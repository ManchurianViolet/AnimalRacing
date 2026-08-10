using UnityEngine;

/// <summary>
/// [스팀] Facepunch.Steamworks 초기화 + 신원 제공 (static 허브).
/// 게임 시작 시 1회 초기화 → 닉네임(PersonaName)과 SteamID를 내놓는다.
/// 스팀 미실행/에디터 실패 시 IsAvailable=false — 기존 수동 닉네임 입력으로 폴백 (입력창 유지 이유).
///
/// SteamID는 NetworkLauncher가 Photon UserId(AuthValues)로 쓴다 —
/// 창을 껐다 켜도 "같은 사람"이라 재접속 복귀(PlayerTtl 60초)가 성립하게 된다 (§10 미해결이던 것).
/// </summary>
public static class SteamHub
{
    // TODO 출시 전: Steamworks 등록 후 진짜 App ID로 교체. 480 = Valve 공개 테스트용(Spacewar).
    public const uint AppId = 480;

    public static bool IsAvailable { get; private set; }
    public static string PersonaName { get; private set; } = "";
    public static string SteamId { get; private set; } = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
#if UNITY_EDITOR
        // MPPM 가상 플레이어는 스팀을 쓰지 않는다 — 전 창이 같은 스팀 계정이라
        // SteamID(=Photon UserId)가 겹쳐 서로를 밀어낸다. 테스트 신원은 MppmTestClient 담당.
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor) return;
#endif
        try
        {
            // asyncCallbacks=false: 유니티는 메인 스레드에서 RunCallbacks를 직접 돌리는 게 안전
            Steamworks.SteamClient.Init(AppId, asyncCallbacks: false);
            IsAvailable = true;
            PersonaName = Steamworks.SteamClient.Name;
            SteamId = Steamworks.SteamClient.SteamId.Value.ToString();
            Debug.Log($"[STEAM] 초기화 성공 — {PersonaName} ({SteamId})");

            Application.quitting += Shutdown;   // 에디터 플레이 종료에도 호출됨 — 재진입 Init 실패 방지

            var driver = new GameObject("[Steam 콜백 펌프]");
            Object.DontDestroyOnLoad(driver);
            driver.AddComponent<SteamCallbackPump>();
        }
        catch (System.Exception e)
        {
            IsAvailable = false;
            Debug.Log($"[STEAM] 스팀 미실행 또는 초기화 실패 — 수동 닉네임으로 폴백 ({e.Message})");
        }
    }

    private static void Shutdown()
    {
        if (!IsAvailable) return;
        IsAvailable = false;
        Steamworks.SteamClient.Shutdown();
    }

    /// <summary>콜백 펌프 — 지금은 필수 아님, 나중에 친구 초대/오버레이 이벤트 받을 때의 기반.</summary>
    private class SteamCallbackPump : MonoBehaviour
    {
        private void Update()
        {
            if (IsAvailable) Steamworks.SteamClient.RunCallbacks();
        }
    }
}
