using UnityEditor;
using UnityEngine;

namespace DS2.EditorTools
{
    /// <summary>
    /// Finds the Combat Girls pack wherever it currently lives, instead of every tool hardcoding
    /// "Assets/CombatGirlsCharacterPack/...".
    ///
    /// WHY THIS EXISTS: the pack was moved under "Assets/Used Assets/" while dressing the map, and
    /// that silently broke four tools at once - Build Move Assets, Rebuild Boss, the clip report
    /// and the corpse baker - none of which fail until the day someone re-runs them, which the
    /// docs tell every fresh clone to do first. A path that is derived cannot rot that way.
    ///
    /// Resolved from K_Die.fbx because it is unique to this pack and sits at a known depth. The
    /// result is cached per domain reload; call Forget() if the pack moves mid-session.
    /// </summary>
    static class VendorPaths
    {
        const string Marker = "/Katana_Girl/Animations/";

        static string cachedRoot;

        /// <summary>The pack folder, e.g. "Assets/Used Assets/CombatGirlsCharacterPack".</summary>
        public static string PackRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(cachedRoot) && AssetDatabase.IsValidFolder(cachedRoot))
                    return cachedRoot;

                foreach (string guid in AssetDatabase.FindAssets("K_Die"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    int i = path.IndexOf(Marker, System.StringComparison.Ordinal);
                    if (i <= 0) continue;

                    cachedRoot = path.Substring(0, i);
                    return cachedRoot;
                }

                Debug.LogError("[DS2] Could not locate the Combat Girls pack (searched for " +
                               "K_Die.fbx). Has it been deleted rather than moved?");
                return null;
            }
        }

        public static string AnimFolder => Combine(PackRoot, "Katana_Girl/Animations");
        public static string VendorPrefabFolder => Combine(PackRoot, "Katana_Girl/Prefab");
        public static string DieFbx => Combine(PackRoot, "Katana_Girl/Animations/Normal/K_Die.fbx");

        public static void Forget() => cachedRoot = null;

        static string Combine(string root, string tail) =>
            string.IsNullOrEmpty(root) ? null : root + "/" + tail;
    }
}
