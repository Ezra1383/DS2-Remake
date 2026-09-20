using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DS2.EditorTools
{
    /// <summary>
    /// Creates the music object, picks the two tracks out of the Music folder by name, points it
    /// at the BossGate, and fixes the import settings. Re-runnable.
    ///
    /// MUSIC IMPORTS THE OPPOSITE WAY TO THE SFX. AudioWiring deliberately sets every impact and
    /// swing to DecompressOnLoad + PCM, because those have to arrive inside ~12 ms of the hit and
    /// are a few KB each. Doing that to a pair of 20 MB tracks would hold ~41 MB of decoded audio
    /// in memory and put the raw WAVs in the build. Streaming + Vorbis instead: a few MB on disk,
    /// almost nothing resident, and the start latency that would ruin a sword hit is irrelevant
    /// for something that plays for four minutes.
    ///
    /// Stereo is kept (forceToMono stays off) - the SFX are mono because they are positional, but
    /// collapsing a score to mono throws away the width it was mixed with.
    /// </summary>
    static class MusicWiring
    {
        const string ObjectName = "Music";
        const string MusicFolder = "Assets/_Game/Audio/Music";

        /// <summary>Matched case-insensitively against the file name, first hit wins.</summary>
        static readonly string[] ExplorationKeys = { "eclipsed", "desolation", "explore", "ambient" };
        static readonly string[] CombatKeys = { "dread", "march", "combat", "battle", "boss" };

        [MenuItem("Tools/DS2/Wire Music")]
        static void Wire()
        {
            Scene scene = EditorSceneManager.GetActiveScene();

            if (!AssetDatabase.IsValidFolder(MusicFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Game/Audio", "Music");
                Debug.LogWarning("[DS2] Created " + MusicFolder + " - it was empty, so no tracks " +
                                 "were assigned.");
            }

            AudioClip exploration = FindClip(ExplorationKeys);
            AudioClip combat = FindClip(CombatKeys);

            if (exploration != null) Import(exploration);
            if (combat != null) Import(combat);

            var player = Object.FindAnyObjectByType<MusicPlayer>();
            if (player == null)
            {
                // Its own childless root object, same reason ProgressionManager needs one.
                var go = new GameObject(ObjectName);
                player = go.AddComponent<MusicPlayer>();
                Debug.Log("[DS2] Created the Music object.", go);
            }

            var so = new SerializedObject(player);
            Assign(so, "explorationTrack", exploration);
            Assign(so, "combatTrack", combat);
            Assign(so, "gate", Object.FindAnyObjectByType<BossGate>());
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(player);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = player.gameObject;

            Debug.Log($"[DS2] Music wired. Exploration: {Name(exploration)}. " +
                      $"Combat: {Name(combat)}. Both set to Streaming + Vorbis.");
        }

        static string Name(AudioClip c) => c != null ? c.name : "<none found>";

        static AudioClip FindClip(string[] keys)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { MusicFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

                foreach (string key in keys)
                    if (file.Contains(key))
                        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }

            return null;
        }

        static void Import(AudioClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            if (AssetImporter.GetAtPath(path) is not AudioImporter ai) return;

            ai.forceToMono = false;

            // Decoded on the fly from disk rather than held in RAM. The trade is a little CPU and
            // a start delay measured in tens of milliseconds, which nothing here cares about.
            ai.loadInBackground = true;

            AudioImporterSampleSettings s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            s.preloadAudioData = false;
            ai.defaultSampleSettings = s;

            ai.SaveAndReimport();
        }

        static void Assign(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[DS2] MusicPlayer has no field '" + field + "'.");
                return;
            }

            prop.objectReferenceValue = value;
        }
    }
}
