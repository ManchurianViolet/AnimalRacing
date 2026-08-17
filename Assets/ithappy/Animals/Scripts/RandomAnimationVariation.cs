using UnityEngine;

namespace ithappy
{
    public class RandomAnimatorCycleOffset : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        [Header("Random offset in the current starting state")]
        [SerializeField] private float minOffset = 0f;
        [SerializeField] private float maxOffset = 3f;

        [Header("Optional speed variation")]
        [SerializeField] private bool randomizeSpeed = false;
        [SerializeField] private float minSpeed = 1f;
        [SerializeField] private float maxSpeed = 1f;

        private void Awake()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
        }

        private void Start()
        {
            if (animator == null)
                return;

            // Force Animator to enter its default Entry state first.
            animator.Update(0f);

            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

            float offset = Random.Range(minOffset, maxOffset);

            // Restart the current starting state from a random normalized time.
            animator.Play(stateInfo.fullPathHash, 0, offset);
            animator.Update(0f);

            if (randomizeSpeed)
                animator.speed = Random.Range(minSpeed, maxSpeed);
        }
    }
}
