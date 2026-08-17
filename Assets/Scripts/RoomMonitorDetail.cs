using TMPro;
using UnityEngine;

/// <summary>
/// 관찰 전시대 위 스크린 — 전시대에 올려둔 피규어의 스탯/스킬 표시
/// (안내판 팝업 AnimalInfoPopup과 같은 서식).
/// ⚠ 조준(호버) 기반이 아니다: 곁눈질로 벽을 봐야 해서 불편하다는 판단으로
///   "전시대에 올린 동물"만 띄우도록 기획 변경됨. 올리면 그 안에서 달리기까지 재생.
/// </summary>
public class RoomMonitorDetail : MonoBehaviour
{
    [SerializeField] private BettingRoom room;
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private InspectStand stand;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text bodyText;

    private int shownId = -2;   // -2 = 강제 첫 갱신

    // 언어가 바뀌면 캐시를 깨서 다음 프레임에 새 언어로 다시 그린다
    private void OnEnable() => Loc.OnLanguageChanged += ForceRefresh;
    private void OnDisable() => Loc.OnLanguageChanged -= ForceRefresh;
    private void ForceRefresh() => shownId = -2;

    private void Awake()
    {
        if (room == null) room = GetComponentInParent<BettingRoom>();
        if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();
        if (stand == null && room != null) stand = room.GetComponentInChildren<InspectStand>(true);
    }

    /// <summary>런타임 셋업용 (씬 조립 스크립트가 호출).</summary>
    public void Setup(TMP_Text nameText, TMP_Text bodyText, InspectStand stand)
    {
        this.nameText = nameText;
        this.bodyText = bodyText;
        this.stand = stand;
    }

    private void Update()
    {
        // 전시대에 올린 동물만 표시 (내 방 한정 — 남의 방은 출입 불가지만 방어)
        int id = room != null && room.IsLocalRoom && stand != null && stand.Current != null
            ? stand.Current.RacerId : -1;
        if (id == shownId) return;
        shownId = id;

        if (id < 0)
        {
            if (nameText != null) nameText.text = Loc.Get("monitor.title");
            if (bodyText != null) bodyText.text = Loc.Get("monitor.hint");
            return;
        }

        Racer racer = null;
        if (raceManager != null)
            foreach (var r in raceManager.Racers)
                if (r.RacerId == id) { racer = r; break; }
        if (racer == null || racer.Definition == null) return;

        var def = racer.Definition;
        if (nameText != null) nameText.text = Loc.Format("monitor.name", id + 1, def.displayName);
        if (bodyText != null)
            bodyText.text =
                Loc.Format("monitor.stats",
                    def.minSpeed.ToString("F0"), def.maxSpeed.ToString("F0"), def.acceleration) +
                $"\n\n<b>{SkillTuning.DisplayName(def.skill)}</b>\n" +
                $"{SkillTuning.Description(def.skill)}";
    }
}
