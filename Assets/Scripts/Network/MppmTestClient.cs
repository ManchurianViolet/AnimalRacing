#if UNITY_EDITOR
using System.Linq;
using Photon.Pun;
using Unity.Multiplayer.PlayMode;
using UnityEngine;

/// <summary>
/// [테스트 전용 · 빌드 제외] 멀티플레이어 플레이 모드의 "가상 플레이어" 창에서만 살아난다.
///
/// 가상 플레이어는 본체 에디터와 같은 PlayerPrefs(레지스트리)를 읽기 때문에
/// 그냥 두면 닉네임도 커마 외형도 전부 똑같아서 동기화가 되는지 안 되는지 알 수가 없다.
/// 그래서 창마다 ① 다른 닉네임 ② 확연히 다른 옷을 입히고 ③ 방에 자동 입장시킨다.
///
/// 창 번호는 MPPM 창에서 붙인 태그의 숫자(P2, P3...)를 쓰고, 태그가 없으면 자동으로 갈라준다.
/// </summary>
public class MppmTestClient : MonoBehaviour
{
    // [몸,표정,상의,하의,소품,안경,장갑,머리,모자,신발] — 멀리서도 구분되게 상의/모자 위주로 다르게
    private static readonly string[] LookPresets =
    {
        "0,1,4,2,1,-1,-1,2,-1,0",   // 마스코트 인형 + 반바지 + 헤드폰
        "0,0,0,1,0,1,-1,0,3,2",     // 정장 + 광대코 + 안경 + 모자4
        "0,2,3,0,2,-1,0,1,1,1",     // 상의4 + 콧수염A + 장갑 + 모자2
    };

    private int index;
    private float retryTimer;
    private bool joined;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (CurrentPlayer.IsMainEditor) return;   // 본체 에디터는 건드리지 않는다

        var go = new GameObject("[MPPM 테스트 클라이언트]");
        go.AddComponent<MppmTestClient>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        index = ResolveIndex();
        PlayerLook.Override = LookPresets[(index - 2 + LookPresets.Length) % LookPresets.Length];
        Debug.Log($"[MPPM] 가상 플레이어 #{index} 시작 — 외형 {PlayerLook.Override}");
    }

    private void Update()
    {
        // TitleMenu가 저장된 닉네임으로 계속 되돌리므로 매 프레임 덮어쓴다 (저장은 하지 않음)
        string want = $"제비#{index}";
        if (PhotonNetwork.NickName != want) PhotonNetwork.NickName = want;

        if (joined || !PhotonNetwork.InLobby) return;

        retryTimer -= Time.deltaTime;
        if (retryTimer > 0f) return;
        retryTimer = 1.5f;

        var launcher = FindFirstObjectByType<NetworkLauncher>();
        if (launcher == null) return;

        // 비밀번호 없는 첫 번째 방에 자동 입장 (호스트가 만든 방)
        var room = launcher.Rooms.Values.FirstOrDefault(r =>
            r.IsOpen && r.IsVisible && r.PlayerCount > 0 &&
            (!r.CustomProperties.TryGetValue(NetworkLauncher.PropPassword, out var pw) ||
             string.IsNullOrEmpty(pw as string)));
        if (room == null) return;

        Debug.Log($"[MPPM] '{room.Name}' 자동 입장 시도");
        PlayerLook.Publish();          // 내 외형을 먼저 올려두고
        launcher.JoinRoom(room.Name);
        joined = true;
    }

    /// <summary>창 번호: MPPM 태그의 숫자 우선, 없으면 클론 폴더 경로로 갈라낸다.</summary>
    private static int ResolveIndex()
    {
        foreach (var tag in CurrentPlayer.ReadOnlyTags())
        {
            string digits = new string(tag.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int n) && n >= 2) return n;
        }
        int h = Mathf.Abs(Application.dataPath.GetHashCode());
        return 2 + (h % 3);
    }
}
#endif
