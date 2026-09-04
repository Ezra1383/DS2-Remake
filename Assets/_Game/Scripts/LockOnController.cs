using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DS2
{
    /// <summary>
    /// Soft lock-on.
    ///
    /// While locked, the camera aims at a CinemachineTargetGroup containing BOTH the player and
    /// the target, so the shot always contains the fight. Aiming straight at the target instead
    /// lets the player walk out of frame, which is the whole point of the group.
    ///
    /// Follow stays on the player; only LookAt is swapped. Camera feel - orbit, damping, framing -
    /// is configured on the CinemachineCamera in the inspector, where it can be tuned without
    /// recompiling.
    /// </summary>
    public class LockOnController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] InputActionAsset inputAsset;

        [Header("Camera")]
        [SerializeField] CinemachineCamera cinemachineCamera;

        [Tooltip("Framed when nothing is locked. A child of the player at about chest height, so " +
                 "the camera does not stare at her feet.")]
        [SerializeField] Transform defaultLookAt;

        [Tooltip("Optional. Left empty, one is created at runtime.")]
        [SerializeField] CinemachineTargetGroup targetGroup;

        [Tooltip("Optional. Left empty, it is taken from the CinemachineCamera. Required for the " +
                 "camera to swing behind the player while locked.")]
        [SerializeField] CinemachineOrbitalFollow orbital;

        [Tooltip("Seconds for the camera to swing round behind her. Too low snaps and feels " +
                 "robotic; too high and she outruns her own camera.")]
        [SerializeField] float orbitDamping = 0.35f;

        [Header("Framing")]
        [Tooltip("Higher favours the player, lower favours the target. 1:1 centres the shot " +
                 "between them.")]
        [SerializeField] float playerWeight = 1f;
        [SerializeField] float targetWeight = 1f;

        [Tooltip("How much space each member is given. Larger pulls the camera back.")]
        [SerializeField] float memberRadius = 1f;

        [Header("Targeting")]
        [SerializeField] float acquireRange = 15f;

        [Tooltip("Lock breaks past this. Wider than acquireRange so it does not flicker at the edge.")]
        [SerializeField] float breakRange = 20f;
        [SerializeField] LayerMask targetLayers = ~0;

        InputAction lockOnAction;
        Transform target;
        readonly Collider[] hits = new Collider[16];
        float yawVelocity;

        /// <summary>Null when free-look. PlayerLocomotion reads this to decide idle facing.</summary>
        public Transform CurrentTarget => target;

        void Awake()
        {
            if (inputAsset == null)
            {
                Debug.LogError("[LockOn] No InputActionAsset assigned.", this);
                enabled = false;
                return;
            }
            lockOnAction = inputAsset.FindActionMap("Player", true).FindAction("LockOn", true);
            if (defaultLookAt == null) defaultLookAt = transform;

            if (orbital == null && cinemachineCamera != null)
                orbital = cinemachineCamera.GetComponent<CinemachineOrbitalFollow>();

            // HorizontalAxis is only a world yaw in WorldSpace binding. In any other mode the
            // angle is measured against the player, who turns constantly, and the camera spins.
            if (orbital != null &&
                orbital.TrackerSettings.BindingMode != Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace)
                Debug.LogWarning("[LockOn] Orbital Follow Binding Mode should be World Space; " +
                                 "lock-on framing will drift otherwise.", orbital);
        }

        /// <summary>
        /// Swings the camera onto the line running from the target through the player, so the
        /// enemy sits ahead of her and the camera sits behind her - the Dark Souls arrangement.
        /// Runs in Update so the value is set before Cinemachine evaluates in LateUpdate.
        /// </summary>
        void Update()
        {
            if (target == null || orbital == null) return;

            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) return;

            // The yaw the camera must look along to see her with the target beyond her.
            float desiredYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;

            float next = Mathf.SmoothDampAngle(
                orbital.HorizontalAxis.Value, desiredYaw, ref yawVelocity, orbitDamping);

            // The axis range is -180..180; SmoothDampAngle can hand back values outside it.
            orbital.HorizontalAxis.Value = Mathf.Repeat(next + 180f, 360f) - 180f;
        }

        void OnEnable() => lockOnAction.performed += OnLockOnPressed;
        void OnDisable() => lockOnAction.performed -= OnLockOnPressed;

        void OnLockOnPressed(InputAction.CallbackContext _)
        {
            SetTarget(target != null ? null : FindBestTarget());
        }

        void LateUpdate()
        {
            if (target == null) return;

            // Drop the lock if the target dies, is disabled, or walks away.
            if (!target.gameObject.activeInHierarchy ||
                Vector3.Distance(transform.position, target.position) > breakRange)
                SetTarget(null);
        }

        void SetTarget(Transform value)
        {
            target = value;
            if (cinemachineCamera == null) return;

            if (target == null)
            {
                cinemachineCamera.LookAt = defaultLookAt;
                return;
            }

            EnsureGroup();
            targetGroup.Targets.Clear();
            targetGroup.AddMember(defaultLookAt, playerWeight, memberRadius);
            targetGroup.AddMember(target, targetWeight, memberRadius);
            cinemachineCamera.LookAt = targetGroup.transform;
        }

        void EnsureGroup()
        {
            if (targetGroup != null) return;
            var go = new GameObject("LockOnTargetGroup");
            targetGroup = go.AddComponent<CinemachineTargetGroup>();
        }

        /// <summary>
        /// Nearest thing to the centre of the screen, not simply the nearest thing - so that
        /// facing an enemy is how you choose it.
        /// </summary>
        Transform FindBestTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, acquireRange, hits, targetLayers, QueryTriggerInteraction.Ignore);

            Vector3 viewForward = Camera.main != null
                ? Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized
                : transform.forward;

            Transform best = null;
            float bestScore = -1f;

            for (int i = 0; i < count; i++)
            {
                Transform candidate = hits[i].transform;
                if (candidate == transform || candidate.IsChildOf(transform)) continue;

                Vector3 toTarget = candidate.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.01f) continue;

                float score = Vector3.Dot(viewForward, toTarget.normalized);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            // Behind the camera is not a target you meant to pick.
            return bestScore > 0.2f ? best : null;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.44f, 0.5f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, acquireRange);
            if (target != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position + Vector3.up, target.position + Vector3.up);
            }
        }
    }
}
