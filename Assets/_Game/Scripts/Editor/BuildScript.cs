using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DS2.EditorTools
{
    /// <summary>
    /// Makes the standalone Windows build, from the menu or from the command line.
    ///
    /// Exists so the build can run in batch mode without the editor open:
    ///
    ///   Unity.exe -quit -batchmode -nographics -projectPath "&lt;project&gt;" \
    ///             -executeMethod DS2.EditorTools.BuildScript.BuildWindows64 \
    ///             -logFile "&lt;log&gt;"
    ///
    /// Scenes come from Build Settings rather than a hardcoded list, so whatever is ticked there
    /// is what ships - one less thing to keep in sync by hand.
    /// </summary>
    public static class BuildScript
    {
        const string OutputDir = "Build/Windows";
        const string ExeName = "TenDeathsToTheMirror.exe";

        [MenuItem("Tools/DS2/Build Windows")]
        public static void BuildWindows64()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Fail("No enabled scenes in Build Settings - nothing to build.");
                return;
            }

            Debug.Log("[DS2] Building " + scenes.Length + " scene(s): " + string.Join(", ", scenes));

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputDir + "/" + ExeName,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,

                // Release: no profiler, no deep logging, no script debugging. A development build
                // would also keep the Debug.Log calls cheap-but-present and ship a bigger player.
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[DS2] BUILD SUCCEEDED -> {summary.outputPath}\n" +
                          $"      size {summary.totalSize / (1024f * 1024f):0.0} MB, " +
                          $"time {summary.totalTime.TotalMinutes:0.0} min, " +
                          $"errors {summary.totalErrors}, warnings {summary.totalWarnings}");

                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }

            // Name the actual steps that failed - a bare "Build failed" in a batch log is useless.
            var problems = new List<string>();
            foreach (BuildStep step in report.steps)
                foreach (BuildStepMessage m in step.messages)
                    if (m.type is LogType.Error or LogType.Exception)
                        problems.Add($"  [{step.name}] {m.content}");

            Fail($"BUILD {summary.result} after {summary.totalTime.TotalMinutes:0.0} min, " +
                 $"{summary.totalErrors} error(s).\n" +
                 (problems.Count > 0 ? string.Join("\n", problems) : "  (no step messages captured)"));
        }

        static void Fail(string message)
        {
            Debug.LogError("[DS2] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
