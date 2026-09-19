
using System.IO;
using UnityEditor;
using Unity.Rendering.Toon;

namespace UnityEditor.Rendering.Toon {

internal static class ToonEditorConstants {

    internal const int CUR_MATERIAL_VERSION = (int) ToonMaterialVersion.Initial;
    
    internal static readonly string PACKAGE_PATH = ResolvePackagePath();
    
    
    internal static readonly string TOON_SHADER_PATH = 
        Path.Combine(PACKAGE_PATH,"Runtime/Shaders/UnityToon.shader").Replace('\\','/');
    internal static readonly string TOON_TESS_SHADER_PATH = 
        Path.Combine(PACKAGE_PATH,"Runtime/Shaders/UnityToonTessellation.shader").Replace('\\','/');
    
    static string ResolvePackagePath() {
        string[] candidatePaths = {
            "Assets/Unity Chan Toon Shader - SDF - Unity6_URP",
            Path.Combine("Assets", ToonConstants.PACKAGE_NAME).Replace('\\','/'),
            Path.Combine("Packages", ToonConstants.PACKAGE_NAME).Replace('\\','/')
        };

        foreach (string candidatePath in candidatePaths) {
            if (AssetDatabase.IsValidFolder(candidatePath)) {
                return candidatePath;
            }
        }

        return candidatePaths[0];
    }

}

} //end namespace
