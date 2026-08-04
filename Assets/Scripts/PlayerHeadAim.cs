using Photon.Pun;
using UnityEngine;

/// <summary>
/// 아바타 머리 본을 시선 상하각(pitch)대로 기울인다 (NetPlayer 프리팹에 부착).
/// - 내 것: FirstPersonController의 pitch를 그대로 사용.
/// - 남의 것: OnPhotonSerializeView로 받은 값을 부드럽게 따라감
///   (⚠ PhotonView Observed 목록에 이 컴포넌트가 들어가야 동기화됨 — 동기화 구성 변경이라 실빌드 갱신 필요).
/// 좌우는 몸통 전체가 도니까(yaw=transform 회전) 머리는 상하만 담당한다.
/// 카메라는 머리 본의 "위치"만 빌려 쓰므로(회전 무관) 이 회전이 시점을 흔들지 않는다.
/// </summary>
public class PlayerHeadAim : MonoBehaviourPun, IPunObservable
{
    [SerializeField] private FirstPersonController fpc;

    [Tooltip("머리가 기울 수 있는 최대 각도 (도) — 목 꺾임 방지")]
    [SerializeField] private float maxPitch = 55f;

    [Tooltip("원격 아바타가 수신값을 따라가는 속도 (도/초)")]
    [SerializeField] private float remoteFollowSpeed = 360f;

    private Transform head;
    private float pitch;         // 화면에 적용 중인 각
    private float targetPitch;   // 원격: 마지막 수신값

    private void Awake()
    {
        if (fpc == null) fpc = GetComponent<FirstPersonController>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "Head") { head = t; break; }
        if (head == null) Debug.LogWarning("[머리시선] Head 본을 못 찾음");
    }

    private bool IsMineAvatar =>
        !PhotonNetwork.IsConnected || photonView == null || photonView.IsMine;

    // 애니메이터가 본을 갱신한 뒤에 겹쳐 써야 하므로 LateUpdate
    private void LateUpdate()
    {
        if (head == null) return;

        if (IsMineAvatar && fpc != null)
            pitch = fpc.Pitch;
        else
            pitch = Mathf.MoveTowards(pitch, targetPitch, remoteFollowSpeed * Time.deltaTime);

        float p = Mathf.Clamp(pitch, -maxPitch, maxPitch);
        head.rotation = Quaternion.AngleAxis(p, transform.right) * head.rotation;
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting) stream.SendNext(pitch);
        else targetPitch = (float)stream.ReceiveNext();
    }
}
