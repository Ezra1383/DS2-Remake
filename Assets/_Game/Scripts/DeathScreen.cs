using UnityEngine;
using UnityEngine.InputSystem;

namespace DS2
{
    /// <summary>
    /// Closes the loop: die, learn the move that killed you, go again.
    ///
    /// The budget is TWO SECONDS from killing blow to next attempt. Death is the progression
    /// mechanic here, so death has to be cheap - no menu, no load, no walk back to a fog gate.
    /// Everything resets in place, which also means every reference and event subscription
    /// survives the retry.
    ///
    /// The presentation is deliberately a placeholder. This is an OnGUI card so the loop can be
    /// played and tuned now; the real unlock card belongs with the Week 4 HUD, and it only needs
    /// to listen to ProgressionManager.Granted to replace this wholesale.
    /// </summary>
    public class DeathScreen : MonoBehaviour
    {
        [Header("Actors")]
        [SerializeField] CombatActor player;
        [SerializeField] CombatActor boss;

        [Header("Timing")]
        [Tooltip("Seconds after death before the card appears. Raised from 1.2 to 3.0 on 20 Sep: " +
                 "the Die clip runs 2.833 s and the killing blow's slow motion stretches it to " +
                 "nearly 3.9 s of real time, so at 1.2 the card was dropping over her while she " +
                 "was still falling.")]
        [SerializeField] float cardDelay = 3f;

        [Tooltip("Ignore input for this long AFTER THE DEATH, not after the card. Stops the button " +
                 "press that killed you from also skipping the death, while still letting an " +
                 "impatient player retry before the card has finished appearing.")]
        [SerializeField] float inputLockout = 0.8f;

        [Header("Debug")]
        [SerializeField] bool logRetries;

        Vector3 playerSpawn, bossSpawn;
        Quaternion playerFacing, bossFacing;

        float cardShownAt = -1f;
        float diedAt = -1f;
        bool waiting;

        /// <summary>
        /// True while the death or victory card is up and swallowing input. PauseMenu reads this
        /// so Escape cannot open a pause menu behind a card that already retries on ANY key -
        /// which would otherwise both pause and restart on the same press.
        /// </summary>
        public bool IsShowing => waiting;
        MoveId learned = MoveId.None;
        MoveDefinition killer;
        bool ladderComplete;
        bool bossWon;

        int attempts = 1;
        GUIStyle big, small;

        static DeathScreen active;

        void Awake()
        {
            // Two of these both subscribe to Died, both draw a card over each other, and both
            // call Retry - which resets the fight twice and double-counts the attempt.
            if (active != null && active != this)
            {
                Debug.LogWarning("[DeathScreen] A second DeathScreen on '" + name +
                                 "' was removed. Only one should exist.", this);
                Destroy(this);
                enabled = false;
                return;
            }
            active = this;

            if (player == null)
            {
                PlayerCombat pc = FindAnyObjectByType<PlayerCombat>();
                if (pc != null) player = pc.GetComponent<CombatActor>();
            }
            if (boss == null)
            {
                BossBrain bb = FindAnyObjectByType<BossBrain>();
                if (bb != null) boss = bb.GetComponent<CombatActor>();
            }

            if (player == null || boss == null)
            {
                Debug.LogError("[DeathScreen] Player or boss not found.", this);
                enabled = false;
                return;
            }

            playerSpawn = player.transform.position;
            playerFacing = player.transform.rotation;
            bossSpawn = boss.transform.position;
            bossFacing = boss.transform.rotation;

            player.Died += OnPlayerDied;
            boss.Died += OnBossDied;
        }

        void OnDestroy()
        {
            if (active == this) active = null;
            if (player != null) player.Died -= OnPlayerDied;
            if (boss != null) boss.Died -= OnBossDied;
        }

        void OnPlayerDied(CombatActor _)
        {
            bossWon = true;

            // Captured before the grant, because the grant is what it resolves FROM.
            killer = player.LastDamageSource;

            if (ProgressionManager.Instance != null)
            {
                learned = ProgressionManager.Instance.GrantForDeath(killer);
                ladderComplete = learned == MoveId.None;
            }
            else
            {
                learned = MoveId.None;
                ladderComplete = false;
                Debug.LogWarning("[DeathScreen] No ProgressionManager - nothing was learned.", this);
            }

            BeginCard();
        }

        void OnBossDied(CombatActor _)
        {
            bossWon = false;
            killer = null;
            learned = MoveId.None;
            ladderComplete = false;
            BeginCard();
        }

        void BeginCard()
        {
            waiting = true;
            diedAt = Time.unscaledTime;
            cardShownAt = diedAt + cardDelay;
        }

        void Update()
        {
            // INPUT IS GATED ON THE DEATH, NOT ON THE CARD. Those were the same thing while the
            // card came up in 1.2 s; now that it waits out the full death animation, tying them
            // together would have forced everyone to sit through 3.8 s before they could retry,
            // and the whole loop is budgeted at about two seconds.
            //
            // So the animation plays uninterrupted for anyone watching it, and anyone who has
            // seen it nine times already can press a key and go.
            if (!waiting || Time.unscaledTime < diedAt + inputLockout) return;

            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;

            bool pressed = (kb != null && kb.anyKey.wasPressedThisFrame) ||
                           (pad != null && (pad.buttonSouth.wasPressedThisFrame ||
                                            pad.startButton.wasPressedThisFrame));

            if (pressed) Retry();
        }

        void Retry()
        {
            waiting = false;
            attempts++;

            // Whatever the killing blow's slow motion left behind, the next attempt starts at
            // full speed. Forgetting this is how a retry silently plays at 30%.
            Time.timeScale = 1f;

            player.ResetForRetry(playerSpawn, playerFacing);
            boss.ResetForRetry(bossSpawn, bossFacing);

            if (logRetries) Debug.Log($"[DeathScreen] attempt {attempts}", this);
        }

        void OnGUI()
        {
            if (!waiting || Time.unscaledTime < cardShownAt) return;

            big ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 34, alignment = TextAnchor.MiddleCenter, richText = true,
            };
            small ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, alignment = TextAnchor.MiddleCenter, richText = true,
            };

            float w = 560f, h = 220f;
            var box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.Box(box, GUIContent.none);
            GUI.color = prev;

            var line1 = new Rect(box.x, box.y + 26f, w, 46f);
            var line2 = new Rect(box.x, box.y + 84f, w, 34f);
            var line3 = new Rect(box.x, box.y + 128f, w, 28f);
            var line4 = new Rect(box.x, box.y + 168f, w, 28f);

            if (!bossWon)
            {
                GUI.Label(line1, "<b>THE MIRROR IS EVEN</b>", big);
                GUI.Label(line2, "You win.", small);
            }
            else if (learned != MoveId.None)
            {
                GUI.Label(line1, "<b>" + Pretty(learned) + "</b>", big);
                GUI.Label(line2, killer != null
                    ? "Her " + Pretty(killer.moveId) + " killed you. Now it is yours."
                    : "Learned.", small);

                if (ProgressionManager.Instance != null)
                    GUI.Label(line3, ProgressionManager.Instance.UnlockedCount + " of " +
                                     ProgressionManager.Instance.TotalCount + " learned", small);
            }
            else
            {
                GUI.Label(line1, "<b>NOTHING LEFT TO LEARN</b>", big);
                GUI.Label(line2, ladderComplete
                    ? "You have everything she has. The rest is you."
                    : "Died.", small);
            }

            GUI.Label(line4, "<i>any key to go again</i>   ·   attempt " + attempts, small);
        }

        /// <summary>Shared with the HUD so the card and the learned-moves list never disagree.</summary>
        static string Pretty(MoveId id) => MoveDefinition.DisplayName(id);
    }
}
