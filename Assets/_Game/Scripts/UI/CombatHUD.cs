using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DS2
{
    /// <summary>
    /// The player-facing HUD: her health, the boss's health, THE BOSS'S POSTURE, and what has
    /// been learned so far.
    ///
    /// The posture meter is the one that earns its place. "Aggression is the correct defense" is
    /// the whole thesis of the fight, and until now the only channel carrying it was the rising
    /// impact pitch from the juice pass - a player could pressure her for four seconds, break
    /// her, and never learn that the two were connected. A meter that visibly climbs while you
    /// press and visibly drains while you back off teaches the economy in one exchange.
    ///
    /// IMGUI, not a uGUI Canvas, and deliberately so: no prefabs, no sprites, no CanvasScaler to
    /// get wrong between the editor and the build, and it scales off one matrix. The death card
    /// and the boss debug HUD are already OnGUI, so this is also the same thing three times
    /// rather than two systems that both draw.
    ///
    /// EVERYTHING HERE ANIMATES ON UNSCALED TIME. Hit stop sets Time.timeScale to 0, which makes
    /// Time.deltaTime 0 - a bar lerping on scaled time freezes mid-slide every time you connect,
    /// which reads as the HUD hitching rather than as the hit landing.
    /// </summary>
    public class CombatHUD : MonoBehaviour
    {
        [Header("Actors (found automatically if empty)")]
        [SerializeField] CombatActor player;
        [SerializeField] CombatActor boss;
        [SerializeField] string bossName = "THE MIRROR";

        [Header("Display")]
        [SerializeField] bool visible = true;

        [Tooltip("F1 is the boss debug HUD. This is the player-facing one.")]
        [SerializeField] Key toggleKey = Key.F2;

        [Tooltip("Layout is authored against this height in virtual pixels and scaled to fit, " +
                 "so the build and the editor Game view agree at any resolution.")]
        [SerializeField] float referenceHeight = 1080f;

        [Header("Bar feel")]
        [Tooltip("Units per second the trailing 'chip' bar catches up. Slow enough to read the " +
                 "size of a hit after it has landed, fast enough to be gone before the next one.")]
        [SerializeField] float chipCatchUpPerSecond = 55f;

        [Tooltip("Pause before the chip bar starts draining, so a three-hit chain reads as one " +
                 "bite out of her rather than three separate ones.")]
        [SerializeField] float chipDelay = 0.45f;

        [Header("Damage vignette")]
        [Tooltip("Red at the screen edge when she is hit. The mirror-match rule says feedback " +
                 "belongs to the victim - this is the player's half of it, and it is the piece " +
                 "the juice pass had to leave out for want of a Canvas.")]
        [SerializeField] bool vignette = true;
        [SerializeField] float vignetteStrength = 0.62f;
        [SerializeField] float vignetteFadePerSecond = 1.9f;

        [Header("Death")]
        [Tooltip("Hold the HUD up briefly after the killing blow - a health bar arriving at zero " +
                 "is the payoff - then clear the screen before the death card lands on top.")]
        [SerializeField] float hideDelayOnDeath = 0.85f;
        [SerializeField] float hideFadeTime = 0.35f;

        // --- palette -------------------------------------------------------------------------
        static readonly Color Backdrop = new(0.04f, 0.04f, 0.05f, 0.72f);
        static readonly Color Frame = new(0f, 0f, 0f, 0.85f);
        static readonly Color PlayerFill = new(0.76f, 0.26f, 0.23f);
        static readonly Color BossFill = new(0.62f, 0.17f, 0.18f);
        static readonly Color Chip = new(0.93f, 0.78f, 0.52f, 0.85f);
        static readonly Color PostureLow = new(0.85f, 0.64f, 0.25f);
        static readonly Color PostureHigh = new(1f, 0.95f, 0.78f);
        static readonly Color Ink = new(0.93f, 0.92f, 0.90f);
        static readonly Color InkDim = new(0.62f, 0.60f, 0.58f);

        PostureSystem bossPosture;

        float playerShown, playerChip, playerChipHoldUntil;
        float bossShown, bossChip, bossChipHoldUntil;

        float vignetteAlpha;
        float deadAt = -1f;
        float fade = 1f;

        MoveId justLearned = MoveId.None;
        float justLearnedAt = -99f;

        Texture2D white, vignetteTex;
        GUIStyle label, labelRight, title, small;

        static CombatHUD active;

        // -------------------------------------------------------------------------------------

        void Awake()
        {
            // Two of these draw every bar twice at slightly different alphas, which looks like a
            // rendering fault rather than a duplicate. Resolve by killing the COMPONENT - never
            // the GameObject, which may own something that matters. See Docs/project-notes.md.
            if (active != null && active != this)
            {
                Debug.LogWarning("[CombatHUD] A second CombatHUD on '" + name +
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
                Debug.LogError("[CombatHUD] Player or boss not found - HUD disabled.", this);
                enabled = false;
                return;
            }

            bossPosture = boss.GetComponent<PostureSystem>();
            if (bossPosture == null)
                Debug.LogWarning("[CombatHUD] No PostureSystem on the boss - no posture meter.", this);

            playerShown = playerChip = player.MaxHealth;
            bossShown = bossChip = boss.MaxHealth;

            player.Damaged += OnPlayerDamaged;
            player.Died += OnAnyDeath;
            boss.Died += OnAnyDeath;
        }

        void Start()
        {
            // Subscribed in Start, not Awake or OnEnable: Awake order between two scene objects
            // is undefined and this one is not the singleton, so ProgressionManager.Instance is
            // only reliably non-null by the time Start runs. Once, for the object's whole life -
            // an OnEnable subscription would stack a second handler on every re-enable.
            if (ProgressionManager.Instance != null)
                ProgressionManager.Instance.Granted += OnGranted;
        }

        void OnDestroy()
        {
            if (active == this) active = null;
            if (player != null) { player.Damaged -= OnPlayerDamaged; player.Died -= OnAnyDeath; }
            if (boss != null) boss.Died -= OnAnyDeath;
            if (ProgressionManager.Instance != null)
                ProgressionManager.Instance.Granted -= OnGranted;

            // HideAndDontSave means nothing else will collect these. Destroy is the runtime
            // call; DestroyImmediate is the only one that works when a domain reload or an
            // edit-mode teardown is what got us here.
            DestroyTexture(white);
            DestroyTexture(vignetteTex);
            white = vignetteTex = null;
        }

        static void DestroyTexture(Texture2D t)
        {
            if (t == null) return;
            if (Application.isPlaying) Destroy(t);
            else DestroyImmediate(t);
        }

        void OnPlayerDamaged(CombatActor _, MoveDefinition __) => vignetteAlpha = vignetteStrength;

        void OnAnyDeath(CombatActor _) => deadAt = Time.unscaledTime;

        void OnGranted(MoveId id)
        {
            justLearned = id;
            justLearnedAt = Time.unscaledTime;
        }

        // -------------------------------------------------------------------------------------

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb[toggleKey].wasPressedThisFrame) visible = !visible;

            float dt = Time.unscaledDeltaTime;

            Track(player.Health, ref playerShown, ref playerChip, ref playerChipHoldUntil, dt);
            Track(boss.Health, ref bossShown, ref bossChip, ref bossChipHoldUntil, dt);

            if (vignetteAlpha > 0f)
                vignetteAlpha = Mathf.Max(0f, vignetteAlpha - vignetteFadePerSecond * dt);

            // A retry restores both actors to full without firing anything, so recovery is
            // detected rather than announced: health going UP means a new attempt, not healing.
            if (deadAt > 0f && !player.IsDead && !boss.IsDead)
            {
                deadAt = -1f;
                fade = 1f;
                vignetteAlpha = 0f;
            }

            if (deadAt > 0f)
            {
                float since = Time.unscaledTime - deadAt - hideDelayOnDeath;
                fade = since <= 0f ? 1f : Mathf.Clamp01(1f - since / Mathf.Max(0.01f, hideFadeTime));
            }
        }

        /// <summary>
        /// Drives the solid bar and the trailing chip bar toward current health. The solid bar
        /// snaps down on a hit (you should feel the damage immediately) and the chip bar follows
        /// after a beat, which is what makes the size of the hit legible at all.
        /// </summary>
        void Track(int current, ref float shown, ref float chip, ref float holdUntil, float dt)
        {
            if (current > shown)
            {
                // Upward is only ever a retry reset. Snap; do not animate a refill.
                shown = chip = current;
                return;
            }

            if (current < shown)
            {
                shown = current;
                holdUntil = Time.unscaledTime + chipDelay;
            }

            if (chip > shown && Time.unscaledTime >= holdUntil)
                chip = Mathf.Max(shown, chip - chipCatchUpPerSecond * dt);
        }

        // -------------------------------------------------------------------------------------

        void OnGUI()
        {
            if (!visible || fade <= 0.001f) return;

            EnsureTextures();
            EnsureStyles();

            Matrix4x4 prevMatrix = GUI.matrix;
            Color prevColor = GUI.color;

            float scale = Screen.height / referenceHeight;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float vw = Screen.width / scale;
            float vh = referenceHeight;

            // The vignette sits under everything, and it is the one element that should still
            // show while the rest of the HUD is fading out after the killing blow.
            if (vignette && vignetteAlpha > 0.002f)
            {
                GUI.color = new Color(0.55f, 0.03f, 0.04f, vignetteAlpha);
                GUI.DrawTexture(new Rect(0f, 0f, vw, vh), vignetteTex, ScaleMode.StretchToFill, true);
                GUI.color = prevColor;
            }

            DrawPlayer(vw, vh);
            DrawBoss(vw, vh);
            DrawLearned(vw, vh);

            GUI.color = prevColor;
            GUI.matrix = prevMatrix;
        }

        void DrawPlayer(float vw, float vh)
        {
            const float w = 430f, h = 24f;
            float x = 42f, y = 42f;

            var bar = new Rect(x, y, w, h);
            DrawBar(bar, playerShown / player.MaxHealth, playerChip / player.MaxHealth, PlayerFill);

            GUI.color = WithFade(InkDim);
            GUI.Label(new Rect(x, y + h + 3f, w, 20f), "KATANA GIRL", small);
            GUI.color = WithFade(Ink);
            GUI.Label(new Rect(x, y + h + 3f, w, 20f),
                      Mathf.CeilToInt(playerShown) + " / " + player.MaxHealth, labelRight);
        }

        void DrawBoss(float vw, float vh)
        {
            float w = Mathf.Min(880f, vw - 240f);
            const float healthH = 20f, postureH = 14f, gap = 7f;

            float x = (vw - w) * 0.5f;
            float y = vh - 128f;

            GUI.color = WithFade(Ink);
            GUI.Label(new Rect(x, y - 30f, w, 26f), bossName, title);

            var health = new Rect(x, y, w, healthH);
            DrawBar(health, bossShown / boss.MaxHealth, bossChip / boss.MaxHealth, BossFill);

            if (bossPosture == null) return;

            float p = Mathf.Clamp01(bossPosture.Normalized);
            var posture = new Rect(x, y + healthH + gap, w, postureH);

            // Amber at rest, running white-hot as it fills. The colour shift is doing the
            // teaching here: by the time it is pale you should already expect the break.
            Color fill = Color.Lerp(PostureLow, PostureHigh, p * p);

            bool postureBroken = bossPosture.IsStunned;
            bool staggered = boss.IsStunned && !postureBroken;

            if (postureBroken)
            {
                // Full and pulsing, so the opening is unmistakable at a glance mid-fight.
                float pulse = 0.72f + 0.28f * Mathf.Sin(Time.unscaledTime * 11f);
                DrawBar(posture, 1f, 1f, Color.Lerp(PostureHigh, Color.white, pulse));
            }
            else
            {
                DrawBar(posture, p, p, fill);
            }

            string caption = postureBroken ? "POSTURE BROKEN"
                           : staggered ? "STAGGERED"
                           : "POSTURE";

            GUI.color = WithFade(postureBroken || staggered ? PostureHigh : InkDim);
            GUI.Label(new Rect(x, posture.yMax + 2f, w, 20f), caption, small);
        }

        void DrawLearned(float vw, float vh)
        {
            ProgressionManager pm = ProgressionManager.Instance;
            if (pm == null) return;

            const float w = 236f, row = 21f;
            float x = vw - w - 42f;
            float y = 42f;

            int rows = ProgressionManager.StartingMoves.Count + ProgressionManager.LadderOrder.Count;
            var panel = new Rect(x - 12f, y - 10f, w + 24f, rows * row + 52f);

            GUI.color = WithFade(Backdrop);
            GUI.DrawTexture(panel, white);
            GUI.color = WithFade(Ink);
            GUI.Label(new Rect(x, y, w, 22f),
                      "LEARNED   " + pm.UnlockedCount + " / " + pm.TotalCount, small);

            y += 28f;
            DrawLearnedRows(ProgressionManager.StartingMoves, pm, x, ref y, w, row);
            DrawLearnedRows(ProgressionManager.LadderOrder, pm, x, ref y, w, row);
        }

        void DrawLearnedRows(IReadOnlyList<MoveId> ids, ProgressionManager pm,
                             float x, ref float y, float w, float row)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                MoveId id = ids[i];
                bool have = pm.IsUnlocked(id);

                // Locked rungs stay as rules rather than names: the count of what is left is
                // information the player should have, the identity of it is the reward.
                string text = have ? MoveDefinition.DisplayName(id) : "---------";

                Color c = InkDim;
                if (have) c = Ink;

                // The move from the last death card, briefly lit so the panel and the card agree.
                if (have && id == justLearned && Time.unscaledTime - justLearnedAt < 6f)
                {
                    float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                    c = Color.Lerp(Ink, PostureHigh, t);
                }

                GUI.color = WithFade(c);
                GUI.Label(new Rect(x, y, w, row), text, label);
                y += row;
            }
        }

        // -------------------------------------------------------------------------------------

        /// <summary>Backdrop, trailing chip, solid fill, 1px frame. One bar, four draws.</summary>
        void DrawBar(Rect r, float fill01, float chip01, Color fillColor)
        {
            fill01 = Mathf.Clamp01(fill01);
            chip01 = Mathf.Clamp01(chip01);

            GUI.color = WithFade(Frame);
            GUI.DrawTexture(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), white);

            GUI.color = WithFade(Backdrop);
            GUI.DrawTexture(r, white);

            if (chip01 > fill01)
            {
                GUI.color = WithFade(Chip);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * chip01, r.height), white);
            }

            GUI.color = WithFade(fillColor);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * fill01, r.height), white);
        }

        Color WithFade(Color c) => new(c.r, c.g, c.b, c.a * fade);

        void EnsureTextures()
        {
            if (white == null)
            {
                white = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }

            if (vignetteTex != null) return;

            // 64px is plenty - it is stretched over the whole screen and sampled bilinearly, and
            // a vignette has no detail worth resolving. Clamp, or the edges wrap and seam.
            const int n = 64;
            vignetteTex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var px = new Color[n * n];
            for (int yy = 0; yy < n; yy++)
            {
                for (int xx = 0; xx < n; xx++)
                {
                    float u = (xx + 0.5f) / n * 2f - 1f;
                    float v = (yy + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v) / 1.41421356f;
                    float a = Mathf.Clamp01((d - 0.32f) / 0.68f);
                    px[yy * n + xx] = new Color(1f, 1f, 1f, a * a);
                }
            }

            vignetteTex.SetPixels(px);
            vignetteTex.Apply();
        }

        void EnsureStyles()
        {
            if (label != null) return;

            label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            labelRight = new GUIStyle(label) { alignment = TextAnchor.UpperRight };
            small = new GUIStyle(label) { fontSize = 13 };
            title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                richText = true,
            };
        }
    }
}
