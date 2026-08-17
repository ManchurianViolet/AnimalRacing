using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [타이틀] 진입 인트로 — 카메라가 뒤에서 제자리로 다가온 뒤에야 UI(로고·메뉴·서버바)가 뜬다.
///
/// 씬에 저장된 카메라 포즈가 곧 "도착 지점"이다 (유저가 잡아둔 타이틀 앵글을 단일 출처로 삼는다 —
/// 도착 좌표를 따로 적어두면 카메라를 옮길 때마다 두 곳을 고쳐야 한다).
/// 시작 지점은 거기서 startOffset만큼 물러난 자리.
///
/// ⚠ 실행 순서를 뒤로 미뤄둔 이유: TitleMenu.Start()가 ShowPanel(mainPanel)로 메뉴를 켜므로,
///    그보다 먼저 끄면 도로 켜져서 첫 프레임에 UI가 깜빡인다.
/// </summary>
[DefaultExecutionOrder(200)]
public class TitleIntro : MonoBehaviour
{
    [SerializeField] private Camera cam;                       // 비면 Camera.main

    [Header("카메라 이동")]
    [Tooltip("도착 지점 기준 시작 오프셋(월드). z 음수 = 뒤, y 양수 = 위에서 내려옴")]
    [SerializeField] private Vector3 startOffset = new Vector3(0f, 2f, -22f);
    [Tooltip("뒤에서 제자리까지 오는 시간")]
    [SerializeField] private float moveSeconds = 2.6f;
    [Tooltip("도착 후 UI가 뜨기까지의 뜸")]
    [SerializeField] private float holdAfterArrive = 0.15f;

    [Header("UI")]
    [Tooltip("도착 후 뜰 것들 — 로고 / 메인 메뉴 / 서버바")]
    [SerializeField] private GameObject[] uiToShow;
    [SerializeField] private float uiFadeSeconds = 0.5f;

    [Tooltip("아무 키·클릭으로 인트로 건너뛰기 (매번 보면 지겨우므로 기본 켬)")]
    [SerializeField] private bool skippable = true;

    /// <summary>인트로 재생 중 — 다른 시스템이 참조할 수 있게 공개.</summary>
    public static bool Playing { get; private set; }

    private Vector3 homePos;
    private Quaternion homeRot;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) { enabled = false; return; }

        // 씬에 저장된 포즈 = 도착 지점. Awake에서 잡아둬야 다른 스크립트가 카메라를 만지기 전 값이다.
        homePos = cam.transform.position;
        homeRot = cam.transform.rotation;

        // 첫 프레임부터 뒤에 있어야 한다 (Start까지 기다리면 제자리가 한 프레임 보인다)
        cam.transform.position = homePos + startOffset;
        SetUiActive(false);
        Playing = true;
    }

    private void Start() => StartCoroutine(Run());

    private IEnumerator Run()
    {
        // TitleMenu.Start()가 메뉴를 켜 놨을 수 있으니 한 프레임 뒤 한 번 더 끈다
        yield return null;
        SetUiActive(false);

        Vector3 from = homePos + startOffset;
        float t = 0f;
        while (t < 1f)
        {
            if (skippable && Input.anyKeyDown) break;
            t += Time.deltaTime / Mathf.Max(0.01f, moveSeconds);
            // ease-out cubic — 뒤에서 빠르게 출발해 제자리에서 부드럽게 선다
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            cam.transform.position = Vector3.Lerp(from, homePos, e);
            yield return null;
        }

        cam.transform.position = homePos;
        cam.transform.rotation = homeRot;

        if (holdAfterArrive > 0f) yield return new WaitForSeconds(holdAfterArrive);
        yield return ShowUi();
        Playing = false;
    }

    private void SetUiActive(bool on)
    {
        if (uiToShow == null) return;
        foreach (var go in uiToShow) if (go != null) go.SetActive(on);
    }

    /// <summary>CanvasGroup으로 부드럽게 등장. 그룹이 없으면 런타임에 붙인다(씬은 안 건드림).</summary>
    private IEnumerator ShowUi()
    {
        var groups = new List<CanvasGroup>();
        foreach (var go in uiToShow)
        {
            if (go == null) continue;
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;                 // 켜기 전에 투명으로 — 안 그러면 한 프레임 불쑥 보인다
            groups.Add(cg);
            go.SetActive(true);
        }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, uiFadeSeconds);
            float a = Mathf.Clamp01(t);
            foreach (var cg in groups) if (cg != null) cg.alpha = a;
            yield return null;
        }
        foreach (var cg in groups) if (cg != null) cg.alpha = 1f;
    }
}
