using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Keeps the katana drawn between attacks.
    ///
    /// The vendor clips are authored for an iaijutsu character: the blade lives in its scabbard
    /// and every attack is a complete draw-cut-sheathe cycle. Idle, Walk and Run each carry a
    /// SwitchSocket event at frame 0 that stows it - and because Idle LOOPS, that event re-fires
    /// every few seconds. Setting the socket once, however late, cannot survive that.
    ///
    /// So it is re-asserted every frame instead. Runs in LateUpdate, after the Animator has
    /// evaluated and its events have fired, so this always gets the last word.
    ///
    /// The exception is deliberate: while a move that does NOT ask for a drawn blade is playing -
    /// the three Special skills - the clip is left in charge, because there the sheathe IS the
    /// move. That asymmetry is the design: constant sheathing reads as a tax on the player, but
    /// as spectacle on a special and as a punish window on the boss.
    /// </summary>
    [RequireComponent(typeof(CombatActor))]
    public class WeaponStance : MonoBehaviour
    {
        [Tooltip("Socket the blade is held in when ready. Must match an eventStringName on " +
                 "Character_Weapon_Controller.")]
        [SerializeField] string drawnSocket = "To_Hand_R_Socket-Blade";

        [Tooltip("Where the blade sits in Special stance - back in its scabbard, scabbard in the " +
                 "left hand. Copied from Sp_Idle's own frame-0 event, which is also exactly what " +
                 "Idle uses: the pack's characters are sheathed by default and every attack is a " +
                 "complete draw-cut-sheathe.")]
        [SerializeField] string sheathedSocket = "To_Katana_Close-Blade, To_Hand_L_Socket-Sheath";

        [Tooltip("Off restores the vendor behaviour - sheathed except mid-attack.")]
        [SerializeField] bool keepDrawn = true;

        CombatActor actor;
        Character_Weapon_Controller weapon;
        Animator animator;

        static readonly int StanceParam = Animator.StringToHash("Stance");

        void Awake()
        {
            actor = GetComponent<CombatActor>();
            weapon = GetComponent<Character_Weapon_Controller>();
            animator = GetComponent<Animator>();

            if (weapon == null)
                Debug.LogError("[WeaponStance] No Character_Weapon_Controller on this object.", this);
        }

        void LateUpdate()
        {
            if (!keepDrawn || weapon == null || actor.IsDead) return;

            // A move with no endWeaponSocket wants its own sheathing - stay out of its way.
            MoveDefinition move = actor.CurrentMove;
            if (move != null && string.IsNullOrEmpty(move.endWeaponSocket)) return;

            // In Special stance the blade belongs in its scabbard. Every Special begins from a
            // sheathed blade, so holding it drawn here is what made the katana pop into the
            // scabbard in the middle of the move - the clip's own frame-0 event putting back
            // what this component had been yanking out every frame. Sheathing during the stance
            // change instead moves that switch somewhere the player is not watching a swing.
            bool special = animator != null && animator.GetBool(StanceParam);

            weapon.SwitchSocketByString(special ? sheathedSocket : drawnSocket);
        }
    }
}
