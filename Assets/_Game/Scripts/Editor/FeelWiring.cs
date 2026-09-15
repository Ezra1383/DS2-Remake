using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DS2.EditorTools
{
    /// <summary>
    /// Puts the feedback layer into the scene and onto the actors.
    ///
    /// The piece that is easy to miss by hand: Cinemachine Impulse does nothing at all without a
    /// CinemachineImpulseListener on the camera. Sources broadcast, listeners react, and with no
    /// listener every impulse is silently discarded - no error, no warning, just a camera that
    /// never moves. That single missing component would look exactly like broken shake code.
    /// </summary>
    static class FeelWiring
    {
        const string ArenaScene = "Assets/_Game/Scenes/Arena.unity";
        const string MovesFolder = "Assets/_Game/Moves";
        const string KatanaGirl = "Assets/_Game/Prefabs/KatanaGirl.prefab";
        const string BossPrefab = "Assets/_Game/Prefabs/Boss Variant.prefab";

        [MenuItem("Tools/DS2/Wire Feel")]
        static void Wire()
        {
            AddFlashTo(KatanaGirl);
            AddFlashTo(BossPrefab);
            WireScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Fills in the player's move references and drops the progression loop into the arena.
        /// The Specials and the stance moves existed as assets but nothing on the player pointed
        /// at them, so half the moveset was unreachable no matter what the player pressed.
        /// </summary>
        [MenuItem("Tools/DS2/Wire Progression")]
        static void WireProgression()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KatanaGirl);
            if (prefab == null)
            {
                Debug.LogError("[DS2] Player prefab not found: " + KatanaGirl);
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(KatanaGirl);
            try
            {
                var combat = root.GetComponent<PlayerCombat>();
                if (combat == null)
                {
                    Debug.LogError("[DS2] No PlayerCombat on the player prefab.");
                    return;
                }

                var so = new SerializedObject(combat);
                (string field, MoveId id)[] slots =
                {
                    ("slash1", MoveId.Slash1), ("evade", MoveId.Evade),
                    ("quickShiftF", MoveId.QuickShiftF), ("quickShiftB", MoveId.QuickShiftB),
                    ("quickShiftL", MoveId.QuickShiftL), ("quickShiftR", MoveId.QuickShiftR),
                    ("parry", MoveId.Parry),
                    ("draw", MoveId.Draw), ("sheathe", MoveId.Sheathe),
                    ("skill1", MoveId.Skill1), ("skill2", MoveId.Skill2), ("skill3", MoveId.Skill3),
                };

                int filled = 0;
                foreach ((string field, MoveId id) in slots)
                {
                    SerializedProperty prop = so.FindProperty(field);
                    if (prop == null)
                    {
                        Debug.LogWarning("[DS2] PlayerCombat has no field '" + field + "'.");
                        continue;
                    }

                    var move = AssetDatabase.LoadAssetAtPath<MoveDefinition>(
                        MovesFolder + "/Move_" + id + ".asset");

                    if (move == null)
                    {
                        Debug.LogWarning("[DS2] Missing Move_" + id + " - run Build Move Assets.");
                        continue;
                    }

                    prop.objectReferenceValue = move;
                    filled++;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, KatanaGirl);
                Debug.Log("[DS2] PlayerCombat: " + filled + "/" + slots.Length + " move slots filled.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ArenaScene)
                scene = EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);

            EnsureSingleton<ProgressionManager>("ProgressionManager");
            EnsureSingleton<DeathScreen>("DeathScreen");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DS2] Progression wired.");
        }

        /// <summary>
        /// Exactly one of T in the scene, on its own empty GameObject.
        ///
        /// A duplicate of one of these is not a cosmetic problem. ProgressionManager takes
        /// DontDestroyOnLoad, so a stray copy on a scene object drags that object's whole subtree
        /// out of the scene - and before it was hardened, the loser of the Awake race had its
        /// entire GameObject destroyed. A copy that had landed on the arena root therefore
        /// deleted the floor and all four walls, about half the time, depending on which instance
        /// happened to wake first.
        ///
        /// Copies are removed as COMPONENTS, never by destroying the object they sit on, because
        /// that object may well own something that matters.
        /// </summary>
        static void EnsureSingleton<T>(string objectName) where T : Component
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            // Prefer a dedicated host: no children, nothing else of ours on it.
            T keep = null;
            foreach (T candidate in found)
            {
                bool dedicated = candidate.transform.childCount == 0 &&
                                 candidate.transform.parent == null;
                if (dedicated) { keep = candidate; break; }
            }

            foreach (T extra in found)
            {
                if (extra == keep) continue;
                Debug.LogWarning("[DS2] Removed a duplicate " + typeof(T).Name + " from '" +
                                 extra.gameObject.name + "'.", extra.gameObject);
                Object.DestroyImmediate(extra);
            }

            // Nothing dedicated survived the cull (or there was nothing to begin with), so the
            // component gets the clean home it should have had.
            if (keep == null)
            {
                new GameObject(objectName).AddComponent<T>();
                Debug.Log("[DS2] Created " + objectName + ".");
            }
        }

        static void AddFlashTo(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("[DS2] Prefab not found: " + prefabPath);
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (root.GetComponent<HitFlash>() == null)
                {
                    root.AddComponent<HitFlash>();
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log("[DS2] Added HitFlash to " + System.IO.Path.GetFileName(prefabPath));
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void WireScene()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            bool opened = false;

            if (scene.path != ArenaScene)
            {
                scene = EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);
                opened = true;
            }

            // The conductor. One per scene; HitFeedback disables duplicates itself.
            HitFeedback feedback = Object.FindAnyObjectByType<HitFeedback>();
            if (feedback == null)
            {
                var go = new GameObject("HitFeedback");
                feedback = go.AddComponent<HitFeedback>();
                Debug.Log("[DS2] Created HitFeedback in the arena.");
            }

            if (feedback.GetComponent<CinemachineImpulseSource>() == null)
            {
                feedback.gameObject.AddComponent<CinemachineImpulseSource>();
                Debug.Log("[DS2] Added CinemachineImpulseSource.");
            }

            // WITHOUT THIS THE CAMERA NEVER MOVES. Impulse sources broadcast into nothing unless
            // a listener is present, and the failure is completely silent.
            int listeners = 0;
            foreach (CinemachineCamera cam in Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            {
                if (cam.GetComponent<CinemachineImpulseListener>() == null)
                {
                    cam.gameObject.AddComponent<CinemachineImpulseListener>();
                    Debug.Log("[DS2] Added CinemachineImpulseListener to " + cam.name);
                }
                listeners++;
            }

            if (listeners == 0)
                Debug.LogWarning("[DS2] No CinemachineCamera in the scene - camera shake will not work.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[DS2] Feel wired" + (opened ? " (opened Arena)." : "."));
        }
    }
}
