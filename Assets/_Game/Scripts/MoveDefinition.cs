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

        // Appended deliberately: MoveId is serialized by value on every move asset, so inserting
        // anywhere but the end would silently re-label existing assets.
        Parry,
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
        [Tooltip("THE tuning dial for this move. Drives the animation playback rate AND every " +
                 "timing window, live, while the game is running. 1 is the animator's own pacing.")]
        public float speedMultiplier = 1f;

        [Tooltip("Raw clip length in seconds at speed 1. Measured from the source FBX - not a " +
                 "tuning value, do not hand-edit. See Docs/clip-report.csv.")]
        public float measuredLength = 1f;

        [Tooltip("On for attacks and dodges - the clips carry real displacement and stripping it " +
                 "makes every swing feel weightless. Off for locomotion.")]
        public bool useRootMotion = true;

        [Header("Damage")]
        public int damage;
        public int postureDamage;

        [Header("Hitbox window (normalized)")]
        [Range(0f, 1f)] public float hitboxOpen;
        [Range(0f, 1f)] public float hitboxClose;

        [Header("Parry window (normalized)")]
        [Tooltip("While inside this window an incoming attack is DEFLECTED rather than taken: no " +
                 "damage, and the attacker is staggered. Leave at 0 for a move that cannot parry.\n\n" +
                 "Note this is not invulnerability. Outside the window the move has no defence at " +
                 "all, which is the entire cost of reaching for a parry instead of a dodge.")]
        [Range(0f, 1f)] public float parryStart;
        [Range(0f, 1f)] public float parryEnd;

        [Header("Invulnerability window (normalized)")]
        [Range(0f, 1f)] public float iframeStart;
        [Range(0f, 1f)] public float iframeEnd;

        [Header("Tracking")]
        [Tooltip("Normalized point where facing LOCKS. Until then the actor keeps turning toward " +
                 "its target at the rate below. This is what makes a dodge a timing decision " +
                 "rather than a walk-away: with no tracking at all, every committed swing whiffs " +
                 "against a player who is simply moving sideways, and the exchange resolves to " +
                 "nothing. Usually a little before hitboxOpen.")]
        [Range(0f, 1f)] public float trackUntil = 0.25f;

        [Tooltip("Degrees per second of turn during that window. 0 is a fully committed swing " +
                 "that cannot correct at all - FromSoft splits exactly this distinction into " +
                 "spinning and non-spinning attacks. Player moves sit at 0 so input stays in " +
                 "charge of where a swing points.")]
        public float trackDegreesPerSecond;

        [Header("Trim")]
        [Tooltip("Normalized point where the move is considered over and control returns. 1 plays " +
                 "the whole clip. Lower it to cut dead recovery: these clips are full draw-cut-" +
                 "sheathe cycles and most of them end with the character simply standing there.")]
        [Range(0.1f, 1f)] public float moveEnd = 1f;

        [Header("Weapon socket")]
        [Tooltip("Optional. Applied to Character_Weapon_Controller when the move starts, e.g. " +
                 "To_Hand_R_Socket-Blade to draw. Leave empty to let the clip's own baked " +
                 "SwitchSocket events decide. " +
                 "Required for imported animations: downloaded clips carry no SwitchSocket events, " +
                 "so without this the katana stays wherever the last vendor clip left it - usually " +
                 "sheathed, while she swings an empty hand.")]
        public string weaponSocket;

        [Tooltip("Optional. Applied when the move ENDS. The clip's own baked events fire partway " +
                 "through and will sheathe the blade whether you asked or not, so this is what " +
                 "actually keeps it drawn between attacks.")]
        public string endWeaponSocket;

        [Header("Feel")]
        [Tooltip("Whoosh played as the move starts. This is also the Step 2.3 TELEGRAPH - the " +
                 "build plan calls a distinct audio cue per move the cheapest, largest-effect " +
                 "tell available, and since no new animation can be authored it is most of what " +
                 "the player has to read an attack by. Optional; silence is never an error.")]
        public AudioClip swingSound;

        [Tooltip("When the whoosh fires, as normalized time. Normalized for the same reason every " +
                 "other window here is: the global 1.4x tempo would slide a fixed offset out of " +
                 "sync with the animation. Keep it below hitboxOpen so it reads as a wind-up.")]
        [Range(0f, 1f)] public float swingSoundNormalized = 0.12f;

        [Tooltip("Optional. Overrides the shared impact bank when this move should sound unique.")]
        public AudioClip impactSound;

        [Tooltip("Seconds of hit stop this move causes. 0 derives it from damage, which is " +
                 "usually what you want.")]
        public float hitstopOverride;

        [Tooltip("Camera trauma, 0-1. 0 derives it from damage and from who got hit.")]
        [Range(0f, 1f)] public float traumaOverride;

        [Header("Cancelling")]
        [Tooltip("Earliest normalized time an input may cancel this move into nextInChain. " +
                 "1 means uncancellable. This is what stops mashing.")]
        [Range(0f, 1f)] public float cancelWindow = 1f;

        public MoveDefinition nextInChain;

        [Tooltip("How often an AI continues into nextInChain. 1 always, 0 never. The player " +
                 "ignores this - they chain by pressing attack. This is what makes her combos " +
                 "learnable: a fixed opener that sometimes extends is readable, a coin flip at " +
                 "every link is not.")]
        [Range(0f, 1f)] public float chainChance = 1f;

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

        /// <summary>
        /// How long the move actually takes, in seconds. Derived rather than stored so that
        /// changing speedMultiplier can never desync the animation from the timing windows.
        /// </summary>
        public float Duration => speedMultiplier > 0.01f ? measuredLength / speedMultiplier : measuredLength;

        /// <summary>Turn rate to apply at normalized time t, or 0 once facing has locked.</summary>
        public float TrackingAt(float t) => t < trackUntil ? trackDegreesPerSecond : 0f;

        public bool HasSwingSound => swingSound != null;

        public bool HasParry => parryEnd > parryStart;

        public bool HasHitbox => hitboxClose > hitboxOpen;
        public bool HasIFrames => iframeEnd > iframeStart;
        public bool CanChain => nextInChain != null && cancelWindow < 1f;

        /// <summary>
        /// Move names as the player should read them, not as the enum spells them. Lives here
        /// rather than on either screen because the death card and the HUD have to agree - the
        /// card names the move you just learned and the HUD lists it a second later, and two
        /// copies of this table would eventually disagree about one of them.
        /// </summary>
        public static string DisplayName(MoveId id) => id switch
        {
            MoveId.Slash1 => "Slash 1",
            MoveId.Slash2 => "Slash 2",
            MoveId.Slash3 => "Slash 3",
            MoveId.Evade => "Evade",
            MoveId.Parry => "Parry",
            MoveId.QuickShiftF => "Quick Shift - Forward",
            MoveId.QuickShiftB => "Quick Shift - Back",
            MoveId.QuickShiftL => "Quick Shift - Left",
            MoveId.QuickShiftR => "Quick Shift - Right",
            MoveId.Draw => "The Draw",
            MoveId.Sheathe => "Sheathe",
            MoveId.Skill1 => "Skill 1",
            MoveId.Skill2 => "Skill 2",
            MoveId.Skill3 => "Skill 3",
            _ => id.ToString(),
        };
    }
}
