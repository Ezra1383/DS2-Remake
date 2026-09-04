using UnityEngine;
using UnityEngine.InputSystem;

namespace DS2
{
    /// <summary>
    /// Camera-relative movement, rotation, and the root-motion reconciliation.
    ///
    /// Lives on the character prefab root, alongside the Animator - a separate parent GameObject
    /// would just fight root motion.
    ///
    /// THE IMPORTANT PART: the clips carry real horizontal displacement, and the Animator owns the
    /// transform whenever a move is playing. Everything funnels through one CharacterController.Move
    /// call in OnAnimatorMove so the two systems can never both write position in the same frame.
    /// Getting this wrong and discovering it in week three is the classic way this project loses a week.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    public class PlayerLocomotion : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] InputActionAsset inputAsset;

        [Header("Speeds (m/s)")]
        [Tooltip("Measured from the Walk clip: 1.753 units over 1.133 s. Matching the animation " +
                 "is what stops the feet sliding.")]
        [SerializeField] float walkSpeed = 1.55f;

        [Tooltip("Measured from the Run clip: 3.185 units over 0.667 s.")]
        [SerializeField] float runSpeed = 4.8f;

        [SerializeField] float acceleration = 12f;

        [Tooltip("Degrees per second. Low enough to read as weight, high enough that she is not " +
                 "fighting you. 540 is about a third of a second for a half-turn.")]
        [SerializeField] float rotationSpeed = 540f;

        [Header("Gravity")]
        [SerializeField] float gravity = -25f;
        [SerializeField] float groundedStick = -2f;

        CharacterController controller;
        Animator animator;
        LockOnController lockOn;
        Transform cam;

        InputAction moveAction, sprintAction;
        Vector3 planarVelocity;
        float verticalVelocity;

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int MoveXParam = Animator.StringToHash("MoveX");
        static readonly int MoveYParam = Animator.StringToHash("MoveY");

        /// <summary>
        /// Set by PlayerCombat while a move with useRootMotion is playing. When true the animation
        /// drives horizontal position and this script stops steering.
        /// </summary>
        public bool RootMotionDriven { get; set; }

        /// <summary>True while a committed action owns the character. Blocks steering and turning.</summary>
        public bool IsBusy { get; set; }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            animator = GetComponent<Animator>();
            lockOn = GetComponent<LockOnController>();

            if (Camera.main != null) cam = Camera.main.transform;

            if (inputAsset == null)
            {
                Debug.LogError("[PlayerLocomotion] No InputActionAsset assigned.", this);
                enabled = false;
                return;
            }

            InputActionMap player = inputAsset.FindActionMap("Player", true);
            moveAction = player.FindAction("Move", true);
            sprintAction = player.FindAction("Sprint", true);
        }

        void OnEnable() => inputAsset?.FindActionMap("Player", true).Enable();
        void OnDisable() => inputAsset?.FindActionMap("Player", true).Disable();

        void Update()
        {
            Vector2 input = moveAction.ReadValue<Vector2>();
            bool sprinting = sprintAction.IsPressed();

            Vector3 desired = CameraRelative(input);
            float targetSpeed = input.sqrMagnitude > 0.01f && !IsBusy
                ? (sprinting ? runSpeed : walkSpeed)
                : 0f;

            // Steering is scripted; displacement is applied in OnAnimatorMove.
            planarVelocity = Vector3.MoveTowards(
                planarVelocity, desired * targetSpeed, acceleration * Time.deltaTime);

            UpdateRotation(desired);
            UpdateAnimator(input, sprinting);
        }

        Vector3 CameraRelative(Vector2 input)
        {
            if (input.sqrMagnitude < 0.01f) return Vector3.zero;
            if (cam == null) return new Vector3(input.x, 0f, input.y).normalized;

            Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            return (forward * input.y + right * input.x).normalized;
        }

        void UpdateRotation(Vector3 desired)
        {
            // While a move plays the animation owns facing - turning mid-swing feels wrong
            // and would fight the rotation curve baked into the clip.
            if (IsBusy) return;

            // She always turns to face the way she is travelling, lock-on or not.
            //
            // This is forced by the asset: the pack ships Idle, Walk and Run only - there are no
            // strafe or walk-back clips. Holding her facing at a lock-on target while she moves
            // sideways would play a forward walk across a sideways translation, which reads as
            // skating. Lock-on still does its real jobs: it frames the fight and it picks the
            // target attacks commit toward.
            Vector3 face = desired;

            // Only when she is standing still does the lock take over her facing.
            if (face.sqrMagnitude < 0.0001f && lockOn != null && lockOn.CurrentTarget != null)
            {
                face = lockOn.CurrentTarget.position - transform.position;
                face.y = 0f;
            }

            if (face.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(face, Vector3.up),
                rotationSpeed * Time.deltaTime);
        }

        void UpdateAnimator(Vector2 input, bool sprinting)
        {
            // Blend tree thresholds are Idle 0, Walk 0.5, Run 1.
            float speed = input.sqrMagnitude < 0.01f || IsBusy
                ? 0f
                : Mathf.Clamp01(input.magnitude) * (sprinting ? 1f : 0.5f);

            animator.SetFloat(SpeedParam, speed, 0.1f, Time.deltaTime);

            // Local-space direction, for the strafing blend trees that lock-on will want later.
            Vector3 local = transform.InverseTransformDirection(CameraRelative(input));
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
