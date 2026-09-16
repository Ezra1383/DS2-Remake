using UnityEngine;

namespace DS2
{
    /// <summary>
    /// The ribbon off the blade tip - and, on the boss, a telegraph.
    ///
    /// `build-plan.md` Step 2.3 lists "weapon trail that colours in during startup" as the second
    /// cheapest telegraph available, behind per-move audio. That is what the telegraph mode here
    /// is: her blade warms from cold to hot across the wind-up and snaps to the swing colour the
    /// instant the hitbox opens. The player reads the ramp and times the dodge off it, which is
    /// the same thing `trackUntil` exists to make worth doing.
    ///
    /// THE PLAYER DOES NOT GET THE RAMP. It is a mirror match - same clips, same silhouette - so
    /// symmetric feedback makes exchanges unreadable. The camera already shakes for the victim
    /// rather than the attacker for exactly this reason; a wind-up glow that only she has is the
    /// same rule applied to the blade. The player's trail appears with the hitbox and nowhere
    /// else, which also keeps her swings clean.
    ///
    /// Rides the same normalized clock as the hitbox: no animation events, so nothing here is
    /// undone by a pack reimport, and the windows stay draggable while the game is running.
    /// </summary>
    public class WeaponTrail : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Leave empty to use the CombatActor on this GameObject.")]
        [SerializeField] CombatActor actor;

        [Tooltip("The TrailRenderer at the blade tip. Built by Tools > DS2 > Build VFX.")]
        [SerializeField] TrailRenderer trail;

        [Header("Telegraph")]
        [Tooltip("ON for the boss, OFF for the player. Her blade colours in across the wind-up " +
                 "so the swing can be read before it lands; the player's trail only appears with " +
                 "the hitbox. Asymmetric on purpose - see the class summary.")]
        [SerializeField] bool telegraph;

        [Tooltip("Normalized time the wind-up glow starts. Not 0 - the first frames of a move " +
                 "are still the previous pose blending out, and a trail there reads as a smear.")]
        [Range(0f, 0.5f)] [SerializeField] float telegraphStart = 0.06f;

        [Header("Window")]
        [Tooltip("Normalized time before the hitbox opens that the trail starts. The blade is " +
                 "already travelling by then; starting exactly on the hitbox looks like it " +
                 "switched on mid-arc.")]
        [Range(0f, 0.3f)] [SerializeField] float lead = 0.05f;

        [Tooltip("Normalized time after the hitbox closes that the trail keeps emitting, so the " +
                 "follow-through has a ribbon and the arc does not end on the damage frame.")]
        [Range(0f, 0.3f)] [SerializeField] float lag = 0.08f;

        [Header("Colour")]
        [SerializeField] Color swingColor = new(1f, 0.62f, 0.78f, 1f);
        [SerializeField] Color telegraphCold = new(0.35f, 0.42f, 0.62f, 1f);
        [SerializeField] Color telegraphHot = new(1f, 0.32f, 0.30f, 1f);

        [Tooltip("Width multiplier during the wind-up, before the swing proper. A thin warm line " +
                 "that thickens into the swing reads as building rather than as already going.")]
        [Range(0.05f, 1f)] [SerializeField] float telegraphWidth = 0.3f;

        [Tooltip("Tip travel in one frame above which the ribbon is cut rather than drawn. A " +
                 "retry teleports both actors, and without this the trail draws a bright line " +
                 "across the whole arena from where she died to where she respawned.")]
        [SerializeField] float teleportThreshold = 1.5f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        MaterialPropertyBlock mpb;
        float baseWidth;
        bool emitting;
        bool hadTipLastFrame;
        Vector3 lastTip;

        void Awake()
        {
            if (actor == null) actor = GetComponent<CombatActor>();

            if (actor == null || trail == null)
            {
                if (trail != null) trail.emitting = false;
                enabled = false;
                return;
            }

            mpb = new MaterialPropertyBlock();
            baseWidth = trail.widthMultiplier;

            trail.emitting = false;
            trail.Clear();
        }

        /// <summary>
        /// LateUpdate, not Update: the blade's transform is driven by the Animator, so reading
        /// the tip position any earlier samples last frame's pose.
        /// </summary>
        void LateUpdate()
        {
            CutOnTeleport();

            MoveDefinition move = actor.CurrentMove;

            if (move == null || !move.HasHitbox || actor.IsDead)
            {
                SetEmitting(false);
                return;
            }

            float t = actor.MoveProgress;
            float open = move.hitboxOpen;
            float close = move.hitboxClose;

            bool inSwing = t >= open - lead && t <= close + lag;
            bool inWindUp = telegraph && t >= telegraphStart && t < open - lead;

            SetEmitting(inSwing || inWindUp);
            if (!inSwing && !inWindUp) return;

            if (inWindUp)
            {
                // Squared so the last third of the wind-up carries most of the visible change -
                // the ramp should read as accelerating into the swing, not as a steady fade.
                float k = Mathf.InverseLerp(telegraphStart, Mathf.Max(telegraphStart + 0.01f, open), t);
                Apply(Color.Lerp(telegraphCold, telegraphHot, k * k),
                      Mathf.Lerp(telegraphWidth, 1f, k * k));
            }
            else
            {
                Apply(swingColor, 1f);
            }
        }

        void SetEmitting(bool on)
        {
            if (on == emitting) return;
            emitting = on;

            // Starting a new ribbon: drop whatever the last swing left, or the two arcs join up
            // across the gap between them.
            if (on) trail.Clear();
            trail.emitting = on;
        }

        void Apply(Color c, float widthScale)
        {
            trail.widthMultiplier = baseWidth * widthScale;

            // A property block rather than trail.material: touching .material would instance the
            // shared material once per character and leak one per retry.
            trail.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            trail.SetPropertyBlock(mpb);
        }

        void CutOnTeleport()
        {
            Vector3 tip = trail.transform.position;

            if (hadTipLastFrame && (tip - lastTip).sqrMagnitude > teleportThreshold * teleportThreshold)
                trail.Clear();

            lastTip = tip;
            hadTipLastFrame = true;
        }
    }
}
