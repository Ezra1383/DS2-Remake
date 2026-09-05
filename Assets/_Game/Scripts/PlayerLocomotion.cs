using UnityEngine;
using UnityEngine.InputSystem;

namespace DS2
{
    /// <summary>
    /// Turns input into movement intent. Everything below that - acceleration, rotation, the
    /// root-motion reconciliation - lives in ActorLocomotion, shared with the boss.
    /// </summary>
    public class PlayerLocomotion : ActorLocomotion
    {
        [Header("Input")]
        [SerializeField] InputActionAsset inputAsset;

        LockOnController lockOn;
        Transform cam;
        InputAction moveAction, sprintAction;

        protected override void Awake()
        {
            base.Awake();
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

        protected override void Update()
        {
            Vector2 input = moveAction.ReadValue<Vector2>();

            MoveDirection = CameraRelative(input);
            Running = sprintAction.IsPressed();
            FaceTarget = lockOn != null ? lockOn.CurrentTarget : null;

            base.Update();
        }

        Vector3 CameraRelative(Vector2 input)
        {
            if (input.sqrMagnitude < 0.01f) return Vector3.zero;
            if (cam == null) return new Vector3(input.x, 0f, input.y).normalized;

            Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            return (forward * input.y + right * input.x).normalized;
        }
    }
}
