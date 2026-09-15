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
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hitbox : MonoBehaviour
    {
        [SerializeField] LayerMask hittableLayers = ~0;

        [Header("Parry")]
        [Tooltip("Seconds the ATTACKER is staggered when this swing is parried. Matched to the " +
                 "posture break by design decision: a parry is the skill route to the same " +
                 "opening that sustained pressure earns.")]
        [SerializeField] float parryStagger = 2f;

        [Tooltip("Damage taken multiplier while staggered by a parry. Matches the posture break.")]
        [SerializeField] float parryStaggerDamageMultiplier = 2f;

        readonly HashSet<CombatActor> alreadyHit = new();
        Collider trigger;
        CombatActor owner;
        MoveDefinition move;

        void Awake()
        {
            trigger = GetComponent<Collider>();
            trigger.isTrigger = true;
            trigger.enabled = false;
        }

        public void Open(CombatActor attacker, MoveDefinition sourceMove)
        {
            owner = attacker;
            move = sourceMove;
            alreadyHit.Clear();
            trigger.enabled = true;
        }

        public void Close()
        {
            trigger.enabled = false;
            alreadyHit.Clear();
            move = null;
        }

        void OnTriggerEnter(Collider other)
        {
            if (move == null) return;
            if ((hittableLayers.value & (1 << other.gameObject.layer)) == 0) return;

            var hurtbox = other.GetComponent<Hurtbox>();
            if (hurtbox == null || hurtbox.Owner == null) return;

            CombatActor victim = hurtbox.Owner;
            if (victim == owner) return;
            if (!alreadyHit.Add(victim)) return;

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
