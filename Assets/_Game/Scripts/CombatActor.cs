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
        ActorLocomotion locomotion;
        Character_Weapon_Controller weapon;

        public int Health { get; private set; }

        /// <summary>Denominator for the HUD bar. The field itself stays private and tunable.</summary>
        public int MaxHealth => maxHealth;
        public bool IsDead { get; private set; }
        public bool IsInvulnerable { get; private set; }

        /// <summary>
        /// Blocks every action. Driven by Stagger() and its timer - a posture break and a parry
        /// both route through there, so there is exactly one owner of "is this actor helpless".
        /// </summary>
        public bool IsStunned { get; private set; }

        /// <summary>True during a move's parry window. An attack landing here is deflected.</summary>
        public bool IsParrying { get; private set; }

        /// <summary>Raised when this actor is staggered, with the duration. (actor, seconds)</summary>
        public event System.Action<CombatActor, float> Staggered;

        /// <summary>Raised when a stagger runs out. PostureSystem uses it to clear the meter.</summary>
        public event System.Action<CombatActor> StaggerEnded;

        /// <summary>The move currently executing, or null when neutral.</summary>
        public MoveDefinition CurrentMove { get; private set; }

        /// <summary>0-1 through the current move. Lets an opponent read a swing in flight.</summary>
        public float MoveProgress =>
            CurrentMove != null ? Mathf.Clamp01(moveTimer / CurrentMove.Duration) : 0f;

        /// <summary>
        /// True while this actor's hitbox is live - i.e. while it is actually threatening
        /// something. BossDebugHUD integrates this into threat density, which is the number
        /// that says whether the fight has pressure or dead air.
        /// </summary>
        public bool IsHitboxOpen => hitboxOpen;

        /// <summary>Set on every hit taken. On death this is the move that killed you.</summary>
        public MoveDefinition LastDamageSource { get; private set; }

        /// <summary>Multiplies incoming damage. PostureSystem raises this to 2 during stun.</summary>
        public float DamageTakenMultiplier { get; set; } = 1f;

        /// <summary>
        /// Scales damage this actor DEALS. The moves are mirrored, so this is what makes the boss
        /// hit for three-to-five-hits-and-you-die while using the player's own numbers. It is also
        /// the fastest lever on how long the ten-death arc runs - tune it first in Week 4.
        /// </summary>
        public float DamageDealtMultiplier => damageDealtMultiplier;

        [SerializeField] float damageDealtMultiplier = 1f;

        public event System.Action<CombatActor, MoveDefinition> Damaged;
        public event System.Action<CombatActor> Died;

        static readonly int MoveSpeedParam = Animator.StringToHash("MoveSpeed");
        static readonly int StanceParam = Animator.StringToHash("Stance");
        static readonly int LocomotionState = Animator.StringToHash("Locomotion");
        static readonly int LocomotionSpecialState = Animator.StringToHash("LocomotionSpecial");
        static readonly int StunState = Animator.StringToHash("Stun");

        float moveTimer;
        float staggerEndsAt;
        bool hitboxOpen;
        bool swingSoundPlayed;
        float moveEndOverride;

        /// <summary>Where the current move actually ends, honouring a per-execution override.</summary>
        float CurrentMoveEnd => moveEndOverride > 0f ? moveEndOverride : CurrentMove.moveEnd;

        protected Animator Anim => animator;

        protected virtual void Awake()
        {
            animator = GetComponent<Animator>();
            locomotion = GetComponent<ActorLocomotion>();
            weapon = GetComponent<Character_Weapon_Controller>();
            Health = maxHealth;
        }

        protected virtual void Update()
        {
            if (IsDead) return;

            // Before the early-out below: a staggered actor has no CurrentMove, so timing the
            // stagger inside the move block would leave it stunned forever.
            if (staggerEndsAt > 0f && Time.time >= staggerEndsAt) EndStagger();

            if (CurrentMove == null) return;

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

                    // Under logHits rather than the Hitbox's own switch on purpose: this is the
                    // one fact that separates "the window never opened" from "the window opened
                    // and the blade was not on the target", and it needs to be in the same log
                    // as the damage lines to be read against them.
                    if (logHits)
                        Debug.Log($"[{name}] {CurrentMove.moveId} hitbox " +
                                  $"{(shouldBeOpen ? "OPEN" : "CLOSE")} at t={t:0.000}", this);
                }
            }

            // The wind-up whoosh, polled exactly like the hitbox and for the same reason: a
            // normalized window cannot drift against the 1.4x tempo the way a fixed offset would.
            // This doubles as the move's telegraph.
            if (!swingSoundPlayed && CurrentMove.HasSwingSound && t >= CurrentMove.swingSoundNormalized)
            {
                swingSoundPlayed = true;
                HitFeedback.Swing(CurrentMove.swingSound, transform.position + Vector3.up,
                                  CurrentMove.speedMultiplier);
            }

            IsParrying = CurrentMove.HasParry &&
                         t >= CurrentMove.parryStart && t < CurrentMove.parryEnd;

            IsInvulnerable = CurrentMove.HasIFrames &&
                             t >= CurrentMove.iframeStart && t < CurrentMove.iframeEnd;

            // Facing tracks the target through the wind-up, then locks. Polled here for the same
            // reason the hitbox is: it is a normalized window on the move asset, so it can be
            // dragged while the game runs.
            if (locomotion != null) locomotion.AttackTracking = CurrentMove.TrackingAt(t);

            if (t >= CurrentMoveEnd) EndMove();
        }

        /// <summary>
        /// Plays a move if the actor is free, or if the current move has reached its cancel window
        /// and this is the move it chains into. Returns false when the input should be dropped -
        /// that refusal is what stops mashing.
        /// </summary>
        /// <param name="endOverride">
        /// Optional per-execution moveEnd, 0 to use the asset's. The boss spends this on the last
        /// step of a phrase to play the full draw-cut-sheathe cycle: a long, obvious, deliberate
        /// recovery that says "I am committed, punish me now". It only reads as a signal because
        /// the rest of her moves are trimmed.
        /// </param>
        public virtual bool TryExecute(MoveDefinition move, float endOverride = 0f)
        {
            if (IsDead || IsStunned || move == null) return false;

            if (CurrentMove != null)
            {
                bool canCancel = CurrentMove.CanChain &&
                                 CurrentMove.nextInChain == move &&
                                 moveTimer / CurrentMove.Duration >= CurrentMove.cancelWindow;
                if (!canCancel) return false;
            }

            BeginMove(move, endOverride);
            return true;
        }

        void BeginMove(MoveDefinition move, float endOverride)
        {
            CloseHitbox();

            if (logHits)
                Debug.Log($"[{name}] BEGIN {move.moveId} (state '{move.stateName}', " +
                          $"dur {move.Duration:0.000}s, hitbox {move.hitboxOpen:0.000}-" +
                          $"{move.hitboxClose:0.000}, end {move.moveEnd:0.000})", this);

            CurrentMove = move;
            moveTimer = 0f;
            moveEndOverride = endOverride;
            swingSoundPlayed = false;

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
                locomotion.AttackTracking = move.TrackingAt(0f);
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
            IsParrying = false;
            moveEndOverride = 0f;

            if (locomotion != null)
            {
                locomotion.IsBusy = false;
                locomotion.RootMotionDriven = false;
                locomotion.AttackTracking = 0f;
            }
        }

        /// <summary>
        /// Back to full, in place. Death is the progression mechanic here, so the retry has a
        /// two-second budget from killing blow to next attempt - which a scene load cannot meet
        /// and does not need to. Nothing is destroyed, so every reference and subscription that
        /// existed before the death still holds afterwards.
        /// </summary>
        public virtual void ResetForRetry(Vector3 position, Quaternion rotation)
        {
            CloseHitbox();

            Health = maxHealth;
            IsDead = false;
            IsStunned = false;
            IsInvulnerable = false;
            CurrentMove = null;
            LastDamageSource = null;
            DamageTakenMultiplier = 1f;
            IsParrying = false;
            staggerEndsAt = 0f;
            moveTimer = 0f;
            moveEndOverride = 0f;
            swingSoundPlayed = false;

            if (locomotion != null)
            {
                locomotion.enabled = true;
                locomotion.IsBusy = false;
                locomotion.RootMotionDriven = false;
                locomotion.AttackTracking = 0f;
                locomotion.MoveDirection = Vector3.zero;
            }

            // Teleporting a CharacterController needs it off for the write, or it resolves the
            // move against the collision it is standing in and slides somewhere else.
            var controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            if (controller != null) controller.enabled = true;

            animator.SetBool(StanceParam, false);
            animator.SetFloat(MoveSpeedParam, 1f);
            animator.CrossFadeInFixedTime(LocomotionState, 0.05f);

            if (TryGetComponent(out PostureSystem posture)) posture.ResetPosture();
        }

        /// <summary>
        /// Helpless for a while: drops whatever was executing, plays Stun, blocks every action.
        ///
        /// One entry point for both routes to an opening - the posture break and a parry - so
        /// there is a single owner of the stunned state. Two systems each setting IsStunned and
        /// each running their own timer is how an actor ends up permanently frozen.
        /// </summary>
        public void Stagger(float seconds, float damageMultiplier = 1f)
        {
            if (IsDead || seconds <= 0f) return;

            Interrupt();
            IsStunned = true;
            DamageTakenMultiplier = damageMultiplier;
            staggerEndsAt = Time.time + seconds;

            animator.CrossFadeInFixedTime(StunState, 0.08f);
            Staggered?.Invoke(this, seconds);
        }

        void EndStagger()
        {
            staggerEndsAt = 0f;
            IsStunned = false;
            DamageTakenMultiplier = 1f;

            animator.CrossFadeInFixedTime(
                animator.GetBool(StanceParam) ? LocomotionSpecialState : LocomotionState, blendOut);

            StaggerEnded?.Invoke(this);
        }

        /// <summary>
        /// Holds the Stun pose with no clock and no damage multiplier - a boss waiting for the
        /// fight to begin rather than one who has been opened up.
        ///
        /// DELIBERATELY NOT Stagger(). A stagger is an OPENING: it runs a timer, raises Staggered,
        /// and on a parry or a posture break doubles incoming damage. Routing dormancy through it
        /// would hand the player a free double-damage window before the fight even starts, and
        /// would end on its own the moment the timer expired.
        ///
        /// The Stun clip loops (2.0 s, 60 frames) and the animator's states are isolated with no
        /// inbound transitions, so one cross-fade holds indefinitely on its own.
        /// </summary>
        public void EnterDormantPose()
        {
            if (IsDead) return;

            Interrupt();
            animator.CrossFadeInFixedTime(StunState, 0.08f);
        }

        /// <summary>Back to locomotion, respecting whichever stance she is holding.</summary>
        public void ExitDormantPose()
        {
            if (IsDead) return;

            animator.CrossFadeInFixedTime(
                animator.GetBool(StanceParam) ? LocomotionSpecialState : LocomotionState, blendOut);
        }

        /// <summary>
        /// Re-asserts the dormant pose if something pulled her out of it - a hit reaction is the
        /// realistic case, since a dormant boss is still a valid target.
        ///
        /// Checked rather than cross-faded every frame: re-issuing a cross-fade continuously would
        /// restart the blend each frame and freeze her on the first frame of the clip.
        /// </summary>
        public void HoldDormantPose()
        {
            if (IsDead || IsStunned || CurrentMove != null) return;
            if (animator.IsInTransition(0)) return;
            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == StunState) return;

            animator.CrossFadeInFixedTime(StunState, 0.15f);
        }

        /// <summary>
        /// Drops whatever is executing without playing its recovery. Used by PostureSystem on a
        /// break, so a stun can cut her out of her own swing.
        /// </summary>
        public void Interrupt()
        {
            if (CurrentMove == null) return;
            CloseHitbox();
            CurrentMove = null;
            IsInvulnerable = false;
            IsParrying = false;
            moveEndOverride = 0f;

            if (locomotion != null)
            {
                locomotion.IsBusy = false;
                locomotion.RootMotionDriven = false;
                locomotion.AttackTracking = 0f;
            }
        }

        void CloseHitbox()
        {
            if (!hitboxOpen) return;
            hitboxOpen = false;
            if (hitbox != null) hitbox.Close();
        }

        /// <summary>
        /// Called by a Hitbox that overlapped this actor's Hurtbox. Reports what actually
        /// happened so the feedback layer can tell a landed hit from one the target dodged -
        /// a successful dodge that produces no sound at all reads as the game failing to notice
        /// rather than as the player succeeding.
        /// </summary>
        public virtual HitResult ApplyDamage(int amount, MoveDefinition source, Vector3 fromPosition)
        {
            if (IsDead) return HitResult.NoEffect;

            // Parry outranks i-frames: a move could carry both windows, and deflecting is the
            // more interesting outcome. The attacker is staggered by the Hitbox that called this,
            // which is the only thing holding a reference to them.
            if (IsParrying) return HitResult.Parried;

            if (IsInvulnerable) return HitResult.Evaded;

            int dealt = Mathf.RoundToInt(amount * DamageTakenMultiplier);
            Health = Mathf.Max(0, Health - dealt);
            LastDamageSource = source;

            if (logHits)
                Debug.Log($"[{name}] took {dealt} from {(source != null ? source.moveId.ToString() : "unknown")}, {Health} left", this);

            Damaged?.Invoke(this, source);

            if (Health == 0)
            {
                Die();
                return HitResult.Killed;
            }

            if (hitReaction != null) hitReaction.Play(fromPosition);
            return HitResult.Damaged;
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
