using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DS2
{
    /// <summary>
    /// Implemented by whatever is currently driving the boss, so the HUD can show what she is
    /// thinking without knowing which brain is attached. Optional - the HUD works without it.
    /// </summary>
    public interface IBossDebugInfo
    {
        /// <summary>What she is doing right now, e.g. "Pressure 2/3", "GAP", "POSITIONING".</summary>
        string DebugState { get; }

        /// <summary>Seconds of pressure left before she owes the player an opening. -1 if n/a.</summary>
        float DebugBudget { get; }

        /// <summary>Last reactive decision, e.g. "read swing -> dodge".</summary>
        string DebugLastReaction { get; }
    }

    /// <summary>
    /// The tuning instrument. Boss feel was being judged by vibe, which is why iterating on the
    /// numbers stopped working - "she feels passive" is not something you can tune against.
    ///
    /// THREAT DENSITY is the number that matters: the fraction of the last N seconds during which
    /// a boss hitbox was actually open. Everything else she does - walking, repositioning, playing
    /// out a sheathe animation - is dead air from the player's point of view. Shipped melee games
    /// threaten roughly every 2-3 seconds; measure against that, not against how busy she looks.
    ///
    /// Attach to the Boss. Toggle with the key below at runtime.
    /// </summary>
    public class BossDebugHUD : MonoBehaviour
    {
        [Header("Actors")]
        [Tooltip("Leave empty to use the CombatActor on this GameObject.")]
        [SerializeField] CombatActor boss;

        [Tooltip("Leave empty to find the PlayerCombat in the scene.")]
        [SerializeField] CombatActor player;

        [Header("Display")]
        [SerializeField] bool visible = true;

        [Tooltip("activeInputHandler is 1 - new Input System only - so the old Input class is " +
                 "not available here. This reads Keyboard.current directly.")]
        [SerializeField] Key toggleKey = Key.F1;

        [Tooltip("Rolling window for the threat-density average, in seconds.")]
        [SerializeField] float window = 10f;

        [Header("Targets (drawn pass/fail)")]
        [Tooltip("Fraction of the window with a boss hitbox open. 0.20-0.25 was borrowed from " +
                 "generic melee-pacing advice and is NOT reachable here: the Pressure phrase at " +
                 "maximum compression - every link in the cancel window, no gap, no stun, no " +
                 "travel - is only 26%, because Slash 1 spends 30% of itself winding up and that " +
                 "wind-up is the telegraph. A mandatory opening and posture stuns come off the " +
                 "top of that. Measured healthy play sits at 16%; the old AI sat at 14%. " +
                 "Which is the real caveat: density alone barely separates good from bad here, " +
                 "because time she spends stunned by your posture pressure counts against her " +
                 "even though that is the game working. Watch WORST QUIET instead - it went " +
                 "1.2 s to 0.34 s inside a phrase across the rewrite, and it is what actually " +
                 "changed how she feels. Use this row to catch a regression, not as a goal.")]
        [SerializeField] float threatDensityTarget = 0.15f;

        [Tooltip("Longest acceptable stretch with nothing threatening the player, in seconds.")]
        [SerializeField] float maxQuietTarget = 3f;

        struct Sample { public float time, open; }

        readonly Queue<Sample> samples = new();
        float openInWindow;

        PostureSystem posture;
        IBossDebugInfo info;

        float lastThreatAt = float.NegativeInfinity;
        float worstQuiet;

        // Swing accounting. The reactive dodge was rolling its chance per frame rather than per
        // swing, so it fired on almost every swing instead of the 35% the inspector claimed.
        // Counting both sides makes the fix verifiable rather than assumed.
        int playerSwings;
        int dodgedSwings;
        bool swingOpen;
        MoveDefinition lastPlayerMove;
        MoveDefinition lastBossMove;

        GUIStyle style;

        void Awake()
        {
            if (boss == null) boss = GetComponent<CombatActor>();
            if (player == null)
            {
                PlayerCombat pc = FindAnyObjectByType<PlayerCombat>();
                if (pc != null) player = pc.GetComponent<CombatActor>();
            }
            if (boss != null)
            {
                posture = boss.GetComponent<PostureSystem>();
                info = boss.GetComponent<IBossDebugInfo>();
            }
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb[toggleKey].wasPressedThisFrame) visible = !visible;
            if (boss == null) return;

            Sample s;
            s.time = Time.time;
            s.open = boss.IsHitboxOpen ? Time.deltaTime : 0f;

            samples.Enqueue(s);
            openInWindow += s.open;

            while (samples.Count > 0 && samples.Peek().time < Time.time - window)
                openInWindow -= samples.Dequeue().open;

            if (boss.IsHitboxOpen)
            {
                if (!float.IsNegativeInfinity(lastThreatAt) && Time.time - lastThreatAt > 0.05f)
                    worstQuiet = Mathf.Max(worstQuiet, Time.time - lastThreatAt);
                lastThreatAt = Time.time;
            }

            TrackSwings();
        }

        /// <summary>
        /// Counts player swings and how many the boss answered with an evasive move. Done by
        /// observation rather than by hooking the brain, so the same numbers can be read against
        /// the old AI and the new one.
        /// </summary>
        void TrackSwings()
        {
            if (player == null) return;

            MoveDefinition pm = player.CurrentMove;
            if (pm != lastPlayerMove)
            {
                lastPlayerMove = pm;
                if (pm != null && pm.HasHitbox)
                {
                    playerSwings++;
                    swingOpen = true;
                }
                else if (pm == null)
                {
                    swingOpen = false;
                }
            }

            MoveDefinition bm = boss.CurrentMove;
            if (bm != lastBossMove)
            {
                lastBossMove = bm;
                bool evasive = bm != null && bm.damage <= 0 && bm.HasIFrames;
                if (evasive && swingOpen)
                {
                    dodgedSwings++;
                    swingOpen = false;
                }
            }
        }

        void OnGUI()
        {
            if (!visible || boss == null) return;

            style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
            };

            float span = samples.Count > 0 ? Mathf.Max(0.0001f, Time.time - samples.Peek().time) : 0f;
            float density = span > 0.5f ? openInWindow / span : 0f;

            float quiet = float.IsNegativeInfinity(lastThreatAt) ? 0f : Time.time - lastThreatAt;

            float distance = player != null
                ? Vector3.Distance(boss.transform.position, player.transform.position)
                : 0f;

            var sb = new System.Text.StringBuilder(512);
            sb.AppendLine("<b>BOSS</b>    " + toggleKey + " to hide");
            sb.AppendLine();
            sb.AppendLine(Row("threat dens", density.ToString("P0"), density >= threatDensityTarget));
            sb.AppendLine(Row("quiet now", quiet.ToString("0.0") + "s", quiet <= maxQuietTarget));
            sb.AppendLine(Row("worst quiet", worstQuiet.ToString("0.0") + "s", worstQuiet <= maxQuietTarget));
            sb.AppendLine();
            sb.AppendLine("distance    " + distance.ToString("0.00") + " m");
            sb.AppendLine("move        " + (boss.CurrentMove != null
                ? boss.CurrentMove.moveId + " " + boss.MoveProgress.ToString("0.00")
                : "-"));
            sb.AppendLine("health      " + boss.Health);
            if (posture != null)
                sb.AppendLine("posture     " + posture.Normalized.ToString("P0") +
                              (posture.IsStunned ? "  STUNNED" : ""));

            if (info != null)
            {
                sb.AppendLine();
                sb.AppendLine("state       " + info.DebugState);
                if (info.DebugBudget >= 0f)
                    sb.AppendLine("budget      " + info.DebugBudget.ToString("0.0") + "s");
                sb.AppendLine("reaction    " + info.DebugLastReaction);
            }

            sb.AppendLine();
            float rate = playerSwings > 0 ? (float)dodgedSwings / playerSwings : 0f;
            sb.AppendLine("swings      " + playerSwings);
            sb.AppendLine("she evaded  " + dodgedSwings + "  " + rate.ToString("P0"));

            GUI.Box(new Rect(10f, 10f, 250f, 320f), GUIContent.none);
            GUI.Label(new Rect(22f, 18f, 236f, 306f), sb.ToString(), style);
        }

        static string Row(string label, string value, bool pass)
        {
            string colour = pass ? "#7FD67F" : "#E08A6A";
            return label.PadRight(12) + "<color=" + colour + ">" + value + "</color>";
        }
    }
}
