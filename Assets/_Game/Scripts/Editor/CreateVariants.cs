using System.IO;
using UnityEditor;
using UnityEngine;

namespace DS2.EditorTools
{
    /// <summary>
    /// Creates the Prefab Variants the game builds on.
    ///
    /// Gameplay components (CombatActor, PlayerLocomotion, PlayerCombat, CharacterController)
    /// must never live on the vendor prefabs: reimporting the Combat Girls pack would destroy
    /// them, the same way it reverts the HLSL patch documented in Docs/project-notes.md.
    /// A variant keeps our components in our file while the vendor prefab stays the base,
    /// so genuine upstream fixes still flow down.
    /// </summary>
    static class CreateVariants
    {
        const string VendorFolder = "Assets/CombatGirlsCharacterPack/Katana_Girl/Prefab";
        const string TargetFolder = "Assets/_Game/Prefabs";

        [MenuItem("Tools/DS2/Create Prefab Variants")]
        static void Run()
        {
            CreateVariant($"{VendorFolder}/KatanaGirl_FullBody.prefab", $"{TargetFolder}/KatanaGirl.prefab");
            CreateVariant($"{VendorFolder}/Humanoid_F_Katana.prefab",   $"{TargetFolder}/TrainingDummy.prefab");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void CreateVariant(string basePath, string variantPath)
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            if (basePrefab == null)
            {
                Debug.LogError($"[Variants] Base prefab not found: {basePath}");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) != null)
            {
                Debug.LogWarning($"[Variants] Already exists, skipping: {variantPath}");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(variantPath)));

            // SaveAsPrefabAsset on a *prefab instance* produces a Variant rather than a copy.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            GameObject variant = PrefabUtility.SaveAsPrefabAsset(instance, variantPath, out bool ok);
            Object.DestroyImmediate(instance);

            if (!ok || variant == null)
            {
                Debug.LogError($"[Variants] Failed to create {variantPath}");
                return;
            }

            // Confirm it really is a Variant and not a flattened copy — the whole point.
            PrefabAssetType type = PrefabUtility.GetPrefabAssetType(variant);
            if (type == PrefabAssetType.Variant)
                Debug.Log($"[Variants] Created Variant '{Path.GetFileName(variantPath)}' of '{Path.GetFileName(basePath)}'.", variant);
            else
                Debug.LogError($"[Variants] '{variantPath}' came out as {type}, not Variant. " +
                               "Delete it and create it by hand (drag the scene instance into _Game/Prefabs).", variant);
        }
    }
}
