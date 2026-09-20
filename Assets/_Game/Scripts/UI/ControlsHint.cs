using UnityEngine;

namespace DS2
{
    /// <summary>
    /// The key binds, shown in the training ground and gone once the fight starts.
    ///
    /// TIED TO BossGate RATHER THAN A TIMER OR A KEY. The gate already knows the one moment that
    /// matters - the player crossing into the arena - so the panel is up for exactly as long as
    /// there is nothing to do but read it, and clears itself the instant it would be in the way.
    /// No prompt to dismiss, and nothing to forget to turn off.
    ///
    /// Bindings are duplicated from InputSystem_Actions by hand, so if a bind changes this has to
    /// change with it. That is a real cost and it is taken deliberately: reading the live bindings
    /// out of the asset means display strings for composites and gamepad paths, which is a lot of
    /// code for a panel that exists for the first thirty seconds of the game.
    /// </summary>
    public class ControlsHint : MonoBehaviour
    {
        [SerializeField] BossGate gate;

        [Tooltip("Also hide once the player has seen it for this long. 0 keeps it until the gate.")]
        [SerializeField] float autoHideAfter = 0f;

        [Range(0f, 1f)] [SerializeField] float backgroundAlpha = 0.55f;

        static readonly (string action, string bind)[] Bindings =
        {
            ("Move",           "WASD"),
            ("Sprint",         "Left Shift"),
            ("Attack",         "Left Mouse"),
            ("Dodge",          "Space"),
            ("Parry",          "F  /  Right Mouse"),
            ("Lock on",        "Q"),
            ("Stance",         "R"),
            ("Special (hold)", "Left Ctrl"),
        };

        float shownFor;
        GUIStyle head, label, value;

        void Awake()
        {
            if (gate == null) gate = FindAnyObjectByType<BossGate>();
        }

        bool Visible
        {
            get
            {
                if (gate != null && gate.IsAwake) return false;
                if (autoHideAfter > 0f && shownFor >= autoHideAfter) return false;
                return true;
            }
        }

        void Update()
        {
            if (Visible) shownFor += Time.unscaledDeltaTime;
        }

        void OnGUI()
        {
            if (!Visible) return;

            head ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, alignment = TextAnchor.MiddleLeft, richText = true,
            };
            label ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, alignment = TextAnchor.MiddleLeft, richText = true,
            };
            value ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, alignment = TextAnchor.MiddleRight, richText = true,
            };

            const float w = 260f, row = 19f, pad = 12f;
            float h = pad * 2f + 26f + Bindings.Length * row + 34f;

            // Bottom left: out of the way of the health bars along the top and of her, centre.
            var box = new Rect(24f, Screen.height - h - 24f, w, h);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, backgroundAlpha);
            GUI.Box(box, GUIContent.none);
            GUI.color = prev;

            float y = box.y + pad;
            GUI.Label(new Rect(box.x + pad, y, w - pad * 2f, 22f), "<b>CONTROLS</b>", head);
            y += 26f;

            foreach ((string action, string bind) in Bindings)
            {
                GUI.Label(new Rect(box.x + pad, y, w * 0.55f, row), action, label);
                GUI.Label(new Rect(box.x + w * 0.45f - pad, y, w * 0.55f, row), bind, value);
                y += row;
            }

            y += 6f;
            GUI.Label(new Rect(box.x + pad, y, w - pad * 2f, row),
                      "<i>Hold Special + Attack / Dodge / Stance</i>", label);
            y += row;
            GUI.Label(new Rect(box.x + pad, y, w - pad * 2f, row),
                      "<i>Esc to pause  ·  F1 boss debug</i>", label);
        }
    }
}
