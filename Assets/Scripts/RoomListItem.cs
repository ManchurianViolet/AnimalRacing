using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 방 목록 한 줄 — v13 스크롤 개편: 열 고정 배치 (방 이름 | 인원 | 라운드 | 잠금 | 참가 버튼).
/// 잠금은 전용 열이라 비밀방/오픈방이 섞여도 줄이 안 흔들린다. 열 x좌표는 헤더(ListHeader)와 맞춰 유지할 것.
/// </summary>
public class RoomListItem : MonoBehaviour
{
    private static readonly Color LockOn = new(0.737f, 0.482f, 0.247f);   // 앰버
    private static readonly Color LockOff = new(0.45f, 0.45f, 0.45f);

    [SerializeField] private TMP_Text nameText;      // 말줄임
    [SerializeField] private TMP_Text playersText;   // "2/4"
    [SerializeField] private TMP_Text roundsText;    // "3"
    [SerializeField] private TMP_Text lockText;      // "잠금" / "—"
    [SerializeField] private Button joinButton;

    public void Bind(RoomInfo room, System.Action onJoin)
    {
        bool hasPw = room.CustomProperties.TryGetValue(NetworkLauncher.PropPassword, out var pw)
                     && !string.IsNullOrEmpty((string)pw);
        int rounds = room.CustomProperties.TryGetValue(NetworkLauncher.PropRounds, out var r)
                     ? (int)r : 3;

        if (nameText != null) nameText.text = room.Name;
        if (playersText != null) playersText.text = $"{room.PlayerCount}/{room.MaxPlayers}";
        if (roundsText != null) roundsText.text = rounds.ToString();
        if (lockText != null)
        {
            lockText.text = hasPw ? "잠금" : "—";
            lockText.color = hasPw ? LockOn : LockOff;
        }

        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(() => onJoin());
        joinButton.interactable = room.PlayerCount < room.MaxPlayers && room.IsOpen;
    }
}
