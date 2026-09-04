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

            // Step 2.1 adds the posture call here: move.postureDamage into the victim's
            // PostureSystem. Deliberately absent until that system exists.
            victim.ApplyDamage(move.damage, move, owner != null ? owner.transform.position : transform.position);
        }
    }
}
