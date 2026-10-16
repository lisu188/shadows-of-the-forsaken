#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;
using UnityEngine.Scripting;

[assembly: TestRunCallback(typeof(ForsakenStandalonePresentationTestSupport.StandalonePresentationPlayerEvidence))]

namespace ForsakenStandalonePresentationTestSupport
{
    // DOCX 3/7: separate test-only HUD/camera evidence; route/audio and ordinary runs are inactive.
    [Preserve]
    public sealed class StandalonePresentationPlayerEvidence : ITestRunCallback
    {
        private const string Profile = "presentation";
        private const string UnityVersion = "6000.6.3f1";
        private const string HudWestCase = "ShadowsOfTheForsaken.Tests.PlayMode.PrimaryCastleHudRenderingTests.PrimaryHudRendersAtRecordedPuzzleWestWallPose";
        private const string HudNorthwestCase = "ShadowsOfTheForsaken.Tests.PlayMode.PrimaryCastleHudRenderingTests.PrimaryHudRendersAtRecordedPuzzleNorthwestCornerPose";
        private const string ExplorationCase = "ShadowsOfTheForsaken.Tests.PlayMode.CastleCameraExplorationTests.LowerRoomsRetainVisibleCameraDuringUnsnappedInputExploration";
        private static readonly string[] ExpectedCases = { HudWestCase, HudNorthwestCase, ExplorationCase };
        private bool initialized, active, finished, failed;
        private string directory;
        private StreamWriter events;
        private StandalonePresentationPlayerExit exit;

        public void RunStarted(ITest test) => Guard(() => WriteEvent("run-started", test, null));
        public void TestStarted(ITest test) => Guard(() => { if (!test.IsSuite) WriteEvent("test-started", test, null); });
        public void TestFinished(ITestResult result) => Guard(() => { if (!result.Test.IsSuite) WriteEvent("test-finished", result.Test, result); });

        public void RunFinished(ITestResult result)
        {
            Guard(() =>
            {
                if (finished) throw new InvalidOperationException("Duplicate native presentation result.");
                finished = true;
                var cases = new List<ITestResult>();
                CollectCases(result, cases);
                string[] names = cases.Select(item => item.Test.FullName).ToArray();
                bool complete = cases.Count == ExpectedCases.Length && names.Distinct(StringComparer.Ordinal).Count() == ExpectedCases.Length &&
                    new HashSet<string>(names, StringComparer.Ordinal).SetEquals(ExpectedCases) &&
                    cases.All(item => item.ResultState.Status == TestStatus.Passed) && result.PassCount == ExpectedCases.Length &&
                    result.FailCount == 0 && result.SkipCount == 0 && result.InconclusiveCount == 0;
                int exitCode = complete ? 0 : 1;
                string resultsPath = Path.Combine(directory, "results.xml");
                using (var stream = new FileStream(resultsPath, FileMode.CreateNew, FileAccess.Write))
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
                {
                    var run = new TNode("test-run");
                    run.AddAttribute("id", "standalone-presentation");
                    run.AddAttribute("testcasecount", cases.Count.ToString(CultureInfo.InvariantCulture));
                    run.AddAttribute("total", cases.Count.ToString(CultureInfo.InvariantCulture));
                    run.AddAttribute("result", result.ResultState.Status.ToString());
                    run.AddAttribute("passed", result.PassCount.ToString(CultureInfo.InvariantCulture));
                    run.AddAttribute("failed", result.FailCount.ToString(CultureInfo.InvariantCulture));
                    run.AddAttribute("skipped", result.SkipCount.ToString(CultureInfo.InvariantCulture));
                    run.AddAttribute("inconclusive", result.InconclusiveCount.ToString(CultureInfo.InvariantCulture));
                    run.AddAttribute("duration", result.Duration.ToString("R", CultureInfo.InvariantCulture));
                    run.ChildNodes.Add(result.ToXml(true));
                    run.WriteTo(writer);
                }
                WriteEvent("run-finished", result.Test, result);
                events.Dispose();
                events = null;
                SaveNew("completion.json", new Completion
                {
                    recorded_utc = Utc(), total = cases.Count, passed = result.PassCount, failed = result.FailCount,
                    skipped = result.SkipCount, inconclusive = result.InconclusiveCount, duration_seconds = result.Duration,
                    case_names = cases.Select(item => item.Test.FullName).ToArray(), exit_code = exitCode,
                    results_sha256 = Sha(resultsPath), events_sha256 = Sha(Path.Combine(directory, "events.jsonl"))
                });
                exit.Schedule(exitCode);
            });
        }

        private bool Initialize()
        {
            if (initialized) return active;
            initialized = true;
            string requested = Environment.GetEnvironmentVariable("SHADOWS_PRESENTATION_PLAYER_OUTPUT");
            if (Application.isEditor || Application.platform != RuntimePlatform.WindowsPlayer || string.IsNullOrWhiteSpace(requested)) return false;
            active = true;
            var helper = new GameObject("Standalone presentation test completion") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(helper);
            exit = helper.AddComponent<StandalonePresentationPlayerExit>();
            if (Application.unityVersion != UnityVersion) throw new InvalidOperationException("Native presentation probe requires Unity " + UnityVersion + ".");
            foreach (string variable in new[] { "SHADOWS_ROUTE_PLAYER_BUILD", "SHADOWS_ROUTE_PLAYER_OUTPUT",
                "SHADOWS_AUDIO_PLAYER_BUILD", "SHADOWS_AUDIO_PLAYER_OUTPUT", "SHADOWS_AUDIO_PROBE_OUTPUT",
                "SHADOWS_PLAYER_OUTPUT", "SHADOWS_PLAYER_EVIDENCE_DIR" })
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
                    throw new InvalidOperationException("Conflicting native presentation environment override: " + variable);
            if (!Path.IsPathRooted(requested)) throw new InvalidOperationException("SHADOWS_PRESENTATION_PLAYER_OUTPUT requires an absolute fresh evidence directory.");
            if (requested.StartsWith(@"\\", StringComparison.Ordinal) || requested.StartsWith("//", StringComparison.Ordinal))
                throw new InvalidOperationException("Native presentation evidence requires a local directory.");
            directory = Path.GetFullPath(requested);
            string captures = Environment.GetEnvironmentVariable("SHADOWS_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(captures) || !Path.IsPathRooted(captures) ||
                !string.Equals(Path.GetFullPath(captures), Path.Combine(directory, "captures"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Native presentation captures must use this run's captures directory.");
            Directory.CreateDirectory(directory);
            events = new StreamWriter(new FileStream(Path.Combine(directory, "events.jsonl"), FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false));
            SaveNew("runtime.json", new Runtime
            {
                recorded_utc = Utc(), unity_version = Application.unityVersion, platform = Application.platform.ToString(),
                is_editor = Application.isEditor, batch_mode = Application.isBatchMode, graphics_api = SystemInfo.graphicsDeviceType.ToString(),
                pid = System.Diagnostics.Process.GetCurrentProcess().Id, command = Environment.GetCommandLineArgs(), output_directory = directory,
                hardware = new Hardware
                {
                    cpu = SystemInfo.processorType, logical_processors = SystemInfo.processorCount, system_memory_mb = SystemInfo.systemMemorySize,
                    gpu = SystemInfo.graphicsDeviceName, graphics_vendor = SystemInfo.graphicsDeviceVendor, graphics_memory_mb = SystemInfo.graphicsMemorySize,
                    os = SystemInfo.operatingSystem, display_width = Display.main.systemWidth, display_height = Display.main.systemHeight,
                    screen_width = Screen.width, screen_height = Screen.height
                }
            });
            return true;
        }

        private void Guard(Action action)
        {
            if (failed) return;
            try { if (Initialize()) action(); }
            catch (Exception error)
            {
                failed = true;
                try
                {
                    events?.Dispose(); events = null;
                    if (directory != null) SaveNew("failure.json", new Failure { recorded_utc = Utc(), error = error.ToString() });
                }
                catch (Exception saveError) { Debug.LogError("Native presentation evidence could not be preserved: " + saveError); }
                finally
                {
                    Debug.LogError("Native presentation evidence failed: " + error);
                    exit?.Schedule(2);
                }
            }
        }

        private void WriteEvent(string kind, ITest test, ITestResult result)
        {
            events.WriteLine(JsonUtility.ToJson(new CaseEvent
            {
                kind = kind, utc = Utc(), id = test.Id, full_name = test.FullName, is_suite = test.IsSuite,
                result = result?.ResultState.Status.ToString(), passed = result?.PassCount ?? 0, failed = result?.FailCount ?? 0,
                skipped = result?.SkipCount ?? 0, inconclusive = result?.InconclusiveCount ?? 0, duration_seconds = result?.Duration ?? 0
            }));
            events.Flush();
        }
        private static void CollectCases(ITestResult result, List<ITestResult> cases)
        {
            if (!result.Test.IsSuite) { cases.Add(result); return; }
            foreach (var child in result.Children) CollectCases(child, cases);
        }
        private void SaveNew(string name, object value)
        {
            using (var stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.WriteLine(JsonUtility.ToJson(value, true));
        }
        private static string Utc() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        private static string Sha(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path)) return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        [Serializable] private sealed class Runtime
        {
            public int schema_version = 1, pid, expected_case_count = 3;
            public string test_profile = Profile;
            public string[] expected_cases = (string[])ExpectedCases.Clone();
            public string recorded_utc, unity_version, platform, graphics_api, output_directory;
            public bool is_editor, batch_mode;
            public string[] command;
            public Hardware hardware;
            public string test_input_source = "Test-only input events and fixed HUD presentation poses";
            public string scope = "Instrumented native ordinary-frame HUD and unsnapped input exploration; separate from shipping Release, physical audio, human readability and hardware performance";
        }
        [Serializable] private sealed class Hardware
        {
            public string cpu, gpu, graphics_vendor, os;
            public int logical_processors, system_memory_mb, graphics_memory_mb, display_width, display_height, screen_width, screen_height;
        }
        [Serializable] private sealed class CaseEvent
        {
            public string kind, utc, id, full_name, result;
            public bool is_suite;
            public int passed, failed, skipped, inconclusive;
            public double duration_seconds;
        }
        [Serializable] private sealed class Completion
        {
            public int schema_version = 1, total, passed, failed, skipped, inconclusive, exit_code;
            public string test_profile = Profile;
            public string[] expected_cases = (string[])ExpectedCases.Clone();
            public string recorded_utc, results_sha256, events_sha256;
            public double duration_seconds;
            public string[] case_names;
        }
        [Serializable] private sealed class Failure
        {
            public int schema_version = 1;
            public string test_profile = Profile;
            public string recorded_utc, error;
        }
    }

    public sealed class StandalonePresentationPlayerExit : MonoBehaviour
    {
        private bool pending;
        private int exitCode, exitFrame;
        public void Schedule(int code) { pending = true; exitCode = Math.Max(exitCode, code); exitFrame = Time.frameCount + 2; }
        private void Update() { if (pending && Time.frameCount >= exitFrame) Application.Quit(exitCode); }
    }
}
#endif
