using UnityEngine;

/// <summary>
/// [호랑이] 포효 연출 — 발동 순간 머리 본을 확 키우고 살짝 앞으로 내밀어 "소리 지르는" 그림.
/// 발동 감지는 전 클라로 이미 중계되는 스킬 피드(OnSkillProc) 문자열 — "OO의 포효"로 시작하면
/// 그 OO(내 DisplayName)일 때 재생. 네트워크 추가 통신 0 (BoostDustFx의 OnItemUsed 구독과 같은 철학).
/// 본 스케일은 애니메이터가 안 건드리는 채널이라 안전, 전방 오프셋은 LateUpdate에서 포즈 위에 덧셈.
/// RaceManager가 호랑이에만 부착.
/// </summary>
public class RoarFx : MonoBehaviour
{
    private Racer racer;
    private GameConfig config;
    private Transform head;
    private Vector3 headBaseScale = Vector3.one;
    private Vector3 headBaseLocalPos;   // 클립이 본 위치를 안 쓰므로 기준점을 잡아두고 매번 새로 계산
    private float timer = -1f;   // -1 = 대기

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        this.config = config;

        head = FindHeadBone();
        if (head != null)
        {
            headBaseScale = head.localScale;
            headBaseLocalPos = head.localPosition;
        }
        else Debug.LogWarning($"[RoarFx] {name}: 머리 본을 못 찾음 (연출 생략)");
    }

    /// <summary>
    /// 머리 본 탐색 — 리그마다 이름이 다르다 (6종은 "scull"인데 호랑이만 없고 spine.012가 머리).
    /// 이름 → 턱(jaw)의 부모 → 척추 말단 순으로 구조에 기대어 찾는다.
    /// </summary>
    private Transform FindHeadBone()
    {
        var bones = GetComponentsInChildren<Transform>(true);

        foreach (var tr in bones)
        {
            string n = tr.name.ToLowerInvariant();
            if (n == "scull" || n == "skull" || n == "head") return tr;
        }

        // 턱이 달린 본이 곧 머리 (호랑이: jaw의 부모 = spine.012)
        foreach (var tr in bones)
            if (tr.name.ToLowerInvariant() == "jaw" && tr.parent != null) return tr.parent;

        // 최후 폴백: 가장 깊은 spine.N (목 끝 = 머리)
        Transform deepest = null;
        int bestIdx = -1;
        foreach (var tr in bones)
        {
            if (!tr.name.StartsWith("spine.")) continue;
            if (int.TryParse(tr.name.Substring(6), out int idx) && idx > bestIdx)
            { bestIdx = idx; deepest = tr; }
        }
        return deepest;
    }

    private void OnEnable()  => GameEvents.OnSkillProc += HandleSkillProc;
    private void OnDisable() => GameEvents.OnSkillProc -= HandleSkillProc;

    private void HandleSkillProc(string line)
    {
        if (racer == null || head == null || string.IsNullOrEmpty(line)) return;
        if (line.StartsWith(racer.DisplayName + "의 포효")) timer = 0f;
    }

    private void LateUpdate()
    {
        if (timer < 0f || head == null || config == null) return;

        timer += Time.deltaTime;
        float total = Mathf.Max(0.5f, config.roarFxSeconds);
        float rise = 0.2f, fall = 0.4f;

        if (timer >= total)
        {
            head.localScale = headBaseScale;   // 원상 복구 후 대기
            head.localPosition = headBaseLocalPos;
            timer = -1f;
            return;
        }

        // 엔벨로프: 확 커졌다(0.2초) → 유지 → 스르륵 복귀(0.4초)
        float k = timer < rise ? timer / rise
                : timer > total - fall ? (total - timer) / fall
                : 1f;

        head.localScale = headBaseScale * Mathf.Lerp(1f, config.roarHeadScale, k);

        // ⚠ position에 += 금지: 클립이 본 위치를 안 쓰는 탓에 프레임마다 누적돼 머리가 수십 m 날아간다.
        // 기준 localPosition에서 매 프레임 새로 계산 (전방 벡터는 부모 공간으로 환산).
        Vector3 offsetLocal = head.parent != null
            ? head.parent.InverseTransformVector(racer.transform.forward * config.roarHeadForward * k)
            : racer.transform.forward * config.roarHeadForward * k;
        head.localPosition = headBaseLocalPos + offsetLocal;
    }
}
