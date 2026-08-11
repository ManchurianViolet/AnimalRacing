using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 출발/결승 아치 간판. 레이스 초반엔 START, 선두가 마지막 랩에 들어서면 FINISH로 바뀐다.
///
/// 텍스처(Start arch.png) 한 장에 두 글자가 위아래로 나란히 그려져 있고, 간판 앞면 정점의 UV가
/// 그중 START 줄만 잘라 쓰고 있다. 그래서 앞면 정점의 UV를 글자 줄 간격만큼 아래로 밀면 FINISH가 된다.
/// 텍스처 교체도, 오브젝트 교체도 필요 없다.
///
/// 진행도는 ScoreboardBoard와 같은 "로컬 위치 기반" 계산이라 클라도 자기 화면에서 스스로 바꾼다
/// = 네트워크 추가 통신 0 (전광판·부스트 먼지와 같은 철학).
/// </summary>
public class GantrySign : MonoBehaviour
{
    [Header("대상")]
    [SerializeField, Tooltip("글자가 그려진 간판 메시. 비우면 자식 중 이름에 board가 든 것을 찾는다.")]
    private MeshFilter boardMesh;

    [Header("텍스처")]
    [SerializeField, Tooltip("START 줄과 FINISH 줄의 간격(텍스처 픽셀). 이 에셋 실측값 205px.")]
    private float lineSpacingPixels = 205f;

    [SerializeField, Tooltip("UV의 V가 이 값 이상인 정점만 '글자면'으로 보고 민다. 앞면 0.88~0.99 / 뒷면·측면 0.58~0.71이라 0.8이면 앞면만 잡힌다.")]
    private float frontFaceMinV = 0.8f;

    [Header("전환 시점")]
    [SerializeField, Tooltip("선두가 마지막 랩에 들어서기 몇 m 전에 미리 바꿀지. 0이면 선두가 출발선을 넘는 순간.")]
    private float leadMargin = 0f;

    [Header("참조 (비우면 자동 탐색)")]
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private TrackPath path;

    private Mesh runtimeMesh;
    private Vector2[] uvStart;
    private Vector2[] uvFinish;
    private bool showingFinish;
    private GamePhase phase = GamePhase.Lobby;

    // 동물별 마지막 진행도 (GetDistanceNear는 연속성 투영이라 직전 값이 필요하다)
    private readonly Dictionary<Racer, float> lastProgress = new();

    private void Awake()
    {
        if (boardMesh == null) boardMesh = FindBoard();
        if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();
        if (path == null && raceManager != null) path = raceManager.Path;
        if (path == null) path = FindFirstObjectByType<TrackPath>();
        BuildUvSets();
    }

    private void OnEnable() => GameEvents.OnPhaseChanged += HandlePhase;
    private void OnDisable() => GameEvents.OnPhaseChanged -= HandlePhase;

    private void OnDestroy()
    {
        if (runtimeMesh != null) Destroy(runtimeMesh);
    }

    /// <summary>앞면 UV를 글자 줄 간격만큼 내린 사본을 미리 구워둔다 (전환은 배열 스왑 한 번).</summary>
    private void BuildUvSets()
    {
        if (boardMesh == null || boardMesh.sharedMesh == null)
        {
            Debug.LogWarning("[GantrySign] 간판 메시를 못 찾았다 — 글자 전환 비활성.", this);
            return;
        }

        // ⚠ FBX의 Read/Write Enabled가 꺼져 있으면 런타임에 UV를 못 쓴다.
        // 에디터에선 플레이 직후 잠깐 되는 것처럼 보이다가 CPU 사본이 해제되는 순간 터지므로
        // (그리고 빌드에선 처음부터 안 되므로) 여기서 확실히 잡아둔다.
        if (!boardMesh.sharedMesh.isReadable)
        {
            Debug.LogError($"[GantrySign] 간판 메시 '{boardMesh.sharedMesh.name}'의 Read/Write Enabled가 꺼져 있다. " +
                           "FBX 임포트 설정에서 켜야 글자 전환이 동작한다 — 전환 비활성.", this);
            return;
        }

        // 에셋 원본은 건드리지 않는다. 사본을 만들어 이 렌더러에만 물린다.
        runtimeMesh = Instantiate(boardMesh.sharedMesh);
        runtimeMesh.name = boardMesh.sharedMesh.name + " (GantrySign)";
        boardMesh.sharedMesh = runtimeMesh;

        // 텍스처 높이는 머티리얼에서 실측 — 픽셀 간격을 UV 단위로 바꾸는 데 쓴다.
        float texHeight = 2048f;
        var mr = boardMesh.GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.mainTexture != null)
            texHeight = mr.sharedMaterial.mainTexture.height;

        float dV = lineSpacingPixels / Mathf.Max(1f, texHeight);

        uvStart = runtimeMesh.uv;
        uvFinish = new Vector2[uvStart.Length];
        int moved = 0;
        for (int i = 0; i < uvStart.Length; i++)
        {
            uvFinish[i] = uvStart[i];
            if (uvStart[i].y >= frontFaceMinV)
            {
                uvFinish[i].y -= dV;
                moved++;
            }
        }

        if (moved == 0)
            Debug.LogWarning($"[GantrySign] 글자면 정점을 하나도 못 골랐다 (frontFaceMinV={frontFaceMinV}). 임계값을 낮춰라.", this);
    }

    private void HandlePhase(GamePhase p)
    {
        phase = p;
        // 다음 라운드는 다시 START부터
        if (p == GamePhase.Betting || p == GamePhase.Lobby)
        {
            lastProgress.Clear();
            SetFinish(false);
        }
    }

    private void Update()
    {
        if (runtimeMesh == null || raceManager == null || path == null) return;
        if (phase != GamePhase.Racing || showingFinish) return;

        // 마지막 랩 진입 지점 = 완주 거리 − 한 바퀴. 1랩 레이스면 바꿀 게 없다.
        float lastLapStart = raceManager.RaceLength - path.TotalLength;
        if (lastLapStart < 1f) return;

        float lead = 0f;
        foreach (var r in raceManager.Racers)
        {
            if (r == null) continue;
            lastProgress.TryGetValue(r, out float prev);
            float prog = path.GetDistanceNear(r.transform.position, prev);
            lastProgress[r] = prog;
            if (prog > lead) lead = prog;
        }

        if (lead >= lastLapStart - leadMargin) SetFinish(true);
    }

    private void SetFinish(bool finish)
    {
        if (runtimeMesh == null || uvStart == null || showingFinish == finish) return;
        showingFinish = finish;
        runtimeMesh.uv = finish ? uvFinish : uvStart;
    }

    private MeshFilter FindBoard()
    {
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            if (mf.name.IndexOf("board", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return mf;
        return null;
    }

    [ContextMenu("미리보기: FINISH")]
    private void PreviewFinish() => SetFinish(true);

    [ContextMenu("미리보기: START")]
    private void PreviewStart() => SetFinish(false);
}
