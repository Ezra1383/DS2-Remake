using UnityEngine;
using UnityEngine.InputSystem;

namespace DS2
{
    /// <summary>
    /// Turns input into move execution: the attack chain, the dodges, and stance.
    ///
    /// Buffering rule: a press during the current move is remembered for bufferWindow seconds and
    /// replayed the moment the move becomes cancellable. Without it a chain demands frame-perfect
    /// timing; with too much of it the game plays itself. Everything else that stops mashing lives
    /// in the move assets as recovery frames and cancel windows.
    /// </summary>
    [RequireComponent(typeof(CombatActor))]
    public class PlayerCombat : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] InputActionAsset inputAsset;

        [Header("Moves")]
        [SerializeField] MoveDefinition slash1;
        [SerializeField] MoveDefinition evade;
        [SerializeField] MoveDefinition quickShiftF;
        [SerializeField] MoveDefinition quickShiftB;
        [SerializeField] MoveDefinition quickShiftL;
        [SerializeField] MoveDefinition quickShiftR;

        [Tooltip("Deflects instead of dodging. Its own button, so the two defensive options are " +
                 "a genuine choice rather than one input that is always better timed.")]
        [SerializeField] MoveDefinition parry;

        [Header("Stance")]
        [SerializeField] MoveDefinition draw;
        [SerializeField] MoveDefinition sheathe;

        [Header("Specials - hold Special and press Attack / Dodge / Stance")]
        [SerializeField] MoveDefinition skill1;
        [SerializeField] MoveDefinition skill2;
        [SerializeField] MoveDefinition skill3;

        [Header("Tuning")]
        [Tooltip("How long an early press is remembered. 0.2 is forgiving without playing itself.")]
        [SerializeField] float bufferWindow = 0.2f;

        [Tooltip("Below this stick deflection a dodge becomes the in-place Evade rather than a " +
                 "directional Quick Shift.")]
        [SerializeField] float directionDeadzone = 0.3f;

        CombatActor actor;
        Animator animator;
        InputAction attackAction, dodgeAction, moveAction, specialAction, stanceAction, parryAction;

        static readonly int StanceParam = Animator.StringToHash("Stance");

        /// <summary>
        /// Held modifier. Three skills on one button plus the three you already use beats three
        /// more bindings, and it keeps the whole moveset reachable on a pad.
        /// </summary>
        bool SpecialHeld => specialAction != null && specialAction.IsPressed();

        MoveDefinition buffered;
        float bufferedAt = float.NegativeInfinity;

        /// <summary>Persistent stance, toggled by the Stance button. Holding Special adds to it.</summary>
        bool baseStance;

        void Awake()
        {
            actor = GetComponent<CombatActor>();
            animator = GetComponent<Animator>();

            if (inputAsset == null)
            {
                Debug.LogError("[PlayerCombat] No InputActionAsset assigned.", this);
                enabled = false;
                return;
            }

            InputActionMap player = inputAsset.FindActionMap("Player", true);
            attackAction = player.FindAction("Attack", true);
            dodgeAction = player.FindAction("Dodge", true);
            moveAction = player.FindAction("Move", true);
            specialAction = player.FindAction("Special", true);
            stanceAction = player.FindAction("Stance", true);
            parryAction = player.FindAction("Parry", true);
        }

        void OnEnable()
        {
            actor.Died += OnDied;
            attackAction.performed += OnAttack;
            dodgeAction.performed += OnDodge;
            stanceAction.performed += OnStance;
            parryAction.performed += OnParry;
        }

        void OnDisable()
        {
            actor.Died -= OnDied;
            attackAction.performed -= OnAttack;
            dodgeAction.performed -= OnDodge;
            stanceAction.performed -= OnStance;
            parryAction.performed -= OnParry;
        }

        void Update()
        {
            UpdateStance();

            if (buffered == null) return;

            if (Time.time - bufferedAt > bufferWindow)
            {
                buffered = null;
                return;
            }

            if (actor.TryExecute(buffered)) buffered = null;
        }

        /// <summary>
        /// Holding Special settles her into the iai stance - blade sheathed, Sp_Idle locomotion -
        /// so a Special begins from where its animation expects to begin. Gated on the Draw,
        /// which is what the ladder says grants access to Special stance in the first place.
        /// </summary>
        /// <summary>
        /// ResetForRetry clears the animator flag, but this component owns the intent behind it -
        /// without clearing that too, the next attempt silently begins in Special stance because
        /// UpdateStance would put the flag straight back.
        /// </summary>
        void OnDied(CombatActor _) => baseStance = false;

        void UpdateStance()
        {
            if (animator == null || actor.IsDead) return;

            bool hold = SpecialHeld && draw != null && Unlocked(draw);
            bool want = baseStance || hold;

            // Never mid-move. Switching sockets during a swing is the exact pop this removes.
            if (actor.CurrentMove == null && animator.GetBool(StanceParam) != want)
                animator.SetBool(StanceParam, want);
        }

        void OnAttack(InputAction.CallbackContext _)
        {
            if (SpecialHeld) { Attempt(skill1); return; }

            // Mid-chain, the next link is whatever the current move chains into. Neutral, it is
            // the opener. The move assets own the chain order, not this script.
            MoveDefinition wanted = actor.CurrentMove != null && actor.CurrentMove.CanChain
                ? actor.CurrentMove.nextInChain
                : slash1;

            Attempt(wanted);
        }

        void OnDodge(InputAction.CallbackContext _)
        {
            Attempt(SpecialHeld ? skill2 : SelectDodge());
        }

        /// <summary>
        /// Deliberately never buffered. A parry is a read on a specific swing; replaying a stored
        /// press a moment later would parry something the player never actually saw coming, and
        /// the whole point of the window is that they did.
        /// </summary>
        void OnParry(InputAction.CallbackContext _)
        {
            if (parry == null || !Unlocked(parry)) return;
            actor.TryExecute(parry);
        }

        /// <summary>
        /// Held: the third special. Tapped: take or leave the Special stance. Draw and Sheathe are
        /// deliberately vulnerable throughout - committing to the stance is the cost of the
        /// skills it unlocks.
        /// </summary>
        void OnStance(InputAction.CallbackContext _)
        {
            if (SpecialHeld) { Attempt(skill3); return; }

            // Take brings the scabbard into her left hand (ready to draw); Put stows it away.
            // Neither moves the blade - it is in the scabbard for both.
            MoveDefinition wanted = baseStance ? sheathe : draw;
            if (wanted == null || !Unlocked(wanted)) return;

            // Set the flag as the move starts, so CombatActor.EndMove cross-fades into the
            // locomotion tree the stance actually ends in.
            if (actor.TryExecute(wanted))
            {
                baseStance = !baseStance;
                if (animator != null) animator.SetBool(StanceParam, baseStance);
            }
        }

        /// <summary>
        /// Evade is the in-place dodge - measured net displacement is zero, it moves and returns.
        /// The Quick Shifts are the ones that relocate you, 2.8-3.1 m. Learning when to trade the
        /// safe roll for the aggressive dash is the skill curve of the game.
        /// </summary>
        MoveDefinition SelectDodge()
        {
            Vector2 input = moveAction.ReadValue<Vector2>();
            if (input.magnitude < directionDeadzone) return evade;

            // Relative to where she is facing, so a dash reads as forward or back from her view.
            Vector3 world = CameraRelative(input);
            Vector3 local = transform.InverseTransformDirection(world);

            MoveDefinition wanted = Mathf.Abs(local.z) >= Mathf.Abs(local.x)
                ? (local.z >= 0f ? quickShiftF : quickShiftB)
                : (local.x >= 0f ? quickShiftR : quickShiftL);

            // Early on most of the Quick Shifts are still locked. Falling back to Evade keeps the
            // dodge button ALWAYS doing something - a defensive input that silently does nothing
            // reads as the game dropping your press, and gets you killed while you wonder why.
            return Unlocked(wanted) ? wanted : evade;
        }

        /// <summary>
        /// Progression gate. No manager in the scene means everything is available, so a test
        /// scene without one still plays.
        /// </summary>
        static bool Unlocked(MoveDefinition move)
        {
            return move != null &&
                   (ProgressionManager.Instance == null || ProgressionManager.Instance.IsUnlocked(move));
        }

        Vector3 CameraRelative(Vector2 input)
        {
            Camera cam = Camera.main;
            if (cam == null) return new Vector3(input.x, 0f, input.y).normalized;

            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            return (forward * input.y + right * input.x).normalized;
        }

        void Attempt(MoveDefinition move)
        {
            if (move == null) return;

            // Not learned yet: drop it outright rather than buffering. Buffering a locked move
            // would fire it the instant it unlocks, which is a very confusing gift.
            if (!Unlocked(move)) return;

            if (actor.TryExecute(move)) return;

            // Refused - hold it briefly in case the cancel window opens a moment later.
            buffered = move;
            bufferedAt = Time.time;
        }
    }
}
