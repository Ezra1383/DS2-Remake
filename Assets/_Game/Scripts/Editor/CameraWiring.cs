using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DS2.EditorTools
{
    /// <summary>
    /// Stops the camera passing through walls, by adding and tuning a CinemachineDeoccluder.
    ///
    /// The arena had no deoccluder at all - which was invisible while the map was a floor and four
    /// boundary cubes you never backed into, and became obvious the moment it was a dungeon with
    /// pillars and corridors. The orbital follow will happily place the camera inside geometry;
    /// nothing was asking it not to.
    ///
    /// THE LAYER MASK IS THE PART THAT MATTERS. It collides against Default only. Enemy (3) and
    /// Player (6) are excluded deliberately: with the characters in the mask the camera treats the
    /// fighters themselves as obstacles and shoves itself forward every time the boss crosses in
    /// front of you - in a mirror match, constantly. Walls and props are what should push it.
    /// </summary>
    static class CameraWiring
    {
        /// <summary>Default only. NOT Enemy, NOT Player - see the class note.</summary>
        const int ObstacleLayers = 1 << 0;

        [MenuItem("Tools/DS2/Wire Camera Deoccluder")]
        static void Wire()
        {
            Scene scene = EditorSceneManager.GetActiveScene();

            var cam = Object.FindAnyObjectByType<CinemachineCamera>();
            if (cam == null)
            {
                Debug.LogError("[DS2] No CinemachineCamera in the open scene.");
                return;
            }

            var deoccluder = cam.GetComponent<CinemachineDeoccluder>();
            bool created = deoccluder == null;
            if (created) deoccluder = cam.gameObject.AddComponent<CinemachineDeoccluder>();

            deoccluder.CollideAgainst = ObstacleLayers;
            deoccluder.TransparentLayers = 0;

            // Never let it end up inside her head when it runs out of room.
            deoccluder.MinimumDistanceFromTarget = 0.4f;

            var avoid = deoccluder.AvoidObstacles;
            avoid.Enabled = true;
            avoid.DistanceLimit = 0f;          // no cap - pull in as far as needed

            // A pillar edge clipping the view for two frames should not yank the camera. This is
            // the difference between "solid" and "twitchy" in a scene this cluttered.
            avoid.MinimumOcclusionTime = 0.15f;

            // The camera is a sphere, not a point: without a radius the near plane still slices
            // into the wall the moment the centre clears it.
            avoid.CameraRadius = 0.25f;

            // PreserveCameraHeight rather than PullCameraForward. Pulling straight forward in a
            // corridor drags the camera down to floor level and you end up looking at her shins;
            // this keeps the framing while it closes the distance.
            avoid.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PreserveCameraHeight;
            avoid.MaximumEffort = 4;

            avoid.SmoothingTime = 0.3f;

            // ASYMMETRIC ON PURPOSE, and this is the setting people get wrong. Pulling IN has to
            // be nearly instant or you see through the wall for a moment, which is the whole bug.
            // Easing back OUT should be slow, or the camera pumps in and out along every pillar.
            avoid.DampingWhenOccluded = 0.1f;
            avoid.Damping = 0.5f;

            deoccluder.AvoidObstacles = avoid;

            EditorUtility.SetDirty(deoccluder);
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[DS2] Camera deoccluder {(created ? "added" : "retuned")} on '{cam.name}': " +
                      "collides against Default only, radius 0.25, pull-in 0.1s / return 0.5s.");
        }
    }
}
