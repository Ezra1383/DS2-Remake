using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Shared base for the player and the boss. It is a mirror match, so both sides run the same
    /// execution model and the same MoveDefinition assets - which is also why this class exists
    /// rather than two parallel implementations.
    ///
    /// This owns the normalized-time loop: while a move plays it watches the animator clock and
    /// opens/closes the hitbox and the i-frame window from the move asset. No animation events -
    /// see Docs/project-notes.md for why.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CombatActor : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField] int maxHealth = 100;

        [Header("Wiring")]
        [Tooltip("Trigger collider on the weapon. Leave empty on an actor that cannot attack.")]
        [SerializeField] Hitbox hitbox;

        [SerializeField] HitReaction hitReaction;

        [Header("Blend")]
        [Tooltip("Cross-fade back to locomotion when a move ends. Short enough that a trimmed " +
                 "move feels cut, long enough not to snap.")]
        [SerializeField] float blendOut = 0.12f;

        [Header("Debug")]
        [SerializeField] bool logHits;

        Animator animator;
        PlayerLocomotion locomotion;
        Character_Weapon_Controller weapon;

        public int Health { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsInvulnerable { get; private set; }

        /// <summary>The move currently executing, or null when neutral.</summary>
        public MoveDefinition CurrentMove { get; private set; }

        /// <summary>Set on every hit taken. On death this is the move that killed you.</summary>
        public MoveDefinition LastDamageSource { get; private set; }

        /// <summary>Multiplies incoming damage. PostureSystem raises this to 2 during stun.</summary>
        public float DamageTakenMultiplier { get; set; } = 1f;

        public event System.Action<CombatActor, MoveDefinition> Damaged;
        public event System.Action<CombatActor> Died;

        static readonly int MoveSpeedParam = Animator.StringToHash("MoveSpeed");
        static readonly int StanceParam = Animator.StringToHash("Stance");
        static readonly int LocomotionState = Animator.StringToHash("Locomotion");
        static readonly int LocomotionSpecialState = Animator.StringToHash("LocomotionSpecial");

        float moveTimer;
        bool hitboxOpen;

        protected Animator Anim => animator;

        protected virtual void Awake()
        {
            animator = GetComponent<Animator>();
            locomotion = GetComponent<PlayerLocomotion>();
            weapon = GetComponent<Character_Weapon_Controller>();
            Health = maxHealth;
        }

        protected virtual void Update()
        {
            if (IsDead || CurrentMove == null) return;

            moveTimer += Time.deltaTime;
            float t = Mathf.Clamp01(moveTimer / CurrentMove.Duration);

            if (CurrentMove.HasHitbox && hitbox != null)
            {
                bool shouldBeOpen = t >= CurrentMove.hitboxOpen && t < CurrentMove.hitboxClose;
                if (shouldBeOpen != hitboxOpen)
                {
                    hitboxOpen = shouldBeOpen;
                    if (shouldBeOpen) hitbox.Open(this, CurrentMove);
                    else hitbox.Close();
                }
            }

            IsInvulnerable = CurrentMove.HasIFrames &&
                             t >= CurrentMove.iframeStart && t < CurrentMove.iframeEnd;

            if (t >= CurrentMove.moveEnd) EndMove();
        }

        /// <summary>
        /// Plays a move if the actor is free, or if the current move has reached its cancel window
        /// and this is the move it chains into. Returns false when the input should be dropped -
        /// that refusal is what stops mashing.
        /// </summary>
        public virtual bool TryExecute(MoveDefinition move)
        {
            if (IsDead || move == null) return false;

            if (CurrentMove != null)
            {
                bool canCancel = CurrentMove.CanChain &&
                                 CurrentMove.nextInChain == move &&
                                 moveTimer / CurrentMove.Duration >= CurrentMove.cancelWindow;
                if (!canCancel) return false;
            }

            BeginMove(move);
            return true;
        }

        void BeginMove(MoveDefinition move)
        {
            CloseHitbox();

            CurrentMove = move;
            moveTimer = 0f;

            // The state reads its playback rate from this parameter, so speedMultiplier on the
            // asset is live: change it in the inspector mid-play and the next swing uses it.
            // Imported clips carry no SwitchSocket events, so the move asset can place the
            // blade itself. Vendor clips leave this empty and drive it from their own events.
            if (weapon != null && !string.IsNullOrEmpty(move.weaponSocket))
                weapon.SwitchSocketByString(move.weaponSocket);

            animator.SetFloat(MoveSpeedParam, move.speedMultiplier);
            animator.CrossFadeInFixedTime(move.StateHash, 0.05f);

            if (locomotion != null)
            {
                locomotion.IsBusy = true;
                locomotion.RootMotionDriven = move.useRootMotion;
            }
        }

        void EndMove()
        {
            CloseHitbox();

            if (weapon != null && CurrentMove != null && !string.IsNullOrEmpty(CurrentMove.endWeaponSocket))
                weapon.SwitchSocketByString(CurrentMove.endWeaponSocket);

            // Leave the state explicitly rather than waiting for its exit-time transition.
            // Otherwise a move trimmed by moveEnd keeps animating - which is how you end up
            // watching her mime sheathing a sword that is still in her hand.
            animator.CrossFadeInFixedTime(
                animator.GetBool(StanceParam) ? LocomotionSpecialState : LocomotionState, blendOut);

            CurrentMove = null;
            IsInvulnerable = false;

            if (locomotion != null)
            {
                locomotion.IsBusy = false;
                locomotion.RootMotionDriven = false;
            }
        }

        void CloseHitbox()
        {
            if (!hitboxOpen) return;
            hitboxOpen = false;
            if (hitbox != null) hitbox.Close();
        }

        /// <summary>Called by a Hitbox that overlapped this actor's Hurtbox.</summary>
        public virtual void ApplyDamage(int amount, MoveDefinition source, Vector3 fromPosition)
        {
            if (IsDead || IsInvulnerable) return;

            int dealt = Mathf.RoundToInt(amount * DamageTakenMultiplier);
            Health = Mathf.Max(0, Health - dealt);
            LastDamageSource = source;

            if (logHits)
                Debug.Log($"[{name}] took {dealt} from {(source != null ? source.moveId.ToString() : "unknown")}, {Health} left", this);

            Damaged?.Invoke(this, source);

            if (Health == 0) Die();
            else if (hitReaction != null) hitReaction.Play(fromPosition);
        }

        protected virtual void Die()
        {
            IsDead = true;
            CloseHitbox();
            CurrentMove = null;

            if (locomotion != null)
            {
                locomotion.IsBusy = true;
                locomotion.enabled = false;
            }

            animator.CrossFadeInFixedTime(Animator.StringToHash("Die"), 0.1f);
            Died?.Invoke(this);
        }
    }
}
