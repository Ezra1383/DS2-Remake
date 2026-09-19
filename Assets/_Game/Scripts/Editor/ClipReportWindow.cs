using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DS2.EditorTools
{
    /// <summary>
    /// Measures every AnimationClip under a folder and writes the results to CSV.
    ///
    /// The frame data in Docs/combat-design.html is a table of targets, not measurements.
    /// This window produces the measurements. The SpeedMultiplier column is the number to
    /// copy onto each MoveDefinition asset; the RootXZNet column tells you how far a move
    /// actually travels, which decides how the boss's range bands are tuned later.
    ///
    /// Root motion figures come from the clip's RootT curves and are in the avatar's
    /// normalised units. Treat them as relative measures for comparing moves against each
    /// other, not as exact metres.
    /// </summary>
    public class ClipReportWindow : EditorWindow
    {
        static string DefaultSearchFolder => VendorPaths.AnimFolder;
        const string DefaultOutputPath   = "Docs/clip-report.csv";

        /// <summary>How long each move wants to be, per Docs/combat-design.html.</summary>
        static readonly Dictionary<string, float> TargetDurations = new Dictionary<string, float>
        {
            { "Attack1",      0.60f },
            { "Attack2",      0.62f },
            { "Attack3",      0.90f },
            { "Evade",        0.60f },
            { "Quickshift_F", 0.44f },
            { "Quickshift_B", 0.44f },
            { "Quickshift_L", 0.44f },
            { "Quickshift_R", 0.44f },
            { "Take",         0.45f },
            { "Put",          0.45f },
            { "Sp_Skill1",    0.97f },
            { "Sp_Skill2",    1.16f },
            { "Sp_Skill3",    1.45f },
        };

        /// <summary>Above this, speeding the clip up reads as fast-forwarded video.</summary>
        const float MultiplierWarnThreshold = 2.5f;

        const int RootSampleCount = 120;

        string searchFolder = DefaultSearchFolder;
        string outputPath   = DefaultOutputPath;
        bool   includeEmotionClips;
        Vector2 scroll;
        string lastResult;

        [MenuItem("Tools/DS2/Clip Report")]
        static void Open()
        {
            GetWindow<ClipReportWindow>(true, "Clip Report", true).minSize = new Vector2(460f, 260f);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Measure animation clips", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Writes real clip durations, root-motion displacement and baked animation events " +
                "to a CSV. Run this before authoring any MoveDefinition assets.",
                MessageType.Info);

            EditorGUILayout.Space();
            searchFolder = EditorGUILayout.TextField("Search folder", searchFolder);
            outputPath   = EditorGUILayout.TextField("Output CSV", outputPath);
            includeEmotionClips = EditorGUILayout.Toggle("Include CBG_ emotion clips", includeEmotionClips);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!AssetDatabase.IsValidFolder(searchFolder)))
            {
                if (GUILayout.Button("Generate report", GUILayout.Height(28f)))
                    Generate();
            }

            if (!AssetDatabase.IsValidFolder(searchFolder))
                EditorGUILayout.HelpBox($"'{searchFolder}' is not a folder in this project.", MessageType.Error);

            if (!string.IsNullOrEmpty(lastResult))
            {
                EditorGUILayout.Space();
                scroll = EditorGUILayout.BeginScrollView(scroll);
                EditorGUILayout.TextArea(lastResult, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        void Generate()
        {
            List<AnimationClip> clips = FindClips(searchFolder, includeEmotionClips);
            if (clips.Count == 0)
            {
                lastResult = "No clips found.";
                Debug.LogWarning($"[ClipReport] No AnimationClips under '{searchFolder}'.");
                return;
            }

            var csv = new StringBuilder();
            csv.AppendLine("Clip,SourceFile,Length,FrameRate,Frames,Looping,HasRootCurves," +
                           "RootXZNet,RootXZPath,RootYNet,TargetLength,SpeedMultiplier,Flag,EventCount,Events");

            var warnings = new List<string>();
            var summary  = new StringBuilder();
            summary.AppendLine($"{clips.Count} clips measured.\n");

            foreach (AnimationClip clip in clips.OrderBy(c => c.name))
            {
                string sourceFile = Path.GetFileName(AssetDatabase.GetAssetPath(clip));
                float  length     = clip.length;
                float  frameRate  = clip.frameRate;
                int    frames     = Mathf.RoundToInt(length * frameRate);

                bool hasRoot = TryMeasureRootMotion(clip, out Vector3 net, out float pathXZ);
                float xzNet  = new Vector2(net.x, net.z).magnitude;

                string target     = "";
                string multiplier = "";
                string flag       = "";

                if (TargetDurations.TryGetValue(clip.name, out float targetLength) && length > 0f)
                {
                    float m = length / targetLength;
                    target     = F(targetLength);
                    multiplier = F(m);

                    if (m > MultiplierWarnThreshold)
                    {
                        flag = "RAISE TARGET";
                        warnings.Add($"  {clip.name,-14} {F(length)}s -> {F(targetLength)}s needs {F(m)}x " +
                                     "â€” too fast, raise the design target instead");
                    }
                    else if (m < 0.75f)
                    {
                        flag = "SLOWED";
                    }
                }

                AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
                string eventDetail = string.Join(" | ", events.Select(
                    e => $"{F(e.time)}s {e.functionName}({e.stringParameter})"));

                csv.AppendLine(string.Join(",",
                    Csv(clip.name),
                    Csv(sourceFile),
                    F(length),
                    F(frameRate),
                    frames.ToString(CultureInfo.InvariantCulture),
                    clip.isLooping ? "yes" : "no",
                    hasRoot ? "yes" : "no",
                    F(xzNet),
                    F(pathXZ),
                    F(net.y),
                    target,
                    multiplier,
                    Csv(flag),
                    events.Length.ToString(CultureInfo.InvariantCulture),
                    Csv(eventDetail)));

                summary.AppendLine($"{clip.name,-16} {F(length),6}s  {frames,4}f  " +
                                   $"xz {F(xzNet),6}  events {events.Length}" +
                                   (string.IsNullOrEmpty(multiplier) ? "" : $"  x{multiplier}") +
                                   (string.IsNullOrEmpty(flag) ? "" : $"  <- {flag}"));
            }

            string fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, csv.ToString(), new UTF8Encoding(false));

            if (warnings.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine($"{warnings.Count} clip(s) need a speed multiplier above {MultiplierWarnThreshold:0.0}x:");
                foreach (string w in warnings) summary.AppendLine(w);
                summary.AppendLine();
                summary.AppendLine("Raising the design target is the correct fix. Forcing the multiplier");
                summary.AppendLine("makes the animation read as sped-up video.");
            }

            summary.AppendLine();
            summary.AppendLine($"Written to {fullPath}");

            lastResult = summary.ToString();
            Debug.Log($"[ClipReport] {clips.Count} clips -> {fullPath}");
            AssetDatabase.Refresh();
        }

        static List<AnimationClip> FindClips(string folder, bool includeEmotion)
        {
            var found = new List<AnimationClip>();
            var seen  = new HashSet<AnimationClip>();

            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (!includeEmotion && Path.GetFileName(path).StartsWith("CBG_"))
                    continue;

                // Clips inside an FBX are sub-assets, so load every representation rather
                // than the main asset, which would be the imported GameObject.
                foreach (Object o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                    Collect(o as AnimationClip);

                Collect(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
            }

            return found;

            void Collect(AnimationClip clip)
            {
                if (clip == null) return;
                if (clip.name.StartsWith("__preview__")) return;
                if (!seen.Add(clip)) return;
                found.Add(clip);
            }
        }

        /// <summary>
        /// Reads the clip's root translation curves and returns the net displacement plus the
        /// total distance travelled in XZ. Humanoid clips expose these as RootT.*; generic
        /// clips as m_LocalPosition.* on the root transform.
        /// </summary>
        static bool TryMeasureRootMotion(AnimationClip clip, out Vector3 net, out float pathXZ)
        {
            net    = Vector3.zero;
            pathXZ = 0f;

            AnimationCurve x = null, y = null, z = null;

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                bool isRootProperty =
                    binding.propertyName.StartsWith("RootT.") ||
                    (string.IsNullOrEmpty(binding.path) && binding.propertyName.StartsWith("m_LocalPosition."));

                if (!isRootProperty) continue;

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (binding.propertyName.EndsWith(".x")) x = curve;
                else if (binding.propertyName.EndsWith(".y")) y = curve;
                else if (binding.propertyName.EndsWith(".z")) z = curve;
            }

            if (x == null && z == null) return false;

            Vector3 Sample(float t) => new Vector3(
                x?.Evaluate(t) ?? 0f,
                y?.Evaluate(t) ?? 0f,
                z?.Evaluate(t) ?? 0f);

            Vector3 start = Sample(0f);
            Vector3 previous = start;

            for (int i = 1; i <= RootSampleCount; i++)
            {
                Vector3 current = Sample(clip.length * i / RootSampleCount);
                pathXZ += new Vector2(current.x - previous.x, current.z - previous.z).magnitude;
                previous = current;
            }

            net = previous - start;
            return true;
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
