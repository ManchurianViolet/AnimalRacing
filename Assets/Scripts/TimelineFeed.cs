using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class TimelineFeed : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI feedText;
    [SerializeField] private int maxLines = 8;
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private RaceManager raceManager;

    private readonly LinkedList<string> lines = new();

    private void Start()
    {
        if (feedText != null) feedText.text = "";   // 에디터 임시 텍스트 정리
    }

    private void OnEnable()
    {
        GameEvents.OnItemUsed      += HandleItemUsed;
        GameEvents.OnRacerFinished += HandleFinished;
        GameEvents.OnSkillProc     += HandleSkill;
        GameEvents.OnPhaseChanged  += HandlePhase;
    }

    private void OnDisable()
    {
        GameEvents.OnItemUsed      -= HandleItemUsed;
        GameEvents.OnRacerFinished -= HandleFinished;
        GameEvents.OnSkillProc     -= HandleSkill;
        GameEvents.OnPhaseChanged  -= HandlePhase;
    }

    private void HandlePhase(GamePhase phase)
    {
        // 새 라운드(베팅) 시작 시 지난 라운드 피드 초기화
        if (phase == GamePhase.Betting)
        {
            lines.Clear();
            if (feedText != null) feedText.text = "";
        }
    }

    private void HandleItemUsed(int pid, ItemDefinition item, int rid)
        => Push($"<b>[{PlayerName(pid)}]</b> {RacerName(rid)}에게 <color=#FFB020>{item.itemName}</color>!");
    private void HandleFinished(int rid, int rank)
        => Push($"{RacerName(rid)} <b>{rank}위</b> 결승선 통과");
    private void HandleSkill(string line)
        => Push($"<color=#8FD3FF>{line}</color>");

    private void Push(string line)
    {
        lines.AddFirst(line);
        while (lines.Count > maxLines) lines.RemoveLast();
        if (feedText != null) feedText.text = string.Join("\n", lines);
    }

    private string PlayerName(int id)
    {
        foreach (var p in matchManager.Players)
            if (p.PlayerId == id) return p.Nickname;
        return $"P{id}";
    }

    private string RacerName(int id)
    {
        var r = raceManager.GetRacer(id);
        return r != null ? r.DisplayName : $"{id + 1}번";   // 레인 번호는 1부터
    }
}
