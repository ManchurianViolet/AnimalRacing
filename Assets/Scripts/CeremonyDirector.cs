using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [v22] 시상식 — 매치 종료 후 최종 순위 연출 + 방 해산.
/// 출발선 앞 트랙 위에 전원 정렬 → 10pt당 돈다발 1개가 물리로 떨어져 발밑에 쌓임 →
/// 우승자는 춤(1~6키로 교체), 나머지는 낙담(Crying) → 우측 상단 카운트다운 → 타이틀 복귀.
///
/// [멀티] 통신 0 철학: 내 아바타 텔레포트는 각 클라 로컬(원격은 TransformView 미러),
/// 돈다발/카메라/애니는 전부 로컬 연출 — 추가 통신은 우승자 춤 변경 RPC(관문 경유) 하나뿐.
/// 순위·포인트는 이미 경제 방송으로 전 클라에 있으므로 정렬 결과가 어디서나 동일하다.
/// 슬롯/카메라는 TrackPath에서 매번 계산 — 씬 배선 0 (튜닝은 GameConfig "시상식" 섹션).
/// </summary>
public class CeremonyDirector : MonoBehaviour
{
    [Tooltip("비면 자동 탐색 — 복제/배선 누락 대비")]
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private NetworkGateway gateway;

    [Tooltip("돈다발 프리팹 (money pack). 콜라이더/리지드바디는 스폰 때 코드가 붙인다")]
    [SerializeField] private GameObject moneyPrefab;

    // 춤 상태명 = PlayerMovement.controller의 상태명 = 클립명. 1~6키 순서.
    private static readonly string[] DanceStates =
        { "Dance_Chicken", "Dance_Locking", "Dance_Snake", "Dance_Ymca", "Dance_Rumba", "Dance_Flair" };
    private const string CryState = "Crying";

    private bool active;
    private bool leaving;
    private int winnerId = -1;
    private Vector3 camPos;
    private Quaternion camRot;

    private Transform moneyRoot;
    private GameObject uiRoot;
    private TMP_Text countdownText;
    private TMP_Text hintText;
    private FirstPersonController localFpc;
    private float moneyScaleFactor = -1f;   // 첫 스폰 때 실측 정규화 (목표 길이 방식 — §11)

    // 시상식 진입 직전 애니 상태 — 중단(AbortMatch) 복원용 (상태명 몰라도 되게 해시로)
    private readonly Dictionary<GameObject, int> prevAnimState = new();

    private void Awake()
    {
        if (matchManager == null) matchManager = FindFirstObjectByType<MatchManager>();
        if (gateway == null) gateway = FindFirstObjectByType<NetworkGateway>();
    }

    private void OnEnable()
    {
        GameEvents.OnPhaseChanged += HandlePhase;
        GameEvents.OnCeremonyDance += HandleDance;
    }

    private void OnDisable()
    {
        GameEvents.OnPhaseChanged -= HandlePhase;
        GameEvents.OnCeremonyDance -= HandleDance;
    }

    private void HandlePhase(GamePhase p)
    {
        if (p == GamePhase.Ceremony && !active) StartCoroutine(StartCeremony());
        else if (p != GamePhase.Ceremony && active) CleanupAborted();   // 방장 이탈 AbortMatch 등
    }

    // ================= 시작 =================

    private IEnumerator StartCeremony()
    {
        active = true;
        leaving = false;

        // 동물 제거(Destroy)는 프레임 끝 지연 + 클라는 네트워크 파괴가 늦게 온다 —
        // 바닥 레이캐스트가 시체 콜라이더를 맞지 않게 잠깐 기다린다 (§11 Destroy 지연 법칙)
        yield return new WaitForSeconds(0.3f);
        if (!active) yield break;

        var cfg = GameManager.Instance.Config;
        var path = FindFirstObjectByType<TrackPath>();
        if (path == null || matchManager == null || matchManager.Players.Count == 0)
        {
            Debug.LogWarning("[시상식] 트랙/로스터 없음 — 연출 생략, 퇴장 카운트다운만 진행");
            BuildOverlay(false);
            yield break;
        }

        // 최종 순위 (포인트 내림차순, 동점은 id 오름차순 — 전 클라 동일 결정)
        var ordering = matchManager.Players
            .OrderByDescending(pl => pl.Points).ThenBy(pl => pl.PlayerId).ToList();
        winnerId = ordering[0].PlayerId;

        // 슬롯/카메라 배치 — 출발선(진행도 0)에서 ceremonyAheadMeters 앞, 트랙 폭 방향 일렬
        float prog = cfg.ceremonyAheadMeters;
        Vector3 center = path.GetPoint(prog);
        Vector3 tangent = path.GetTangent(prog); tangent.y = 0f; tangent.Normalize();
        Vector3 lateral = path.GetNormal(prog);

        int n = ordering.Count;
        var slots = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 pos = center + lateral * ((i - (n - 1) * 0.5f) * cfg.ceremonySlotSpacing);
            pos.y = GroundY(pos);
            slots[i] = pos;
        }
        Quaternion face = Quaternion.LookRotation(tangent);   // 진행 방향 = 카메라 쪽

        camPos = center + tangent * cfg.ceremonyCamDistance + Vector3.up * cfg.ceremonyCamHeight;
        camRot = Quaternion.LookRotation((center + Vector3.up * 1.1f) - camPos);

        // 내 아바타: 조작 잠금(커서 해제 → 아이템 키 자동 차단 + FPC 카메라 소유권 인수) + 내 슬롯으로 텔레포트
        int myIdx = ordering.FindIndex(pl => pl.PlayerId == NetworkPlayers.LocalPlayerId);
        var myEq = PlayerEquipment.Local;
        if (myEq != null)
        {
            localFpc = myEq.GetComponent<FirstPersonController>();
            localFpc?.SetControlEnabled(false);
            if (myIdx >= 0)
            {
                var cc = myEq.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;   // CC 켜진 채 position 대입은 씹힌다
                myEq.transform.SetPositionAndRotation(slots[myIdx], face);
                if (cc != null) cc.enabled = true;
            }
        }

        // 전 아바타: 소품/상체 레이어 끔 + 머리 시선 정지 + 우승자 춤 / 패자 낙담 (각 클라 로컬 재생 — AnimatorView는 파라미터만 미러)
        prevAnimState.Clear();
        foreach (var setup in FindObjectsByType<NetworkPlayerSetup>(FindObjectsSortMode.None))
        {
            var go = setup.gameObject;
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null) continue;

            go.GetComponent<PlayerEquipment>()?.SuppressForKnockdown();
            var headAim = go.GetComponent<PlayerHeadAim>();
            if (headAim != null) headAim.enabled = false;

            prevAnimState[go] = anim.GetCurrentAnimatorStateInfo(0).shortNameHash;
            string state = setup.OwnerPlayerId == winnerId
                ? DanceStates[Mathf.Abs(winnerId) % DanceStates.Length]
                : CryState;
            anim.CrossFade(state, 0.25f, 0, 0f);
        }

        // 돈다발: 10pt당 1개, 전원 병렬 낙하 (봇/이탈자도 순위대로 돈더미는 쌓인다 — 아바타만 없을 뿐)
        moneyRoot = new GameObject("시상식돈다발").transform;
        for (int i = 0; i < n; i++)
        {
            int bundles = Mathf.Min(cfg.ceremonyMaxBundles,
                ordering[i].Points / Mathf.Max(1, cfg.ceremonyPointsPerBundle));
            if (bundles > 0) StartCoroutine(DropMoney(slots[i], bundles, cfg));
        }

        BuildOverlay(myIdx >= 0 && ordering[myIdx].PlayerId == winnerId);
    }

    /// <summary>슬롯 바닥 높이 — CC/동물 시체를 건너뛰고 진짜 지면만 (§11 실측 원칙).</summary>
    private static float GroundY(Vector3 pos)
    {
        var hits = Physics.RaycastAll(pos + Vector3.up * 3f, Vector3.down, 12f,
                                      ~0, QueryTriggerInteraction.Ignore);
        float best = pos.y;
        float bestDist = float.MaxValue;
        foreach (var h in hits)
        {
            if (h.collider.GetComponent<CharacterController>() != null) continue;
            if (h.collider.GetComponentInParent<Racer>() != null) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h.point.y; }
        }
        return best;
    }

    // ================= 돈다발 =================

    private IEnumerator DropMoney(Vector3 slotPos, int count, GameConfig cfg)
    {
        for (int k = 0; k < count; k++)
        {
            if (!active) yield break;
            SpawnBundle(slotPos, cfg);
            yield return new WaitForSeconds(cfg.ceremonyDropInterval);
        }
    }

    private void SpawnBundle(Vector3 slotPos, GameConfig cfg)
    {
        if (moneyPrefab == null) return;

        Vector2 jitter = Random.insideUnitCircle * 0.3f;
        Vector3 pos = slotPos + new Vector3(jitter.x, cfg.ceremonyDropHeight, jitter.y);

        // 측정은 무회전으로 (회전된 바운즈 AABB는 √2배까지 부풀어난다 — §11), 회전은 스케일 뒤에
        var go = Instantiate(moneyPrefab, pos, Quaternion.identity, moneyRoot);
        if (moneyScaleFactor <= 0f)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
                moneyScaleFactor = maxDim > 0.0001f ? cfg.ceremonyMoneyLength / maxDim : 1f;
            }
            else moneyScaleFactor = 1f;
        }
        go.transform.localScale *= moneyScaleFactor;
        go.transform.rotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));

        // 물리: 프리팹에 콜라이더가 없다 — 메시에 맞는 박스 + RB를 코드로
        var mf = go.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.GetComponent<Collider>() == null)
            mf.gameObject.AddComponent<BoxCollider>();   // MeshFilter가 있는 오브젝트면 메시에 자동 핏

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.2f;
        rb.angularDamping = 0.8f;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;   // 6m 낙하 얇은 판 관통 방지
        rb.angularVelocity = Random.insideUnitSphere * 3f;
    }

    // ================= 진행/퇴장 =================

    private void Update()
    {
        if (!active || leaving) return;
        var cfg = GameManager.Instance != null ? GameManager.Instance.Config : null;
        if (cfg == null) return;

        // ESC 메뉴가 조작을 되살렸으면 회수 — 카메라 소유권 싸움 방지 (§11 단일 소유자 법칙)
        if (!PauseMenu.IsOpen)
        {
            if (localFpc != null && localFpc.ControlEnabled) localFpc.SetControlEnabled(false);
            if (Cursor.visible) Cursor.visible = false;   // 연출 화면 — 커서만 숨김 (잠그진 않음)
        }

        float remaining = matchManager != null ? matchManager.PhaseEndTime - Time.time : 0f;

        // 카운트다운: 연출 구간이 끝나면 우측 상단에 "N초 후 메인메뉴로 이동"
        if (countdownText != null)
        {
            bool show = remaining <= cfg.ceremonyExitSeconds;
            if (countdownText.gameObject.activeSelf != show) countdownText.gameObject.SetActive(show);
            if (show)
                countdownText.text = Loc.Format("ceremony.exit", Mathf.Max(0, Mathf.CeilToInt(remaining)));
        }

        // 우승자: 1~6키로 춤 교체 (관문 중계 → 전 클라 재생)
        if (!PauseMenu.IsOpen && winnerId == NetworkPlayers.LocalPlayerId && gateway != null)
        {
            for (int i = 0; i < DanceStates.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { gateway.RelayCeremonyDance(i); break; }
        }

        // 퇴장: 게스트는 0초, 호스트는 1초 늦게 — 호스트가 먼저 나가면 게스트가
        // 방장 승계 AbortMatch에 휘말려 시상식이 끊긴다 (카운트다운 표기는 동일)
        bool hostOnline = PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
        if (remaining <= (hostOnline ? -1f : 0f)) Leave();
    }

    private void LateUpdate()
    {
        // 시상식 카메라 — 조작 잠금 중이라 FPC는 손을 뗐다 (v8 단말기 카메라와 같은 소유권 규칙)
        if (!active || leaving) return;
        var cam = Camera.main;
        if (cam != null) cam.transform.SetPositionAndRotation(camPos, camRot);
    }

    private void Leave()
    {
        leaving = true;

        // 타이틀은 마우스 화면 — 커서를 UI 모드로 풀고 나간다 (PauseMenu.LeaveToTitle과 동일 규칙)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        var guard = FindFirstObjectByType<NetworkSessionGuard>();
        if (guard != null) guard.LeaveToTitle();
        else UnityEngine.SceneManagement.SceneManager.LoadScene("TitleScene");   // 오프라인 폴백
    }

    // ================= 춤 중계 수신 =================

    private void HandleDance(int pid, int danceIndex)
    {
        if (!active || pid != winnerId) return;                       // 우승자만 유효 (수신 측 검증)
        if (danceIndex < 0 || danceIndex >= DanceStates.Length) return;

        foreach (var setup in FindObjectsByType<NetworkPlayerSetup>(FindObjectsSortMode.None))
        {
            if (setup.OwnerPlayerId != winnerId) continue;
            var anim = setup.GetComponentInChildren<Animator>();
            if (anim == null) return;

            string state = DanceStates[danceIndex];
            // 같은 상태 재-CrossFade는 얼어붙는다 → 재생 중이면 Play로 처음부터 (§11)
            if (anim.GetCurrentAnimatorStateInfo(0).IsName(state) ||
                anim.GetNextAnimatorStateInfo(0).IsName(state))
                anim.Play(state, 0, 0f);
            else
                anim.CrossFade(state, 0.2f, 0, 0f);
            return;
        }
    }

    // ================= UI (코드 조립 — 씬 배선 0, TimelineFeed 방식) =================

    private void BuildOverlay(bool localIsWinner)
    {
        uiRoot = new GameObject("시상식UI");
        var canvas = uiRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        var scaler = uiRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        countdownText = NewText("퇴장카운트다운", new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-48f, -42f), new Vector2(700f, 60f), 38f, TextAlignmentOptions.TopRight);
        countdownText.gameObject.SetActive(false);   // 연출 구간이 끝나야 등장

        if (localIsWinner)
        {
            hintText = NewText("춤힌트", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 64f), new Vector2(900f, 46f), 26f, TextAlignmentOptions.Center);
            hintText.text = Loc.Get("ceremony.dancehint", "[1~6] 춤 바꾸기");
            hintText.color = new Color(1f, 0.82f, 0.4f, 0.95f);   // 앰버 — 타이틀 강조색과 톤 통일
        }
    }

    private TMP_Text NewText(string name, Vector2 anchor, Vector2 pivot,
                             Vector2 anchoredPos, Vector2 size, float fontSize, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(uiRoot.transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        var rt = tmp.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        return tmp;
    }

    // ================= 중단 복구 (방장 이탈 AbortMatch → Lobby) =================

    private void CleanupAborted()
    {
        active = false;
        StopAllCoroutines();
        if (moneyRoot != null) Destroy(moneyRoot.gameObject);
        if (uiRoot != null) Destroy(uiRoot);

        // 아바타 원상 복구 — 남은 사람들은 로비에서 계속 논다
        foreach (var kv in prevAnimState)
        {
            if (kv.Key == null) continue;
            var anim = kv.Key.GetComponentInChildren<Animator>();
            if (anim != null) anim.Play(kv.Value, 0, 0f);
            kv.Key.GetComponent<PlayerEquipment>()?.RestoreAfterKnockdown();
            var headAim = kv.Key.GetComponent<PlayerHeadAim>();
            if (headAim != null) headAim.enabled = true;
        }
        prevAnimState.Clear();

        if (!leaving && localFpc != null) localFpc.SetControlEnabled(true);
        winnerId = -1;
    }
}
