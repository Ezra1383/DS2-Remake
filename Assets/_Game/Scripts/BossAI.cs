using UnityEngine;

namespace DS2
{
    /// <summary>
    /// She never changes. You do.
    ///
    /// Fixed behaviour and the full moveset from the first attempt to the last - the entire
    /// difficulty curve is the player's growing toolkit and growing skill. So this is deliberately
    /// simple: weighted random over range bands with cooldowns. No behaviour tree, no utility AI.
    /// A simple opponent with good telegraphs beats a sophisticated one that arrives on day 25.
    ///
    /// Read the three rules below before changing anything here. They are what make her fair.
    /// </summary>
    [RequireComponent(typeof(CombatActor))]
    [RequireComponent(typeof(ActorLocomotion))]
    public class BossAI : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] Transform player;

        [Header("Moves")]
        [Tooltip("Her full repertoire. Generate it with Tools > DS2 > Build Boss Moveset.")]
        [SerializeField] BossMoveset moveset;

        [Tooltip("How far the player may drift before she abandons a combo mid-way. Wider than " +
                 "closeRange because her slashes carry root motion and travel while swinging.")]
        [SerializeField] float chainBreakRange = 4f;

        [Header("Range bands (m)")]
        [Tooltip("Inside this she uses slash chains. Most of the fight happens here.")]
        [SerializeField] float closeRange = 2.5f;

        [Tooltip("Between close and this she closes the gap or lunges. Measured: Quickshift_F " +
                 "covers 2.85 m and Sp_Skill_2 covers 5.18 m, which is why these numbers.")]
        [SerializeField] float midRange = 5f;

        [Tooltip("She stops walking in at this distance rather than shoving into the player.")]
        [SerializeField] float stopDistance = 1.8f;

        [Header("The three rules that make her fair")]
        [Tooltip("RULE 1. Max attacks before a guaranteed opening. Without this, a fight with no " +
                 "block button reads as unfair no matter how good the player is.")]
        [SerializeField] int maxChainLength = 3;

        [Tooltip("RULE 1. The mandatory neutral gap after that chain. THE most important number " +
                 "in this file.")]
        [SerializeField] float neutralGap = 0.8f;

        [Tooltip("RULE 3. Chance to punish the player's recovery frames. Always punishing is " +
                 "oppressive; never punishing means they can mash. The uncertainty is what makes " +
                 "her feel like an opponent.")]
        [Range(0f, 1f)] [SerializeField] float whiffPunishChance = 0.3f;

        [Tooltip("Pause between attacks inside a chain, so she is readable rather than a blender.")]
        [SerializeField] float betweenAttacks = 0.4f;

        [Header("Reactive dodge")]
        [Tooltip("Chance she evades a swing she has read. Not 1 - a boss who dodges everything " +
                 "cannot be hit, and one who never dodges is a training dummy. The uncertainty is " +
                 "what makes you commit carefully.")]
        [Range(0f, 1f)] [SerializeField] float reactiveDodgeChance = 0.35f;

        [Tooltip("Seconds before she may read another swing. Without this she evades every hit " +
                 "in a chain and the fight stalls.")]
        [SerializeField] float dodgeCooldown = 2.5f;

        [Tooltip("She only bothers reading a swing this close. Further out she is not threatened.")]
        [SerializeField] float dodgeRange = 3.5f;

        [Header("Patience")]
        [Tooltip("A boss you can stall is a boss you can solve boringly. After this long without " +
                 "committing, she closes the distance herself.")]
        [SerializeField] float patience = 4f;

        [Header("Debug")]
        [SerializeField] bool logDecisions;

        CombatActor actor;
        ActorLocomotion locomotion;
        CombatActor playerActor;
        PostureSystem posture;

        int chainCount;
        float nextDecisionAt;
        bool inNeutralGap;
        float lastAggressionAt;
        MoveDefinition lastMove;
        float nextDodgeAt;

        readonly System.Collections.Generic.Dictionary<MoveDefinition, float> readyAt = new();

        void Awake()
        {
            actor = GetComponent<CombatActor>();
            locomotion = GetComponent<ActorLocomotion>();
            posture = GetComponent<PostureSystem>();

            if (player == null)
            {
                var found = FindFirstObjectByType<PlayerCombat>();
                if (found != null) player = found.transform;
            }
            if (player != null) playerActor = player.GetComponent<CombatActor>();

            lastAggressionAt = Time.time;
        }

        void Update()
        {
            if (player == null || actor.IsDead) return;

            // Stunned or mid-move, she is not deciding anything.
            if (actor.IsStunned || (posture != null && posture.IsStunned))
            {
                locomotion.MoveDirection = Vector3.zero;
                return;
            }

            locomotion.FaceTarget = player;

            if (actor.CurrentMove != null)
            {
                locomotion.MoveDirection = Vector3.zero;
                return;
            }

            float distance = Vector3.Distance(transform.position, player.position);

            // Read the player's swing before anything else - but never during the neutral gap.
            // That gap is the guaranteed opening Rule 1 promises, and dodging out of it would
            // take back the one thing she owes the player.
            if (!inNeutralGap && TryReactiveDodge(distance)) return;

            if (Time.time < nextDecisionAt)
            {
                // Backing off is what makes the gap legible. Approaching through it would hand
                // the player an "opening" they cannot actually use.
                if (inNeutralGap) Retreat();
                else Approach(distance);
                return;
            }

            inNeutralGap = false;
            Decide(distance);
        }

        /// <summary>
        /// Evades a swing that is still winding up. Reacting after the hitbox opens is not a
        /// dodge, it is a flinch - so this only fires during the attacker's startup, which is
        /// what makes the player time their commitment instead of mashing.
        /// </summary>
        bool TryReactiveDodge(float distance)
        {
            if (moveset == null || playerActor == null) return false;
            if (Time.time < nextDodgeAt || distance > dodgeRange) return false;

            MoveDefinition threat = playerActor.CurrentMove;
            if (threat == null || !threat.HasHitbox) return false;
            if (playerActor.MoveProgress >= threat.hitboxOpen) return false;
            if (Random.value >= reactiveDodgeChance) return false;

            // Backpedal when crowded, sidestep otherwise.
            MoveDefinition dodge = distance < 2f
                ? moveset.Find(MoveId.QuickShiftB)
                : moveset.Find(Random.value < 0.5f ? MoveId.QuickShiftL : MoveId.QuickShiftR);

            if (dodge == null || !actor.TryExecute(dodge)) return false;

            lastMove = dodge;
            nextDodgeAt = Time.time + dodgeCooldown;
            nextDecisionAt = Time.time + dodge.Duration * dodge.moveEnd;
            locomotion.MoveDirection = Vector3.zero;

            if (logDecisions)
                Debug.Log($"[{name}] READ {threat.moveId} -> {dodge.moveId}", this);
            return true;
        }

        void Decide(float distance)
        {
            // RULE 1 applies at EVERY range, not just up close. Checking it inside one band let
            // her chain indefinitely from mid range, which is the exact unfairness the rule
            // exists to prevent.
            if (chainCount >= maxChainLength)
            {
                BeginNeutralGap();
                return;
            }

            // Continue the combo before re-rolling. Each move carries its own follow-up odds, so
            // she opens the same way every time and only sometimes extends - which is exactly what
            // a player can learn to punish.
            if (lastMove != null && lastMove.nextInChain != null &&
                distance <= chainBreakRange && Random.value < lastMove.chainChance)
            {
                Attack(lastMove.nextInChain, "chain");
                return;
            }

            // RULE 3: punish a player caught in recovery, but only sometimes.
            if (playerActor != null && playerActor.CurrentMove != null &&
                distance <= closeRange && Random.value < whiffPunishChance)
            {
                MoveDefinition punish = moveset != null ? moveset.Select(distance, IsReady) : null;
                if (punish != null) { Attack(punish, "whiff punish"); return; }
            }

            MoveDefinition chosen = moveset != null ? moveset.Select(distance, IsReady) : null;
            if (chosen != null) { Attack(chosen, "range " + distance.ToString("0.0")); return; }

            // Nothing valid here - close the distance and try again shortly.
            DecideFar(distance);
        }

        bool IsReady(MoveDefinition move)
        {
            return !readyAt.TryGetValue(move, out float t) || Time.time >= t;
        }

        /// <summary>
        /// The guaranteed opening. Without it, a fight with no block button reads as unfair no
        /// matter how good the player is - this is the most important behaviour in the file.
        /// </summary>
        void BeginNeutralGap()
        {
            chainCount = 0;
            lastMove = null;
            inNeutralGap = true;
            nextDecisionAt = Time.time + neutralGap;
            lastAggressionAt = Time.time;
            Retreat();

            if (logDecisions) Debug.Log($"[{name}] neutral gap {neutralGap}s", this);
        }

        void Retreat()
        {
            Vector3 away = transform.position - player.position;
            away.y = 0f;
            locomotion.Running = false;
            locomotion.MoveDirection = away.normalized;
        }

        void DecideFar(float distance)
        {
            // Too far to threaten. Walk in, and let the patience timer decide when to run.
            bool impatient = Time.time - lastAggressionAt > patience;
            locomotion.Running = impatient;
            Approach(distance);
            nextDecisionAt = Time.time + 0.25f;
        }

        void Approach(float distance)
        {
            if (distance <= stopDistance)
            {
                locomotion.MoveDirection = Vector3.zero;
                return;
            }

            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            locomotion.MoveDirection = toPlayer.normalized;
        }

        void Attack(MoveDefinition move, string reason)
        {
            if (move == null) return;
            if (!actor.TryExecute(move)) return;

            // RULE 1 counts ATTACKS, not actions. A Quick Shift threatens nothing, so spending
            // the chain budget on one - and then owing the player a neutral gap for it - left her
            // sidestepping three times and pausing, having done nothing at all.
            bool isAttack = move.damage > 0;
            if (isAttack)
            {
                chainCount++;
                lastAggressionAt = Time.time;
            }

            lastMove = move;
            locomotion.MoveDirection = Vector3.zero;
            locomotion.Running = false;

            // Decide again once this move is done, plus a readable beat.
            nextDecisionAt = Time.time + move.Duration * move.moveEnd + betweenAttacks;

            foreach (BossMoveset.Entry e in moveset.entries)
                if (e.move == move && e.cooldown > 0f)
                    readyAt[move] = Time.time + e.cooldown;

            if (logDecisions)
                Debug.Log($"[{name}] {reason}: {move.moveId}" +
                          (isAttack ? $" (chain {chainCount}/{maxChainLength})" : " (reposition)"), this);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.2f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, closeRange);
            Gizmos.color = new Color(0.8f, 0.5f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, midRange);
        }
    }
}
