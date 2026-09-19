using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DS2.EditorTools
{
    /// <summary>
    /// Bakes the death pose into standalone corpse prefabs for dressing the arena. Re-runnable,
    /// like every other DS2 tool - it overwrites what it made and leaves everything else alone.
    ///
    /// WHY BAKED RATHER THAN FROZEN AT RUNTIME. The obvious approach is an Animator held on the
    /// last frame of Die, but that costs an Animator per corpse and - far worse when you are the
    /// one placing them - the pose only exists in Play mode, so the whole field stands up straight
    /// in the Scene view while you are trying to arrange it. Sampling the clip in the editor and
    /// writing the bone transforms into the prefab means what you see while placing is what ships.
    ///
    /// THE POSE IS CAPTURED BEFORE THE ANIMATOR IS STRIPPED. AnimationMode restores the original
    /// pose when it stops, and removing an Animator can snap the rig back to bind pose, so the
    /// bone transforms are read out while the sample is still held and written back afterwards.
    /// Without that you get a T-posed corpse and no clue why.
    /// </summary>
    static class CorpseWiring
    {
        const string KatanaGirl = "Assets/_Game/Prefabs/KatanaGirl.prefab";
        const string DieFbx = "Assets/CombatGirlsCharacterPack/Katana_Girl/Animations/Normal/K_Die.fbx";

        const string PrefabFolder = "Assets/_Game/Prefabs";
        const string CorpseFolder = PrefabFolder + "/Corpses";

        /// <summary>
        /// Fractions of the clip to sample. Die runs 2.833 s and she is still settling over the
        /// last half second, so these four read as four different collapses rather than as one
        /// body rotated four ways - which is what a row of identical corpses looks like.
        /// </summary>
        static readonly (string name, float at)[] Poses =
        {
            ("Corpse_A", 1.00f),
            ("Corpse_B", 0.96f),
            ("Corpse_C", 0.91f),
            ("Corpse_D", 0.86f),
        };

        [MenuItem("Tools/DS2/Build Corpse Prefabs")]
        static void Build()
        {
            AnimationClip die = FindDieClip();
            if (die == null)
            {
                Debug.LogError("[DS2] Could not find the Die clip in " + DieFbx);
                return;
            }

            EnsureFolder();

            var made = new List<string>();
            foreach ((string name, float at) in Poses)
            {
                GameObject prefab = BuildOne(die, die.length * at, name);
                if (prefab != null) made.Add(name);
            }

            FixLockOnMask();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[DS2] Corpses: {made.Count} prefab(s) written to {CorpseFolder} " +
                      $"({string.Join(", ", made)}).");
        }

        static AnimationClip FindDieClip()
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(DieFbx))
            {
                // An FBX carries a hidden __preview__ copy of each clip alongside the real one.
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
            return null;
        }

        static GameObject BuildOne(AnimationClip die, float time, string name)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(KatanaGirl);
            if (source == null)
            {
                Debug.LogError("[DS2] Missing " + KatanaGirl);
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);

            // UNPACKED, so the result is a standalone prefab rather than a variant of the player.
            // A variant would inherit every future change to KatanaGirl - including any component
            // added back - and a corpse that quietly regains a CombatActor is an invincible enemy
            // standing in the corner of the arena.
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            // Clear the junk transform the prefab carries, so the sample and the re-centre below
            // both start from a known place.
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            var animator = go.GetComponent<Animator>();
            if (animator != null) animator.applyRootMotion = false;

            Transform[] bones = go.GetComponentsInChildren<Transform>(true);
            var pos = new Vector3[bones.Length];
            var rot = new Quaternion[bones.Length];
            var scale = new Vector3[bones.Length];

            // The rest pose, kept so we can tell whether sampling actually did anything.
            var before = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++) before[i] = bones[i].localPosition;

            // Sample, then read the rig out while AnimationMode is still holding it.
            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(go, die, time);
                AnimationMode.EndSampling();

                Capture(bones, pos, rot, scale);
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }

            if (!Moved(before, pos))
            {
                // AnimationMode is the editor-sanctioned path and normally the more reliable of
                // the two on a humanoid rig, but if it produced nothing then a silently T-posed
                // corpse is the worst available outcome - so try the direct sampler instead.
                die.SampleAnimation(go, time);
                Capture(bones, pos, rot, scale);
            }

            if (!Moved(before, pos))
            {
                Debug.LogError($"[DS2] {name}: sampling Die at {time:0.00}s changed no bone - the " +
                               "corpse would ship in bind pose, so it was not written.", go);
                Object.DestroyImmediate(go);
                return null;
            }

            StripToRenderers(go);

            // Write the captured pose back over whatever the teardown left behind.
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                bones[i].localPosition = pos[i];
                bones[i].localRotation = rot[i];
                bones[i].localScale = scale[i];
            }

            Recentre(go);
            FixBounds(go);

            go.name = name;

            string path = CorpseFolder + "/" + name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            return prefab;
        }

        static void Capture(Transform[] bones, Vector3[] pos, Quaternion[] rot, Vector3[] scale)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                pos[i] = bones[i].localPosition;
                rot[i] = bones[i].localRotation;
                scale[i] = bones[i].localScale;
            }
        }

        /// <summary>Did sampling actually move the rig, or is this still the bind pose?</summary>
        static bool Moved(Vector3[] before, Vector3[] after)
        {
            for (int i = 0; i < before.Length; i++)
                if ((before[i] - after[i]).sqrMagnitude > 1e-8f) return true;

            return false;
        }

        /// <summary>
        /// Keeps Transforms and renderers, destroys everything else.
        ///
        /// A WHITELIST, DELIBERATELY. Listing what to remove instead means anything later added to
        /// the player silently survives onto every corpse, and the failure mode is a body in the
        /// scenery that takes hits or answers lock-on. Naming what is allowed to stay cannot drift.
        /// </summary>
        static void StripToRenderers(GameObject root)
        {
            Component[] all = root.GetComponentsInChildren<Component>(true);

            // Several passes: RequireComponent dependencies refuse to be destroyed out of order,
            // so a component that will not go on the first pass usually goes on the second.
            for (int pass = 0; pass < 4; pass++)
            {
                foreach (Component c in all)
                {
                    if (c == null) continue;
                    if (c is Transform) continue;
                    if (c is SkinnedMeshRenderer || c is MeshRenderer || c is MeshFilter) continue;

                    Object.DestroyImmediate(c);
                }
            }
        }

        /// <summary>
        /// Puts the body at the prefab origin, feet on the floor.
        ///
        /// Die carries RootXZNet 0.768 and RootYNet -0.862 - she travels and drops as she falls -
        /// so a sampled last frame sits about a metre away from the transform it belongs to. Left
        /// alone you click to place a corpse and it appears somewhere else, which makes dressing
        /// a map by hand genuinely maddening.
        /// </summary>
        static void Recentre(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            Vector3 offset = new Vector3(b.center.x, b.min.y, b.center.z) - root.transform.position;

            // Move the children rather than the root, so the root stays a clean origin handle.
            foreach (Transform child in root.transform)
                child.position -= offset;
        }

        /// <summary>
        /// A SkinnedMeshRenderer keeps the bounds it computed for the BIND pose, and a corpse lying
        /// flat is nowhere near that volume - so it gets frustum-culled and pops out of view at
        /// certain camera angles, which reads as corpses flickering rather than as a culling bug.
        ///
        /// updateWhenOffscreen recomputes the bounds each frame. That is a bounds calculation, not
        /// a re-skin, and at the scale of a dressed arena it is not worth a cheaper, riskier fix.
        /// </summary>
        static void FixBounds(GameObject root)
        {
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;
        }

        /// <summary>
        /// Narrows lock-on to the Enemy layer.
        ///
        /// THIS IS WHY CORPSES WOULD OTHERWISE BREAK THE FIGHT. LockOnController.targetLayers ships
        /// as Everything, FindBestTarget ignores triggers, and the Hurtbox capsule is NOT a trigger
        /// - so any corpse inside acquireRange is a valid target, and scoring is nearest to screen
        /// centre, which is exactly where the bodies between you and her will be. The mask is
        /// already loose enough that the floor and the boundary cubes qualify; corpses only turn
        /// that from a latent bug into a constant one.
        ///
        /// Done here because a tool that produces corpses is not finished if the corpses take over
        /// lock-on the moment they are placed.
        /// </summary>
        static void FixLockOnMask()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(KatanaGirl);
            try
            {
                var lockOn = contents.GetComponentInChildren<LockOnController>(true);
                if (lockOn == null)
                {
                    Debug.LogWarning("[DS2] No LockOnController on " + KatanaGirl +
                                     " - lock-on mask left alone.");
                    return;
                }

                int enemy = LayerMask.NameToLayer("Enemy");
                if (enemy < 0) enemy = 3;

                var so = new SerializedObject(lockOn);
                SerializedProperty prop = so.FindProperty("targetLayers");
                if (prop == null)
                {
                    Debug.LogWarning("[DS2] LockOnController has no field 'targetLayers'.");
                    return;
                }

                int mask = 1 << enemy;
                if (prop.intValue == mask) return;

                prop.intValue = mask;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(contents, KatanaGirl);
                Debug.Log($"[DS2] Lock-on targetLayers narrowed to layer {enemy} (Enemy) " +
                          "so corpses and scenery cannot be targeted.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(CorpseFolder))
                AssetDatabase.CreateFolder(PrefabFolder, "Corpses");
        }
    }
}
