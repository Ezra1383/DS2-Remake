using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DS2.EditorTools
{
    /// <summary>
    /// Builds the whole impact/trail VFX set from nothing: textures, materials, particle prefabs,
    /// the blade trail, and the wiring into HitFeedback. Re-runnable, like every other DS2 tool -
    /// it overwrites what it made and leaves everything else alone.
    ///
    /// THE TEXTURES ARE GENERATED, NOT IMPORTED. The Combat Girls pack ships no particle art and
    /// the project has no VFX Graph and no Shader Graph, so three procedural PNGs - a core flash,
    /// a shard and a soft band - are written to disk here. That means there is no art dependency
    /// to source, and a fresh clone can rebuild the entire effect set from one menu item.
    ///
    /// The look is anime slash rather than realistic sparks, chosen to sit with the UTS toon
    /// shading and the outline pass: hard-edged shards and a bright core read at speed, and
    /// silhouette readability is mechanical in this fight rather than decorative.
    /// </summary>
    static class VfxWiring
    {
        const string ArenaScene = "Assets/_Game/Scenes/Arena.unity";
        const string KatanaGirl = "Assets/_Game/Prefabs/KatanaGirl.prefab";
        const string BossPrefab = "Assets/_Game/Prefabs/Boss Variant.prefab";

        const string VfxFolder = "Assets/_Game/VFX";
        const string TexFolder = VfxFolder + "/Textures";
        const string MatFolder = VfxFolder + "/Materials";

        const string TrailObject = "BladeTrail";

        /// <summary>
        /// Distance along the HitBox's local +Z to put the trail. The blade collider measures
        /// 0.06 x 0.07 x 1.12 and is centred on the blade, so the ends are at +/-0.56.
        ///
        /// IF THE TRAIL COMES OFF THE HILT INSTEAD OF THE TIP, negate this and re-run. Which end
        /// of that box is the point is not recorded anywhere and cannot be read off the YAML.
        /// </summary>
        const float BladeTipLocalZ = 0.56f;

        [MenuItem("Tools/DS2/Build VFX")]
        static void BuildAll()
        {
            EnsureFolders();

            Texture2D core = WriteTexture("FX_Core", 128, 128, CorePixel);
            Texture2D shard = WriteTexture("FX_Shard", 128, 128, ShardPixel);
            Texture2D band = WriteTexture("FX_Band", 64, 32, BandPixel);

            Material coreMat = Additive("M_FX_Core", core);
            Material shardMat = Additive("M_FX_Shard", shard);
            Material trailMat = Additive("M_FX_Trail", band);

            ParticleSystem light = BuildImpact("FX_Impact", coreMat, shardMat, heavy: false);
            ParticleSystem heavy = BuildImpact("FX_ImpactHeavy", coreMat, shardMat, heavy: true);
            ParticleSystem parry = BuildParry("FX_Parry", coreMat, shardMat);
            ParticleSystem evade = BuildEvade("FX_Evade", coreMat);

            AddTrail(KatanaGirl, trailMat, telegraph: false);

            // Flush the base prefab before touching the variant: the boss inherits WeaponTrail
            // from KatanaGirl, and LoadPrefabContents on her would not see a component that is
            // still only in memory.
            AssetDatabase.SaveAssets();
            SetBossTelegraph();

            WireFeedback(light, heavy, parry, evade);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DS2] VFX built: 3 textures, 3 materials, 3 impact prefabs, blade trail on " +
                      "both characters. Boss telegraph ON, player OFF.");
        }

        // === textures ========================================================================

        /// <summary>
        /// Bright core with four axis spikes - the anime impact flash. RGB stays white and the
        /// shape lives entirely in alpha, because additive blending multiplies by alpha and the
        /// particle system tints per-effect from startColor.
        /// </summary>
        static Color CorePixel(float u, float v)
        {
            float d = Mathf.Sqrt(u * u + v * v);
            float core = Mathf.Pow(Mathf.Clamp01(1f - d), 4f);

            // A spike is "close to one axis and not far from the centre".
            float nearAxis = Mathf.Min(Mathf.Abs(u), Mathf.Abs(v));
            float spike = Mathf.Clamp01(1f - nearAxis * 9f) * Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f);

            return new Color(1f, 1f, 1f, Mathf.Clamp01(core + spike * 0.75f));
        }

        /// <summary>
        /// A lens - pointed at both ends, fat in the middle. Hard-edged with a pixel of softening
        /// so it stays crisp when stretched, which is what separates a slash shard from a smudge.
        /// </summary>
        static Color ShardPixel(float u, float v)
        {
            float halfHeight = 0.62f * Mathf.Pow(Mathf.Clamp01(1f - u * u), 0.85f);
            float a = Mathf.Clamp01((halfHeight - Mathf.Abs(v)) / 0.045f);
            return new Color(1f, 1f, 1f, a);
        }

        /// <summary>
        /// A soft horizontal band, uniform along its length. The TrailRenderer's own gradient and
        /// width curve do the head-to-tail taper - doing it in the texture too would mean knowing
        /// which end of the UV is the head, which differs by texture mode.
        /// </summary>
        static Color BandPixel(float u, float v)
        {
            return new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v)), 2f));
        }

        static Texture2D WriteTexture(string name, int w, int h, System.Func<float, float, Color> f)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // -1..1 across both axes, sampled at pixel centres.
                    float u = (x + 0.5f) / w * 2f - 1f;
                    float v = (y + 0.5f) / h * 2f - 1f;
                    px[y * w + x] = f(u, v);
                }
            }

            tex.SetPixels(px);
            tex.Apply();

            string path = TexFolder + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // === materials =======================================================================

        /// <summary>
        /// Additive URP particle material. The blend setup is handed to URP's own
        /// BaseShaderGUI.SetupMaterialBlendMode rather than written by hand: the hidden
        /// _SrcBlend/_DstBlend/_ZWrite/queue/keyword combination is easy to get subtly wrong, and
        /// this project has already lost a day to a shader that rendered magenta.
        /// </summary>
        static Material Additive(string name, Texture2D tex)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError("[DS2] URP particle shader not found - is URP still installed?");
                return null;
            }

            string path = MatFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = shader;
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);  // Transparent
            mat.SetFloat("_Blend", 2f);    // Additive

            BaseShaderGUI.SetupMaterialBlendMode(mat);
            BaseShaderGUI.SetMaterialKeywords(mat);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // === particle prefabs ================================================================

        static ParticleSystem BuildImpact(string name, Material coreMat, Material shardMat, bool heavy)
        {
            var root = new GameObject(name);

            ParticleSystem flash = AddSystem(root, coreMat, ParticleSystemRenderMode.Billboard);
            Burst(flash, 1);

            ParticleSystem.MainModule m = flash.main;
            m.startLifetime = heavy ? 0.18f : 0.13f;
            m.startSpeed = 0f;
            m.startSize = heavy ? 1.5f : 0.95f;
            m.startColor = heavy ? new Color(1f, 0.72f, 0.72f) : Color.white;

            // Pop: overshoot, then collapse. A flash that only shrinks reads as a fade.
            SizeCurve(flash, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
            AlphaRamp(flash, 1f, 0f);

            var shardsGo = new GameObject("Shards");
            shardsGo.transform.SetParent(root.transform, false);

            ParticleSystem shards = AddSystem(shardsGo, shardMat, ParticleSystemRenderMode.Stretch);
            Burst(shards, heavy ? 12 : 6);

            ParticleSystem.MainModule sm = shards.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(heavy ? 0.20f : 0.14f,
                                                             heavy ? 0.34f : 0.24f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(heavy ? 7f : 5f, heavy ? 13f : 9f);
            sm.startSize = new ParticleSystem.MinMaxCurve(heavy ? 0.28f : 0.20f,
                                                          heavy ? 0.46f : 0.34f);
            sm.startColor = heavy
                ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.52f), Color.white)
                : new ParticleSystem.MinMaxGradient(new Color(1f, 0.62f, 0.78f), Color.white);

            // Local +Z is the blow direction: HitFeedback rotates the spawned effect to look
            // away from the attacker, so a cone down +Z throws the shards along the hit.
            Cone(shards, heavy ? 42f : 34f, 0.06f);
            SizeCurve(shards, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));
            AlphaRamp(shards, 1f, 0f);
            Stretch(shards, heavy ? 3.2f : 2.6f);

            if (heavy) AddDebris(root, shardMat);

            ParticleSystem saved = SavePrefab(root, name);
            return saved;
        }

        /// <summary>
        /// Heavy hits only. A tight, fast, gravity-bound spray straight down the blow vector -
        /// the thing that says this one was different rather than just bigger.
        /// </summary>
        static void AddDebris(GameObject root, Material shardMat)
        {
            var go = new GameObject("Debris");
            go.transform.SetParent(root.transform, false);

            ParticleSystem ps = AddSystem(go, shardMat, ParticleSystemRenderMode.Stretch);
            Burst(ps, 10);

            ParticleSystem.MainModule m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.45f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(8f, 15f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            m.startColor = new Color(1f, 0.55f, 0.58f);
            m.gravityModifier = 2.2f;

            Cone(ps, 16f, 0.04f);
            SizeCurve(ps, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            AlphaRamp(ps, 1f, 0f);
            Stretch(ps, 4.5f);
        }

        /// <summary>
        /// Radial and white, where a damage hit is directional and pink. The difference is the
        /// point: a parry is the one result the player most needs to recognise the instant it
        /// happens, and shape reads faster than colour.
        /// </summary>
        static ParticleSystem BuildParry(string name, Material coreMat, Material shardMat)
        {
            var root = new GameObject(name);

            ParticleSystem flash = AddSystem(root, coreMat, ParticleSystemRenderMode.Billboard);
            Burst(flash, 1);

            ParticleSystem.MainModule m = flash.main;
            m.startLifetime = 0.22f;
            m.startSpeed = 0f;
            m.startSize = 1.8f;
            m.startColor = new Color(1f, 0.97f, 0.86f);

            SizeCurve(flash, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(0.18f, 1f), new Keyframe(1f, 0f)));
            AlphaRamp(flash, 1f, 0f);

            var sparksGo = new GameObject("Sparks");
            sparksGo.transform.SetParent(root.transform, false);

            ParticleSystem sparks = AddSystem(sparksGo, shardMat, ParticleSystemRenderMode.Stretch);
            Burst(sparks, 20);

            ParticleSystem.MainModule sm = sparks.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.40f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(6f, 12f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.20f);
            sm.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.92f, 0.66f), Color.white);
            sm.gravityModifier = 1.6f;

            // Sphere, not cone: the blades met, so it throws in every direction.
            ParticleSystem.ShapeModule shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            SizeCurve(sparks, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            AlphaRamp(sparks, 1f, 0f);
            Stretch(sparks, 3.8f);

            return SavePrefab(root, name);
        }

        // === particle helpers ================================================================

        static ParticleSystem AddSystem(GameObject go, Material mat, ParticleSystemRenderMode mode)
        {
            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps == null) ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule m = ps.main;
            m.loop = false;
            m.playOnAwake = true;
            m.duration = 0.5f;
            m.maxParticles = 64;

            // HitFeedback owns the lifetime and destroys the object itself. A stop action here
            // would race it.
            m.stopAction = ParticleSystemStopAction.None;

            // World space, so a hit landed on a moving actor does not drag its own sparks along.
            m.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule e = ps.emission;
            e.enabled = true;
            e.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            // Additive particles over a toon character: bias them in front, or the outline pass
            // and the blade can sort over the impact.
            r.sortingFudge = -2f;

            return ps;
        }

        static void Burst(ParticleSystem ps, int count)
        {
            ParticleSystem.EmissionModule e = ps.emission;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        static void Cone(ParticleSystem ps, float angle, float radius)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
        }

        static void SizeCurve(ParticleSystem ps, AnimationCurve curve)
        {
            ParticleSystem.SizeOverLifetimeModule s = ps.sizeOverLifetime;
            s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        static void AlphaRamp(ParticleSystem ps, float from, float to)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(from, 0f), new GradientAlphaKey(to, 1f) });

            ParticleSystem.ColorOverLifetimeModule c = ps.colorOverLifetime;
            c.enabled = true;
            c.color = new ParticleSystem.MinMaxGradient(g);
        }

        static void Stretch(ParticleSystem ps, float lengthScale)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = lengthScale;
            r.velocityScale = 0.06f;
        }

        static ParticleSystem SavePrefab(GameObject root, string name)
        {
            string path = VfxFolder + "/" + name + ".prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved.GetComponent<ParticleSystem>();
        }

        // === trail ===========================================================================

        static void AddTrail(string prefabPath, Material trailMat, bool telegraph)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var hitbox = root.GetComponentInChildren<Hitbox>(true);
                if (hitbox == null)
                {
                    Debug.LogError("[DS2] No Hitbox under " + prefabPath + " - cannot place the trail.");
                    return;
                }

                Transform existing = hitbox.transform.Find(TrailObject);
                GameObject tipGo;

                if (existing != null)
                {
                    tipGo = existing.gameObject;
                }
                else
                {
                    tipGo = new GameObject(TrailObject);
                    tipGo.transform.SetParent(hitbox.transform, false);
                }

                // The blade stays put in the hierarchy - Character_Weapon_Controller moves it by
                // ParentConstraint source index rather than reparenting - so a child of the
                // hitbox tracks the katana correctly, exactly as the hitbox itself does.
                tipGo.transform.localPosition = new Vector3(0f, 0f, BladeTipLocalZ);
                tipGo.transform.localRotation = Quaternion.identity;

                TrailRenderer trail = tipGo.GetComponent<TrailRenderer>();
                if (trail == null) trail = tipGo.AddComponent<TrailRenderer>();

                trail.time = 0.16f;
                trail.minVertexDistance = 0.02f;
                trail.autodestruct = false;
                trail.emitting = false;
                trail.alignment = LineAlignment.View;
                trail.textureMode = LineTextureMode.Stretch;
                trail.numCapVertices = 2;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.sharedMaterial = trailMat;

                trail.widthCurve = new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.55f, 0.5f), new Keyframe(1f, 0f));
                trail.widthMultiplier = 0.16f;

                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = g;

                var wt = root.GetComponent<WeaponTrail>();
                if (wt == null) wt = root.AddComponent<WeaponTrail>();

                var so = new SerializedObject(wt);
                so.FindProperty("actor").objectReferenceValue = root.GetComponent<CombatActor>();
                so.FindProperty("trail").objectReferenceValue = trail;
                so.FindProperty("telegraph").boolValue = telegraph;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("[DS2] Blade trail on " + Path.GetFileName(prefabPath) +
                          " (telegraph " + (telegraph ? "ON" : "OFF") + ").");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The boss is a Prefab VARIANT of the player, so she already inherits the trail - only
        /// the telegraph flag differs, and it is stored as an override on her. Adding the trail
        /// to her separately would give her two.
        /// </summary>
        static void SetBossTelegraph()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BossPrefab);
            try
            {
                var wt = root.GetComponent<WeaponTrail>();
                if (wt == null)
                {
                    Debug.LogError("[DS2] Boss has no WeaponTrail - is she still a variant of " +
                                   "KatanaGirl?");
                    return;
                }

                var so = new SerializedObject(wt);
                so.FindProperty("telegraph").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, BossPrefab);
                Debug.Log("[DS2] Boss telegraph ON (wind-up colour ramp).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // === scene wiring ====================================================================

        /// <summary>
        /// The "you passed through i-frames" puff. Cold, soft and expanding, with no shards and
        /// no directionality - it must not read as a hit landing, because the whole reason it
        /// exists is that an evaded swing was previously indistinguishable from a broken hitbox.
        /// </summary>
        static ParticleSystem BuildEvade(string name, Material coreMat)
        {
            var root = new GameObject(name);

            ParticleSystem ps = AddSystem(root, coreMat, ParticleSystemRenderMode.Billboard);
            Burst(ps, 1);

            ParticleSystem.MainModule m = ps.main;
            m.startLifetime = 0.30f;
            m.startSpeed = 0f;
            m.startSize = 1.1f;
            m.startColor = new Color(0.55f, 0.85f, 1f);

            // Expands and fades rather than popping: a soft outward bloom reads as "through",
            // where the snap of the impact flash reads as "into".
            SizeCurve(ps, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f)));
            AlphaRamp(ps, 0.85f, 0f);

            return SavePrefab(root, name);
        }

        static void WireFeedback(ParticleSystem light, ParticleSystem heavy, ParticleSystem parry,
                                 ParticleSystem evade)
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
            Assign(so, "impactVfx", light);
            Assign(so, "heavyImpactVfx", heavy);
            Assign(so, "parryVfx", parry);

            // CLEARED, not merely left alone. The dodge is shown by the backstep, not by a puff at
            // the contact point - and an earlier version of this tool did assign it, so skipping
            // the line would leave that assignment in place in any scene already built. Re-runnable
            // means the tool states the whole result, including the slots it wants empty.
            // FX_Evade is still built, so turning it back on is a drag-and-drop.
            Assign(so, "evadeVfx", null);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(feedback);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void Assign(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[DS2] HitFeedback has no field '" + field + "'.");
                return;
            }
            prop.objectReferenceValue = value;
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(VfxFolder))
                AssetDatabase.CreateFolder("Assets/_Game", "VFX");
            if (!AssetDatabase.IsValidFolder(TexFolder))
                AssetDatabase.CreateFolder(VfxFolder, "Textures");
            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder(VfxFolder, "Materials");
        }
    }
}
