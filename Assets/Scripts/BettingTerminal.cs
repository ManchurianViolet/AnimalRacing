using System.Collections;
using UnityEngine;

/// <summary>
/// 베팅 단말기. E → 카메라가 단말기 화면 앞 지정 위치로 부드럽게 이동 → 패널 열림.
/// 닫으면 카메라 원위치 복귀 + 조작권 반환.
/// 셋업: 자식으로 빈 오브젝트 CameraAnchor를 만들어 화면 잘 보이는 위치/각도로 배치 후 연결.
/// </summary>
public class BettingTerminal : MonoBehaviour, IInteractable
{
    [SerializeField] private BettingPanel panel;
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private FirstPersonController playerController;

    /// <summary>[5-2] 스폰된 내 아바타 배선 (LocalPlayerBinder가 호출).</summary>
    public void BindLocalPlayer(FirstPersonController fpc) => playerController = fpc;

    [Header("카메라 연출")]
    [Tooltip("베팅 중 카메라가 이동할 위치/각도 (단말기 화면을 바라보게 배치)")]
    [SerializeField] private Transform cameraAnchor;
    [SerializeField] private float cameraMoveSeconds = 0.5f;

    private bool occupied;
    private Camera cam;
    private Vector3 savedLocalPos;
    private Quaternion savedLocalRot;
    private Coroutine camRoutine;

    private int LocalPlayerId => NetworkPlayers.LocalPlayerId;

    // 단말기 복제 시 인스펙터 배선 누락 사고 방지 — 비어 있으면 씬에서 자동 탐색
    private void Awake()
    {
        if (matchManager == null) matchManager = FindFirstObjectByType<MatchManager>();
    }

    public string Prompt =>
        matchManager.HasSubmitted(LocalPlayerId) ? "베팅 완료됨" : "E - 베팅하기";

    private void OnEnable()  => GameEvents.OnPhaseChanged += HandlePhase;
    private void OnDisable() => GameEvents.OnPhaseChanged -= HandlePhase;

    private void HandlePhase(GamePhase p)
    {
        if (p != GamePhase.Betting && panel != null) panel.ForceClose();
    }

    public bool CanInteract() =>
        GameManager.Instance != null
        && GameManager.Instance.CurrentPhase == GamePhase.Betting
        && !occupied
        && !matchManager.HasSubmitted(LocalPlayerId);

    public void Interact()
    {
        occupied = true;
        cam = Camera.main;
        if (playerController != null) playerController.SetControlEnabled(false);

        // 현재 카메라의 로컬 자세 저장 (부모=플레이어. 조작 잠금 중이라 부모는 고정)
        savedLocalPos = cam.transform.localPosition;
        savedLocalRot = cam.transform.localRotation;

        StartCamMove(cameraAnchor.position, cameraAnchor.rotation, () =>
        {
            panel.Open(LocalPlayerId, OnPanelClosed);
        });
    }

    private void OnPanelClosed()
    {
        // 원위치(저장해둔 로컬 자세의 월드 좌표)로 복귀 후 조작권 반환
        var parent = cam.transform.parent;
        Vector3 backPos = parent != null ? parent.TransformPoint(savedLocalPos) : savedLocalPos;
        Quaternion backRot = parent != null ? parent.rotation * savedLocalRot : savedLocalRot;

        StartCamMove(backPos, backRot, () =>
        {
            cam.transform.localPosition = savedLocalPos;   // 오차 정리
            cam.transform.localRotation = savedLocalRot;
            if (playerController != null) playerController.SetControlEnabled(true);
            occupied = false;
        });
    }

    private void StartCamMove(Vector3 targetPos, Quaternion targetRot, System.Action onDone)
    {
        if (camRoutine != null) StopCoroutine(camRoutine);
        camRoutine = StartCoroutine(MoveCam(targetPos, targetRot, onDone));
    }

    private IEnumerator MoveCam(Vector3 targetPos, Quaternion targetRot, System.Action onDone)
    {
        Vector3 fromPos = cam.transform.position;
        Quaternion fromRot = cam.transform.rotation;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, cameraMoveSeconds);
            float s = Mathf.SmoothStep(0f, 1f, t);   // 부드러운 가감속 보간
            cam.transform.position = Vector3.Lerp(fromPos, targetPos, s);
            cam.transform.rotation = Quaternion.Slerp(fromRot, targetRot, s);
            yield return null;
        }

        cam.transform.position = targetPos;
        cam.transform.rotation = targetRot;
        camRoutine = null;
        onDone?.Invoke();
    }
}
