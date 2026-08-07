using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>방 목록 한 줄: 방이름 · 인원 · 라운드 · 자물쇠 + 입장 버튼.</summary>
public class RoomListItem : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text infoText;
    [SerializeField] private GameObject lockIcon;   // 비밀방 표시 (선택)
    [SerializeField] private Button joinButton;

    public void Bind(RoomInfo room, System.Action onJoin)
    {
        bool hasPw = room.CustomProperties.TryGetValue(NetworkLauncher.PropPassword, out var pw)
                     && !string.IsNullOrEmpty((string)pw);
        int rounds = room.CustomProperties.TryGetValue(NetworkLauncher.PropRounds, out var r)
                     ? (int)r : 3;

        if (nameText != null) nameText.text = room.Name;
        if (infoText != null) infoText.text = $"{room.PlayerCount}/{room.MaxPlayers}명 · {rounds}라운드";
        if (lockIcon != null) lockIcon.SetActive(hasPw);

        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(() => onJoin());
        joinButton.interactable = room.PlayerCount < room.MaxPlayers && room.IsOpen;
    }
}
