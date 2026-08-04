using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// 빠따 피격 → 쓰러짐/기상 상태 전담 (NetPlayer 프리팹에 부착).
/// 흐름: 맞음(RpcKnockdown) → 쓰러지는 애니(전신, 기본 레이어) → 누움(마지막 프레임 유지)
///       → 아무 키 → 기상 애니 → 조작 복귀 + 무적(GameConfig.knockdownInvulnSeconds).
/// - 쓰러진 동안: 이동/시점/공격/슬롯/상호작용 전부 잠금, 무장 레이어·소품도 끔 (순수하게 누움)
/// - 1인칭 카메라: 머리를 따라 내려가며 하늘을 보게 기울었다가, 기상 중에 원래 시점으로 복귀
/// - 동기화: 전부 RPC 로컬 재생 (판정은 때린 클라이언트가 부채꼴 검사 후 피해자 RPC 호출)
/// </summary>
public class PlayerKnockdown : MonoBehaviourPun
{
    private enum State { Standing, Falling, Down, GettingUp }

    [SerializeField] private FirstPersonController fpc;
    [SerializeField] private PlayerEquipment equipment;
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private Animator animator;

    [Tooltip("기상 애니 재생 배속 — 컨트롤러 GetUp 상태의 speed와 같아야 함")]
    [SerializeField] private float getUpSpeed = 2f;

    [Tooltip("쓰러진 뒤 카메라가 하늘을 향해 기우는 속도")]
    [SerializeField] private float cameraTiltSpeed = 2.5f;

    private State state = State.Standing;
    private float invulnUntil;
    private float fallLength = 2.1f, getUpLength = 7.6f;   // Awake에서 클립 실측으로 갱신

    /// <summary>쓰러짐/기상 중 여부 (입력·아이템·스윙 차단용).</summary>
    public bool IsDown => state != State.Standing;

    /// <summary>지금 맞을 수 있는 상태인가 (서 있고 무적 아님).</summary>
    public bool CanBeHit => state == State.Standing && Time.time >= invulnUntil;

    private bool IsMineAvatar =>
        !PhotonNetwork.IsConnected || photonView == null || photonView.IsMine;

    private void Awake()
    {
        if (fpc == null) fpc = GetComponent<FirstPersonController>();
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();
        if (interactor == null) interactor = GetComponent<PlayerInteractor>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);

        // 클립 길이 실측 (컨트롤러에서 이름으로)
        var rc = animator != null ? animator.runtimeAnimatorController : null;
        if (rc != null)
            foreach (var c in rc.animationClips)
            {
                if (c.name == "Stunned") fallLength = c.length;
                if (c.name == "Getting Up") getUpLength = c.length;
            }
    }

    private void Update()
    {
        // 누워 있는 상태에서 아무 키나 → 기상
        if (state == State.Down && IsMineAvatar && Input.anyKeyDown)
        {
            if (PhotonNetwork.InRoom && photonView != null)
                photonView.RPC(nameof(RpcGetUp), RpcTarget.All);
            else
                RpcGetUp();
        }
    }

    /// <summary>때린 쪽이 호출하는 진입점 — 전 클라에 쓰러짐 방송.</summary>
    public void RequestKnockdown()
    {
        if (PhotonNetwork.InRoom && photonView != null)
            photonView.RPC(nameof(RpcKnockdown), RpcTarget.All);
        else
            RpcKnockdown();
    }

    [PunRPC]
    private void RpcKnockdown()
    {
        if (!CanBeHit) return;   // 이미 누웠거나 무적이면 무시 (동시 타격 방어)
        state = State.Falling;

        if (animator != null)
            animator.CrossFadeInFixedTime("Knockdown", 0.1f, 0);   // 전신 (기본 레이어)
        if (equipment != null) equipment.SuppressForKnockdown();

        if (IsMineAvatar)
        {
            fpc.InputLocked = true;
            if (interactor != null) interactor.enabled = false;
        }

        StopAllCoroutines();
        StartCoroutine(FallToDown());
    }

    private IEnumerator FallToDown()
    {
        yield return new WaitForSeconds(fallLength);   // 쓰러지는 애니 끝난 뒤부터 기상 키 허용
        if (state == State.Falling) state = State.Down;
    }

    [PunRPC]
    private void RpcGetUp()
    {
        if (state != State.Down) return;
        state = State.GettingUp;

        if (animator != null)
            animator.CrossFadeInFixedTime("GetUp", 0.15f, 0);

        StopAllCoroutines();
        StartCoroutine(GetUpToStanding());
    }

    private IEnumerator GetUpToStanding()
    {
        yield return new WaitForSeconds(getUpLength / Mathf.Max(getUpSpeed, 0.01f));
        state = State.Standing;

        var cfg = GameManager.Instance != null ? GameManager.Instance.Config : null;
        invulnUntil = Time.time + (cfg != null ? cfg.knockdownInvulnSeconds : 3f);

        if (equipment != null) equipment.RestoreAfterKnockdown();
        if (IsMineAvatar)
        {
            fpc.InputLocked = false;
            if (interactor != null) interactor.enabled = true;
        }
    }

    // 쓰러진 동안 1인칭 카메라 연출 — 위치는 FPC가 머리 본을 따라가고, 여기선 회전만 겹쳐 쓴다
    private void LateUpdate()
    {
        if (!IsMineAvatar || fpc == null || fpc.CameraPivot == null) return;
        var pivot = fpc.CameraPivot;

        if (state == State.Falling || state == State.Down)
        {
            // 하늘 보기 (뒤통수가 진행 방향 반대로 눕는 그림)
            var target = Quaternion.LookRotation(Vector3.up, -transform.forward);
            pivot.rotation = Quaternion.Slerp(pivot.rotation, target, cameraTiltSpeed * Time.deltaTime);
        }
        else if (state == State.GettingUp)
        {
            // 기상하는 동안 원래 시점(마우스 각도)으로 서서히 복귀 → 잠금 해제 순간 스냅 없음
            var target = transform.rotation * Quaternion.Euler(fpc.Pitch, 0f, 0f);
            pivot.rotation = Quaternion.Slerp(pivot.rotation, target, cameraTiltSpeed * 2f * Time.deltaTime);
        }
    }
}
