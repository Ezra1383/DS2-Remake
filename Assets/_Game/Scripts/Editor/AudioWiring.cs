using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DS2.EditorTools
{
    /// <summary>
    /// Builds the whole combat audio set from nothing: the WAV files, their import settings, the
    /// banks on HitFeedback and the per-move swing clips. Re-runnable, like every other DS2 tool.
    ///
    /// THE CLIPS ARE SYNTHESISED, NOT SOURCED - the same decision VfxWiring made about particle
    /// textures, for the same reason and with more justification. The Combat Girls pack ships no
    /// audio at all, and every sound on the shopping list is a synthesis recipe rather than a
    /// recording: a whoosh is filtered noise on a sweep, an impact is a transient over a decaying
    /// body, a bell is inharmonic partials. None of them want realism. So there is no art
    /// dependency to source, no licence to document in a graded submission, and variations - which
    /// the build plan rightly says matter more than fidelity, because one clip on every slash
    /// fatigues inside a minute - cost nothing but another seed.
    ///
    /// TWO MENU ITEMS, ON PURPOSE. `Build Audio` writes only the clips that are missing and then
    /// wires everything; `Regenerate All` overwrites. That difference is the upgrade path: because
    /// a clip is referenced by its asset GUID, dropping a real recording on top of
    /// `SFX_PostureBreak.wav` keeps every reference that points at it, and the plain Build Audio
    /// will not overwrite your replacement. Swap the FILE, never the assignment.
    ///
    /// The mix lives in one place - the peak column of the bank below. Each clip is normalised to
    /// its own peak, so relative loudness is stated rather than emergent. ImpactAudio applies its
    /// own master volume and pitch jitter on top.
    /// </summary>
    static class AudioWiring
    {
        const string ArenaScene = "Assets/_Game/Scenes/Arena.unity";
        const string AudioFolder = "Assets/_Game/Audio";
        const string MovesFolder = "Assets/_Game/Moves";
        const string BossMovesFolder = "Assets/_Game/Moves/Boss";

        const int SampleRate = 44100;

        // ------------------------------------------------------------------ menu

        [MenuItem("Tools/DS2/Build Audio")]
        static void BuildAudio() => Run(regenerate: false);

        [MenuItem("Tools/DS2/Build Audio (Regenerate All)")]
        static void BuildAudioForce() => Run(regenerate: true);

        static void Run(bool regenerate)
        {
            EnsureFolder();

            var bank = new Dictionary<string, AudioClip>();
            int written = 0, kept = 0;

            foreach (Recipe r in Bank())
            {
                string path = AudioFolder + "/" + r.name + ".wav";

                if (!regenerate && File.Exists(path))
                {
                    kept++;
                    bank[r.name] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    continue;
                }

                WriteWav(path, Finish(r.make(), r.peak));
                written++;
                bank[r.name] = Import(path);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int moves = WireMoves(bank);
            WireFeedback(bank);

            Debug.Log($"[DS2] Audio: {written} clip(s) generated, {kept} kept, " +
                      $"{moves} move asset(s) wired.");
        }

        // ------------------------------------------------------------------ the bank

        class Recipe
        {
            public readonly string name;
            public readonly System.Func<float[]> make;
            public readonly float peak;

            public Recipe(string name, System.Func<float[]> make, float peak)
            {
                this.name = name;
                this.make = make;
                this.peak = peak;
            }
        }

        /// <summary>
        /// Glass and bell. Inharmonic and stretched - a struck bell has modes nothing like a
        /// harmonic series, and integer multiples would give a pitched organ note instead of metal.
        /// </summary>
        static readonly float[] GlassPartials = { 1f, 2.00f, 2.97f, 4.13f, 5.43f, 7.09f, 9.21f };

        /// <summary>Denser and brighter: a guard catching a blade rather than a bell ringing.</summary>
        static readonly float[] GuardPartials = { 1f, 1.73f, 2.41f, 3.19f, 4.62f, 6.05f };

        /// <summary>
        /// Ordered by the build plan priority list, so a run that is interrupted still leaves the
        /// sounds that matter most on disk.
        /// </summary>
        static List<Recipe> Bank() => new()
        {
            // Impacts first - by a wide margin the most frequent sound in the fight.
            new("SFX_Impact_1",        () => Impact(0.30f, 112f, 0.045f, 0.100f, 1101), 0.82f),
            new("SFX_Impact_2",        () => Impact(0.30f,  98f, 0.052f, 0.110f, 1102), 0.82f),
            new("SFX_Impact_3",        () => Impact(0.28f, 124f, 0.040f, 0.090f, 1103), 0.82f),

            // The design thesis made audible. Loudest thing in the game, used nowhere else.
            new("SFX_PostureBreak",    () => Metal(1.80f, 523f, GlassPartials, 1.05f, 0.55f, 1301), 0.95f),

            // "Worth more care than any other clip here" - ImpactAudio says so itself.
            new("SFX_Parry_1",         () => Metal(0.24f, 1480f, GuardPartials, 0.13f, 0.85f, 1401), 0.88f),
            new("SFX_Parry_2",         () => Metal(0.22f, 1660f, GuardPartials, 0.11f, 0.85f, 1402), 0.88f),

            // The outcome that had no voice, and cost three days of misdiagnosis for it.
            new("SFX_Evaded_1",        () => Whoosh(0.20f, 240f,  780f, 1.4f, 1501), 0.42f),
            new("SFX_Evaded_2",        () => Whoosh(0.22f, 210f,  700f, 1.5f, 1502), 0.42f),

            new("SFX_ImpactHeavy_1",   () => Impact(0.55f, 72f, 0.095f, 0.240f, 1201), 0.92f),
            new("SFX_ImpactHeavy_2",   () => Impact(0.60f, 64f, 0.110f, 0.280f, 1202), 0.92f),

            // Swings double as the Step 2.3 telegraph, so these are mechanics, not decoration.
            new("SFX_Swing_Light_1",   () => Whoosh(0.22f, 620f, 2700f, 1.3f, 1601), 0.55f),
            new("SFX_Swing_Light_2",   () => Whoosh(0.24f, 560f, 2450f, 1.3f, 1602), 0.55f),
            new("SFX_Swing_Light_3",   () => Whoosh(0.21f, 680f, 2900f, 1.2f, 1603), 0.55f),

            new("SFX_Swing_Heavy_1",   () => Whoosh(0.32f, 380f, 1650f, 1.1f, 1701), 0.62f),
            new("SFX_Swing_Heavy_2",   () => Whoosh(0.34f, 340f, 1500f, 1.1f, 1702), 0.62f),

            new("SFX_Swing_Special_1", () => Whoosh(0.40f, 260f, 1250f, 1.0f, 1801), 0.70f),
            new("SFX_Swing_Special_2", () => Whoosh(0.44f, 230f, 1120f, 1.0f, 1802), 0.70f),
            new("SFX_Swing_Special_3", () => Whoosh(0.46f, 210f, 1040f, 1.0f, 1803), 0.70f),

            // 0.45 -> 0.27 on 20 Sep, by ear. Broadband noise reads far louder than its peak
            // suggests next to a transient, and a dodge fires more often than any attack does.
            new("SFX_Dodge_1",         () => Whoosh(0.20f, 420f, 1500f, 1.6f, 1901), 0.27f),
            new("SFX_Dodge_2",         () => Whoosh(0.23f, 380f, 1350f, 1.6f, 1902), 0.27f),

            // The iai draw and sheathe. High Q is what makes filtered noise read as sliding metal.
            new("SFX_Sheath_1",        () => Whoosh(0.30f, 900f, 4200f, 2.4f, 2001), 0.50f),

            new("SFX_Death",           () => Death(1.30f, 2101), 0.90f),
            new("SFX_PostureRecover",  () => Recover(0.36f), 0.38f),
        };

        // ------------------------------------------------------------------ synthesis

        static int Samples(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));

        /// <summary>
        /// Deterministic noise. System.Random rather than UnityEngine.Random so that generating a
        /// bank never perturbs the gameplay RNG sequence, and so the same seed gives the same clip
        /// on every machine - a regenerated build should not sound different from the tested one.
        /// </summary>
        class Noise
        {
            readonly System.Random rng;
            public Noise(int seed) { rng = new System.Random(seed); }
            public float Next() => (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        /// <summary>
        /// Chamberlin state-variable filter. Cutoff is set per sample because every whoosh here is
        /// a sweep, and the sweep is the whole difference between "a blade moving past" and "hiss".
        ///
        /// The coefficient goes unstable as it approaches 1, i.e. as cutoff approaches sr/6, which
        /// is why the cutoff is clamped rather than trusted.
        /// </summary>
        class Svf
        {
            float low, band;

            public void Step(float input, float cutoffHz, float q,
                             out float lp, out float bp, out float hp)
            {
                float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(cutoffHz, 20f, 7000f) / SampleRate);
                float damp = Mathf.Clamp(1f / Mathf.Max(0.5f, q), 0.05f, 2f);

                hp = input - low - damp * band;
                band += f * hp;
                low += f * band;

                bp = band;
                lp = low;
            }
        }

        static float Decay(float t, float attack, float tau)
        {
            if (t <= 0f) return 0f;
            if (t < attack) return attack <= 0f ? 1f : t / attack;
            return Mathf.Exp(-(t - attack) / Mathf.Max(0.0005f, tau));
        }

        /// <summary>Rise and fall. A whoosh peaks in the middle, where the blade is closest.</summary>
        static float Arc(float u, float power) => Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(u)), power);

        static float[] Whoosh(float seconds, float fStart, float fPeak, float q, int seed)
        {
            int n = Samples(seconds);
            var buf = new float[n];
            var noise = new Noise(seed);
            var svf = new Svf();

            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;

                // Up on the approach and back down on the departure. A one-way sweep reads as a
                // synth riser rather than as something passing you.
                float cutoff = Mathf.Lerp(fStart, fPeak, Arc(u, 1f));

                svf.Step(noise.Next(), cutoff, q, out _, out float bp, out _);
                buf[i] = bp * Arc(u, 1.6f);
            }

            return buf;
        }

        static float[] Impact(float seconds, float thumpHz, float sliceTau, float thumpTau, int seed)
        {
            int n = Samples(seconds);
            var buf = new float[n];
            var noise = new Noise(seed);
            var svf = new Svf();
            float phase = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float s = noise.Next();

                // Three layers doing three different jobs: the click says "now", the slice says
                // "a blade", the body says "and it had weight". Drop any one and it stops landing.
                float click = s * Decay(t, 0.0002f, 0.0015f) * 0.9f;

                svf.Step(s, 2600f, 0.9f, out _, out _, out float hp);
                float slice = hp * Decay(t, 0.001f, sliceTau) * 0.7f;

                // The body falls in pitch as it decays, which is what a struck object does.
                float f = thumpHz * Mathf.Lerp(1f, 0.72f, Mathf.Clamp01(t / Mathf.Max(0.001f, thumpTau)));
                phase += 2f * Mathf.PI * f / SampleRate;
                float body = Mathf.Sin(phase) * Decay(t, 0.0008f, thumpTau);

                buf[i] = click + slice + body;
            }

            return buf;
        }

        static float[] Metal(float seconds, float baseHz, float[] ratios, float tau, float strike, int seed)
        {
            int n = Samples(seconds);
            var buf = new float[n];
            var noise = new Noise(seed);
            var svf = new Svf();

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float sum = 0f;

                for (int p = 0; p < ratios.Length; p++)
                {
                    float f = baseHz * ratios[p];
                    if (f > SampleRate * 0.45f) continue;

                    // Higher partials shed energy faster. That is why a bell is bright at the
                    // strike and mellow a second later, and why a flat decay sounds synthetic.
                    float pTau = tau / Mathf.Pow(ratios[p], 0.75f);
                    sum += Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t / pTau) / (1f + p);
                }

                // The strike itself. Without a transient the partials fade in and the whole thing
                // reads as a synth pad rather than as something being hit.
                float s = noise.Next();
                svf.Step(s, 5200f, 0.8f, out _, out _, out float hp);
                float hit = (s * Decay(t, 0.0002f, 0.0012f) + hp * Decay(t, 0.001f, 0.02f)) * strike;

                buf[i] = sum + hit;
            }

            return buf;
        }

        static float[] Death(float seconds, int seed)
        {
            int n = Samples(seconds);
            var buf = new float[n];
            var noise = new Noise(seed);
            var svf = new Svf();
            float phase = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float u = (float)i / n;

                // Falling, not ringing. Every other event in this bank rings; the one that ends
                // the fight drops away, and that contrast is what makes it read as final.
                float f = Mathf.Lerp(165f, 48f, Mathf.Pow(u, 0.6f));
                phase += 2f * Mathf.PI * f / SampleRate;
                float tone = Mathf.Sin(phase) * Decay(t, 0.004f, 0.55f);

                svf.Step(noise.Next(), Mathf.Lerp(1800f, 300f, u), 1.1f, out float lp, out _, out _);
                float air = lp * Decay(t, 0.002f, 0.30f) * 0.5f;

                buf[i] = tone * 0.9f + air;
            }

            return buf;
        }

        static float[] Recover(float seconds)
        {
            int n = Samples(seconds);
            var buf = new float[n];

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;

                // Two soft tones falling, and deliberately unremarkable. This fires when her
                // posture has drained back to zero - the player LOST the opening - and a
                // satisfying sound on a failure state teaches exactly the wrong lesson.
                float a = Mathf.Sin(2f * Mathf.PI * 620f * t) * Decay(t, 0.012f, 0.10f);
                float b = Mathf.Sin(2f * Mathf.PI * 415f * t) * Decay(t - 0.10f, 0.012f, 0.16f);

                buf[i] = a * 0.6f + b * 0.8f;
            }

            return buf;
        }

        /// <summary>
        /// Normalise to the recipe peak, then fade the tail. A clip that does not end at zero
        /// clicks on every play, and these play from a pool several times a second.
        /// </summary>
        static float[] Finish(float[] buf, float peak)
        {
            float max = 0f;
            for (int i = 0; i < buf.Length; i++) max = Mathf.Max(max, Mathf.Abs(buf[i]));

            float scale = max > 1e-5f ? peak / max : 0f;
            int fade = Mathf.Min(buf.Length, Samples(0.006f));

            for (int i = 0; i < buf.Length; i++)
            {
                float v = buf[i] * scale;

                int fromEnd = buf.Length - i;
                if (fromEnd < fade) v *= (float)fromEnd / fade;

                buf[i] = Mathf.Clamp(v, -1f, 1f);
            }

            return buf;
        }

        // ------------------------------------------------------------------ files

        /// <summary>16-bit mono PCM at 44.1 kHz, per the build plan shopping list.</summary>
        static void WriteWav(string path, float[] samples)
        {
            int dataBytes = samples.Length * 2;

            using var stream = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(stream);

            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataBytes);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));

            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)1);              // PCM
            w.Write((short)1);              // mono
            w.Write(SampleRate);
            w.Write(SampleRate * 2);        // byte rate
            w.Write((short)2);              // block align
            w.Write((short)16);             // bits per sample

            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataBytes);

            for (int i = 0; i < samples.Length; i++)
                w.Write((short)Mathf.Clamp(Mathf.RoundToInt(samples[i] * 32767f),
                                           short.MinValue, short.MaxValue));
        }

        static AudioClip Import(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var ai = (AudioImporter)AssetImporter.GetAtPath(path);
            ai.forceToMono = true;

            // DECOMPRESS ON LOAD, PCM, PRELOADED. The ImpactAudio contract is that the sound
            // arrives with the hit - inside ~12 ms - and a compressed clip decoding on first play
            // misses that on the one hit that matters most, the first one. These are all under a
            // second; the whole bank costs a couple of MB of RAM and that is the right trade.
            AudioImporterSampleSettings s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.PCM;
            s.preloadAudioData = true;
            ai.defaultSampleSettings = s;

            ai.loadInBackground = false;
            ai.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(AudioFolder))
                AssetDatabase.CreateFolder("Assets/_Game", "Audio");
        }

        // ------------------------------------------------------------------ wiring

        /// <summary>
        /// Which whoosh belongs to which move. Fixed rather than random: a swing is that move's
        /// telegraph, so the same attack has to sound the same every time or it teaches nothing.
        /// The variation that matters is across the impact bank, which ImpactAudio picks from.
        /// </summary>
        static readonly (MoveId id, string clip)[] SwingMap =
        {
            (MoveId.Slash1, "SFX_Swing_Light_1"),
            (MoveId.Slash2, "SFX_Swing_Light_2"),
            (MoveId.Slash3, "SFX_Swing_Heavy_1"),

            (MoveId.Skill1, "SFX_Swing_Special_1"),
            (MoveId.Skill2, "SFX_Swing_Special_2"),
            (MoveId.Skill3, "SFX_Swing_Special_3"),

            (MoveId.Evade,       "SFX_Dodge_1"),
            (MoveId.Parry,       "SFX_Dodge_1"),
            (MoveId.QuickShiftF, "SFX_Dodge_2"),
            (MoveId.QuickShiftB, "SFX_Dodge_2"),
            (MoveId.QuickShiftL, "SFX_Dodge_2"),
            (MoveId.QuickShiftR, "SFX_Dodge_2"),

            (MoveId.Draw,    "SFX_Sheath_1"),
            (MoveId.Sheathe, "SFX_Sheath_1"),
        };

        static int WireMoves(Dictionary<string, AudioClip> bank)
        {
            int count = 0;
            count += WireMoveSet(bank, MovesFolder, "Move_");
            count += WireMoveSet(bank, BossMovesFolder, "Boss_");

            AssetDatabase.SaveAssets();
            return count;
        }

        /// <summary>
        /// Build Move Assets regenerates these assets in place and never touches swingSound, so
        /// this wiring survives a re-run of that tool. Verified against CombatSetupTools.
        /// </summary>
        static int WireMoveSet(Dictionary<string, AudioClip> bank, string folder, string prefix)
        {
            int count = 0;

            foreach ((MoveId id, string clip) in SwingMap)
            {
                string path = folder + "/" + prefix + id + ".asset";
                var move = AssetDatabase.LoadAssetAtPath<MoveDefinition>(path);
                if (move == null) continue;

                move.swingSound = bank.TryGetValue(clip, out AudioClip c) ? c : null;

                // impactSound is left alone, and null is the correct value for it. It is a
                // per-move OVERRIDE; leaving it empty is what makes PlayImpact draw from the
                // shared bank and pick a different variation each time, which is the whole
                // defence against a three-slash chain sounding like one sound played three times.
                EditorUtility.SetDirty(move);
                count++;
            }

            return count;
        }

        static void WireFeedback(Dictionary<string, AudioClip> bank)
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ArenaScene)
                scene = EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);

            var feedback = Object.FindAnyObjectByType<HitFeedback>();
            if (feedback == null)
            {
                Debug.LogWarning("[DS2] No HitFeedback in the arena - run Wire Feel first.");
                return;
            }

            var so = new SerializedObject(feedback);

            AssignArray(so, "sfx.impact", bank, "SFX_Impact_1", "SFX_Impact_2", "SFX_Impact_3");
            AssignArray(so, "sfx.heavyImpact", bank, "SFX_ImpactHeavy_1", "SFX_ImpactHeavy_2");
            AssignArray(so, "sfx.evaded", bank, "SFX_Evaded_1", "SFX_Evaded_2");
            AssignArray(so, "sfx.parry", bank, "SFX_Parry_1", "SFX_Parry_2");

            AssignOne(so, "sfx.postureBreak", bank, "SFX_PostureBreak");
            AssignOne(so, "sfx.postureRecover", bank, "SFX_PostureRecover");
            AssignOne(so, "sfx.death", bank, "SFX_Death");

            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(feedback);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void AssignArray(SerializedObject so, string field,
                                Dictionary<string, AudioClip> bank, params string[] names)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[DS2] HitFeedback has no field '" + field + "'.");
                return;
            }

            prop.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue =
                    bank.TryGetValue(names[i], out AudioClip c) ? c : null;
        }

        static void AssignOne(SerializedObject so, string field,
                              Dictionary<string, AudioClip> bank, string name)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[DS2] HitFeedback has no field '" + field + "'.");
                return;
            }

            prop.objectReferenceValue = bank.TryGetValue(name, out AudioClip c) ? c : null;
        }
    }
}
