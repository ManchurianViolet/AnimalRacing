using System.Collections;
using UnityEngine;

/// <summary>
/// 타이틀 화면 전시 캐릭터의 연기.
/// 메인 화면에서는 춤 4종을 랜덤 순환하고, 커스터마이징 중에는 얌전한 아이들 2종으로 내려간다
/// (옷을 갈아입는 동안 춤추면 부위가 잘 안 보인다 — CustomizationPanel이 SetDancing으로 전환).
/// 전용 컨트롤러(TitleIdle)의 상태 이름 = 클립 이름. 전환은 코드 CrossFade — 트랜지션 배선 불필요.
/// </summary>
public class TitleIdleAnimator : MonoBehaviour
{
    [SerializeField] private Animator animator;

    [Header("메인 화면 — 춤")]
    [Tooltip("순환 재생할 춤 상태 이름 (TitleIdle 컨트롤러의 상태명 = 클립명)")]
    [SerializeField] private string[] danceStates = { "Dance_Chicken", "Dance_Locking", "Dance_Snake", "Dance_Ymca" };

    [Tooltip("한 곡을 유지하는 시간 (초, x~y 랜덤). 클립보다 짧으면 중간에 다음 곡으로 넘어간다")]
    [SerializeField] private Vector2 danceHold = new Vector2(9f, 15f);

    [Header("커마 중 — 대기")]
    [Tooltip("커마를 여는 동안 재생할 아이들 상태 이름")]
    [SerializeField] private string[] idleStates = { "Idle_Relaxed", "Idle_Look_Around" };

    [Tooltip("한 아이들을 유지하는 시간 (초, x~y 랜덤)")]
    [SerializeField] private Vector2 idleHold = new Vector2(6f, 12f);

    [Tooltip("연기 전환 크로스페이드 시간 (초)")]
    [SerializeField] private float blendTime = 0.25f;

    [Tooltip("상태 전환 로그 (검증용)")]
    [SerializeField] private bool debugLog = false;

    private Coroutine loop;
    private bool dancing = true;   // 타이틀 진입 = 춤
    private bool firstPlay = true;
    private int lastIndex = -1;

    public bool IsDancing => dancing;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        Restart();
    }

    private void OnDisable()
    {
        if (loop != null) { StopCoroutine(loop); loop = null; }
    }

    /// <summary>메인 화면=true(춤) / 커마 중=false(아이들). 즉시 그 모드의 연기로 갈아탄다.</summary>
    public void SetDancing(bool on)
    {
        if (dancing == on && loop != null) return;
        dancing = on;
        if (isActiveAndEnabled) Restart();
    }

    private void Restart()
    {
        if (loop != null) StopCoroutine(loop);
        lastIndex = -1;   // 모드가 바뀌면 목록도 바뀌니 인덱스 기억은 무효
        loop = StartCoroutine(Loop());
    }

    private IEnumerator Loop()
    {
        PlayNext();   // 모드 전환 직후 한 박자 기다렸다 바뀌면 어색하니 즉시 갈아탄다
        while (true)
        {
            Vector2 hold = dancing ? danceHold : idleHold;
            yield return new WaitForSeconds(Random.Range(hold.x, hold.y));
            PlayNext();
        }
    }

    private void PlayNext()
    {
        string[] states = dancing ? danceStates : idleStates;
        if (animator == null || states == null || states.Length == 0) return;

        int i = Random.Range(0, states.Length);
        if (states.Length > 1 && i == lastIndex) i = (i + 1) % states.Length;   // 같은 연기 연속 방지
        lastIndex = i;

        if (debugLog) Debug.Log("[타이틀연기] " + (dancing ? "춤 " : "대기 ") + states[i]);

        // 최초 1회만 즉시 — 컨트롤러 기본 상태(Idle_Relaxed)가 한 프레임 보이는 것 방지
        if (firstPlay) { animator.Play(states[i], 0, 0f); firstPlay = false; }
        else animator.CrossFadeInFixedTime(states[i], blendTime);
    }
}
