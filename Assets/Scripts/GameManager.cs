using UnityEngine;

/// <summary>페이즈 상태머신 껍데기. 실질 진행은 MatchManager가 주도.</summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private GameConfig config;
    public GameConfig Config => config;

    public GamePhase CurrentPhase { get; private set; } = GamePhase.Lobby;
    public int PlayerCount { get; private set; } = 4;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void SetPlayerCount(int n) => PlayerCount = n;

    public void SetPhase(GamePhase next)
    {
        // TODO(멀티): 호스트 전용 가드 + RPC 브로드캐스트
        CurrentPhase = next;
        GameEvents.RaisePhaseChanged(next);
    }

    public void Settle() => SetPhase(GamePhase.Settlement);
}
