using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 실내(베팅 방 = garage)에서는 Beautify 태양 후광을 끈다 (환경/Global Volume에 부착).
/// 후광은 스크린 이펙트라 벽·지붕이 물리적으로 못 가린다 — 깊이 가림 판정은
/// 메시 스카이돔과 양립 불가(§11)라서, 로컬 카메라가 방 안이면 강도를 0으로 페이드하는 방식.
/// [멀티] 내 화면 전용 연출 — 네트워크 통신 0.
/// ⚠ 런타임의 volume.profile은 인스턴스 복사본이라 프로파일 에셋은 오염되지 않는다.
/// </summary>
[RequireComponent(typeof(Volume))]
public class SunFlareIndoorMute : MonoBehaviour
{
    [Tooltip("전환 속도 (강도/초) — 문턱을 넘을 때 후광이 자연스럽게 사라지고 돌아오는 빠르기")]
    [SerializeField] private float fadeSpeed = 0.5f;

    private Beautify.Universal.Beautify fx;
    private float outdoorIntensity;   // 야외 원값 (프로파일에서 시작 시 1회 읽음)
    private float current;
    private BettingRoom[] rooms;

    private void Start()
    {
        var volume = GetComponent<Volume>();
        if (volume == null || volume.profile == null ||
            !volume.profile.TryGet(out fx) || fx == null)
        {
            enabled = false;
            return;
        }
        outdoorIntensity = fx.sunFlaresIntensity.value;
        current = outdoorIntensity;
        rooms = FindObjectsByType<BettingRoom>(FindObjectsSortMode.None);
    }

    private void Update()
    {
        var cam = Camera.main;
        if (cam == null || fx == null) return;

        // 방 목록은 씬 로드 후 고정 — 비어 있으면 한 번 더 수집 (스폰 타이밍 대비)
        if (rooms == null || rooms.Length == 0)
            rooms = FindObjectsByType<BettingRoom>(FindObjectsSortMode.None);

        bool indoors = false;
        foreach (var r in rooms)
            if (r != null && r.ContainsPoint(cam.transform.position)) { indoors = true; break; }

        float target = indoors ? 0f : outdoorIntensity;
        if (!Mathf.Approximately(current, target))
        {
            current = Mathf.MoveTowards(current, target, fadeSpeed * Time.deltaTime);
            fx.sunFlaresIntensity.Override(current);
        }
    }
}
