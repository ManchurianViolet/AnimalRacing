using UnityEngine;

/// <summary>
/// [5-2] 내 아바타 배선반.
/// 스폰된 "내" 플레이어를 씬의 모든 소비처(HUD/단말기/레버)에 런타임 연결.
/// 씬 배치 플레이어가 사라지면서 끊긴 인스펙터 참조를 이 한 곳이 대신 이어준다.
/// NetworkPlayerSetup이 내 아바타 확정 시 호출.
/// </summary>
public class LocalPlayerBinder : MonoBehaviour
{
    [Header("내 플레이어를 넘겨받을 소비처들")]
    [SerializeField] private PlayerHUD hud;
    [SerializeField] private BettingTerminal[] bettingTerminals;
    [SerializeField] private StartLever startLever;   // (구) 단일 배선 — 유지, 아래에서 전체 탐색도 함께

    public void BindLocalPlayer(GameObject playerGo)
    {
        var fpc = playerGo.GetComponent<FirstPersonController>();
        var interactor = playerGo.GetComponent<PlayerInteractor>();

        if (hud != null) hud.BindLocalPlayer(fpc, interactor);
        if (bettingTerminals != null)
            foreach (var t in bettingTerminals) if (t != null) t.BindLocalPlayer(fpc);

        // 시작 레버가 베팅 방마다 복제될 수 있어 씬 전체를 배선 (인스펙터 단일 참조는 폴백)
        var levers = FindObjectsByType<StartLever>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var lever in levers) lever.BindLocalPlayer(fpc);
        if (levers.Length == 0 && startLever != null) startLever.BindLocalPlayer(fpc);

        Debug.Log($"[Binder] 내 아바타 배선 완료 (레버 {levers.Length}개)");
    }
}
