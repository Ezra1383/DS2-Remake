using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Movement shared by the player and the boss. It is a mirror match, so both bodies move the
    /// same way; only the thing deciding WHERE to go differs. PlayerLocomotion reads input,
    /// BossAI writes MoveDirection.
    ///
    /// THE IMPORTANT PART: the clips carry real horizontal displacement, and the Animator owns the
    /// transform whenever a move is playing. Everything funnels through one CharacterController.Move
    /// call in OnAnimatorMove so the two systems can never both write position in the same frame.
    /// This is deliberately in ONE place - duplicating it per actor is how it drifts and how the
    /// project loses a week.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    public class ActorLocomotion : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        [Tooltip("Measured from the Walk clip: 1.753 units over 1.133 s. Matching the animation " +
                 "is what stops the feet sliding.")]
        [SerializeField] protected float walkSpeed = 1.55f;

        [Tooltip("Measured from the Run clip: 3.185 units over 0.667 s.")]
        [SerializeField] protected float runSpeed = 4.8f;

        [SerializeField] protected float acceleration = 12f;

        [Tooltip("Degrees per second. Low enough to read as weight, high enough that she is not " +
                 "fighting you. 540 is about a third of a second for a half-turn.")]
        [SerializeField] protected float rotationSpeed = 540f;

        [Header("Locomotion clips")]
        [Tooltip("On only when the locomotion tree has all eight directions. Off, she turns to " +
                 "face her direction of travel rather than strafing, because playing a forward " +
                 "walk across a sideways translation reads as skating. Must match " +
                 "DirectionalLocomotion in CombatSetupTools.")]
        [SerializeField] protected bool hasDirectionalClips;

        [Header("Gravity")]
        [SerializeField] protected float gravity = -25f;
        [SerializeField] protected float groundedStick = -2f;

        protected CharacterController controller;
        protected Animator animator;

        Vector3 planarVelocity;
        float verticalVelocity;

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int MoveXParam = Animator.StringToHash("MoveX");
        static readonly int MoveYParam = Animator.StringToHash("MoveY");

        /// <summary>Where to go, in world space. Magnitude 0-1. Set by input or by AI.</summary>
        public Vector3 MoveDirection { get; set; }

        /// <summary>Run rather than walk.</summary>
        public bool Running { get; set; }

        /// <summary>Face this instead of the direction of travel. Null to face travel.</summary>
        public Transform FaceTarget { get; set; }

        /// <summary>
        /// Set by CombatActor while a move with useRootMotion plays. The animation drives
        /// horizontal position and this stops steering.
        /// </summary>
        public bool RootMotionDriven { get; set; }

        /// <summary>True while a committed action owns the character. Blocks steering and turning.</summary>
        public bool IsBusy { get; set; }

        /// <summary>
        /// Degrees per second this actor may still turn toward FaceTarget while a move is
        /// playing. Written every frame by CombatActor from the active move's tracking window,
        /// and 0 once that window closes - which is the moment the swing is truly committed.
        /// </summary>
        public float AttackTracking { get; set; }

        protected virtual void Awake()
        {
            controller = GetComponent<CharacterController>();
            animator = GetComponent<Animator>();
        }

        protected virtual void Update()
        {
            Vector3 desired = IsBusy ? Vector3.zero : MoveDirection;
            float targetSpeed = desired.sqrMagnitude > 0.01f ? (Running ? runSpeed : walkSpeed) : 0f;

            planarVelocity = Vector3.MoveTowards(
                planarVelocity, desired.normalized * targetSpeed, acceleration * Time.deltaTime);

            UpdateRotation(desired);
            UpdateAnimator(desired);
        }

        void UpdateRotation(Vector3 desired)
        {
            // While a move plays, steering is gone but facing is not necessarily frozen: an
            // attack may keep tracking its target through its wind-up and then lock. Without
            // that window a committed swing cannot correct at all, so anyone who keeps walking
            // is never hit and the fight reads as two people missing each other.
            if (IsBusy)
            {
                if (AttackTracking > 0f) TurnToward(FaceTarget, AttackTracking);
                return;
            }

            // Holding facing at a target only works when there is a clip for every direction.
            // Without them, turning to face travel is the lesser evil.
            bool holdFacing = FaceTarget != null &&
                              (hasDirectionalClips || desired.sqrMagnitude < 0.0001f);

            if (holdFacing) TurnToward(FaceTarget, rotationSpeed);
            else TurnToward(desired, rotationSpeed);
        }

        void TurnToward(Transform target, float degreesPerSecond)
        {
            if (target == null) return;
            TurnToward(target.position - transform.position, degreesPerSecond);
        }

        void TurnToward(Vector3 direction, float degreesPerSecond)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                degreesPerSecond * Time.deltaTime);
        }

        void UpdateAnimator(Vector3 desired)
        {
            // Speed picks the tier: 0 idle, 0.5 walk, 1 run.
            float speed = desired.sqrMagnitude < 0.01f
                ? 0f
                : Mathf.Clamp01(desired.magnitude) * (Running ? 1f : 0.5f);

            animator.SetFloat(SpeedParam, speed, 0.1f, Time.deltaTime);

            // Local space, so these mean "which way am I travelling relative to where I am
            // looking" - what a directional tree needs when facing is pinned to a target.
            Vector3 local = transform.InverseTransformDirection(desired.normalized);
            animator.SetFloat(MoveXParam, local.x, 0.1f, Time.deltaTime);
            animator.SetFloat(MoveYParam, local.z, 0.1f, Time.deltaTime);
        }

        /// <summary>
        /// The single place position is written. Called by the Animator after it evaluates root
        /// motion, which is the only point where animator.deltaPosition is valid.
        /// </summary>
        void OnAnimatorMove()
        {
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = groundedStick;
            else verticalVelocity += gravity * Time.deltaTime;

            Vector3 horizontal = RootMotionDriven
                ? animator.deltaPosition          // the clip decides how far the move travels
                : planarVelocity * Time.deltaTime; // scripted locomotion is more responsive

            horizontal.y = 0f;
            controller.Move(horizontal + Vector3.up * (verticalVelocity * Time.deltaTime));
        }
    }
}
