using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// 빠따 피격 → 쓰러짐/기상 상태 전담 (NetPlayer 프리팹에 부착).
/// 흐름: 맞음(RpcKnockdown) → 쓰러지는 애니(전신, 기본 레이어) → 누움(마지막 프레임 유지)
///       → 아무 키 → 기상 애니 → 조작 복귀 + 무적(GameConfig.knockdownInvulnSeconds).
/// - 쓰러진 동안: 이동/시점/공격/슬롯/상호작용 전부 잠금, 무장 레이어·소품도 끔 (순수하게 누움)
/// - 1인칭 카메라: 쓰러짐~누움 동안 눈을 얼굴 위(하늘 쪽)로 빼며 하늘을 보게 기울인다(몸 관통 방지),
///   기상 중에 눈 위치·시점 모두 원래대로 복귀 (기상 중 관통은 수용 — v7 결정)
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
    [SerializeField] private float getUpSpeed = 1.5f;

    [Tooltip("쓰러진 뒤 카메라가 하늘을 향해 기우는 속도")]
    [SerializeField] private float cameraTiltSpeed = 2.5f;

    [Tooltip("쓰러지는 동안 카메라가 머리 본 회전을 따라가는 속도 — 높을수록 애니와 밀착(격렬), 낮을수록 부드러움")]
    [SerializeField] private float fallCamFollowSpeed = 8f;

    [Tooltip("맞은 뒤 몸이 바닥에 닿는 시점 (초) — 여기서 '쿵' 소리를 낸다. 애니를 보며 맞추면 됨")]
    [SerializeField] private float thudDelay = 0.45f;

    private State state = State.Standing;
    private float invulnUntil;
    private float fallLength = 2.1f, getUpLength = 7.6f;   // Awake에서 클립 실측으로 갱신

    private Transform headBoneForCam;      // 쓰러짐 시점 추종용 머리 본
    private Quaternion headToViewLocal;    // 서 있는 자세 기준 "머리 본 회전 → 시점 회전" 보정값
    private bool headToViewCaptured;

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

        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "Head") { headBoneForCam = t; break; }

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

        // 타격음 — 이 RPC는 전 클라 재생이라 때린 쪽·맞은 쪽·구경꾼 모두 같은 자리에서 듣는다 (3D).
        // (때린 쪽 로컬 판정에 넣으면 정작 맞은 사람 화면이 조용하다)
        SoundManager.PlaySfx(SfxId.BatHit, transform.position);

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
        // 몸이 바닥에 닿는 "쿵" — 타격음(즉시)과 겹치지 않게 한 박자 뒤 (3D)
        float thud = Mathf.Clamp(thudDelay, 0f, fallLength);
        yield return new WaitForSeconds(thud);
        SoundManager.PlaySfx(SfxId.Knockdown, transform.position);

        yield return new WaitForSeconds(fallLength - thud);   // 쓰러지는 애니 끝난 뒤부터 기상 키 허용
        if (state == State.Falling) state = State.Down;
    }

    [PunRPC]
    private void RpcGetUp()
    {
        if (state != State.Down) return;
        state = State.GettingUp;

        SoundManager.PlaySfx(SfxId.GetUp, transform.position);   // 전 클라 재생 (3D)

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

    // 쓰러진 동안 1인칭 카메라 연출 — 회전과 눈 위치 블렌드(LieEyeBlend)를 굴린다.
    // 회전(v7 B안): 쓰러지는 동안 카메라가 머리 본 회전을 부드럽게 추종 — 애니가 바닥을 봤다가
    //   하늘로 넘어가는 고갯짓이 화면에 그대로 나온다 ("리얼해야 해" — 유저 결정. 평시에는 여전히
    //   머리 회전을 안 따라감(멀미 법칙), 쓰러짐 한정 예외).
    // 눈 위치: 쓰러지는 동안 얼굴 위로 — 몸 앞 오프셋 눈이 가슴/몸통을 관통해 뚫려 보이는 것 방지
    //   (쓰러질 때는 깨끗하게, 기상 때 관통은 수용 — 블렌드를 기상에서 도로 0으로).
    private void LateUpdate()
    {
        if (!IsMineAvatar || fpc == null || fpc.CameraPivot == null) return;
        var pivot = fpc.CameraPivot;

        // 서 있는 첫 프레임에 "머리 본 → 시점" 보정값 역산 (머리가 중립일 때 시점 = 몸통 정면)
        if (!headToViewCaptured && headBoneForCam != null && state == State.Standing)
        {
            headToViewLocal = Quaternion.Inverse(headBoneForCam.rotation) * transform.rotation;
            headToViewCaptured = true;
        }

        if (state == State.Falling || state == State.Down)
        {
            fpc.LieEyeBlend = Mathf.MoveTowards(fpc.LieEyeBlend, 1f, cameraTiltSpeed * Time.deltaTime);
            var target = (headToViewCaptured && headBoneForCam != null)
                ? headBoneForCam.rotation * headToViewLocal                    // 머리 본 추종 (바닥→하늘 그대로)
                : Quaternion.LookRotation(Vector3.up, -transform.forward);     // 폴백: 고정 하늘 보기
            pivot.rotation = Quaternion.Slerp(pivot.rotation, target, fallCamFollowSpeed * Time.deltaTime);
        }
        else if (state == State.GettingUp)
        {
            fpc.LieEyeBlend = Mathf.MoveTowards(fpc.LieEyeBlend, 0f, cameraTiltSpeed * 2f * Time.deltaTime);
            // 기상하는 동안 원래 시점(마우스 각도)으로 서서히 복귀 → 잠금 해제 순간 스냅 없음
            var target = transform.rotation * Quaternion.Euler(fpc.Pitch, 0f, 0f);
            pivot.rotation = Quaternion.Slerp(pivot.rotation, target, cameraTiltSpeed * 2f * Time.deltaTime);
        }
        else if (fpc.LieEyeBlend > 0f)
        {
            // 안전망 — 어떤 경로로든 서 있으면 눈 위치를 평소로
            fpc.LieEyeBlend = Mathf.MoveTowards(fpc.LieEyeBlend, 0f, cameraTiltSpeed * 2f * Time.deltaTime);
        }
    }
}
