using System.Collections.Generic;
using UnityEngine;

namespace DS2
{
    /// <summary>
    /// She never changes. You do.
    ///
    /// Four layers, checked in priority order. This is FromSoftware's goal stack flattened into
    /// something one person can finish and tune: a PHRASE is what they push as sub-goals, a
    /// REACTION is what they bubble up as an interrupt.
    ///
    ///   3  Reactions    rare, delayed, one roll per event, can start a phrase
    ///   2  Phrase       an authored attack string - the unit the player learns
    ///   1  Pacing       whether she may act at all, and what she owes the player
    ///   0  Positioning  where she stands when she is not attacking
    ///
    /// WHAT THIS REPLACED AND WHY. The old BossAI re-rolled a single move from a weighted table
    /// every time it became free. Three measured consequences:
    ///
    ///   - She was PASSIVE. Every move ran to the end of its draw-cut-sheathe cycle plus a fixed
    ///     beat, so a three-slash chain took 5.18 s and contained 0.72 s of live hitbox. The
    ///     fight was 86% dead air. Threat density, not action count, is the thing to tune - see
    ///     BossDebugHUD.
    ///   - She was UNREADABLE. A failed chain roll fell through to the same weighted table, which
    ///     re-picked the opener. "New attack" and "combo continuation" looked identical, so there
    ///     was no pattern to learn and every death felt arbitrary.
    ///   - She SHUFFLED. Her dodge chance was rolled per frame rather than per swing, making a
    ///     "35% chance" effectively certain; she evaded almost everything and spent the fight
    ///     walking back in from three metres away.
    ///
    /// Read the pacing rules below before changing anything here. They are what make her fair.
    /// </summary>
    [RequireComponent(typeof(CombatActor))]
    [RequireComponent(typeof(ActorLocomotion))]
    [RequireComponent(typeof(BossPerception))]
    public class BossBrain : MonoBehaviour, IBossDebugInfo
    {
        [Header("Target")]
        [SerializeField] Transform player;

        [Header("Repertoire")]
        [Tooltip("Her phrases. Generate them with Tools > DS2 > Build Boss Phrases.")]
        [SerializeField] BossPhrase[] phrases = System.Array.Empty<BossPhrase>();

        [Tooltip("Started when she catches the player in recovery frames. Give it weight 0 so it " +
                 "never comes up in the ordinary roll - being punished for whiffing should feel " +
                 "like a consequence, not a coincidence.")]
        [SerializeField] BossPhrase punishPhrase;

        [Tooltip("Individual moves, for reactions that are not phrases. Only Find() is used.")]
        [SerializeField] BossMoveset moveset;

        [Header("Pacing - the rules that make her fair")]
        [Tooltip("RULE 1. Seconds of sustained pressure before she owes the player a guaranteed " +
                 "opening. Measured in time rather than attacks because a sidestep and a heavy " +
                 "skill are not the same debt.")]
        [SerializeField] float pressureBudget = 4f;

        [Tooltip("RULE 1, the hard promise. Whatever the budget says, never more attacks than " +
                 "this in a row. In a game with no block button this is the single line that " +
                 "separates hard from broken.")]
        [SerializeField] int maxAttacksBeforeGap = 3;

        [Tooltip("RULE 1. The mandatory neutral window. THE most important number in this file. " +
                 "Raised aggression 12 Sep by shortening it from 1.0; the design doc's original " +
                 "was 0.8, so there is a little room left below this before it stops being a " +
                 "real opening.")]
        [SerializeField] float neutralGap = 0.85f;

        [Tooltip("Seconds of budget refunded per second spent out of a phrase.")]
        [SerializeField] float pressureRefundPerSecond = 2.2f;

        [Tooltip("Back away during the gap rather than holding. OFF by default: retreating turns " +
                 "the opening she just promised into dead air and a slow walk back in, which is " +
                 "most of what made her feel passive.")]
        [SerializeField] bool retreatDuringGap;

        [Header("Positioning")]
        [Tooltip("Where she likes to stand: at the edge of her own threat range, not in the " +
                 "player's face. Standing on the boundary is what creates the tension - at 1.8 m " +
                 "she is simply committed, and there is nothing to read.")]
        [SerializeField] float preferredRange = 2.45f;

        [Tooltip("Slack around preferredRange, so she is not constantly correcting by centimetres.")]
        [SerializeField] float rangeTolerance = 0.35f;

        [Tooltip("Beyond this she runs rather than walks. Travel time is dead air; do not make " +
                 "the player watch her stroll.")]
        [SerializeField] float runBeyond = 4.5f;

        [Tooltip("A boss you can stall is a boss you can solve boringly. After this long without " +
                 "the player committing, she closes regardless.")]
        [SerializeField] float patience = 3.2f;

        [Header("Reactions")]
        [Tooltip("RULE 3. Chance she evades a swing she has read. Rolled ONCE per swing. Not 1 - " +
                 "a boss who dodges everything cannot be hit, one who never dodges is a training " +
                 "dummy, and the uncertainty is what makes you commit carefully. Also an " +
                 "aggression dial in disguise: every evade is ~0.7 s of zero threat plus the " +
                 "walk back in, so lowering it makes her push more.")]
        [Range(0f, 1f)] [SerializeField] float dodgeChance = 0.25f;

        [Tooltip("Chance a reactive dodge sidesteps instead of stepping back, and only from 2 m " +
                 "out. 0 means she always backsteps, which is the readable default - a sidestep " +
                 "at melee range looks like repositioning rather than like avoiding the swing. " +
                 "Raise it only once the dodge reads clearly and you want the variety back.")]
        [Range(0f, 1f)] [SerializeField] float sidestepChance;

        [SerializeField] float dodgeRange = 3.5f;
        [SerializeField] float dodgeCooldown = 2.5f;

        [Tooltip("RULE 3. Chance to punish recovery frames. Rolled ONCE per swing. Always " +
                 "punishing is oppressive; never punishing means they can mash.")]
        [Range(0f, 1f)] [SerializeField] float punishChance = 0.35f;

        [SerializeField] float punishCooldown = 3f;

        [Header("Debug")]
        [SerializeField] bool logDecisions;

        CombatActor actor;
        ActorLocomotion locomotion;
        BossPerception perception;
        PostureSystem posture;

        readonly Dictionary<BossPhrase, float> phraseReadyAt = new();

        BossPhrase currentPhrase;
        BossPhrase lastPhrase;
        int stepIndex;
        bool stepExecuting;
        bool willContinue;
        float nextStepAt;

        float pressureSpent;
        int attacksThisSequence;
        float gapEndsAt;

        float nextDodgeAt;
        float nextPunishAt;

        string debugState = "-";
        string debugReaction = "-";

        string IBossDebugInfo.DebugState => debugState;
        float IBossDebugInfo.DebugBudget => Mathf.Max(0f, pressureBudget - pressureSpent);
        string IBossDebugInfo.DebugLastReaction => debugReaction;

        void Awake()
        {
            actor = GetComponent<CombatActor>();
            locomotion = GetComponent<ActorLocomotion>();
            perception = GetComponent<BossPerception>();
            posture = GetComponent<PostureSystem>();

            if (player == null)
            {
                PlayerCombat found = FindAnyObjectByType<PlayerCombat>();
                if (found != null) player = found.transform;
            }
            if (player != null)
            {
                CombatActor pa = player.GetComponent<CombatActor>();
                if (pa != null) perception.SetPlayer(pa);
            }
        }

        void Update()
        {
            if (player == null || actor.IsDead) return;

            // Stunned, she is not deciding anything - and the phrase she was in the middle of is
            // gone, not paused. Resuming a combo after a posture break would take back the reward
            // the break exists to give.
            if (actor.IsStunned || (posture != null && posture.IsStunned))
            {
                AbandonPhrase("stunned");
                locomotion.MoveDirection = Vector3.zero;
                debugState = "STUNNED";
                return;
            }

            locomotion.FaceTarget = player;
            float distance = perception.Distance;

            if (currentPhrase != null)
            {
                pressureSpent += Time.deltaTime;
                AdvancePhrase(distance);
                return;
            }

            // A reaction move (a dodge) is not a phrase; just let it finish.
            if (actor.CurrentMove != null)
            {
                Hold();
                debugState = "reacting: " + actor.CurrentMove.moveId;
                return;
            }

            // Out of a phrase, the budget refills. This is the pacing layer breathing.
            pressureSpent = Mathf.Max(0f, pressureSpent - pressureRefundPerSecond * Time.deltaTime);
            if (pressureSpent <= 0f) attacksThisSequence = 0;

            if (Time.time < gapEndsAt) { HoldGap(distance); return; }

            if (TryReact(distance)) return;

            if (BudgetSpent()) { BeginGap(); return; }

            BossPhrase chosen = SelectPhrase(distance);
            if (chosen != null) { StartPhrase(chosen, distance); return; }

            Position(distance);
        }

        bool BudgetSpent() =>
            pressureSpent >= pressureBudget || attacksThisSequence >= maxAttacksBeforeGap;

        // ---------------------------------------------------------------- phrases

        /// <summary>
        /// Two passes. The first refuses to repeat the phrase she just used, because with a
        /// repertoire this small one phrase otherwise becomes the whole fight. The second drops
        /// that rule if it was the only thing standing in the way - a boss who stands still
        /// because her preferences are momentarily unsatisfiable is exactly the passivity this
        /// rewrite exists to remove.
        /// </summary>
        BossPhrase SelectPhrase(float distance)
        {
            return WeightedPick(distance, allowRepeat: false) ??
                   WeightedPick(distance, allowRepeat: true);
        }

        BossPhrase WeightedPick(float distance, bool allowRepeat)
        {
            float total = 0f;
            foreach (BossPhrase p in phrases)
            {
                if (!Eligible(p, distance, allowRepeat)) continue;
                total += p.weight;
            }
            if (total <= 0f) return null;

            float roll = Random.value * total;
            foreach (BossPhrase p in phrases)
            {
                if (!Eligible(p, distance, allowRepeat)) continue;
                roll -= p.weight;
                if (roll <= 0f) return p;
            }
            return null;
        }

        bool Eligible(BossPhrase p, float distance, bool allowRepeat)
        {
            if (p == null || p.weight <= 0f || !p.IsValidAt(distance)) return false;
            if (!allowRepeat && p == lastPhrase) return false;

            return !phraseReadyAt.TryGetValue(p, out float ready) || Time.time >= ready;
        }

        void StartPhrase(BossPhrase phrase, float distance)
        {
            currentPhrase = phrase;
            stepIndex = 0;
            stepExecuting = false;
            nextStepAt = Time.time;
            pressureSpent += phrase.extraPressureCost;

            if (phrase.cooldown > 0f) phraseReadyAt[phrase] = Time.time + phrase.cooldown;

            if (logDecisions)
                Debug.Log($"[{name}] phrase '{phrase.phraseName}' at {distance:0.0} m", this);

            AdvancePhrase(distance);
        }

        /// <summary>
        /// Runs the current phrase. A phrase is authored, so this only ever decides three things:
        /// whether to link the next step early, whether the branch continues, and whether the
        /// player has left.
        /// </summary>
        void AdvancePhrase(float distance)
        {
            BossPhrase.Step step = currentPhrase.steps[stepIndex];
            debugState = currentPhrase.phraseName + " " + (stepIndex + 1) + "/" + currentPhrase.steps.Length;

            if (stepExecuting)
            {
                if (actor.CurrentMove != null)
                {
                    Hold();

                    // Link into the next step inside the cancel window rather than waiting for
                    // the clip to finish. The player has always chained this way; her waiting for
                    // the full animation is most of why her combos felt slack next to theirs.
                    if (step.linkInCancel && willContinue &&
                        actor.MoveProgress >= step.move.cancelWindow)
                    {
                        // Refused just means the cancel is not legal yet; the natural end of the
                        // move will advance the phrase anyway.
                        Execute(stepIndex + 1);
                    }
                    return;
                }

                // The step finished on its own.
                stepExecuting = false;

                if (!willContinue) { EndPhrase(); return; }

                stepIndex++;
                nextStepAt = Time.time + step.gapAfter;
            }

            if (Time.time < nextStepAt) { Hold(); return; }

            BossPhrase.Step next = currentPhrase.steps[stepIndex];

            // She is swinging at where the player used to be. Drop the rest of the phrase rather
            // than finish it - committing to air is how a boss looks broken rather than beaten.
            if (next.breakRange > 0f && distance > next.breakRange)
            {
                AbandonPhrase("player left (" + distance.ToString("0.0") + " m)");
                return;
            }

            if (!Execute(stepIndex)) { AbandonPhrase("move refused"); return; }
        }

        /// <summary>
        /// Plays one step and rolls its branch once, up front, never per frame. Takes an index
        /// rather than a step so the branch roll reads the step actually being started - rolling
        /// against the outgoing step is how a three-hit phrase quietly becomes a two-hit one.
        /// </summary>
        bool Execute(int index)
        {
            if (index < 0 || index >= currentPhrase.steps.Length) return false;

            BossPhrase.Step step = currentPhrase.steps[index];
            if (step.move == null) return false;
            if (!actor.TryExecute(step.move, step.endOverride)) return false;

            stepIndex = index;
            stepExecuting = true;

            bool hasNext = index + 1 < currentPhrase.steps.Length;
            willContinue = hasNext && Random.value < step.ContinueChance;

            if (step.move.damage > 0) attacksThisSequence++;

            Hold();
            return true;
        }

        void EndPhrase()
        {
            lastPhrase = currentPhrase;
            currentPhrase = null;
            stepExecuting = false;
            debugState = "-";
        }

        void AbandonPhrase(string why)
        {
            if (currentPhrase == null) return;
            if (logDecisions) Debug.Log($"[{name}] dropped '{currentPhrase.phraseName}': {why}", this);
            EndPhrase();
        }

        // ---------------------------------------------------------------- reactions

        /// <summary>
        /// The interrupt layer. Everything here is delayed by BossPerception's reaction time and
        /// rolled exactly once per swing, so she reacts like an opponent rather than like
        /// something reading the input stream.
        /// </summary>
        bool TryReact(float distance)
        {
            if (perception.SeesIncomingSwing && distance <= dodgeRange && Time.time >= nextDodgeAt &&
                perception.RollForStartup(dodgeChance))
            {
                MoveDefinition dodge = PickDodge(distance);
                if (dodge != null && actor.TryExecute(dodge))
                {
                    nextDodgeAt = Time.time + dodgeCooldown;
                    debugReaction = "read swing -> " + dodge.moveId;
                    if (logDecisions) Debug.Log($"[{name}] {debugReaction}", this);
                    Hold();
                    return true;
                }
            }

            if (punishPhrase != null && perception.SeesRecovery && distance <= punishPhrase.maxRange &&
                Time.time >= nextPunishAt && perception.RollForRecovery(punishChance))
            {
                nextPunishAt = Time.time + punishCooldown;
                debugReaction = "punished recovery";
                StartPhrase(punishPhrase, distance);
                return true;
            }

            return false;
        }

        /// <summary>
        /// A REACTIVE DODGE ALWAYS GOES BACKWARDS, and that is a readability decision rather than
        /// a tactical one.
        ///
        /// The player has to be able to see that a swing was avoided. `Evade` is useless for this
        /// - measured RootXZNet is 0.000, it steps out and returns, so it reads as her standing
        /// still and shrugging the hit off. A sidestep does move her, but sideways at melee range
        /// looks like repositioning; nothing about it says "that attack missed me". Straight back,
        /// 2.82 m of measured root motion, away from the blade that was about to land, is the one
        /// direction that is unambiguous - the movement IS the feedback.
        ///
        /// This replaced a flash-and-VFX approach. Making her behave legibly beats annotating
        /// behaviour the player cannot read.
        ///
        /// The cost is real and worth watching: every backstep is ~0.7 s of zero threat plus the
        /// walk back in, and always-backwards spends more of it than a mix did. If the fight
        /// starts to feel like chasing her, that is this, and sidestepChance is the dial.
        /// </summary>
        MoveDefinition PickDodge(float distance)
        {
            if (moveset == null) return null;

            if (sidestepChance > 0f && distance >= 2f && Random.value < sidestepChance)
            {
                MoveDefinition side =
                    moveset.Find(Random.value < 0.5f ? MoveId.QuickShiftL : MoveId.QuickShiftR);
                if (side != null) return side;
            }

            // The readable default, and the fallback if a side dash is missing from the moveset.
            return moveset.Find(MoveId.QuickShiftB) ?? moveset.Find(MoveId.Evade);
        }

        // ---------------------------------------------------------------- pacing and movement

        /// <summary>
        /// The guaranteed opening. Without it, a fight with no block button reads as unfair no
        /// matter how good the player is.
        /// </summary>
        void BeginGap()
        {
            pressureSpent = 0f;
            attacksThisSequence = 0;
            lastPhrase = null;
            gapEndsAt = Time.time + neutralGap;

            if (logDecisions) Debug.Log($"[{name}] neutral gap {neutralGap}s", this);
        }

        void HoldGap(float distance)
        {
            debugState = "GAP " + (gapEndsAt - Time.time).ToString("0.0") + "s";

            if (retreatDuringGap)
            {
                Vector3 away = transform.position - player.position;
                away.y = 0f;
                locomotion.Running = false;
                locomotion.MoveDirection = away.normalized;
                return;
            }

            // Hold inside her own threat range. The opening is that she is not swinging, not that
            // she has gone somewhere else - and standing here means the player has to walk into
            // range to use it, which is a decision rather than a gift.
            if (distance > preferredRange + rangeTolerance)
            {
                locomotion.Running = false;
                Approach(distance);
            }
            else Hold();
        }

        void Position(float distance)
        {
            bool impatient = perception.PlayerIdleFor > patience || perception.PlayerIsRetreating;
            debugState = impatient ? "CLOSING" : "POSITIONING";

            if (distance > preferredRange + rangeTolerance)
            {
                locomotion.Running = impatient || distance > runBeyond;
                Approach(distance);
                return;
            }

            // Inside her preferred spacing with nothing to throw: hold and face. There is no
            // strafe or walk-back clip, so circling would play a forward walk across a sideways
            // translation and she would turn away from the player to do it. Holding reads better
            // than skating. See Known gaps in build-plan.md.
            Hold();
        }

        void Approach(float distance)
        {
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            locomotion.MoveDirection = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector3.zero;
        }

        void Hold()
        {
            locomotion.MoveDirection = Vector3.zero;
            locomotion.Running = false;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.2f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, preferredRange);
            Gizmos.color = new Color(0.8f, 0.5f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, dodgeRange);
        }
    }
}
