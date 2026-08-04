using System.Collections;
using UnityEngine;

/// <summary>
/// 타이틀 화면 전시 캐릭터의 대기 연기.
/// Idle_Relaxed ↔ Idle_Look_Around 두 아이들을 랜덤 간격으로 교차 재생한다.
/// 전용 컨트롤러(TitleIdle)의 상태 이름 = 클립 이름. 전환은 코드 CrossFade — 트랜지션 배선 불필요.
/// </summary>
public class TitleIdleAnimator : MonoBehaviour
{
    [SerializeField] private Animator animator;

    [Tooltip("한 아이들을 유지하는 시간 (초, x~y 랜덤)")]
    [SerializeField] private Vector2 idleHold = new Vector2(6f, 12f);

    [Tooltip("아이들 전환 크로스페이드 시간 (초)")]
    [SerializeField] private float blendTime = 0.25f;

    [Tooltip("상태 전환 로그 (검증용)")]
    [SerializeField] private bool debugLog = false;

    private static readonly string[] IdleStates = { "Idle_Relaxed", "Idle_Look_Around" };

    private int current;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        StartCoroutine(Loop());
    }

    private IEnumerator Loop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(idleHold.x, idleHold.y));

            current = 1 - current;   // 두 개뿐이라 단순 교대
            if (debugLog) Debug.Log("[타이틀연기] " + IdleStates[current]);
            animator.CrossFadeInFixedTime(IdleStates[current], blendTime);
        }
    }
}
