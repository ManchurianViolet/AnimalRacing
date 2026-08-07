using UnityEngine;

/// <summary>
/// 베팅 방의 예측 확정 버튼 (E 상호작용).
/// 상자 3개가 다 차야 제출 — 관문은 기존 그대로 gateway.RequestSubmitBet
/// (문 열림은 제출 미러(submitted)를 BettingRoomManager가 읽어 자동 처리).
/// </summary>
public class RoomConfirmButton : MonoBehaviour, IInteractable
{
    [SerializeField] private BettingRoom room;
    [SerializeField] private NetworkGateway gateway;
    [SerializeField] private MatchManager matchManager;

    private bool busy;   // 제출 왕복 중 연타 방지

    public string Prompt =>
        room != null && room.BuildTicket() != null
            ? "E - 예측 확정"
            : "상자 3개에 피규어를 넣으세요";

    private void Awake()
    {
        // 배선 누락 대비 자동 탐색 (v8 법칙)
        if (room == null) room = GetComponentInParent<BettingRoom>();
        if (gateway == null) gateway = FindFirstObjectByType<NetworkGateway>();
        if (matchManager == null) matchManager = FindFirstObjectByType<MatchManager>();
    }

    public bool CanInteract() =>
        GameManager.Instance != null
        && GameManager.Instance.CurrentPhase == GamePhase.Betting
        && room != null && room.IsLocalRoom
        && matchManager != null && !matchManager.HasSubmitted(NetworkPlayers.LocalPlayerId)
        && !busy;

    public void Interact()
    {
        var ticket = room.BuildTicket();
        if (ticket == null) return;   // 프롬프트가 이미 안내 중

        busy = true;
        gateway.RequestSubmitBet(ticket.Value, ok =>
        {
            busy = false;
            // 성공 시 문은 submitted 미러로 자동 열림. 실패(시간 초과 등)면 다시 시도 가능.
        });
    }
}
