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

[assembly: TestPlayerBuildModifier(typeof(ShadowsOfTheForsaken.Tests.EditMode.StandaloneRenderedRouteTestBuild))]
[assembly: PostBuildCleanup(typeof(ShadowsOfTheForsaken.Tests.EditMode.StandaloneRenderedRouteTestBuild))]

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    // DOCX 3/6-8 and issue24: opt-in two existing route cases; other native profiles remain unchanged.
    public sealed class StandaloneRenderedRouteTestBuild : ITestPlayerBuildModifier, IPostBuildCleanup, IPostprocessBuildWithReport
    {
        private const string Profile = ForsakenStandaloneRenderedRouteSupport.RenderedRouteBinding.Profile;
        private static readonly string[] ExpectedCases = ForsakenStandaloneRenderedRouteSupport.RenderedRouteBinding.ExpectedCases;
        private const string PresentationFilter = ForsakenStandaloneRenderedRouteSupport.RenderedRouteBinding.Filter;
        private const string PrimaryScene = "Assets/Scenes/ForsakenCastle.unity";
        private static string requestedOutput;
        private static string[] builtScenes;
        private static BuildReport completedReport;
        private static string callbackError;
        private static bool cleanupScheduled;
        public int callbackOrder => int.MaxValue - 1;

        public BuildPlayerOptions ModifyOptions(BuildPlayerOptions options)
        {
            string enabled = Environment.GetEnvironmentVariable("SHADOWS_RENDERED_ROUTE_BUILD");
            if (string.IsNullOrWhiteSpace(enabled)) return options;
            if (enabled != "1" || options.target != BuildTarget.StandaloneWindows64 ||
                (options.options & BuildOptions.IncludeTestAssemblies) == 0)
                throw new InvalidOperationException("Rendered routes require exactly1 and a Windows64 test-assembly build.");
            if (Application.unityVersion != ForsakenStandaloneRenderedRouteSupport.RenderedRouteBinding.UnityVersion)
                throw new InvalidOperationException("Rendered routes require the pinned Unity version.");
            foreach (string variable in ForsakenStandaloneRenderedRouteSupport.RenderedRouteBinding.Conflicts.Concat(new[] { "SHADOWS_RENDERED_ROUTE_OUTPUT" }))
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
                    throw new InvalidOperationException("Conflicting rendered-route build environment: " + variable);
            string[] command = Environment.GetCommandLineArgs();
            int filter = Array.IndexOf(command, "-testFilter");
            int buildPath = Array.IndexOf(command, "-buildPlayerPath");
            if (!command.Contains("-runTests") || !command.Contains("-doNotReportTestResultsBackToEditor") ||
                filter < 0 || filter + 1 >= command.Length || command[filter + 1] != PresentationFilter ||
                buildPath < 0 || buildPath + 1 >= command.Length || !Path.IsPathRooted(command[buildPath + 1]))
                throw new InvalidOperationException("Rendered-route build requires the exact anchored rendered-route filter, an absolute -buildPlayerPath and local build-only UTF flags.");
            string buildBase = ForsakenStandaloneRenderedRouteSupport.RenderedRouteBinding.LocalPath(command[buildPath + 1]);
            if (buildBase.StartsWith(@"\\", StringComparison.Ordinal) || buildBase.StartsWith("//", StringComparison.Ordinal))
                throw new InvalidOperationException("Rendered-route test-player output must use a local build directory.");
            if (!Path.IsPathRooted(options.locationPathName) || !options.locationPathName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("UTF must provide an absolute Windows test-player .exe output.");
            requestedOutput = ForsakenStandaloneRenderedRouteSupport.RenderedRouteBinding.LocalPath(options.locationPathName);
            if (!string.Equals(requestedOutput, Path.Combine(buildBase, "PlayerWithTests", "PlayerWithTests.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("UTF output differs from the explicit rendered-route -buildPlayerPath.");
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (requestedOutput.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Rendered-route test-player output must be outside Assets.");
            string directory = Path.GetDirectoryName(requestedOutput);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new InvalidOperationException("Rendered-route test-player output directory must be fresh and empty.");
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
            if (requestedOutput == null || Environment.GetEnvironmentVariable("SHADOWS_RENDERED_ROUTE_BUILD") != "1") return;
            var summary = report.summary;
            if (summary.platform != BuildTarget.StandaloneWindows64 || (summary.options & BuildOptions.IncludeTestAssemblies) == 0) return;
            try
            {
                if (!string.Equals(Path.GetFullPath(summary.outputPath), requestedOutput, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Postprocess report does not belong to the requested rendered-route test player.");
                // Final bytes/time are available only after BuildPlayer returns.
                completedReport = report;
            }
            catch (Exception error) { callbackError = error.ToString(); }
        }

        public void Cleanup()
        {
            if (requestedOutput == null || cleanupScheduled || Environment.GetEnvironmentVariable("SHADOWS_RENDERED_ROUTE_BUILD") != "1") return;
            cleanupScheduled = true;
            int exitCode = 1;
            try
            {
                if (callbackError != null) throw new InvalidOperationException(callbackError);
                if (completedReport == null) throw new InvalidOperationException("No matching rendered-route postprocess BuildReport was captured.");
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
                    throw new InvalidOperationException("Final rendered-route BuildReport failed success, identity, positive size/duration or local build-only checks.");
                string managed = Path.Combine(Path.GetDirectoryName(requestedOutput),
                    Path.GetFileNameWithoutExtension(requestedOutput) + "_Data", "Managed");
                foreach (string assembly in new[] { "Shadows.PlayMode.Tests.dll", "Shadows.StandaloneRenderedRoutes.PlayerSupport.dll" })
                    if (!File.Exists(Path.Combine(managed, assembly)) || new FileInfo(Path.Combine(managed, assembly)).Length == 0)
                        throw new InvalidOperationException("Required rendered-route assembly is missing: " + assembly);
                SaveNew("test-build.json", receipt);
                exitCode = 0;
            }
            catch (Exception error)
            {
                try { SaveNew("test-build-failure.json", new BuildFailure { recorded_utc = DateTime.UtcNow.ToString("o"), error = error.ToString() }); }
                catch (Exception saveError) { Debug.LogError("Rendered-route build failure receipt could not be saved: " + saveError); }
                Debug.LogError("Rendered-route test build failed: " + error);
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
            public string test_profile = Profile, test_filter = PresentationFilter;
            public string[] expected_cases = (string[])ExpectedCases.Clone();
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
