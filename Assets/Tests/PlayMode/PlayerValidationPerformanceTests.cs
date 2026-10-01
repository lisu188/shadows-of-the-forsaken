using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // Real observer/file/session integration. Histogram cases supply measured
    // intervals to the production pure accumulator; they do not simulate Unity.
    public sealed class PlayerValidationPerformanceTests
    {
        private const string Variable = "SHADOWS_PLAYER_EVIDENCE_DIR";
        private string previousDirectory, directory;
        private float previousTimeScale;
        private Scene scene;
        private InputActionAsset input;
        private LevelProgressionController progression;
        private Component session, health, observer;

        [SetUp]
        public void PrepareRealSession()
        {
            previousDirectory = Environment.GetEnvironmentVariable(Variable);
            Environment.SetEnvironmentVariable(Variable, null);
            previousTimeScale = Time.timeScale; Time.timeScale = 1;
            directory = Path.Combine(Path.GetTempPath(), "ShadowsPerformanceTest-" + Guid.NewGuid().ToString("N"));
            scene = SceneManager.CreateScene("Performance fixture " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            input = ScriptableObject.CreateInstance<InputActionAsset>();
            input.devices = Array.Empty<InputDevice>();
            var map = input.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            foreach (string name in new[] { "Jump", "Attack", "Interact", "Restart" }) map.AddAction(name, InputActionType.Button);
            progression = Make("Progression").AddComponent<LevelProgressionController>();
            var spawn = Make("Spawn").transform; spawn.position = new Vector3(3000, .05f, 3000);
            var floor = Make("Floor"); floor.transform.position = new Vector3(3000, -.25f, 3000);
            floor.AddComponent<BoxCollider>().size = new Vector3(10, .5f, 10);
            var player = Make("Player"); player.SetActive(false); player.transform.position = spawn.position;
            var movement = player.AddComponent(RuntimeType("PlayerMovement")); Set(movement, "inputActions", input);
            var character = player.GetComponent<CharacterController>(); character.height = 2; character.center = Vector3.up;
            health = player.AddComponent(RuntimeType("ShadowsOfTheForsaken.Combat.CombatHealth")); Set(health, "progression", progression);
            player.SetActive(true);
            var root = Make("Session"); root.SetActive(false);
            session = root.AddComponent(RuntimeType("LevelSessionController"));
            Set(session, "progression", progression); Set(session, "player", movement); Set(session, "playerHealth", health); Set(session, "spawn", spawn);
            root.SetActive(true);
            Assert.That((bool)Call(session, "InitializeSession"), Is.True);
            Call(session, "OnApplicationFocus", true);
        }

        [UnityTearDown]
        public IEnumerator RestoreEnvironment()
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                // Disable observation before session/player roots disappear.
                if (observer != null) Object.DestroyImmediate(observer);
                var operation = SceneManager.UnloadSceneAsync(scene);
                float until = Time.realtimeSinceStartup + 10;
                while (operation != null && !operation.isDone && Time.realtimeSinceStartup < until) yield return null;
                Assert.That(operation == null || operation.isDone, Is.True);
            }
            if (input != null) Object.DestroyImmediate(input);
            Environment.SetEnvironmentVariable(Variable, previousDirectory);
            Time.timeScale = previousTimeScale;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void PerformanceHistogramRetainsOverflowFramesAndConservativePercentile()
        {
            object metrics = Activator.CreateInstance(RuntimeType("PlayerFrameMetrics"));
            Record(metrics, 5, "None"); // Explicit warmup, excluded in full.
            for (int i = 0; i < 18; i++) Record(metrics, .01, "None");
            Record(metrics, .8, "None"); Record(metrics, .9, "None");
            var summary = Metrics(metrics, true);
            Assert.That(summary.measuredFrames, Is.EqualTo(20));
            Assert.That(summary.measuredSeconds, Is.EqualTo(1.88).Within(.000001));
            Assert.That(summary.averageMilliseconds, Is.EqualTo(94).Within(.000001));
            Assert.That(summary.maximumMilliseconds, Is.EqualTo(900).Within(.000001));
            Assert.That(summary.p95UpperBoundMilliseconds, Is.EqualTo(900).Within(.000001));
            Assert.That(summary.histogramOverflowFrames, Is.EqualTo(2));
            Assert.That(summary.histogram.Sum(), Is.EqualTo(20));
            Assert.That(summary.histogram.Length, Is.EqualTo(2049));
            Assert.That(summary.withinProvisionalFrameBudget, Is.False);
            Assert.That(Metrics(metrics, false).histogram, Is.Empty);
        }

        [Test]
        public void PerformanceExclusionsPreserveWarmupAndEmptyMetricsCannotPassBudget()
        {
            object metrics = Activator.CreateInstance(RuntimeType("PlayerFrameMetrics"));
            Assert.That(Metrics(metrics, false).withinProvisionalFrameBudget, Is.False);
            foreach (string reason in new[] { "Unfocused", "Paused", "NotRunning", "Transition" }) Record(metrics, 10, reason);
            Record(metrics, 4.9, "None"); Record(metrics, .2, "None"); Record(metrics, .016, "None");
            Record(metrics, double.NaN, "None"); Record(metrics, double.PositiveInfinity, "None"); Record(metrics, -.1, "None");
            var summary = Metrics(metrics, true);
            Assert.That(summary.observedFrames, Is.EqualTo(10));
            Assert.That(summary.measuredFrames, Is.EqualTo(1));
            Assert.That(summary.warmupObservedEligibleSeconds, Is.EqualTo(5.1).Within(.000001));
            Assert.That(summary.excluded.Single(item => item.reason == "Warmup").frames, Is.EqualTo(2));
            Assert.That(summary.excluded.Single(item => item.reason == "Invalid").frames, Is.EqualTo(3));
            Assert.That(summary.excluded.Sum(item => item.frames) + summary.measuredFrames, Is.EqualTo(summary.observedFrames));
            Assert.That(summary.withinProvisionalFrameBudget, Is.True);
            Assert.That(Metrics(Activator.CreateInstance(RuntimeType("PlayerFrameMetrics")), true).observedFrames, Is.Zero);
            Assert.That(Metrics(metrics, false).observedFrames, Is.EqualTo(10));
        }

        [UnityTest]
        public IEnumerator PerformanceObserverCreatesNoOutputWithoutOptIn()
        {
            observer = session.gameObject.AddComponent(RuntimeType("PlayerValidationEvidence")); Set(observer, "session", session);
            yield return null;
            Assert.That(((Behaviour)observer).enabled, Is.False);
            Assert.That(Directory.Exists(directory), Is.False);
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
        }

        [UnityTest, Timeout(15000)]
        public IEnumerator PerformanceObserverMeasuresActualFramesAndExcludesFocusPauseAndTimeScale()
        {
            yield return StartObserver();
            Guid token = progression.Snapshot.SessionId;
            int initialHealth = Get<int>(health, "Current");
            double until = Time.realtimeSinceStartupAsDouble + 5.35;
            while (Time.realtimeSinceStartupAsDouble < until) yield return null;
            Call(observer, "WriteSnapshot");
            var measured = ReadStatus();
            double sampleDeadline = Time.realtimeSinceStartupAsDouble + 5;
            while (measured.performance.measuredFrames == 0 && Time.realtimeSinceStartupAsDouble < sampleDeadline)
            {
                yield return null;
                measured = ReadStatus();
            }
            Assert.That(measured.performance.measuredFrames, Is.GreaterThan(0));
            Assert.That(measured.performance.warmupObservedEligibleSeconds, Is.GreaterThanOrEqualTo(5));
            Assert.That(measured.performance.maximumMilliseconds, Is.GreaterThan(0));
            Call(observer, "OnApplicationFocus", false); yield return null; yield return null;
            Call(observer, "OnApplicationFocus", true); yield return null; yield return null;
            Call(observer, "OnApplicationPause", true); yield return null; yield return null;
            Call(observer, "OnApplicationPause", false); yield return null; yield return null;
            Time.timeScale = 0; yield return null; yield return null;
            Time.timeScale = 1; yield return null; yield return null;
            Call(observer, "WriteSnapshot");
            var snapshot = ReadStatus();
            Assert.That(snapshot.performance.excluded.Single(item => item.reason == "Unfocused").frames, Is.GreaterThan(0));
            Assert.That(snapshot.performance.excluded.Single(item => item.reason == "Paused").frames, Is.GreaterThanOrEqualTo(2));
            Assert.That(snapshot.performance.excluded.Single(item => item.reason == "Transition").frames, Is.GreaterThan(0));
            Assert.That(snapshot.hardware.unityVersion, Is.EqualTo(Application.unityVersion));
            Assert.That(snapshot.hardware.processorCount, Is.EqualTo(SystemInfo.processorCount));
            Assert.That(snapshot.memory.samples, Is.GreaterThan(0));
            Assert.That(snapshot.memory.managedEstimatedBytes, Is.GreaterThan(0));
            Assert.That(progression.Snapshot.SessionId, Is.EqualTo(token));
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(Get<int>(health, "Current"), Is.EqualTo(initialHealth));
            Assert.That(Time.timeScale, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PerformanceLifecycleKeepsOriginalTokenAndResetsCountersAfterDeath()
        {
            yield return StartObserver();
            yield return null;
            Guid before = progression.Snapshot.SessionId;
            Assert.That((bool)Call(health, "TryDamage", Get<int>(health, "Maximum"), Get<Guid>(health,"LifeId"), before), Is.True);
            var defeat = ReadSummaries().Single(item => item.reason == "defeat");
            Assert.That(defeat.sessionId, Is.EqualTo(before.ToString()));
            Assert.That(Get<object>(session, "State").ToString(), Is.EqualTo("Defeated"));
            Assert.That((bool)Call(session, "RestartSession"), Is.True);
            Guid after = progression.Snapshot.SessionId;
            Assert.That(after, Is.Not.EqualTo(before));
            var reset = ReadSummaries().Single(item => item.reason == "reset");
            Assert.That(reset.sessionId, Is.EqualTo(before.ToString()));
            Call(observer, "WriteSnapshot");
            Assert.That(ReadStatus().sessionId, Is.EqualTo(after.ToString()));
            Assert.That(ReadStatus().performance.observedFrames, Is.Zero);
            Assert.That(Get<int>(health, "Current"), Is.EqualTo(Get<int>(health, "Maximum")));
            Call(observer, "OnApplicationQuit");
            Assert.That(ReadSummaries().Single(item => item.reason == "normal-exit").sessionId, Is.EqualTo(after.ToString()));
            Assert.That(progression.Snapshot.SessionId, Is.EqualTo(after));
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(Get<object>(session, "State").ToString(), Is.EqualTo("Running"));
        }

        [UnityTest]
        public IEnumerator PerformanceCompletionWritesBoundedHistogramAndConfiguration()
        {
            yield return StartObserver();
            var token = progression.Snapshot.SessionId;
            foreach (var pair in new[] {
                Tuple.Create(LevelRoom.FirstEncounter, LevelObjective.FirstEnemyDefeated),
                Tuple.Create(LevelRoom.Puzzle, LevelObjective.MainPuzzleSolved),
                Tuple.Create(LevelRoom.ThroneRoom, LevelObjective.MinibossDefeated),
                Tuple.Create(LevelRoom.Library, LevelObjective.LibraryOpened),
                Tuple.Create(LevelRoom.Catacombs, LevelObjective.None),
                Tuple.Create(LevelRoom.FinalArena, LevelObjective.FinalEnemyDefeated) })
            {
                if (progression.Snapshot.Room == LevelRoom.Puzzle && pair.Item1 == LevelRoom.ThroneRoom)
                    Assert.That((bool)Call(session, "TryEnterRoom", LevelRoom.FirstEncounter), Is.True);
                Assert.That((bool)Call(session, "TryEnterRoom", pair.Item1), Is.True);
                if (pair.Item2 != LevelObjective.None) Assert.That(progression.TryComplete(pair.Item2, token), Is.True);
            }
            Assert.That((bool)Call(session, "TryEnterRoom", LevelRoom.Exit), Is.True);
            var summary = ReadSummaries().Single(item => item.reason == "completion");
            Assert.That(summary.sessionId, Is.EqualTo(token.ToString()));
            Assert.That(summary.frameIntervals.histogram.Length, Is.EqualTo(2049));
            Assert.That(summary.frameIntervals.histogram.Sum(), Is.EqualTo(summary.frameIntervals.measuredFrames));
            Assert.That(summary.screenWidth, Is.EqualTo(Screen.width));
            Assert.That(summary.screenHeight, Is.EqualTo(Screen.height));
            Assert.That(summary.vSyncCount, Is.EqualTo(QualitySettings.vSyncCount));
            Assert.That(summary.targetFrameRate, Is.EqualTo(Application.targetFrameRate));
            StringAssert.Contains("not CPU/GPU profiling", summary.measurement);
            Assert.That(Get<int>(health, "Current"), Is.EqualTo(Get<int>(health, "Maximum")));
            Assert.That(progression.Snapshot.IsCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator PerformanceSummaryCapLeavesRollingStatusOperational()
        {
            Directory.CreateDirectory(directory);
            string performance = Path.Combine(directory, "performance.jsonl");
            File.WriteAllText(performance, string.Concat(Enumerable.Repeat("{}\n", 256)));
            yield return StartObserver();
            string original = File.ReadAllText(performance);
            Call(observer, "OnApplicationQuit");
            Assert.That(File.ReadAllText(performance), Is.EqualTo(original));
            Assert.That(File.Exists(Path.Combine(directory, "status.json")), Is.True);
            Assert.That(ReadStatus().memory.samples, Is.GreaterThan(0));
            Assert.That(ReadStatus().performanceSummaryLimitReached, Is.True);
        }

        private IEnumerator StartObserver()
        {
            Environment.SetEnvironmentVariable(Variable, directory);
            observer = session.gameObject.AddComponent(RuntimeType("PlayerValidationEvidence")); Set(observer, "session", session);
            yield return null;
            Call(observer, "OnApplicationFocus", true);
            Assert.That(File.Exists(Path.Combine(directory, "status.json")), Is.True);
        }
        private Status ReadStatus() => JsonUtility.FromJson<Status>(File.ReadAllText(Path.Combine(directory, "status.json")));
        private Summary[] ReadSummaries() => File.ReadAllLines(Path.Combine(directory, "performance.jsonl")).Select(JsonUtility.FromJson<Summary>).ToArray();
        private static FrameSummary Metrics(object metrics, bool histogram) => JsonUtility.FromJson<FrameSummary>(JsonUtility.ToJson(Call(metrics, "Snapshot", histogram)));
        private static void Record(object metrics, double seconds, string reason) => Call(metrics, "Record", seconds, Enum.Parse(RuntimeType("PlayerFrameExclusion"), reason));
        private GameObject Make(string name) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        private static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field).SetValue(target, value);
        private static T Get<T>(object target, string property) => (T)target.GetType().GetProperty(property).GetValue(target);
        private static object Call(object target, string method, params object[] values)
        {
            try { return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, values); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
        [Serializable] private sealed class Status
        {
            public string sessionId;
            public bool performanceSummaryLimitReached;
            public FrameSummary performance;
            public Hardware hardware;
            public Memory memory;
        }
        [Serializable] private sealed class Hardware { public string unityVersion; public int processorCount; }
        [Serializable] private sealed class Memory { public long samples, managedEstimatedBytes; }
        [Serializable] private sealed class Summary
        {
            public string reason, sessionId, measurement;
            public int screenWidth, screenHeight, vSyncCount, targetFrameRate;
            public FrameSummary frameIntervals;
        }
        [Serializable] private sealed class FrameSummary
        {
            public long observedFrames, measuredFrames, histogramOverflowFrames;
            public double measuredSeconds, averageMilliseconds, maximumMilliseconds, p95UpperBoundMilliseconds, warmupObservedEligibleSeconds;
            public bool withinProvisionalFrameBudget;
            public long[] histogram;
            public Excluded[] excluded;
        }
        [Serializable] private sealed class Excluded { public string reason; public long frames; }
    }
}
