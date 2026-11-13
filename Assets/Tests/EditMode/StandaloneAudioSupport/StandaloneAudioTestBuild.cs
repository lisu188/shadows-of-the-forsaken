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

[assembly: TestPlayerBuildModifier(typeof(ShadowsOfTheForsaken.Tests.EditMode.StandaloneAudioTestBuild))]
[assembly: PostBuildCleanup(typeof(ShadowsOfTheForsaken.Tests.EditMode.StandaloneAudioTestBuild))]

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    // Separate opt-in UTF build-only probe. The ten-case route profile is unchanged.
    public sealed class StandaloneAudioTestBuild : ITestPlayerBuildModifier, IPostBuildCleanup, IPostprocessBuildWithReport
    {
        private const string Profile = "audio-mixer";
        private const string UnityVersion = "6000.6.3f1";
        private const string AudioCase = "ShadowsOfTheForsaken.Tests.PlayMode.AudioMixerPlaybackTests.QuietClipTraversesMixerAndReturnsToSilence";
        private const string AudioFilter = @"^ShadowsOfTheForsaken\.Tests\.PlayMode\.AudioMixerPlaybackTests\.QuietClipTraversesMixerAndReturnsToSilence$";
        private const string PrimaryScene = "Assets/Scenes/ForsakenCastle.unity";
        private static string requestedOutput;
        private static string[] builtScenes;
        private static BuildReport completedReport;
        private static string callbackError;
        private static bool cleanupScheduled;
        public int callbackOrder => int.MaxValue - 1;

        public BuildPlayerOptions ModifyOptions(BuildPlayerOptions options)
        {
            string enabled = Environment.GetEnvironmentVariable("SHADOWS_AUDIO_PLAYER_BUILD");
            if (string.IsNullOrWhiteSpace(enabled) || options.target != BuildTarget.StandaloneWindows64 ||
                (options.options & BuildOptions.IncludeTestAssemblies) == 0) return options;
            if (enabled != "1") throw new InvalidOperationException("SHADOWS_AUDIO_PLAYER_BUILD must be exactly 1.");
            if (Application.unityVersion != UnityVersion) throw new InvalidOperationException("Native audio build requires Unity " + UnityVersion + ".");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHADOWS_ROUTE_PLAYER_BUILD")) ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHADOWS_ROUTE_PLAYER_OUTPUT")))
                throw new InvalidOperationException("Conflicting route/audio test-player environment overrides.");
            string[] command = Environment.GetCommandLineArgs();
            int filter = Array.IndexOf(command, "-testFilter");
            int buildPath = Array.IndexOf(command, "-buildPlayerPath");
            if (!command.Contains("-runTests") || !command.Contains("-doNotReportTestResultsBackToEditor") ||
                filter < 0 || filter + 1 >= command.Length || command[filter + 1] != AudioFilter ||
                buildPath < 0 || buildPath + 1 >= command.Length || !Path.IsPathRooted(command[buildPath + 1]))
                throw new InvalidOperationException("Native audio build requires the exact anchored audio filter, an absolute -buildPlayerPath and local build-only UTF flags.");
            string buildBase = Path.GetFullPath(command[buildPath + 1]);
            if (buildBase.StartsWith(@"\\", StringComparison.Ordinal) || buildBase.StartsWith("//", StringComparison.Ordinal))
                throw new InvalidOperationException("Audio test-player output must use a local build directory.");
            if (!Path.IsPathRooted(options.locationPathName) || !options.locationPathName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("UTF must provide an absolute Windows test-player .exe output.");
            requestedOutput = Path.GetFullPath(options.locationPathName);
            if (!string.Equals(requestedOutput, Path.Combine(buildBase, "PlayerWithTests", "PlayerWithTests.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("UTF output differs from the explicit audio -buildPlayerPath.");
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (requestedOutput.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Audio test-player output must be outside Assets.");
            string directory = Path.GetDirectoryName(requestedOutput);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new InvalidOperationException("Audio test-player output directory must be fresh and empty.");
            Directory.CreateDirectory(directory);
            string[] bootstraps = options.scenes?.Where(scene => scene.StartsWith("Assets/InitTestScene", StringComparison.Ordinal)).Distinct().ToArray();
            if (bootstraps == null || bootstraps.Length != 1)
                throw new InvalidOperationException("The UTF bootstrap must be preserved first.");
            options.scenes = bootstraps.Concat(new[] { PrimaryScene }).ToArray();
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
            if (requestedOutput == null || Environment.GetEnvironmentVariable("SHADOWS_AUDIO_PLAYER_BUILD") != "1") return;
            var summary = report.summary;
            if (summary.platform != BuildTarget.StandaloneWindows64 || (summary.options & BuildOptions.IncludeTestAssemblies) == 0) return;
            try
            {
                if (!string.Equals(Path.GetFullPath(summary.outputPath), requestedOutput, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Postprocess report does not belong to the requested audio test player.");
                // Final bytes/time are available only after BuildPlayer returns.
                completedReport = report;
            }
            catch (Exception error) { callbackError = error.ToString(); }
        }

        public void Cleanup()
        {
            if (requestedOutput == null || cleanupScheduled || Environment.GetEnvironmentVariable("SHADOWS_AUDIO_PLAYER_BUILD") != "1") return;
            cleanupScheduled = true;
            int exitCode = 1;
            try
            {
                if (callbackError != null) throw new InvalidOperationException(callbackError);
                if (completedReport == null) throw new InvalidOperationException("No matching audio postprocess BuildReport was captured.");
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
                    throw new InvalidOperationException("Final audio BuildReport failed success, identity, positive size/duration or local build-only checks.");
                SaveNew("test-build.json", receipt);
                exitCode = 0;
            }
            catch (Exception error)
            {
                try { SaveNew("test-build-failure.json", new BuildFailure { recorded_utc = DateTime.UtcNow.ToString("o"), error = error.ToString() }); }
                catch (Exception saveError) { Debug.LogError("Native audio build failure receipt could not be saved: " + saveError); }
                Debug.LogError("Native audio test build failed: " + error);
            }
            finally
            {
                int finalExitCode = exitCode;
                EditorApplication.delayCall += () => EditorApplication.Exit(finalExitCode);
            }
        }

        private static void SaveNew(string name, object value)
        {
            string path = Path.Combine(Path.GetDirectoryName(requestedOutput), name);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.WriteLine(JsonUtility.ToJson(value, true));
        }
        [Serializable] private sealed class BuildReceipt
        {
            public int schema_version = 1;
            public string test_profile = Profile, test_filter = AudioFilter;
            public string[] expected_cases = { AudioCase };
            public string recorded_utc, result, target, output_path;
            public int errors, warnings;
            public ulong bytes;
            public double duration_seconds;
            public string[] scenes;
            public bool auto_run_player, connect_to_host, development, include_test_assemblies;
        }
        [Serializable] private sealed class BuildFailure
        {
            public int schema_version = 1;
            public string test_profile = Profile;
            public string recorded_utc, error;
        }
    }
}
