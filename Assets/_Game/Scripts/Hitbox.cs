using System.Collections.Generic;
using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Trigger collider on the weapon, opened and closed by CombatActor from the move asset.
    ///
    /// Parent this to the katana GameObject. Character_Weapon_Controller moves the blade between
    /// hand and back by switching ParentConstraint sources rather than reparenting, so the katana
    /// object stays put in the hierarchy and anything attached to it follows correctly.
    ///
    /// One hit per target per swing: without the hit-set a single slash registers on every physics
    /// frame the collider overlaps, which reads as a one-hit kill.
    ///
    /// DETECTION IS A SWEPT OVERLAP QUERY, NOT JUST OnTriggerEnter. The first playtest found two
    /// failures that are the same root cause:
    ///
    /// - A slash visibly passing through her doing nothing. The blade is 0.06 x 0.07 in cross
    ///   section and travels fast, and it belongs to the character root's compound rigidbody,
    ///   which is kinematic with DISCRETE collision detection. Physics samples poses 50 times a
    ///   second; between two samples a thin fast box can be entirely one side of her and then
    ///   entirely the other. Classic tunnelling.
    /// - The second slash of a chain doing nothing after the first one hit. OnTriggerEnter fires
    ///   on ENTRY. At close range the blade is often already inside her hurtbox at the instant the
    ///   next window opens, so there is no entry to report and the swing registers nothing.
    ///
    /// So while the window is open this sub-steps between the previous blade pose and the current
    /// one, running Physics.OverlapBox at each step. Sub-stepping is what defeats the tunnelling;
    /// asking "is anything inside right now" rather than "did anything just enter" is what defeats
    /// the chain case. The trigger callback is kept as well - alreadyHit means whichever path sees
    /// the target first wins and the other is a no-op - because losing hits entirely would be a
    /// worse failure than the one being fixed.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hitbox : MonoBehaviour
    {
        [SerializeField] LayerMask hittableLayers = ~0;

        [Header("Sweep")]
        [Tooltip("Poses tested between the blade's position last physics step and this one. The " +
                 "blade is thin and fast enough to pass clean through a target inside one step, " +
                 "so 1 (test only where it is now) drops hits. Raise it if fast Specials still " +
                 "pass through; each step is one OverlapBox.")]
        [Range(1, 12)] [SerializeField] int sweepSubSteps = 5;

        [Tooltip("Logs every sweep hit and which path caught it. Use it to confirm a missed " +
                 "slash is a detection problem rather than a window that never opened.")]
        [SerializeField] bool logSweep;

        [Header("Parry")]
        [Tooltip("Seconds the ATTACKER is staggered when this swing is parried. Matched to the " +
                 "posture break by design decision: a parry is the skill route to the same " +
                 "opening that sustained pressure earns.")]
        [SerializeField] float parryStagger = 2f;

        [Tooltip("Damage taken multiplier while staggered by a parry. Matches the posture break.")]
        [SerializeField] float parryStaggerDamageMultiplier = 2f;

        /// <summary>
        /// Targets this swing has RESOLVED against - damaged or been parried by. Deliberately not
        /// "targets this swing has touched": see TryHit for why that distinction is the whole bug.
        /// </summary>
        readonly HashSet<CombatActor> alreadyHit = new();

        /// <summary>
        /// Targets a non-damaging outcome has already been announced for this swing. Separate from
        /// alreadyHit because an evaded contact must NOT end the swing, but must also not re-fire
        /// its sound and VFX on every frame the blade stays inside an invulnerable target.
        /// </summary>
        readonly HashSet<CombatActor> feedbackReported = new();

        readonly Collider[] overlaps = new Collider[16];

        Collider trigger;
        BoxCollider box;
        CombatActor owner;
        MoveDefinition move;

        Vector3 lastPos;
        Quaternion lastRot;
        bool hasLastPose;

        // Closest-approach diagnostic. Only populated under logSweep.
        Hurtbox[] opposing;
        float closestApproach;
        float closestApproachAt;

        void Awake()
        {
            trigger = GetComponent<Collider>();
            box = trigger as BoxCollider;
            trigger.isTrigger = true;
            trigger.enabled = false;

            if (box == null)
                Debug.LogWarning("[Hitbox] Expected a BoxCollider; the sweep will fall back to " +
                                 "the collider's axis-aligned bounds, which is less accurate.", this);
        }

        public void Open(CombatActor attacker, MoveDefinition sourceMove)
        {
            owner = attacker;
            move = sourceMove;
            alreadyHit.Clear();
            feedbackReported.Clear();
            trigger.enabled = true;

            // Start the sweep from where the blade is NOW. Carrying the pose over from the last
            // swing would sweep across the gap between them - through anything standing in
            // between, and across the whole arena after a retry teleport.
            hasLastPose = false;

            closestApproach = float.PositiveInfinity;
            closestApproachAt = -1f;

            // Cached per swing rather than per frame: FindObjectsByType in LateUpdate across a
            // whole window would cost more than the sweep it is meant to explain.
            opposing = logSweep ? FindObjectsByType<Hurtbox>(FindObjectsInactive.Exclude) : null;
        }

        public void Close()
        {
            ReportSwing();
            trigger.enabled = false;
            alreadyHit.Clear();
            feedbackReported.Clear();
            move = null;
            hasLastPose = false;
        }

        /// <summary>
        /// The real detection path, and it MUST be LateUpdate.
        ///
        /// The frame order is Update -> animation -> OnAnimatorMove -> LateUpdate. The blade is
        /// posed by the Animator and the actors are moved by controller.Move() inside
        /// OnAnimatorMove, so anything earlier than LateUpdate - FixedUpdate above all - reads
        /// both the blade AND the target a full frame stale. Sampling at the physics rate also
        /// aliases against the animation: above 50 fps whole blade poses are never looked at, and
        /// below it the same pose is tested twice.
        ///
        /// Physics.SyncTransforms is then required rather than optional, because the project has
        /// m_AutoSyncTransforms OFF: without it the query tests where the colliders were at the
        /// last physics step, not where they are on screen. That gap is what a player sees as a
        /// blade passing visibly through her and doing nothing.
        /// </summary>
        void LateUpdate()
        {
            if (move == null) return;

            // Push this frame's animation and root motion into the physics scene before asking
            // it anything. Two characters and an arena - the cost is not the issue it would be
            // in a populated scene.
            Physics.SyncTransforms();

            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;

            if (!hasLastPose)
            {
                lastPos = pos;
                lastRot = rot;
                hasLastPose = true;
            }

            int steps = Mathf.Max(1, sweepSubSteps);
            for (int i = 1; i <= steps; i++)
            {
                float k = (float)i / steps;
                SweepAt(Vector3.Lerp(lastPos, pos, k), Quaternion.Slerp(lastRot, rot, k));
            }

            TrackClosestApproach(pos);

            lastPos = pos;
            lastRot = rot;
        }

        /// <summary>
        /// Records how close the blade ever got to an opponent during this swing, and at what
        /// point in the move.
        ///
        /// WHY THIS EXISTS. "The window opened and nothing happened" is not a diagnosis - it is
        /// the same sentence for a blade that swung a metre wide and for one that passed through
        /// and was rejected. Retiming a window by reasoning about which part of a clip "looks
        /// like" the contact has now been wrong three times on this move. This reports the number
        /// instead: closest approach, and the normalized time it happened at. Set the window
        /// around that and stop arguing with the animation.
        ///
        /// Only runs under logSweep, and only while a window is open, so it costs nothing shipped.
        /// </summary>
        void TrackClosestApproach(Vector3 bladePos)
        {
            if (!logSweep || opposing == null) return;

            foreach (Hurtbox h in opposing)
            {
                if (h == null) continue;

                // The attacker's OWN hurtbox is a few centimetres from their own blade at all
                // times. Leaving it in would report 0.02 m on every swing and the diagnostic
                // would say "practically touching" no matter how badly the swing missed.
                if (h.Owner == null || h.Owner == owner) continue;

                // Closest point ON the hurtbox, not its origin - a capsule's centre is a metre
                // off the surface and would make every miss look far worse than it was.
                Collider c = h.GetComponent<Collider>();
                Vector3 target = c != null ? c.ClosestPoint(bladePos) : h.transform.position;

                float d = Vector3.Distance(bladePos, target);
                if (d >= closestApproach) continue;

                closestApproach = d;
                closestApproachAt = owner != null ? owner.MoveProgress : -1f;
            }
        }

        /// <summary>
        /// Says whether a swing that ended found anything, and how close it came. This is the
        /// difference between "the window never opened" and "the window opened and missed" -
        /// guessing between those two is how a hitbox bug eats an afternoon.
        /// </summary>
        void ReportSwing()
        {
            if (!logSweep) return;

            string who = owner != null ? owner.name : "?";
            string what = move != null ? move.moveId.ToString() : "?";

            string approach = closestApproachAt >= 0f
                ? $"closest approach {closestApproach:0.000} m at t={closestApproachAt:0.000}"
                : "closest approach not sampled";

            if (alreadyHit.Count > 0)
            {
                Debug.Log($"[Hitbox] {who} {what}: CONNECTED ({alreadyHit.Count}). {approach}", this);
                return;
            }

            // The number that ends the argument. If the blade got within a few centimetres, the
            // window is in the right place and the miss is detection or rejection. If it never got
            // closer than a metre, no window on this clip will ever land and the swing itself is
            // the problem - a different reach, a different move, or a fatter damage volume.
            Debug.Log($"[Hitbox] {who} {what}: window opened and closed with NO hit. {approach}", this);
        }

        void SweepAt(Vector3 pos, Quaternion rot)
        {
            Vector3 scale = transform.lossyScale;
            Vector3 half, centerOffset;

            if (box != null)
            {
                half = Vector3.Scale(box.size * 0.5f, scale);
                centerOffset = Vector3.Scale(box.center, scale);
            }
            else
            {
                half = trigger.bounds.extents;
                centerOffset = Vector3.zero;
            }

            Vector3 center = pos + rot * centerOffset;

            int count = Physics.OverlapBoxNonAlloc(
                center, half, overlaps, rot, hittableLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                if (logSweep && overlaps[i] != null)
                    Debug.Log("[Hitbox] sweep touched " + overlaps[i].name, this);

                TryHit(overlaps[i]);
            }
        }

        void OnTriggerEnter(Collider other) => TryHit(other);

        void TryHit(Collider other)
        {
            if (move == null || other == null) return;
            if ((hittableLayers.value & (1 << other.gameObject.layer)) == 0) return;

            var hurtbox = other.GetComponent<Hurtbox>();
            if (hurtbox == null || hurtbox.Owner == null) return;

            CombatActor victim = hurtbox.Owner;
            if (victim == owner) return;

            // A SWING IS SPENT BY A HIT THAT LANDED, NOT BY A HIT THAT TOUCHED.
            //
            // This used to be `if (!alreadyHit.Add(victim)) return;` - the target was recorded
            // BEFORE ApplyDamage said what happened, so a contact that returned Evaded or
            // NoEffect still burned the swing, and the real contact later in the same arc was
            // discarded in silence.
            //
            // That is what made Slash 2 undamageable for BOTH actors. Its window opened at 0.080
            // (player) and 0.095 (boss), during the wind-up, where this thin blade can clip an
            // opponent who still has i-frames from a dodge. One throwaway touch, swing over.
            // Slash 1 and Slash 3 open at 0.300 and 0.289 - at the contact itself - so they never
            // got the chance to waste themselves and always worked.
            //
            // The proof is in the 19 Sep notes: widening the window from 0.095-0.320 to
            // 0.080-0.478 took Slash 2 from landing 2 swings in 5 to landing never. Nothing about
            // timing explains a wider window landing LESS. This does - more window in front of the
            // swing is more chance to spend it on nothing.
            if (alreadyHit.Contains(victim)) return;

            // Captured up front. Staggering a parried attacker interrupts their move, which calls
            // back into Close() and nulls the field - so anything read after that point would be
            // reading the aftermath of this hit rather than the hit itself.
            CombatActor attacker = owner;
            MoveDefinition landed = move;
            Vector3 contact = other.ClosestPoint(transform.position);

            int damage = Mathf.RoundToInt(
                landed.damage * (attacker != null ? attacker.DamageDealtMultiplier : 1f));

            Vector3 from = attacker != null ? attacker.transform.position : transform.position;
            HitResult result = victim.ApplyDamage(damage, landed, from);

            // NOW the swing is spent - and only for the outcomes that actually settled it.
            // Damaged and Parried both end the exchange against this target; the alreadyHit set
            // still guarantees one swing can never take health twice. Evaded and NoEffect leave
            // the target eligible, so a blade that touched during i-frames can still land when
            // those i-frames run out a few frames later in the same arc.
            // Killed belongs here as much as Damaged does - it is the most settled outcome there
            // is, and leaving it out would let a killing blow keep re-testing a corpse.
            bool resolved = result == HitResult.Damaged ||
                            result == HitResult.Killed ||
                            result == HitResult.Parried;
            if (resolved) alreadyHit.Add(victim);

            // Non-damaging outcomes announce themselves ONCE. Without this the sweep would re-fire
            // the evade sound and VFX every frame the blade sat inside an invulnerable target,
            // which is a worse artefact than the silence this project already fixed once.
            bool announce = resolved || feedbackReported.Add(victim);

            // The result is the whole diagnosis. "Connected" is not the same as "damaged":
            // Evaded means i-frames ate it, Parried means it was deflected, NoEffect means the
            // target was already dead. Without this, every one of those looks identical to a
            // hitbox that never fired.
            if (logSweep)
                Debug.Log($"[Hitbox] {(attacker != null ? attacker.name : "?")} " +
                          $"{landed.moveId} -> {victim.name}: {result}, dmg={damage}, " +
                          $"victim i-frames={victim.IsInvulnerable}, parrying={victim.IsParrying}, " +
                          $"swing {(resolved ? "SPENT" : "still live")}", this);

            if (!announce) return;

            // Deflected. The victim decided that; only we hold a reference to who swung, so the
            // stagger is applied from here.
            if (result == HitResult.Parried && attacker != null)
                attacker.Stagger(parryStagger, parryStaggerDamageMultiplier);

            // Posture is boss-only, so most targets have no PostureSystem and that is fine.
            victim.TryGetComponent(out PostureSystem posture);
            if (posture != null && result == HitResult.Damaged) posture.Add(landed.postureDamage);

            HitFeedback.Report(new HitInfo
            {
                attacker = attacker,
                victim = victim,
                move = landed,

                // The real contact point, not the attacker's feet - VFX and the camera impulse
                // both want to originate where the blade actually met the body.
                point = contact,

                damage = damage,
                result = result,
                victimIsPlayer = victim.GetComponent<PlayerCombat>() != null,

                // Read AFTER the posture hit lands, so the rising-pitch cue reflects where the
                // meter is now rather than where it was a moment ago.
                victimPosture = posture != null ? posture.Normalized : 0f,
            });
        }
    }
}
