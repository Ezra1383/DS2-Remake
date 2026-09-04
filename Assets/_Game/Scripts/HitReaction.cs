using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Picks the left or right flinch from the angle between the hit and the actor's facing.
    /// Cheap, and it does a lot for making hits read as landing somewhere rather than just
    /// subtracting a number.
    ///
    /// Naming trap: clip Hit1 comes from K_Hit_R.fbx and clip Hit2 from K_Hit_L.fbx. The state
    /// names in KG_Combat are already corrected for this, so use the state names below and do not
    /// second-guess them against the clip names.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class HitReaction : MonoBehaviour
    {
        [SerializeField] float crossFade = 0.08f;

        Animator animator;
        CombatActor actor;

        static readonly int HitLeft = Animator.StringToHash("Hit_L");
        static readonly int HitRight = Animator.StringToHash("Hit_R");

        void Awake()
        {
            animator = GetComponent<Animator>();
            actor = GetComponent<CombatActor>();
        }

        /// <summary>Called by CombatActor when damage lands and the actor survives it.</summary>
        public void Play(Vector3 fromPosition)
        {
            // A flinch must never interrupt a committed move - being staggered out of your own
            // swing on every chip of damage makes the fight feel like it is playing itself.
            if (actor != null && actor.CurrentMove != null) return;

            Vector3 toSource = fromPosition - transform.position;
            toSource.y = 0f;

            bool fromRight = Vector3.Dot(transform.right, toSource) >= 0f;
            animator.CrossFadeInFixedTime(fromRight ? HitRight : HitLeft, crossFade);
        }
    }
}
