using UnityEngine;
using UnityEngine.InputSystem;

namespace DS2
{
    /// <summary>
    /// Escape stops the world. IMGUI, like DeathScreen and CombatHUD - a fourth instance of one
    /// pattern rather than the project's first Canvas on shipping day.
    ///
    /// IT RE-ASSERTS timeScale EVERY FRAME, and that is not paranoia. HitFeedback writes
    /// Time.timeScale in five places: hit stop sets it to 0, slow motion to a fraction, and timers
    /// running on unscaledDeltaTime put it back to 1 when they expire. Those timers keep running
    /// while paused - unscaled time is exactly the time that does not stop - so a freeze that
    /// began a moment before Escape would quietly un-pause the game a tenth of a second later.
    /// Setting it once on open is not enough; it has to be held.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        [SerializeField] DeathScreen deathScreen;

        [Tooltip("Scale restored on resume. 1 unless the whole game is being run slowed.")]
        [SerializeField] float resumeTimeScale = 1f;

        [Tooltip("Hide and lock the cursor during play. Off if you need the mouse free to debug.")]
        [SerializeField] bool lockCursor = true;

        public bool IsPaused { get; private set; }

        GUIStyle title, item;

        void Awake()
        {
            // Destroy the COMPONENT, never the GameObject - the arena-deleting lesson.
            if (FindObjectsByType<PauseMenu>(FindObjectsInactive.Include).Length > 1
                && this != FindAnyObjectByType<PauseMenu>())
            {
                Destroy(this);
                return;
            }

            if (deathScreen == null) deathScreen = FindAnyObjectByType<DeathScreen>();
        }

        void Start() => ApplyCursor();

        void OnDisable()
        {
            // Never leave the game frozen, or the cursor trapped, because this object went away.
            if (IsPaused) Time.timeScale = resumeTimeScale;
            IsPaused = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// Alt-tabbing away releases the cursor; coming back has to take it again, or the player
        /// returns to a game with a visible pointer floating over it.
        /// </summary>
        void OnApplicationFocus(bool focused)
        {
            if (focused) ApplyCursor();
        }

        /// <summary>
        /// Locked and hidden during play, free and visible while paused - the pause menu has
        /// buttons and they need something to click with.
        ///
        /// Locked rather than merely hidden: Confined still lets the pointer drift to a screen
        /// edge and stall the camera, and CinemachineInputAxisController reads mouse DELTA, which
        /// keeps working perfectly while the cursor is pinned. Attack and Parry being mouse
        /// buttons is likewise unaffected - only the pointer position is taken away.
        /// </summary>
        void ApplyCursor()
        {
            if (!lockCursor) return;

            Cursor.lockState = IsPaused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = IsPaused;
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            bool escape = kb != null && kb.escapeKey.wasPressedThisFrame;

            // The death card retries on ANY key. Letting Escape through would pause and restart
            // on the same press, and the pause would then be sitting behind a fresh attempt.
            bool cardUp = deathScreen != null && deathScreen.IsShowing;

            if (escape && !cardUp) Toggle();

            if (IsPaused) Time.timeScale = 0f;
        }

        public void Toggle()
        {
            IsPaused = !IsPaused;
            Time.timeScale = IsPaused ? 0f : resumeTimeScale;
            ApplyCursor();
        }

        void OnGUI()
        {
            if (!IsPaused) return;

            title ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 34, alignment = TextAnchor.MiddleCenter, richText = true,
            };
            item ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, alignment = TextAnchor.MiddleCenter, richText = true,
            };

            // Dim the whole screen so the frozen frame reads as stopped rather than hitched.
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);
            GUI.color = prev;

            float w = 420f, h = 230f;
            var box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            GUI.Label(new Rect(box.x, box.y + 20f, w, 46f), "<b>PAUSED</b>", title);

            if (GUI.Button(new Rect(box.x + 110f, box.y + 88f, 200f, 34f), "Resume"))
                Toggle();

            if (GUI.Button(new Rect(box.x + 110f, box.y + 132f, 200f, 34f), "Quit"))
                Quit();

            GUI.Label(new Rect(box.x, box.y + 180f, w, 24f), "<i>Esc to resume</i>", item);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            // Application.Quit does nothing in the editor, which makes the button look broken.
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
