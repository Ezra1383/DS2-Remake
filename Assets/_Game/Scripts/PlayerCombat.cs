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

        [Header("Tuning")]
        [Tooltip("How long an early press is remembered. 0.2 is forgiving without playing itself.")]
        [SerializeField] float bufferWindow = 0.2f;

        [Tooltip("Below this stick deflection a dodge becomes the in-place Evade rather than a " +
                 "directional Quick Shift.")]
        [SerializeField] float directionDeadzone = 0.3f;

        CombatActor actor;
        InputAction attackAction, dodgeAction, moveAction;

        MoveDefinition buffered;
        float bufferedAt = float.NegativeInfinity;

        void Awake()
        {
            actor = GetComponent<CombatActor>();

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
        }

        void OnEnable()
        {
            attackAction.performed += OnAttack;
            dodgeAction.performed += OnDodge;
        }

        void OnDisable()
        {
            attackAction.performed -= OnAttack;
            dodgeAction.performed -= OnDodge;
        }

        void Update()
        {
            if (buffered == null) return;

            if (Time.time - bufferedAt > bufferWindow)
            {
                buffered = null;
                return;
            }

            if (actor.TryExecute(buffered)) buffered = null;
        }

        void OnAttack(InputAction.CallbackContext _)
        {
            // Mid-chain, the next link is whatever the current move chains into. Neutral, it is
            // the opener. The move assets own the chain order, not this script.
            MoveDefinition wanted = actor.CurrentMove != null && actor.CurrentMove.CanChain
                ? actor.CurrentMove.nextInChain
                : slash1;

            Attempt(wanted);
        }

        void OnDodge(InputAction.CallbackContext _)
        {
            Attempt(SelectDodge());
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

            if (Mathf.Abs(local.z) >= Mathf.Abs(local.x))
                return local.z >= 0f ? quickShiftF : quickShiftB;
            return local.x >= 0f ? quickShiftR : quickShiftL;
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
            if (actor.TryExecute(move)) return;

            // Refused - hold it briefly in case the cancel window opens a moment later.
            buffered = move;
            bufferedAt = Time.time;
        }
    }
}
