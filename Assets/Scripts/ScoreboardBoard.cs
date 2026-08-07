using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 거대 전광판 — 상단 러닝타임 + 실시간 순위표.
/// 행 구성: 레인 배지(RacerColors 단일 출처) / 동물 아이콘 / 이름 / 현재 속도.
/// 완주하면 속도 자리에 최종 기록(러닝타임)이 뜬다. 순위 변동 시 행이 슬롯으로 부드럽게 이동.
/// 호스트/클라 공용: 진행도·속도는 로컬 위치 기반 계산(클라는 TransformView 미러 위치로 동일 계산),
/// 완주 확정만 OnRacerFinished 이벤트(호스트 발신 → 게이트웨이가 클라 중계) 기준.
/// </summary>
public class ScoreboardBoard : MonoBehaviour
{
    [Header("배선 (에디터 조립)")]
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private RectTransform rowContainer;
    [SerializeField] private GameObject rowTemplate;   // 비활성 템플릿 (자식: Badge/BadgeText, Icon, NameText, ValueText)

    [Header("연출")]
    [Tooltip("행 높이 (캔버스 px)")] public float rowHeight = 110f;
    [Tooltip("순위 변동 시 행 이동 속도")] public float rowMoveSpeed = 6f;
    [Tooltip("속도 표기 평활 시간(초) — 클수록 숫자가 차분함")] public float speedSmooth = 0.35f;

    private class Row
    {
        public Racer racer;
        public RectTransform rect;
        public TMP_Text badge, name, value;
        public Image icon;
        public float lastProg;      // 연속성 투영용 (8자 교차에서 반대편 변 포획 방지)
        public Vector3 lastPos;
        public float dispSpeed;
        public bool finished;
        public bool eliminated;
        public int finishRank;
        public float finishTime;
    }

    private readonly List<Row> rows = new();
    private RaceManager raceManager;
    private TrackPath path;
    private float clock;
    private bool clockRunning;

    private void Awake()
    {
        raceManager = FindFirstObjectByType<RaceManager>();
        path = FindFirstObjectByType<TrackPath>();
        if (rowTemplate != null) rowTemplate.SetActive(false);
    }

    private void OnEnable()
    {
        GameEvents.OnPhaseChanged += HandlePhase;
        GameEvents.OnRacerFinished += HandleFinished;
    }

    private void OnDisable()
    {
        GameEvents.OnPhaseChanged -= HandlePhase;
        GameEvents.OnRacerFinished -= HandleFinished;
    }

    private void HandlePhase(GamePhase p)
    {
        if (p == GamePhase.Racing) { clock = 0f; clockRunning = true; }
        else if (p == GamePhase.Settlement) clockRunning = false;
        else if (p == GamePhase.Betting) { clock = 0f; clockRunning = false; }
    }

    private void HandleFinished(int racerId, int rank, bool eliminated)
    {
        var row = rows.Find(r => r.racer != null && r.racer.RacerId == racerId);
        if (row == null || row.finished) return;
        row.finished = true;
        row.eliminated = eliminated;
        row.finishRank = rank;
        row.finishTime = clock;
        if (eliminated) ApplyEliminatedLook(row);
    }

    /// <summary>탈락 행: 전체를 뿌옇게(회색 반투명) + 기록 자리는 빨간 "탈락".</summary>
    private static void ApplyEliminatedLook(Row row)
    {
        if (row.rect == null) return;
        var cg = row.rect.GetComponent<CanvasGroup>();
        if (cg == null) cg = row.rect.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0.45f;
        if (row.value != null) row.value.color = new Color(1f, 0.35f, 0.35f);
    }

    private void Update()
    {
        if (raceManager == null || path == null) return;
        if (clockRunning) clock += Time.deltaTime;
        if (timeText != null) timeText.text = FormatTime(clock);

        // 라인업 변동(새 라운드 스폰/파괴) 감지 → 행 재구성
        bool stale = rows.Count != raceManager.Racers.Count;
        if (!stale) foreach (var r in rows) if (r.racer == null) { stale = true; break; }
        if (stale) Rebuild();

        float dt = Time.deltaTime;
        foreach (var row in rows)
        {
            if (row.racer == null) continue;
            var pos = row.racer.transform.position;

            row.lastProg = path.GetDistanceNear(pos, row.lastProg);   // 랩 누적 — 2랩째 선두가 뒤로 안 밀림

            // 표시 속도: 위치 변화 기반 — 클라의 kinematic 미러에도 유효
            float rawSpeed = dt > 1e-5f
                ? Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(row.lastPos.x, row.lastPos.z)) / dt
                : 0f;
            row.lastPos = pos;
            row.dispSpeed = Mathf.Lerp(row.dispSpeed, rawSpeed, dt / Mathf.Max(0.05f, speedSmooth));

            row.value.text = row.finished
                ? (row.eliminated ? "탈락" : FormatTime(row.finishTime))
                : $"{row.dispSpeed:F1} m/s";
        }

        // 정렬: 완주(순위순) → 주행 중(진행도 내림차순) → 탈락(항상 맨 아래, 순위순)
        var order = new List<Row>(rows);
        order.Sort((a, b) =>
        {
            if (a.eliminated != b.eliminated) return a.eliminated ? 1 : -1;
            if (a.eliminated) return a.finishRank.CompareTo(b.finishRank);
            if (a.finished != b.finished) return a.finished ? -1 : 1;
            if (a.finished) return a.finishRank.CompareTo(b.finishRank);
            return b.lastProg.CompareTo(a.lastProg);
        });

        for (int i = 0; i < order.Count; i++)
        {
            var rt = order[i].rect;
            if (rt == null) continue;
            var p = rt.anchoredPosition;
            p.y = Mathf.Lerp(p.y, -i * rowHeight, rowMoveSpeed * dt);
            rt.anchoredPosition = p;
        }
    }

    private void Rebuild()
    {
        foreach (var r in rows) if (r.rect != null) Destroy(r.rect.gameObject);
        rows.Clear();
        if (rowTemplate == null || rowContainer == null) return;

        int i = 0;
        foreach (var racer in raceManager.Racers)
        {
            if (racer == null) continue;
            var go = Instantiate(rowTemplate, rowContainer);
            go.SetActive(true);

            var row = new Row
            {
                racer = racer,
                rect = go.GetComponent<RectTransform>(),
                lastPos = racer.transform.position,
            };
            row.rect.anchoredPosition = new Vector2(0f, -i * rowHeight);

            int post = racer.RacerId + 1;   // 스폰 규칙: 등번호 = RacerId + 1 (호스트/클라 동일)
            var badgeImg = go.transform.Find("Badge").GetComponent<Image>();
            badgeImg.color = RacerColors.Of(post);
            row.badge = go.transform.Find("Badge/BadgeText").GetComponent<TMP_Text>();
            row.badge.text = post.ToString();
            row.badge.color = RacerColors.TextOn(post);

            row.icon = go.transform.Find("Icon").GetComponent<Image>();
            var sprite = racer.Definition != null ? racer.Definition.icon : null;
            row.icon.sprite = sprite;
            row.icon.enabled = sprite != null;   // 아이콘 없는 동물은 칸만 비움

            row.name = go.transform.Find("NameText").GetComponent<TMP_Text>();
            row.name.text = racer.Definition != null ? racer.Definition.displayName : racer.DisplayName;

            row.value = go.transform.Find("ValueText").GetComponent<TMP_Text>();
            row.value.text = "";

            rows.Add(row);
            i++;
        }
    }

    private static string FormatTime(float t)
    {
        int m = (int)(t / 60f);
        return $"{m:00}:{t - m * 60f:00.0}";
    }
}
