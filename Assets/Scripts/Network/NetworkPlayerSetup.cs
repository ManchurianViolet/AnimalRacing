using Photon.Pun;
using UnityEngine;

/// <summary>
/// [멀티 2단계] 스폰된 플레이어의 "내 것/남의 것" 분기.
/// 내 아바타: 조작/카메라 켬. 남의 아바타: 전부 꺼서 껍데기(모델+동기화)만 남김.
/// 이거 없으면 내 키보드로 모든 캐릭터가 동시에 움직이는 대참사가 남.
/// </summary>
public class NetworkPlayerSetup : MonoBehaviourPun
{
    [Header("내 것일 때만 켤 것들")]
    [SerializeField] private FirstPersonController controller;
    [SerializeField] private Camera playerCamera;          // 자식 카메라
    [SerializeField] private AudioListener audioListener;  // 카메라에 붙은 것
    [SerializeField] private PlayerInteractor interactor;  // 없으면 비워둠

    private void Awake()
    {
        bool mine = photonView.IsMine;

        if (controller != null) controller.enabled = mine;
        if (playerCamera != null) playerCamera.gameObject.SetActive(mine);
        if (audioListener != null) audioListener.enabled = mine;
        if (interactor != null) interactor.enabled = mine;

        // 남의 아바타의 Rigidbody는 물리 계산 대상이 아니라 "받아쓰기" 대상
        if (!mine && TryGetComponent<Rigidbody>(out var rb))
            rb.isKinematic = true;

        gameObject.name = mine ? "Player(나)" : $"Player(원격 {photonView.Owner.NickName})";
    }
}
