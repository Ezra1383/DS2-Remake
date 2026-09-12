using UnityEngine;

namespace DS2
{
    /// <summary>
    /// What she is allowed to know about the player, and when.
    ///
    /// Two rules, both about fairness, both learned the hard way:
    ///
    /// 1. SHE REACTS LATE. Responding on the frame a swing begins is input reading, and players
    ///    can feel it even when they cannot name it - it reads as the game cheating rather than
    ///    as an opponent being good. Human reaction time is 0.2-0.4 s, so hers is too, with a
    ///    little jitter so it is never the same twice.
    ///
    /// 2. SHE ROLLS ONCE PER EVENT. The old AI rolled its dodge chance every frame the player was
    ///    in start-up. Slash 1's start-up is about 26 frames, so a "35% chance" was really
    ///    1 - 0.65^26, i.e. certainty. Every swing got evaded and she spent the fight three
    ///    metres away walking back in. Each swing here gets exactly one roll, ever.
    ///
    /// Everything published is observable - distance, animation state, which way the player is
    /// moving. Nothing reads input.
    /// </summary>
    public class BossPerception : MonoBehaviour
    {
        [Header("Reaction")]
        [Tooltip("Seconds before an observed player action becomes visible to the brain. Human " +
                 "reaction is 0.2-0.4 s. Lower it and she starts to feel like she is reading " +
                 "your controller.")]
        [SerializeField] float reactionDelay = 0.25f;

        [Tooltip("Plus or minus, rolled per event, so her timing is never mechanical.")]
        [SerializeField] float reactionJitter = 0.08f;

        [Tooltip("Window used to decide whether the player is closing or backing off.")]
        [SerializeField] float approachSampleTime = 0.35f;

        [Tooltip("Metres per second of change before movement counts as approaching or retreating.")]
        [SerializeField] float approachThreshold = 0.6f;

        CombatActor player;
        Transform playerTransform;

        float distanceSample;
        float sampleTakenAt;

        MoveDefinition observedMove;
        float observedAt;
        bool startupRolled;
        bool recoveryRolled;

        /// <summary>Straight-line distance to the player, on the ground plane.</summary>
        public float Distance { get; private set; }

        /// <summary>Metres per second of closure. Negative while the player backs away.</summary>
        public float ClosingSpeed { get; private set; }

        public bool PlayerIsApproaching => ClosingSpeed > approachThreshold;
        public bool PlayerIsRetreating => ClosingSpeed < -approachThreshold;

        public CombatActor Player => player;

        /// <summary>Seconds the player has spent doing nothing at all.</summary>
        public float PlayerIdleFor { get; private set; }

        /// <summary>
        /// True once she has actually noticed the current swing - i.e. the reaction delay has
        /// elapsed - and it has not reached its hitbox yet. Reacting after the hitbox opens is
        /// not a dodge, it is a flinch.
        /// </summary>
        public bool SeesIncomingSwing
        {
            get
            {
                if (observedMove == null || !observedMove.HasHitbox) return false;
                if (Time.time < observedAt) return false;
                return player.MoveProgress < observedMove.hitboxOpen;
            }
        }

        /// <summary>
        /// True while the player is past their own hitbox but not yet free - the recovery frames
        /// a whiff punish exists to take.
        /// </summary>
        public bool SeesRecovery
        {
            get
            {
                MoveDefinition m = player != null ? player.CurrentMove : null;
                if (m == null || !m.HasHitbox) return false;
                if (Time.time < observedAt) return false;
                return player.MoveProgress >= m.hitboxClose;
            }
        }

        /// <summary>The swing she is currently looking at, or null.</summary>
        public MoveDefinition IncomingSwing => observedMove;

        void Awake()
        {
            PlayerCombat pc = FindAnyObjectByType<PlayerCombat>();
            if (pc != null)
            {
                player = pc.GetComponent<CombatActor>();
                playerTransform = pc.transform;
            }
            if (player == null)
                Debug.LogWarning("[BossPerception] No PlayerCombat in the scene.", this);
        }

        /// <summary>Lets the brain point perception at a target it found itself.</summary>
        public void SetPlayer(CombatActor value)
        {
            player = value;
            playerTransform = value != null ? value.transform : null;
        }

        void Update()
        {
            if (player == null || playerTransform == null) return;

            Vector3 delta = playerTransform.position - transform.position;
            delta.y = 0f;
            Distance = delta.magnitude;

            if (Time.time - sampleTakenAt >= approachSampleTime)
            {
                float elapsed = Time.time - sampleTakenAt;
                if (sampleTakenAt > 0f && elapsed > 0.0001f)
                    ClosingSpeed = (distanceSample - Distance) / elapsed;

                distanceSample = Distance;
                sampleTakenAt = Time.time;
            }

            MoveDefinition current = player.CurrentMove;
            if (current == null)
            {
                PlayerIdleFor += Time.deltaTime;
                observedMove = null;
                startupRolled = false;
                recoveryRolled = false;
            }
            else
            {
                PlayerIdleFor = 0f;

                // A new move: note when she will actually have seen it, and clear the roll so
                // this swing gets its one chance.
                if (current != observedMove)
                {
                    observedMove = current;
                    observedAt = Time.time + Mathf.Max(0f, reactionDelay +
                                 Random.Range(-reactionJitter, reactionJitter));
                    startupRolled = false;
                    recoveryRolled = false;
                }
            }
        }

        /// <summary>
        /// One roll per swing, ever - the whole point of this class. A failed roll still consumes
        /// the swing, because a boss who re-rolls until she wins is a boss who always wins.
        /// Start-up and recovery get separate tokens: they are different decisions about the same
        /// swing (evade it, or punish the whiff), and sharing one token meant a failed dodge roll
        /// silently cancelled the punish.
        /// </summary>
        public bool RollForStartup(float chance)
        {
            if (startupRolled || observedMove == null) return false;
            startupRolled = true;
            return Random.value < chance;
        }

        /// <summary>One roll per swing against its recovery frames. See RollForStartup.</summary>
        public bool RollForRecovery(float chance)
        {
            if (recoveryRolled || observedMove == null) return false;
            recoveryRolled = true;
            return Random.value < chance;
        }

        void OnDrawGizmosSelected()
        {
            if (playerTransform == null) return;
            Gizmos.color = SeesIncomingSwing ? Color.red : Color.gray;
            Gizmos.DrawLine(transform.position + Vector3.up, playerTransform.position + Vector3.up);
        }
    }
}
