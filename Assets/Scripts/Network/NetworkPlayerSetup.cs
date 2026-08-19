using Photon.Pun;
using TMPro;
using UnityEngine;

/// <summary>
/// 스폰된 플레이어의 "내 것/남의 것" 분기 + 내 것이면 씬 배선, 남의 것이면 닉네임 표시.
/// </summary>
public class NetworkPlayerSetup : MonoBehaviourPunCallbacks
{
    [Header("내 것일 때만 켤 것들")]
    [SerializeField] private FirstPersonController controller;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener audioListener;
    [SerializeField] private PlayerInteractor interactor;

    [Header("남의 것일 때만: 머리 위 닉네임 (월드 TMP)")]
    [SerializeField] private TMP_Text nameLabel;

    [Header("외형 (커스터마이징)")]
    [SerializeField] private CharacterCustomization look;

    private bool isRemote;

    /// <summary>
    /// 이 아바타의 주인 playerId (오프라인 = 0, 온라인 = ActorNumber) —
    /// CeremonyDirector가 로스터 ↔ 아바타 매핑에 사용.
    /// </summary>
    public int OwnerPlayerId =>
        !PhotonNetwork.IsConnected || photonView.Owner == null
            ? NetworkPlayers.LocalPlayerId
            : photonView.Owner.ActorNumber;

    private void Awake()
    {
        // 오프라인(미접속)은 무조건 내 것 — 접속이 없으면 PhotonView의 소유자가
        // null이라 IsMine이 false로 나오는 함정이 있음 (원격 취급 → 카메라/조작 꺼짐)
        bool mine = !Photon.Pun.PhotonNetwork.IsConnected || photonView.IsMine;
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

        ApplyLook(mine);

        // 플레이어-동물 충돌 무시 (기획 확정: 유령 통과) — 매치 중 재접속 복귀 아바타도 커버
        FindFirstObjectByType<RaceManager>()?.IgnorePlayerCollisions();
    }

    /// <summary>내 것이면 이 컴퓨터에 저장된 외형을, 남의 것이면 그 사람이 방송한 외형을 입힌다.</summary>
    private void ApplyLook(bool mine)
    {
        if (look == null) return;

        // 코드가 비어 있어도 기본 차림을 입힌다 — 예전엔 이때 아무것도 안 해서
        // Awake가 입힌 "이 컴퓨터의 저장 옷"이 남의 아바타에 그대로 남는 버그가 있었다
        string code = mine ? PlayerLook.Local : PlayerLook.Of(photonView.Owner);
        look.ApplyCode(code);

        // 타이틀에서 못 올렸거나(오프라인 시작 등) 값이 바뀐 경우를 위한 보강
        if (mine) PlayerLook.Publish();
    }

    /// <summary>남이 매치 중에 외형을 바꿔도 따라가게 (지금은 타이틀에서만 바꾸지만 안전장치).</summary>
    public override void OnPlayerPropertiesUpdate(Photon.Realtime.Player target,
                                                  ExitGames.Client.Photon.Hashtable changed)
    {
        if (look == null || !isRemote || photonView.Owner == null) return;
        if (target.ActorNumber != photonView.Owner.ActorNumber) return;
        if (!changed.ContainsKey(PlayerLook.PropKey)) return;

        look.ApplyCode(PlayerLook.Of(target));
    }

    private void LateUpdate()
    {
        // 원격 닉네임은 항상 내 카메라를 향하게 (빌보드)
        if (!isRemote || nameLabel == null || Camera.main == null) return;
        nameLabel.transform.rotation =
            Quaternion.LookRotation(nameLabel.transform.position - Camera.main.transform.position);
    }
}
