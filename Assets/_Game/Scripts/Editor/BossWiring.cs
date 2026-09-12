using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DS2.EditorTools
{
    /// <summary>
    /// Swaps the boss prefab from the old BossAI over to the layered BossBrain and fills in its
    /// references.
    ///
    /// Done in code rather than by hand because the reference wiring is the part that silently
    /// rots: a phrase asset regenerated under a new name, a moveset moved, and the boss quietly
    /// stands still in play with no error. Re-running this is always safe and always correct.
    /// </summary>
    static class BossWiring
    {
        const string BossPrefab = "Assets/_Game/Prefabs/Boss Variant.prefab";
        const string PhraseFolder = "Assets/_Game/Moves/Boss/Phrases";
        const string MovesetAsset = "Assets/_Game/Moves/Boss/BossMoveset.asset";
        const string PunishPhrase = "Phrase_Punish";

        [MenuItem("Tools/DS2/Wire Boss Brain")]
        internal static void Wire()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefab);
            if (prefab == null)
            {
                Debug.LogError("[DS2] Boss prefab not found at " + BossPrefab);
                return;
            }

            List<BossPhrase> phrases = LoadPhrases(out BossPhrase punish);
            if (phrases.Count == 0)
            {
                Debug.LogError("[DS2] No phrases in " + PhraseFolder +
                               " - run Tools > DS2 > Build Boss Phrases first.");
                return;
            }

            var moveset = AssetDatabase.LoadAssetAtPath<BossMoveset>(MovesetAsset);
            if (moveset == null)
                Debug.LogWarning("[DS2] No BossMoveset at " + MovesetAsset +
                                 " - her reactive dodges will not have moves to pick from.");

            GameObject root = PrefabUtility.LoadPrefabContents(BossPrefab);
            try
            {
                // The old brain has to go, not just be disabled: two things writing
                // locomotion.MoveDirection in the same frame is the fight nobody wins.
                var legacy = root.GetComponent<BossAI>();
                if (legacy != null)
                {
                    Object.DestroyImmediate(legacy, true);
                    Debug.Log("[DS2] Removed BossAI from the boss prefab.");
                }

                Require<BossPerception>(root);
                BossBrain brain = Require<BossBrain>(root);
                Require<BossDebugHUD>(root);

                var so = new SerializedObject(brain);

                SerializedProperty list = so.FindProperty("phrases");
                list.arraySize = phrases.Count;
                for (int i = 0; i < phrases.Count; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = phrases[i];

                so.FindProperty("punishPhrase").objectReferenceValue = punish;
                so.FindProperty("moveset").objectReferenceValue = moveset;

                ApplyTuning(so);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, BossPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[DS2] Boss wired: BossBrain + BossPerception + BossDebugHUD, " +
                      phrases.Count + " phrases" +
                      (punish != null ? ", punish phrase set" : ", NO punish phrase found") +
                      ", tuning applied.");
        }

        /// <summary>
        /// Her pacing and reaction numbers, in one reviewable block.
        ///
        /// These are [SerializeField]s, which means the values that actually run are the ones
        /// BAKED ON THE PREFAB - editing the default in BossBrain.cs does nothing to a component
        /// that already exists. So aggression lives here, in source control, applied by a menu
        /// item, exactly like the move Specs table. Same contract as Build Move Assets: re-running
        /// this OVERWRITES hand edits, so anything you want to keep belongs in this table.
        ///
        /// Raised 12 Sep for roughly 5% more pressure. maxAttacksBeforeGap stays at 3 - that is
        /// the design's promise to the player and not an aggression dial.
        /// </summary>
        static readonly (string field, float value)[] Tuning =
        {
            ("pressureBudget",           4.0f),
            ("neutralGap",               0.85f),   // was 1.0
            ("pressureRefundPerSecond",  2.2f),    // was 1.5 - fewer budget-triggered gaps
            ("preferredRange",           2.45f),   // was 2.6 - less travel before she can commit
            ("rangeTolerance",           0.35f),
            ("runBeyond",                4.5f),
            ("patience",                 3.2f),    // was 4.0
            ("dodgeChance",              0.25f),   // was 0.30 - each evade is dead time
            ("dodgeRange",               3.5f),
            ("dodgeCooldown",            2.5f),
            ("punishChance",             0.35f),
            ("punishCooldown",           3.0f),
        };

        static readonly (string field, int value)[] TuningInt =
        {
            // RULE 1. Not a tuning dial - the promise that makes a fight with no block button fair.
            ("maxAttacksBeforeGap", 3),
        };

        static void ApplyTuning(SerializedObject so)
        {
            foreach ((string field, float value) in Tuning)
            {
                SerializedProperty prop = so.FindProperty(field);
                if (prop == null)
                {
                    Debug.LogWarning("[DS2] BossBrain has no field '" + field +
                                     "' - the tuning table is stale.");
                    continue;
                }
                prop.floatValue = value;
            }

            foreach ((string field, int value) in TuningInt)
            {
                SerializedProperty prop = so.FindProperty(field);
                if (prop == null)
                {
                    Debug.LogWarning("[DS2] BossBrain has no field '" + field + "'.");
                    continue;
                }
                prop.intValue = value;
            }
        }

        /// <summary>
        /// Every phrase in the folder goes on the brain, including the weight-0 punish phrase -
        /// weight 0 keeps it out of the random roll, it does not need to be kept out of the list.
        /// </summary>
        static List<BossPhrase> LoadPhrases(out BossPhrase punish)
        {
            punish = null;
            var found = new List<BossPhrase>();

            if (!Directory.Exists(Path.GetFullPath(PhraseFolder))) return found;

            foreach (string guid in AssetDatabase.FindAssets("t:BossPhrase", new[] { PhraseFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var phrase = AssetDatabase.LoadAssetAtPath<BossPhrase>(path);
                if (phrase == null) continue;

                found.Add(phrase);
                if (Path.GetFileNameWithoutExtension(path) == PunishPhrase) punish = phrase;
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }

        static T Require<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            if (existing != null) return existing;

            Debug.Log("[DS2] Added " + typeof(T).Name + " to the boss prefab.");
            return go.AddComponent<T>();
        }
    }
}
