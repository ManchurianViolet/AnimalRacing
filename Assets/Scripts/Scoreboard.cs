using TMPro;
using UnityEngine;

/// <summary>
/// 월드스페이스 전광판. GameEvents 구독 + 타이머만 MatchManager.PhaseEndTime 읽기.
/// </summary>
public class Scoreboard : MonoBehaviour
{
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private TMP_Text phaseText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text roundText;

    private void OnEnable()
    {
        GameEvents.OnPhaseChanged += HandlePhase;
        GameEvents.OnRoundChanged += HandleRound;
    }
    private void OnDisable()
    {
        GameEvents.OnPhaseChanged -= HandlePhase;
        GameEvents.OnRoundChanged -= HandleRound;
    }

    private void HandlePhase(GamePhase p)
    {
        if (phaseText == null) return;
        phaseText.text = p switch
        {
            GamePhase.Lobby      => "대기 중",
            GamePhase.Betting    => "베팅 접수 중",
            GamePhase.Loadout    => "아이템 준비",
            GamePhase.Countdown  => "출발 준비",
            GamePhase.Racing     => "경기 중",
            GamePhase.Settlement => "정산 중",
            _ => ""
        };
    }

    private void HandleRound(int cur, int total)
    {
        if (roundText != null) roundText.text = $"ROUND {cur} / {total}";
    }

    private void Update()
    {
        if (timerText == null || matchManager == null) return;
        float remain = matchManager.PhaseEndTime - Time.time;
        timerText.text = remain > 0f ? $"{remain:0}" : "";
    }
}
