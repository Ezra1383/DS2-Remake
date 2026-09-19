using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DS2.EditorTools
{
    /// <summary>
    /// Turns the arena into night lit by its own fires. Re-runnable - it restates the whole result
    /// every time, so tuning the numbers below and running it again is the intended workflow.
    ///
    /// THE ORDER THESE MATTER IN, which is not the order you would guess:
    ///
    /// 1. AMBIENT. Nothing else registers until this is dark. Ambient light has no direction and
    ///    no source, so while it is bright every surface is already lit and no lamp you add can
    ///    make a difference to it. Scenes that "will not go dark" are almost always this.
    /// 2. THE FIRES ACTUALLY EMITTING LIGHT. The Vefects fire prefabs are particles only - they
    ///    carry no Light at all - so before this tool runs, the fires cannot illuminate anything.
    /// 3. The sun, off. 4. Fog, on. 5. Bloom, so the flames read as the brightest thing on screen.
    ///
    /// Forward+ is already set on PC_Renderer, which removes the per-object additional light cap,
    /// so a light per fire is affordable here in a way it would not be under plain Forward.
    /// </summary>
    static class NightLightingWiring
    {
        // ---- tune these, then re-run -------------------------------------------------

        /// <summary>Near-black, faintly blue. The colour of everything the fire does not reach.</summary>
        static readonly Color Ambient = new(0.035f, 0.040f, 0.055f);

        /// <summary>Moonlight. Kept dim rather than off - see the note in SetSun.</summary>
        static readonly Color MoonColor = new(0.55f, 0.65f, 0.95f);
        const float MoonIntensity = 0.12f;

        /// <summary>Smoke. Warm-black rather than grey, so it sits under the firelight.</summary>
        static readonly Color FogColor = new(0.045f, 0.035f, 0.030f);
        const float FogDensity = 0.022f;

        static readonly Color FlameColor = new(1f, 0.55f, 0.22f);
        const float FireIntensity = 4.5f;
        const float FireRange = 13f;
        const float FireHeight = 1.1f;

        /// <summary>
        /// How many fires cast shadows, nearest the boss first. Three is deliberate, not timid -
        /// see SetShadowTier for why the number is small and what happens if you raise it too far.
        /// </summary>
        const int ShadowCastingFires = 3;

        static readonly Color TorchColor = new(1f, 0.62f, 0.30f);
        const float TorchIntensity = 2.6f;
        const float TorchRange = 9f;

        const string VolumeProfilePath = "Assets/Settings/ArenaNight_Volume.asset";
        const string FireVfxMarker = "Free Fire VFX";
        const string LightChild = "FireLight";

        // ------------------------------------------------------------------------------

        [MenuItem("Tools/DS2/Wire Night Lighting")]
        static void Wire()
        {
            Scene scene = EditorSceneManager.GetActiveScene();

            SetEnvironment();
            SetSun();
            int torches = SetTorches();
            int fires = LightTheFires();
            SetVolume();

            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[DS2] Night lighting: ambient darkened, fog on, {torches} torch light(s) " +
                      $"made realtime, {fires} fire(s) given a flickering light.");
        }

        /// <summary>
        /// Ambient, sky and fog. The skybox is cleared rather than darkened: with AmbientMode.Flat
        /// the sky no longer contributes light anyway, so keeping a bright procedural one would
        /// only paint a blue daytime gradient behind a night scene.
        /// </summary>
        static void SetEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.skybox = null;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = FogDensity;

            // With no skybox the camera paints its own background, so it has to agree with the fog
            // or the far wall fades into a colour that is not there.
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = FogColor;
                EditorUtility.SetDirty(cam);
            }
        }

        /// <summary>
        /// The directional light down to moonlight rather than disabled.
        ///
        /// TEMPTING TO SWITCH OFF, AND WRONG TO. This fight is a mirror match - same clips, same
        /// silhouette - and the project has already established that silhouette readability is
        /// mechanical here rather than decorative. A faint cool key from above keeps both figures
        /// separable when they step outside the firelight; total darkness would make the fight
        /// unreadable in exactly the moments the player backs off to breathe.
        /// </summary>
        static void SetSun()
        {
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (l.type != LightType.Directional) continue;

                l.color = MoonColor;
                l.intensity = MoonIntensity;
                l.shadows = LightShadows.Soft;
                EditorUtility.SetDirty(l);
            }
        }

        /// <summary>
        /// Existing point and spot lights to warm realtime flame.
        ///
        /// They were set to Baked, and lighting has never been baked in this scene, so every one
        /// of them was contributing exactly nothing. Realtime costs frame time but needs no bake
        /// and - unlike baked light with no light probes in the scene - it lands on the characters
        /// too, which is the half that matters.
        /// </summary>
        static int SetTorches()
        {
            int n = 0;

            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (l.type == LightType.Directional) continue;
                if (l.GetComponent<FireLight>() != null) continue;   // ours, handled below

                l.lightmapBakeType = LightmapBakeType.Realtime;
                l.color = TorchColor;
                l.intensity = TorchIntensity;
                l.range = TorchRange;

                // Shadow-casting realtime point lights are the expensive part, and a dozen of them
                // in a dungeon is where the frame rate goes. The directional keeps the real shadows.
                l.shadows = LightShadows.None;

                AddFlicker(l, TorchIntensity, 0.22f, 8f);

                EditorUtility.SetDirty(l);
                n++;
            }

            return n;
        }

        /// <summary>
        /// Gives every Vefects fire instance a child point light, because the prefabs ship with
        /// none - they are particles and nothing else.
        ///
        /// A CHILD rather than a component on the prefab instance: the light then follows the fire
        /// if it is moved, and adding a child to a prefab instance is a clean override, where
        /// editing the shared prefab would change every fire in every scene at once.
        /// </summary>
        static int LightTheFires()
        {
            var roots = new HashSet<GameObject>();

            foreach (ParticleSystem ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include))
            {
                GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(ps.gameObject);
                if (root == null) continue;

                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (string.IsNullOrEmpty(path) || !path.Contains(FireVfxMarker)) continue;

                roots.Add(root);
            }

            // Only the fires closest to the fight cast shadows - see ShadowCastingFires. Sorted by
            // distance to the boss, because she stands where the arena is and shadows are only
            // worth their cost where the player is actually looking.
            var brain = Object.FindAnyObjectByType<BossBrain>();
            Vector3 centre = brain != null ? brain.transform.position : Vector3.zero;

            var ordered = new List<GameObject>(roots);
            ordered.Sort((a, b) =>
                (a.transform.position - centre).sqrMagnitude
                .CompareTo((b.transform.position - centre).sqrMagnitude));

            for (int i = 0; i < ordered.Count; i++)
            {
                GameObject fire = ordered[i];
                bool castsShadows = i < ShadowCastingFires;
                Transform existing = fire.transform.Find(LightChild);
                GameObject go;

                if (existing != null)
                {
                    go = existing.gameObject;
                }
                else
                {
                    go = new GameObject(LightChild);
                    go.transform.SetParent(fire.transform, false);
                    go.transform.localPosition = new Vector3(0f, FireHeight, 0f);
                }

                Light l = go.GetComponent<Light>();
                if (l == null) l = go.AddComponent<Light>();

                l.type = LightType.Point;
                l.color = FlameColor;
                l.intensity = FireIntensity;
                l.range = FireRange;
                l.shadows = castsShadows ? LightShadows.Soft : LightShadows.None;
                l.lightmapBakeType = LightmapBakeType.Realtime;

                if (castsShadows) SetShadowTier(l);

                AddFlicker(l, FireIntensity, 0.38f, 7f);
                EditorUtility.SetDirty(go);
            }

            return roots.Count;
        }

        /// <summary>
        /// Pins a shadow-casting fire to the LOW resolution tier (256 per face).
        ///
        /// THE ATLAS IS THE WHOLE CONSTRAINT. Every shadow-casting additional light shares one
        /// 2048x2048 atlas, and a POINT light is a cubemap - it needs six faces, not one. At 256
        /// that is six 256-squares each; at the medium tier of 512 only about two point lights fit
        /// in the entire atlas, and at high none do.
        ///
        /// When the atlas overflows, URP does not warn - it silently drops shadows from whichever
        /// lights did not fit. "Only some of my fires cast shadows" is that, every time, and it is
        /// why this pins the tier instead of leaving URP to choose per light.
        /// </summary>
        static void SetShadowTier(Light l)
        {
            var data = l.GetComponent<UniversalAdditionalLightData>();
            if (data == null) data = l.gameObject.AddComponent<UniversalAdditionalLightData>();

            var so = new SerializedObject(data);
            SerializedProperty tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier");
            if (tier != null)
            {
                tier.intValue = 1;   // Low - 256 per cube face
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void AddFlicker(Light l, float intensity, float amplitude, float speed)
        {
            FireLight f = l.GetComponent<FireLight>();
            if (f == null) f = l.gameObject.AddComponent<FireLight>();

            var so = new SerializedObject(f);
            so.FindProperty("target").objectReferenceValue = l;
            so.FindProperty("baseIntensity").floatValue = intensity;
            so.FindProperty("amplitude").floatValue = amplitude;
            so.FindProperty("speed").floatValue = speed;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// A scene Volume carrying bloom and a filmic tonemap.
        ///
        /// Bloom is not decoration here - it is what separates a flame from an orange polygon.
        /// Fire is meant to be the brightest thing in frame by a wide margin, and without a bloom
        /// threshold to blow past it simply cannot read that way against a dark room.
        /// </summary>
        static void SetVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
            bloom.active = true;
            bloom.threshold.overrideState = true; bloom.threshold.value = 0.85f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 1.1f;
            bloom.scatter.overrideState = true; bloom.scatter.value = 0.72f;
            bloom.tint.overrideState = true; bloom.tint.value = new Color(1f, 0.85f, 0.7f);

            if (!profile.TryGet(out Tonemapping tone)) tone = profile.Add<Tonemapping>(true);
            tone.active = true;
            tone.mode.overrideState = true; tone.mode.value = TonemappingMode.ACES;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var volume = Object.FindAnyObjectByType<Volume>();
            if (volume == null)
            {
                var go = new GameObject("Global Volume (Night)");
                volume = go.AddComponent<Volume>();
            }

            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
        }
    }
}
