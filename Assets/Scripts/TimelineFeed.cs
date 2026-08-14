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
        GameEvents.OnSkillEvent    += HandleSkill;
        GameEvents.OnPhaseChanged  += HandlePhase;
    }

    private void OnDisable()
    {
        GameEvents.OnItemUsed      -= HandleItemUsed;
        GameEvents.OnRacerFinished -= HandleFinished;
        GameEvents.OnSkillEvent    -= HandleSkill;
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

    // [로컬라이제이션] 사건은 id로 받고 문장은 각자 자기 언어로 조립 — 색/굵기 태그는 CSV 서식에 포함
    private void HandleItemUsed(int pid, ItemDefinition item, int rid)
        => Push(rid >= 0
            ? Loc.Format("feed.item.target", PlayerName(pid), RacerName(rid), item.LocalizedName)
            : Loc.Format("feed.item.notarget", PlayerName(pid), item.LocalizedName));   // 처형 무전기: 대상은 5초 후 확정
    private void HandleFinished(int rid, int rank, bool eliminated)
        => Push(eliminated
            ? Loc.Format("feed.eliminated", RacerName(rid))
            : Loc.Format("feed.finish", RacerName(rid), rank));

    private void HandleSkill(SkillFeedEvent evt, int rid)
    {
        string line = evt switch
        {
            SkillFeedEvent.Roar           => Loc.Format("feed.skill.roar", RacerName(rid)),
            SkillFeedEvent.PenguinIgnore  => Loc.Format("feed.skill.ignore", RacerName(rid)),
            SkillFeedEvent.CatWalk        => Loc.Format("feed.skill.catwalk", RacerName(rid)),
            SkillFeedEvent.Dash           => Loc.Format("feed.skill.dash", RacerName(rid)),
            SkillFeedEvent.Rudolph        => Loc.Format("feed.skill.rudolph", RacerName(rid)),
            SkillFeedEvent.ExecuteWarning => Loc.Get("feed.exec.warning"),
            SkillFeedEvent.ExecuteHit     => Loc.Format("feed.exec.hit", RacerName(rid)),
            _ => null
        };
        if (line != null) Push($"<color=#8FD3FF>{line}</color>");
    }

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
        return r != null ? r.DisplayName : Loc.Format("racer.fallback", id + 1);   // 레인 번호는 1부터
    }
}
