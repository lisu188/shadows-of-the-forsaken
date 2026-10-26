using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;

[assembly: TestPlayerBuildModifier(typeof(ShadowsOfTheForsaken.Tests.EditMode.StandaloneRouteTestBuild))]
[assembly: PostBuildCleanup(typeof(ShadowsOfTheForsaken.Tests.EditMode.StandaloneRouteTestBuild))]

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    // The public UTF 1.8 split-build/run pattern; it never modifies ordinary builds.
    public sealed class StandaloneRouteTestBuild : ITestPlayerBuildModifier, IPostBuildCleanup, IPostprocessBuildWithReport
    {
        private const string PrimaryScene = "Assets/Scenes/ForsakenCastle.unity";
        private static string requestedOutput;
        private static string[] builtScenes;
        private static BuildReport completedReport;
        private static string callbackError;
        private static bool cleanupScheduled;
        public int callbackOrder => int.MaxValue - 1;

        public BuildPlayerOptions ModifyOptions(BuildPlayerOptions options)
        {
            string requested = Environment.GetEnvironmentVariable("SHADOWS_ROUTE_PLAYER_BUILD");
            if (string.IsNullOrWhiteSpace(requested) || options.target != BuildTarget.StandaloneWindows64 ||
                (options.options & BuildOptions.IncludeTestAssemblies) == 0) return options;
            string[] command = Environment.GetCommandLineArgs();
            int filter = Array.IndexOf(command, "-testFilter");
            if (!command.Contains("-runTests") || !command.Contains("-doNotReportTestResultsBackToEditor") || filter < 0 || filter + 1 >= command.Length ||
                (command[filter + 1] != "FullCastleRouteTests" && command[filter + 1] != "ShadowsOfTheForsaken.Tests.PlayMode.FullCastleRouteTests"))
                throw new InvalidOperationException("Standalone route build requires -runTests -testFilter FullCastleRouteTests -doNotReportTestResultsBackToEditor.");
            if (!Path.IsPathRooted(requested) || !requested.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SHADOWS_ROUTE_PLAYER_BUILD must be an absolute fresh Windows .exe path.");
            requestedOutput = Path.GetFullPath(requested);
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (requestedOutput.StartsWith(assets, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test-player output must be outside Assets.");
            string directory = Path.GetDirectoryName(requestedOutput);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new InvalidOperationException("Test-player output directory must be fresh and empty.");
            Directory.CreateDirectory(directory);
            if (options.scenes == null || !options.scenes.Any(scene => scene.StartsWith("Assets/InitTestScene", StringComparison.Ordinal)))
                throw new InvalidOperationException("The UTF-generated bootstrap scene must be preserved first.");
            options.scenes = options.scenes.Where(scene => scene.StartsWith("Assets/InitTestScene", StringComparison.Ordinal)).Concat(new[] { PrimaryScene }).Distinct().ToArray();
            options.options &= ~(BuildOptions.AutoRunPlayer | BuildOptions.ConnectToHost);
            options.locationPathName = requestedOutput;
            builtScenes = options.scenes;
            completedReport = null;
            callbackError = null;
            cleanupScheduled = false;
            return options;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (requestedOutput == null) return;
            try
            {
                var summary = report.summary;
                if (summary.platform != BuildTarget.StandaloneWindows64 ||
                    (summary.options & BuildOptions.IncludeTestAssemblies) == 0 ||
                    !string.Equals(Path.GetFullPath(summary.outputPath), requestedOutput, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Postprocess report does not belong to the requested Windows test player.");
                // Unity has not finalized totalSize/totalTime here. Keep the actual report
                // until UTF calls Cleanup after BuildPipeline.BuildPlayer has returned.
                completedReport = report;
            }
            catch (Exception error) { callbackError = error.ToString(); }
        }

        public void Cleanup()
        {
            if (requestedOutput == null || cleanupScheduled) return;
            cleanupScheduled = true;
            int exitCode = 1;
            try
            {
                if (callbackError != null) throw new InvalidOperationException(callbackError);
                if (completedReport == null) throw new InvalidOperationException("No matching postprocess BuildReport was captured.");
                var summary = completedReport.summary;
                var receipt = new BuildReceipt
                {
                    recorded_utc = DateTime.UtcNow.ToString("o"), result = summary.result.ToString(), errors = summary.totalErrors,
                    warnings = summary.totalWarnings, bytes = summary.totalSize, duration_seconds = summary.totalTime.TotalSeconds,
                    target = summary.platform.ToString(), output_path = summary.outputPath, scenes = builtScenes,
                    auto_run_player = (summary.options & BuildOptions.AutoRunPlayer) != 0,
                    connect_to_host = (summary.options & BuildOptions.ConnectToHost) != 0,
                    development = (summary.options & BuildOptions.Development) != 0,
                    include_test_assemblies = (summary.options & BuildOptions.IncludeTestAssemblies) != 0
                };
                if (summary.result != BuildResult.Succeeded || summary.totalErrors != 0 || summary.totalSize == 0 ||
                    summary.totalTime.TotalSeconds <= 0 || summary.platform != BuildTarget.StandaloneWindows64 ||
                    !string.Equals(Path.GetFullPath(summary.outputPath), requestedOutput, StringComparison.OrdinalIgnoreCase) ||
                    receipt.auto_run_player || receipt.connect_to_host || !receipt.include_test_assemblies || !receipt.development)
                    throw new InvalidOperationException("Final BuildReport failed success, identity, positive size/duration or local build-only configuration checks.");
                SaveNew("test-build.json", receipt);
                exitCode = 0;
            }
            catch (Exception error)
            {
                try { SaveNew("test-build-failure.json", new BuildFailure { recorded_utc = DateTime.UtcNow.ToString("o"), error = error.ToString() }); }
                catch (Exception saveError) { Debug.LogError("Standalone route build failure receipt could not be saved: " + saveError); }
                Debug.LogError("Standalone route test build failed: " + error);
            }
            finally
            {
                int finalExitCode = exitCode;
                EditorApplication.delayCall += () => EditorApplication.Exit(finalExitCode);
            }
        }

        private static void SaveNew(string name, object receipt)
        {
            string path = Path.Combine(Path.GetDirectoryName(requestedOutput), name);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.WriteLine(JsonUtility.ToJson(receipt, true));
        }

        [Serializable] private sealed class BuildReceipt
        {
            public int schema_version = 1;
            public string recorded_utc, result, target, output_path;
            public int errors, warnings;
            public ulong bytes;
            public double duration_seconds;
            public string[] scenes;
            public bool auto_run_player, connect_to_host, development, include_test_assemblies;
        }
        [Serializable] private sealed class BuildFailure { public int schema_version = 1; public string recorded_utc, error; }
    }
}
