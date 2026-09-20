using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Keeps the Mirror dormant - held in the looping Stun pose, deciding nothing - until the
    /// player walks into the arena volume this sits on.
    ///
    /// WHY POLLING RATHER THAN OnTriggerEnter. The retry loop teleports both actors rather than
    /// reloading the scene, and trigger callbacks around a teleport are unreliable: the player can
    /// be moved from inside the volume to outside it without either event firing, which leaves the
    /// gate latched open and the boss awake before the next attempt has begun. Asking "is the
    /// player inside right now" every frame cannot get out of sync with anything, and it is the
    /// same reasoning that moved Hitbox off OnTriggerEnter onto a swept overlap query.
    ///
    /// Collider.ClosestPoint returns the point itself when the point is inside, so this works for
    /// a rotated box, a sphere or a capsule - not only an axis-aligned one.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BossGate : MonoBehaviour
    {
        [Header("Wiring - all optional, resolved at Awake if left empty")]
        [Tooltip("The volume the player has to enter. Defaults to the collider on this object.")]
        [SerializeField] Collider arenaVolume;

        [SerializeField] BossBrain brain;
        [SerializeField] CombatActor bossActor;
        [SerializeField] CombatActor player;

        [Header("Behaviour")]
        [Tooltip("She goes back to sleep if the player leaves. Off by default: a boss who resets " +
                 "because you stepped backwards over a line reads as broken rather than as merciful.")]
        [SerializeField] bool sleepWhenPlayerLeaves;

        [Tooltip("Hitting a dormant boss wakes her. On by default - a boss who stands there being " +
                 "hit is indistinguishable from a boss whose AI has failed, which is exactly the " +
                 "bug this project has already lost days to.")]
        [SerializeField] bool wakeOnDamage = true;

        [SerializeField] bool logGate;

        public bool IsAwake { get; private set; }

        void Awake()
        {
            if (arenaVolume == null) arenaVolume = GetComponent<Collider>();

            if (brain == null) brain = FindAnyObjectByType<BossBrain>();
            if (brain != null && bossActor == null) bossActor = brain.GetComponent<CombatActor>();

            if (player == null)
            {
                PlayerCombat found = FindAnyObjectByType<PlayerCombat>();
                if (found != null) player = found.GetComponent<CombatActor>();
            }

            // A solid volume would wall the player out of the fight entirely.
            if (arenaVolume != null && !arenaVolume.isTrigger)
            {
                arenaVolume.isTrigger = true;
                Debug.LogWarning("[BossGate] Arena volume was not a trigger; set to one.", this);
            }

            if (brain == null || bossActor == null || player == null)
                Debug.LogError("[BossGate] Could not resolve boss or player - gate disabled.", this);
        }

        void OnEnable()
        {
            if (bossActor != null) bossActor.Damaged += OnBossDamaged;
            if (player != null) player.Died += OnPlayerDied;
        }

        void OnDisable()
        {
            if (bossActor != null) bossActor.Damaged -= OnBossDamaged;
            if (player != null) player.Died -= OnPlayerDied;
        }

        /// <summary>
        /// Back to dormant the moment the player dies, whatever sleepWhenPlayerLeaves says.
        ///
        /// WITHOUT THIS THE GATE ONLY EVER OPENS ONCE. The retry teleports the player back to a
        /// spawn outside the arena, but sleepWhenPlayerLeaves defaults to false - so she stayed
        /// awake and came straight for them across the training ground before they had walked back
        /// in. Death is not "the player stepped over a line", it is the fight ending, and the
        /// fight ending has to re-arm the gate.
        ///
        /// Sleeping here rather than waiting for the teleport also stops her attacking the corpse
        /// while the death card is up. If the player happens to respawn INSIDE the volume, the
        /// poll in Update wakes her again on the next frame, which is correct.
        /// </summary>
        void OnPlayerDied(CombatActor _) => Sleep("player died");

        void Start()
        {
            // Start asleep regardless of where the player begins, then let the first Update decide.
            // Doing it in Start rather than Awake gives CombatActor time to resolve its animator.
            Sleep("start");
        }

        void Update()
        {
            if (brain == null || bossActor == null || player == null) return;
            if (bossActor.IsDead) return;

            bool inside = PlayerInside();

            if (inside && !IsAwake) Wake("player entered");
            else if (!inside && IsAwake && sleepWhenPlayerLeaves) Sleep("player left");

            // She is a valid target while she waits, so a hit reaction can pull her out of the
            // pose. Put her back, or the fight starts with her standing in a flinch.
            if (!IsAwake) bossActor.HoldDormantPose();
        }

        bool PlayerInside()
        {
            if (arenaVolume == null) return true;

            Vector3 p = player.transform.position + Vector3.up;
            return (arenaVolume.ClosestPoint(p) - p).sqrMagnitude < 0.0001f;
        }

        void OnBossDamaged(CombatActor _, MoveDefinition __)
        {
            if (wakeOnDamage && !IsAwake) Wake("damaged");
        }

        void Wake(string why)
        {
            IsAwake = true;
            brain.Dormant = false;
            bossActor.ExitDormantPose();

            if (logGate) Debug.Log($"[BossGate] AWAKE ({why})", this);
        }

        void Sleep(string why)
        {
            IsAwake = false;
            brain.Dormant = true;
            bossActor.EnterDormantPose();

            if (logGate) Debug.Log($"[BossGate] dormant ({why})", this);
        }

        void OnDrawGizmosSelected()
        {
            Collider c = arenaVolume != null ? arenaVolume : GetComponent<Collider>();
            if (c == null) return;

            Gizmos.color = new Color(1f, 0.35f, 0.5f, 0.25f);
            Gizmos.DrawCube(c.bounds.center, c.bounds.size);
            Gizmos.color = new Color(1f, 0.35f, 0.5f, 0.9f);
            Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
        }
    }
}
