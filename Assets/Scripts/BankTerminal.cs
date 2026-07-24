using System.Collections;
using UnityEngine;

/// <summary>
/// ATM 단말기. 베팅 페이즈 동안 E → 카메라 이동 → BankPanel.
/// BettingTerminal과 같은 패턴. 루트에 Collider 필요.
/// </summary>
public class BankTerminal : MonoBehaviour, IInteractable
{
    [SerializeField] private BankPanel panel;
    [SerializeField] private MatchManager matchManager;
    [SerializeField] private FirstPersonController playerController;

    [Header("카메라 연출")]
    [SerializeField] private Transform cameraAnchor;
    [SerializeField] private float cameraMoveSeconds = 0.5f;

    private bool occupied;
    private Camera cam;
    private Vector3 savedLocalPos;
    private Quaternion savedLocalRot;
    private Coroutine camRoutine;

    private int LocalPlayerId => NetworkPlayers.LocalPlayerId;

    public string Prompt => "E - ATM";

    private void OnEnable()  => GameEvents.OnPhaseChanged += HandlePhase;
    private void OnDisable() => GameEvents.OnPhaseChanged -= HandlePhase;

    private void HandlePhase(GamePhase p)
    {
        if (p != GamePhase.Betting && panel != null) panel.ForceClose();
    }

    public bool CanInteract() =>
        GameManager.Instance != null
        && GameManager.Instance.CurrentPhase == GamePhase.Betting
        && !occupied;

    public void Interact()
    {
        occupied = true;
        cam = Camera.main;
        if (playerController != null) playerController.SetControlEnabled(false);

        savedLocalPos = cam.transform.localPosition;
        savedLocalRot = cam.transform.localRotation;

        StartCamMove(cameraAnchor.position, cameraAnchor.rotation, () =>
        {
            panel.Open(LocalPlayerId, OnPanelClosed);
        });
    }

    private void OnPanelClosed()
    {
        var parent = cam.transform.parent;
        Vector3 backPos = parent != null ? parent.TransformPoint(savedLocalPos) : savedLocalPos;
        Quaternion backRot = parent != null ? parent.rotation * savedLocalRot : savedLocalRot;

        StartCamMove(backPos, backRot, () =>
        {
            cam.transform.localPosition = savedLocalPos;
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
            float s = Mathf.SmoothStep(0f, 1f, t);
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
