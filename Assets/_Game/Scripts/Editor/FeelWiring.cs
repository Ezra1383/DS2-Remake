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
