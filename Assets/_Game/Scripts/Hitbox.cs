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

            int damage = Mathf.RoundToInt(
                move.damage * (owner != null ? owner.DamageDealtMultiplier : 1f));

            Vector3 from = owner != null ? owner.transform.position : transform.position;
            HitResult result = victim.ApplyDamage(damage, move, from);

            // Posture is boss-only, so most targets have no PostureSystem and that is fine.
            victim.TryGetComponent(out PostureSystem posture);
            if (posture != null && result == HitResult.Damaged) posture.Add(move.postureDamage);

            HitFeedback.Report(new HitInfo
            {
                attacker = owner,
                victim = victim,
                move = move,

                // The real contact point, not the attacker's feet - VFX and the camera impulse
                // both want to originate where the blade actually met the body.
                point = other.ClosestPoint(transform.position),

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
