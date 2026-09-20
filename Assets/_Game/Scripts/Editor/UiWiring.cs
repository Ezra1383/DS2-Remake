using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DS2.EditorTools
{
    /// <summary>
    /// Adds the pause menu and the controls panel to the open scene and wires their references.
    /// Re-runnable - finds what already exists rather than duplicating it.
    /// </summary>
    static class UiWiring
    {
        const string ObjectName = "GameUI";

        [MenuItem("Tools/DS2/Wire Pause and Controls")]
        static void Wire()
        {
            Scene scene = EditorSceneManager.GetActiveScene();

            var pause = Object.FindAnyObjectByType<PauseMenu>();
            var hint = Object.FindAnyObjectByType<ControlsHint>();

            GameObject host = null;
            if (pause != null) host = pause.gameObject;
            else if (hint != null) host = hint.gameObject;

            if (host == null)
            {
                // Its own childless root object, for the same reason ProgressionManager needs one.
                host = new GameObject(ObjectName);
                Debug.Log("[DS2] Created " + ObjectName + ".", host);
            }

            if (pause == null) pause = host.AddComponent<PauseMenu>();
            if (hint == null) hint = host.AddComponent<ControlsHint>();

            var gate = Object.FindAnyObjectByType<BossGate>();
            var death = Object.FindAnyObjectByType<DeathScreen>();

            Assign(new SerializedObject(pause), "deathScreen", death);
            Assign(new SerializedObject(hint), "gate", gate);

            EditorUtility.SetDirty(pause);
            EditorUtility.SetDirty(hint);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = host;

            Debug.Log($"[DS2] Pause menu + controls panel wired on '{host.name}'. " +
                      $"DeathScreen {(death != null ? "found" : "NOT FOUND")}, " +
                      $"BossGate {(gate != null ? "found" : "NOT FOUND")}.");
        }

        static void Assign(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[DS2] Missing field '" + field + "'.");
                return;
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
