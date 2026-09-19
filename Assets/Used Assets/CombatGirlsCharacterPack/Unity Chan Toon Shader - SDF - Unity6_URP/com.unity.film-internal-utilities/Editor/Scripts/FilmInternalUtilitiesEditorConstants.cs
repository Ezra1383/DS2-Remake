using System.IO;
using UnityEditor;

namespace Unity.FilmInternalUtilities.Editor {

internal static class FilmInternalUtilitiesEditorConstants  {
    internal const string PACKAGE_NAME = "com.unity.film-internal-utilities";
    public static readonly string PACKAGE_PATH = ResolvePackagePath();

    static string ResolvePackagePath() {
        string[] candidatePaths = {
            "Assets/Unity Chan Toon Shader - SDF - Unity6_URP/com.unity.film-internal-utilities",
            Path.Combine("Assets", PACKAGE_NAME).Replace('\\','/'),
            Path.Combine("Packages", PACKAGE_NAME).Replace('\\','/')
        };

        foreach (string candidatePath in candidatePaths) {
            if (AssetDatabase.IsValidFolder(candidatePath)) {
                return candidatePath;
            }
        }

        return candidatePaths[0];
    }

}

}
