using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ShadowsOfTheForsaken.Editor
{
    public static class ForsakenBuild
    {
        public const string Scene = "Assets/Scenes/ForsakenRuntimeCastle.unity";
        [MenuItem("Forsaken/Build runtime castle Windows player")]
        public static void BuildWindows()
        {
            if (Application.unityVersion != "6000.6.3f1")
                throw new InvalidOperationException("The Windows delivery requires pinned Unity 6000.6.3f1.");
            string output = Path.GetFullPath("Builds/RuntimeCastle-Windows/ShadowsOfTheForsaken.exe");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-forsakenBuildPath") output = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            Directory.CreateDirectory("artifacts/level-completion/build");
            var summary = new BuildSummaryEvidence
            {
                unity = Application.unityVersion, scene = Scene, output = output,
                result = report.summary.result.ToString(), bytes = report.summary.totalSize,
                errors = report.summary.totalErrors, warnings = report.summary.totalWarnings,
                seconds = report.summary.totalTime.TotalSeconds
            };
            File.WriteAllText("artifacts/level-completion/build/build-summary.json", JsonUtility.ToJson(summary, true));
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Player build failed: " + report.summary.result);
        }

        [Serializable]
        private sealed class BuildSummaryEvidence
        {
            public string unity, scene, output, result;
            public ulong bytes;
            public int errors, warnings;
            public double seconds;
        }
    }
}
