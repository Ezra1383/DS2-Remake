using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DS2.EditorTools
{
    /// <summary>
    /// Drops a boss gate volume into the ACTIVE scene and wires it. Re-runnable: if a gate already
    /// exists it is re-wired and re-selected rather than duplicated, so running this after moving
    /// the arena around costs nothing.
    ///
    /// Works on whichever scene is open rather than forcing Arena.unity, because the arena is
    /// being rebuilt by hand and the gate belongs to whatever scene that ends up in.
    /// </summary>
    static class BossGateWiring
    {
        const string GateObject = "BossGate";

        [MenuItem("Tools/DS2/Wire Boss Gate")]
        static void Wire()
        {
            Scene scene = EditorSceneManager.GetActiveScene();

            var brain = Object.FindAnyObjectByType<BossBrain>();
            if (brain == null)
            {
                Debug.LogError("[DS2] No BossBrain in the open scene - run Rebuild Boss first.");
                return;
            }

            var gate = Object.FindAnyObjectByType<BossGate>();
            if (gate == null)
            {
                var go = new GameObject(GateObject);

                // Centred on the boss, because that is where the fight is. Sized as a starting
                // point only - scale it to the Main Arena by hand, which is the one thing this
                // tool genuinely cannot guess.
                go.transform.position = brain.transform.position;

                BoxCollider box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(20f, 10f, 20f);
                box.center = new Vector3(0f, 4f, 0f);

                gate = go.AddComponent<BossGate>();
                Debug.Log("[DS2] Created BossGate at the boss. SCALE ITS BOX to cover the arena.", go);
            }

            var so = new SerializedObject(gate);
            Assign(so, "arenaVolume", gate.GetComponent<Collider>());
            Assign(so, "brain", brain);
            Assign(so, "bossActor", brain.GetComponent<CombatActor>());

            var player = Object.FindAnyObjectByType<PlayerCombat>();
            Assign(so, "player", player != null ? player.GetComponent<CombatActor>() : null);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(gate);
            EditorSceneManager.MarkSceneDirty(scene);

            Selection.activeGameObject = gate.gameObject;
            SceneView.FrameLastActiveSceneView();

            Debug.Log("[DS2] Boss gate wired. She holds the Stun pose until the player is inside " +
                      "the volume.", gate);
        }

        static void Assign(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[DS2] BossGate has no field '" + field + "'.");
                return;
            }

            prop.objectReferenceValue = value;
        }
    }
}
