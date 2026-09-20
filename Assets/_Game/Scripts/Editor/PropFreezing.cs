using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DS2.EditorTools
{
    /// <summary>
    /// Makes the training-ground props immovable scenery: no gravity, no pushing, no walking
    /// through them. They are a memory she cannot change, so they should not respond to her.
    ///
    /// TWO SEPARATE FAULTS, and the props split cleanly between them:
    ///
    /// - The Low-Poly pack props (backpack, smartphone, ladder, crown, hat, dice, piggy bank,
    ///   headphones, smiley) ship WITH a Rigidbody and a collider. Those fall under gravity and
    ///   get shoved around by anything that touches them.
    /// - The Furniture pack props (gas stoves, beds, sofas, cushions) ship with NEITHER. Those
    ///   just hang in the air and she walks straight through them.
    ///
    /// So a fix that only froze rigidbodies would leave half of them intangible, and one that only
    /// added colliders would leave the other half falling over.
    ///
    /// WHY THE RIGIDBODY IS REMOVED RATHER THAN SET KINEMATIC. A collider with no Rigidbody is a
    /// STATIC collider in Unity: it cannot be moved by physics, by a CharacterController pushing
    /// on it, or by an errant force, and it costs the physics engine the least of any option. A
    /// kinematic body is still a body - it keeps a simulation island, and any script that grabs it
    /// can still move it. "Static memory" is the literal requirement here.
    ///
    /// WHY THIS EDITS SCENE INSTANCES AND NOT THE SOURCE PREFABS. Those prefabs live under
    /// Assets/Used Assets in vendor packs, and this project has already learned what happens to
    /// hand edits inside vendor folders - a reimport silently reverts them. Overrides on the
    /// scene instances belong to the scene, which is ours.
    /// </summary>
    static class PropFreezing
    {
        /// <summary>
        /// Candidate names for the props root. "Promps" is the spelling actually in the scene;
        /// it is matched rather than corrected, because renaming someone's scene object out from
        /// under them is not this tool's job.
        /// </summary>
        static readonly string[] RootNames =
        {
            "TrainingGroundPromps", "TrainingGroundProps", "Training Ground Props",
        };

        [MenuItem("Tools/DS2/Freeze Training Ground Props")]
        static void Freeze()
        {
            GameObject root = FindRoot();

            Transform[] targets;
            if (root != null)
            {
                targets = root.GetComponentsInChildren<Transform>(true);
            }
            else if (Selection.transforms.Length > 0)
            {
                // Fallback so the tool is still useful if the props get reorganised or renamed.
                targets = Selection.transforms;
                Debug.Log("[DS2] No props root found by name - freezing the current selection.");
            }
            else
            {
                Debug.LogError("[DS2] Could not find a props root (looked for " +
                               string.Join(", ", RootNames) + ") and nothing is selected.");
                return;
            }

            int bodies = 0, colliders = 0, triggers = 0, marked = 0;

            foreach (Transform t in targets)
            {
                if (t == null) continue;
                GameObject go = t.gameObject;

                bodies += StripBodies(go);
                colliders += EnsureCollider(go);
                triggers += SolidifyColliders(go);
                if (MarkStatic(go)) marked++;

                EditorUtility.SetDirty(go);
            }

            Scene scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[DS2] Props frozen across {targets.Length} object(s): " +
                      $"{bodies} rigidbody(s) removed, {colliders} collider(s) added, " +
                      $"{triggers} trigger(s) made solid, {marked} marked static.");
        }

        static GameObject FindRoot()
        {
            foreach (string name in RootNames)
            {
                GameObject found = GameObject.Find(name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Rigidbody and anything else that would let the prop be shoved. Joints go too - a joint
        /// with no body is meaningless and would log warnings every load.
        /// </summary>
        static int StripBodies(GameObject go)
        {
            int n = 0;

            foreach (Joint j in go.GetComponents<Joint>())
            {
                Object.DestroyImmediate(j, true);
                n++;
            }

            foreach (Rigidbody rb in go.GetComponents<Rigidbody>())
            {
                Object.DestroyImmediate(rb, true);
                n++;
            }

            return n;
        }

        /// <summary>
        /// Gives a prop that renders but has no collider something to be solid with.
        ///
        /// Non-convex MeshCollider on purpose: a convex hull would round a bed or a stove out to
        /// its silhouette and let her stand inside the gaps. Non-convex is exact, and it is only
        /// disallowed for MOVING colliders - which, having just stripped the Rigidbody, these
        /// permanently are not.
        /// </summary>
        static int EnsureCollider(GameObject go)
        {
            if (go.GetComponent<Collider>() != null) return 0;

            var filter = go.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return 0;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = filter.sharedMesh;
            mc.convex = false;

            return 1;
        }

        /// <summary>
        /// A trigger does not stop a CharacterController - she would walk through it exactly as if
        /// there were no collider at all, which is the bug this tool exists to remove.
        /// </summary>
        static int SolidifyColliders(GameObject go)
        {
            int n = 0;

            foreach (Collider c in go.GetComponents<Collider>())
            {
                if (!c.isTrigger) continue;
                c.isTrigger = false;
                n++;
            }

            return n;
        }

        /// <summary>
        /// Static editor flags, for the batching more than anything - 34 props that provably never
        /// move are free to merge into shared draw calls.
        ///
        /// ContributeGI is deliberately NOT set. The arena has no baked lighting and no light
        /// probes; flagging these for GI would only matter if someone baked later, and would then
        /// silently change how they light. Everything here is a rendering optimisation that cannot
        /// alter the look.
        /// </summary>
        static bool MarkStatic(GameObject go)
        {
            const StaticEditorFlags Flags = StaticEditorFlags.BatchingStatic |
                                            StaticEditorFlags.OccluderStatic |
                                            StaticEditorFlags.OccludeeStatic |
                                            StaticEditorFlags.ReflectionProbeStatic;

            if (GameObjectUtility.GetStaticEditorFlags(go) == Flags) return false;

            GameObjectUtility.SetStaticEditorFlags(go, Flags);
            return true;
        }
    }
}
