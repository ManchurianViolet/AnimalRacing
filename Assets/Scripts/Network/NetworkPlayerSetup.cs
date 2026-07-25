using Photon.Pun;
using TMPro;
using UnityEngine;

/// <summary>
/// 스폰된 플레이어의 "내 것/남의 것" 분기 + 내 것이면 씬 배선, 남의 것이면 닉네임 표시.
/// </summary>
public class NetworkPlayerSetup : MonoBehaviourPun
{
    [Header("내 것일 때만 켤 것들")]
    [SerializeField] private FirstPersonController controller;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener audioListener;
    [SerializeField] private PlayerInteractor interactor;

    [Header("남의 것일 때만: 머리 위 닉네임 (월드 TMP)")]
    [SerializeField] private TMP_Text nameLabel;

    private bool isRemote;

    private void Awake()
    {
        // 오프라인(방 밖)에선 IsMine이 true → 싱글도 "내 것" 흐름 그대로
        bool mine = photonView.IsMine;
        isRemote = !mine;

        if (controller != null) controller.enabled = mine;
        if (playerCamera != null) playerCamera.gameObject.SetActive(mine);
        if (audioListener != null) audioListener.enabled = mine;
        if (interactor != null) interactor.enabled = mine;

        if (!mine && TryGetComponent<Rigidbody>(out var rb))
            rb.isKinematic = true;

        if (nameLabel != null)
        {
            bool showName = !mine && photonView.Owner != null;
            nameLabel.gameObject.SetActive(showName);
            if (showName) nameLabel.text = photonView.Owner.NickName;
        }

        if (mine)
        {
            gameObject.name = "Player(나)";
            FindFirstObjectByType<LocalPlayerBinder>()?.BindLocalPlayer(gameObject);
        }
        else
        {
            gameObject.name = $"Player(원격 {photonView.Owner?.NickName})";
        }
    }

    private void LateUpdate()
    {
        // 원격 닉네임은 항상 내 카메라를 향하게 (빌보드)
        if (!isRemote || nameLabel == null || Camera.main == null) return;
        nameLabel.transform.rotation =
            Quaternion.LookRotation(nameLabel.transform.position - Camera.main.transform.position);
    }
}
