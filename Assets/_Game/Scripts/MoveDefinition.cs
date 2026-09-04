using UnityEngine;

namespace DS2
{
    /// <summary>Stable identity for a move, used by progression and the unlock ladder.</summary>
    public enum MoveId
    {
        None = 0,
        Slash1, Slash2, Slash3,
        Evade,
        QuickShiftF, QuickShiftB, QuickShiftL, QuickShiftR,
        Draw, Sheathe,
        Skill1, Skill2, Skill3,
    }

    /// <summary>
    /// One move, as data. Tuning is where the fun gets made and you will do it a hundred times,
    /// so every number that decides how a move feels lives here rather than in code.
    ///
    /// Timing windows are NORMALIZED (0-1 of the clip), never animation events. The source FBXs
    /// already carry baked SwitchSocket events; adding our own would be reverted by any reimport
    /// and would mean leaving the editor to retune. CombatActor polls normalizedTime instead,
    /// which makes a hitbox a float you can drag while the game is running.
    ///
    /// Because the windows are normalized, they are independent of speedMultiplier - changing how
    /// fast a move plays does not move its hitbox.
    /// </summary>
    [CreateAssetMenu(menuName = "DS2/Move Definition", fileName = "Move_")]
    public class MoveDefinition : ScriptableObject
    {
        [Header("Identity")]
        public MoveId moveId;

        [Tooltip("Animator state name in KG_Combat. NOT the clip name - they differ " +
                 "(state Sp_Skill2 plays clip K_Sp_Skill_2).")]
        public string stateName;

        [Header("Playback")]
        [Tooltip("measuredClipLength / duration. Measured 4 Sep 2026; see Docs/build-plan.md.")]
        public float speedMultiplier = 1f;

        [Tooltip("How long the move takes once speedMultiplier is applied, in seconds.")]
        public float duration = 1f;

        [Tooltip("On for attacks and dodges - the clips carry real displacement and stripping it " +
                 "makes every swing feel weightless. Off for locomotion.")]
        public bool useRootMotion = true;

        [Header("Damage")]
        public int damage;
        public int postureDamage;

        [Header("Hitbox window (normalized)")]
        [Range(0f, 1f)] public float hitboxOpen;
        [Range(0f, 1f)] public float hitboxClose;

        [Header("Invulnerability window (normalized)")]
        [Range(0f, 1f)] public float iframeStart;
        [Range(0f, 1f)] public float iframeEnd;

        [Header("Cancelling")]
        [Tooltip("Earliest normalized time an input may cancel this move into nextInChain. " +
                 "1 means uncancellable. This is what stops mashing.")]
        [Range(0f, 1f)] public float cancelWindow = 1f;

        public MoveDefinition nextInChain;

        [System.NonSerialized] int cachedHash;

        /// <summary>Animator state hash, resolved once on first use.</summary>
        public int StateHash
        {
            get
            {
                if (cachedHash == 0 && !string.IsNullOrEmpty(stateName))
                    cachedHash = Animator.StringToHash(stateName);
                return cachedHash;
            }
        }

        public bool HasHitbox => hitboxClose > hitboxOpen;
        public bool HasIFrames => iframeEnd > iframeStart;
        public bool CanChain => nextInChain != null && cancelWindow < 1f;
    }
}
