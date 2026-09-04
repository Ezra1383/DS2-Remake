using UnityEngine;

namespace DS2
{
    /// <summary>
    /// The volume that can be hit. Kept separate from the CharacterController capsule so the
    /// two can be tuned independently - collision wants a tight capsule, getting hit wants a
    /// generous one.
    ///
    /// Put this on a child object on the Enemy or Player layer, not on the actor root, so hitboxes
    /// can filter by layer without also filtering out the character controller.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hurtbox : MonoBehaviour
    {
        [Tooltip("Left empty, the nearest CombatActor up the hierarchy is used.")]
        [SerializeField] CombatActor owner;

        public CombatActor Owner => owner;

        void Awake()
        {
            if (owner == null) owner = GetComponentInParent<CombatActor>();
            if (owner == null)
                Debug.LogError("[Hurtbox] No CombatActor found in parents.", this);
        }
    }
}
