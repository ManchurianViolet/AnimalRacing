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
    private float timer = -1f;   // -1 = 대기

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        this.config = config;

        // ithappy 리그의 머리 본 이름은 "scull" — 없으면 "head" 계열 폴백
        foreach (var tr in GetComponentsInChildren<Transform>(true))
        {
            string n = tr.name.ToLowerInvariant();
            if (n == "scull" || n == "skull" || n == "head") { head = tr; break; }
        }
        if (head == null)
            foreach (var tr in GetComponentsInChildren<Transform>(true))
                if (tr.name.ToLowerInvariant().Contains("scull")) { head = tr; break; }

        if (head != null) headBaseScale = head.localScale;
        else Debug.LogWarning($"[RoarFx] {name}: 머리 본을 못 찾음 (연출 생략)");
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
            timer = -1f;
            return;
        }

        // 엔벨로프: 확 커졌다(0.2초) → 유지 → 스르륵 복귀(0.4초)
        float k = timer < rise ? timer / rise
                : timer > total - fall ? (total - timer) / fall
                : 1f;

        head.localScale = headBaseScale * Mathf.Lerp(1f, config.roarHeadScale, k);
        head.position += racer.transform.forward * (config.roarHeadForward * k);
    }
}
