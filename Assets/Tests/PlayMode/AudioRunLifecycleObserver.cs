#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ForsakenStandaloneRenderedRouteSupport;
using ForsakenPassiveAudioLifecycleTestSupport;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;
using UnityEngine.Scripting;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // One attribute is allowed per assembly. Keep the original ten-route
    // callback as an unchanged owned delegate and add the optional companion.
    [Preserve]
    public sealed class StandaloneMainTestCallbacks : ITestRunCallback
    {
        private readonly ITestRunCallback route, audio;
        public StandaloneMainTestCallbacks() : this(new ForsakenStandaloneRouteTestSupport.StandaloneRoutePlayerEvidence(), new AudioRunLifecycleCallbacks()) { }
        public StandaloneMainTestCallbacks(ITestRunCallback route, ITestRunCallback audio)
        {
            this.route = route ?? throw new ArgumentNullException(nameof(route));
            this.audio = audio ?? throw new ArgumentNullException(nameof(audio));
        }
        public void RunStarted(ITest test) { try { route.RunStarted(test); } finally { audio.RunStarted(test); } }
        public void TestStarted(ITest test) { try { route.TestStarted(test); } finally { audio.TestStarted(test); } }
        public void TestFinished(ITestResult result) { try { route.TestFinished(result); } finally { audio.TestFinished(result); } }
        public void RunFinished(ITestResult result) { try { route.RunFinished(result); } finally { audio.RunFinished(result); } }
    }

    // Optional test-only companion. It neither changes the existing two tests
    // nor owns quitting. UTF callback order is deliberately not assumed.
    [Preserve]
    public sealed class AudioRunLifecycleCallbacks : ITestRunCallback
    {
        private bool initialized, active;
        private AudioRunLifecycleObserver observer;
        private bool Initialize()
        {
            if (initialized) return active;
            initialized = true;
            string requested = Environment.GetEnvironmentVariable("SHADOWS_AUDIO_RUN_LIFECYCLE_OUTPUT");
            if (Application.isEditor || string.IsNullOrWhiteSpace(requested)) return false;
            if (!RenderedRouteBinding.Active || !string.Equals(RenderedRouteBinding.LocalPath(requested), RenderedRouteBinding.Output(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Audio run lifecycle must use the active rendered-route output exactly.");
            var owner = new GameObject("Owned rendered-route passive audio lifecycle");
            Object.DontDestroyOnLoad(owner);
            observer = owner.AddComponent<AudioRunLifecycleObserver>();
            observer.Begin(RenderedRouteBinding.Output(), RenderedRouteBinding.Pid(),
                new AudioConfigurationTelemetry.UnityEvents(), new AudioConfigurationTelemetry.UnityReader(),
                new AudioRunLifecycleFiles(RenderedRouteBinding.Output()));
            active = true;
            return true;
        }
        private void Guard(Action action)
        {
            try { if (Initialize()) action(); }
            catch (Exception error)
            {
                if (observer != null) observer.RecordFailure(error);
                UnityEngine.Debug.LogError("Audio run lifecycle initialization/callback failed: " + error);
            }
        }
        public void RunStarted(ITest test) => Guard(() => observer.ObserveTest("run-started", test.Id, test.FullName, test.IsSuite, ""));
        public void TestStarted(ITest test) => Guard(() => { if (!test.IsSuite) observer.ObserveTest("test-started", test.Id, test.FullName, false, ""); });
        public void TestFinished(ITestResult result) => Guard(() => { if (!result.Test.IsSuite) observer.ObserveTest("test-finished", result.Test.Id, result.Test.FullName, false, result.ResultState.Status.ToString()); });
        public void RunFinished(ITestResult result) => Guard(() => observer.ObserveTest("run-finished", result.Test.Id, result.Test.FullName, result.Test.IsSuite, result.ResultState.Status.ToString()));
    }

}

// The unchanged route fixture suspends gameplay-namespaced Behaviours. This
// owned diagnostic must keep draining while both actual routes are running.
namespace ForsakenPassiveAudioLifecycleTestSupport
{
    using ShadowsOfTheForsaken.Tests.PlayMode;

    public interface IAudioRunLifecycleSink
    {
        void Append(AudioRunLifecycleObserver.Marker marker);
        string SavePreQuit(AudioRunLifecycleObserver.PreQuit evidence);
        void Complete(AudioRunLifecycleObserver.Completion evidence);
    }

    // Reuses the proven bounded managed callback queue/interfaces. The old
    // mixer component remains unchanged: its destruction fallback reads Unity
    // configuration, which is deliberately unsuitable after quitting begins.
    public sealed class AudioRunLifecycleObserver : MonoBehaviour
    {
        public const string Profile = "rendered-routes-audio-lifecycle";
        public const int MarkerCapacity = 32, RecordCapacity = 256;
        private readonly AudioConfigurationObservationQueue queue = new AudioConfigurationObservationQueue();
        private readonly AudioConfigurationObservationQueue.Entry[] drain = new AudioConfigurationObservationQueue.Entry[AudioConfigurationObservationQueue.Capacity];
        private readonly List<AudioConfigurationTelemetry.EventData> retained = new List<AudioConfigurationTelemetry.EventData>(RecordCapacity);
        private readonly List<string> errors = new List<string>(16);
        private IAudioConfigurationEvents publisher;
        private IAudioConfigurationReader reader;
        private IAudioRunLifecycleSink sink;
        private StandaloneRenderedRoutePlayerExit exit;
        private AudioConfigurationTelemetry.ConfigurationData initial, final;
        private int mainThread, phaseToken, sequence, pid, subscribeAttempts, unsubscribeAttempts, exitAttachCount, exitDetachCount;
        private int wantsSubscribeCount, wantsDetachCount, wantsCount, quitCount, destroyCount;
        private long recordDrops;
        private ulong exitEntityId;
        private bool bound, subscribed, audioClosed, quitting, wantsSubscribed, preQuitObserved, lifecycleEvents;
        private string output, runId, preQuitSha;
        private void Awake() { mainThread = Thread.CurrentThread.ManagedThreadId; }
        private void MainOnly()
        {
            if (mainThread == 0 || Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new InvalidOperationException("Audio lifecycle work requires the captured Unity main thread.");
        }
        public void Begin(string directory, int processId, IAudioConfigurationEvents events, IAudioConfigurationReader configuration, IAudioRunLifecycleSink target, bool subscribeLifecycleEvents = true)
        {
            MainOnly();
            if (bound || events == null || configuration == null || target == null) throw new InvalidOperationException("One complete owned lifecycle lease is required.");
            bound = true; output = directory; pid = processId; runId = Guid.NewGuid().ToString("N");
            publisher = events; reader = configuration; sink = target; lifecycleEvents = subscribeLifecycleEvents;
            try
            {
                Append("observer-began", false);
                if (lifecycleEvents) { wantsSubscribeCount++; wantsSubscribed = true; Application.wantsToQuit += ObserveWantsToQuit; }
                subscribeAttempts++; subscribed = true; publisher.Subscribe(OnChanged);
                initial = ReadConfiguration();
                TryAttachExit();
            }
            catch (Exception error) { RecordFailure(error); CloseAudio(false); }
        }
        private void OnChanged(bool changed)
        {
            queue.Record(changed, DateTime.UtcNow.Ticks, Stopwatch.GetTimestamp(), Thread.CurrentThread.ManagedThreadId, Volatile.Read(ref phaseToken));
        }
        public void ObserveTest(string kind, string id, string name, bool suite, string result)
        {
            MainOnly();
            if (!bound || quitting || audioClosed) throw new InvalidOperationException("Test callback escaped the audio run lease.");
            if (kind == "test-started" || kind == "test-finished") RenderedRouteBinding.CaseKey(name);
            TryAttachExit();
            Append(kind, false, id, name, suite, result);
        }
        public void AttachExit(StandaloneRenderedRoutePlayerExit value)
        {
            MainOnly();
            if (value == null || !ReferenceEquals(exit, null) || quitting || audioClosed) throw new InvalidOperationException("An exact single live exit binding is required.");
            exit = value; exitAttachCount++; exit.BeforeQuit += CloseBeforeQuit;
            exitEntityId = EntityId.ToULong(value.GetEntityId());
            Append("exit-attached", false);
        }
        private void TryAttachExit()
        {
            if (!ReferenceEquals(exit, null) || quitting || audioClosed) return;
            var candidates = Resources.FindObjectsOfTypeAll<StandaloneRenderedRoutePlayerExit>();
            if (candidates.Length > 1) throw new InvalidOperationException("Ambiguous rendered-route exit owner.");
            if (candidates.Length == 1)
            {
                if (!candidates[0].isActiveAndEnabled || candidates[0].name != "Standalone rendered-route test completion" ||
                    candidates[0].gameObject.hideFlags != HideFlags.HideAndDontSave || candidates[0].gameObject.scene.name != "DontDestroyOnLoad")
                    throw new InvalidOperationException("Unexpected rendered-route exit owner.");
                AttachExit(candidates[0]);
            }
        }
        private void Update()
        {
            if (!bound || quitting || audioClosed) return;
            try { MainOnly(); TryAttachExit(); Drain(); }
            catch (Exception error) { RecordFailure(error); }
        }
        private AudioConfigurationTelemetry.ConfigurationData ReadConfiguration()
        {
            MainOnly();
            if (quitting) throw new InvalidOperationException("Configuration reads are forbidden after managed quitting begins.");
            var value = reader.Read();
            return new AudioConfigurationTelemetry.ConfigurationData
            {
                readUtc = Utc(), readClockTicks = Stopwatch.GetTimestamp(), readThreadId = mainThread,
                sampleRate = value.sampleRate, dspBufferSize = value.dspBufferSize, numRealVoices = value.numRealVoices,
                numVirtualVoices = value.numVirtualVoices, speakerMode = value.speakerMode.ToString(), frame = Time.frameCount,
                sceneHandle = gameObject.scene.handle.GetRawData(), scenePath = gameObject.scene.path, sceneLoaded = gameObject.scene.isLoaded
            };
        }
        private void Drain()
        {
            int count = queue.DrainInto(drain);
            if (count == 0) return;
            AudioConfigurationTelemetry.ConfigurationData configuration;
            try { configuration = ReadConfiguration(); }
            catch { recordDrops += count; Array.Clear(drain, 0, count); throw; }
            for (int i = 0; i < count; i++)
            {
                var value = drain[i]; drain[i] = default;
                if (retained.Count == RecordCapacity) { recordDrops++; continue; }
                retained.Add(new AudioConfigurationTelemetry.EventData
                {
                    sequence = value.sequence, callbackUtc = new DateTime(value.utcTicks, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
                    callbackClockTicks = value.clockTicks, callbackThreadId = value.threadId, callbackPhaseToken = value.phase,
                    deviceWasChanged = value.deviceWasChanged, drainedPhaseToken = Volatile.Read(ref phaseToken), configuration = configuration,
                    observedDelaySeconds = (configuration.readClockTicks - value.clockTicks) / (double)Stopwatch.Frequency
                });
            }
        }
        private void CloseAudio(bool finalRead)
        {
            if (audioClosed) return;
            queue.CloseAcceptance();
            try { if (subscribed) { unsubscribeAttempts++; publisher.Unsubscribe(OnChanged); subscribed = false; } }
            catch (Exception error) { RecordFailure(error); }
            try
            {
                if (finalRead) { Drain(); final = ReadConfiguration(); }
                else { recordDrops += queue.DrainInto(drain); Array.Clear(drain, 0, drain.Length); }
            }
            catch (Exception error) { RecordFailure(error); }
            audioClosed = true;
        }
        public void CloseBeforeQuit()
        {
            MainOnly();
            try
            {
                if (preQuitObserved || quitting || audioClosed) throw new InvalidOperationException("Duplicate or late pre-quit audio closure.");
                preQuitObserved = true; Append("quit-intent", false);
                CloseAudio(true); DetachExit(); Append("audio-closed", false);
                var counts = queue.Snapshot();
                preQuitSha = sink.SavePreQuit(new PreQuit
                {
                    pid = pid, output_directory = output, run_id = runId, main_thread_id = mainThread,
                    stopwatch_frequency = Stopwatch.Frequency, is_editor = Application.isEditor, initial = initial, final = final,
                    exit_entity_id = exitEntityId,
                    callback_count = counts.observed, queue_drops = counts.dropped, record_drops = recordDrops, pending_callbacks = counts.pending,
                    counter_overflow = counts.overflowed, accepting_callbacks = counts.accepting, subscribed = subscribed,
                    subscribe_attempts = subscribeAttempts, unsubscribe_attempts = unsubscribeAttempts, retained = retained.ToArray(), errors = errors.ToArray()
                });
            }
            catch (Exception error) { RecordFailure(error); }
        }
        private void DetachExit()
        {
            if (ReferenceEquals(exit, null) || exitDetachCount != 0) return;
            exit.BeforeQuit -= CloseBeforeQuit; exitDetachCount++;
        }
        public bool ObserveWantsToQuit()
        {
            MainOnly();
            quitting = true; wantsCount++;
            if (!audioClosed) { RecordFailure(new InvalidOperationException("Quit bypassed pre-quit audio closure.")); CloseAudio(false); }
            AppendSafely("wants-to-quit");
            return true; // Never cancel or defer the application's existing quit.
        }
        public void ObserveApplicationQuit()
        {
            MainOnly(); quitting = true; quitCount++;
            if (!audioClosed) { RecordFailure(new InvalidOperationException("Application quit bypassed pre-quit audio closure.")); CloseAudio(false); }
            AppendSafely("application-quit"); DetachWants(); DetachExit();
        }
        private void OnApplicationQuit() { if (bound) ObserveApplicationQuit(); }
        private void DetachWants()
        {
            if (!wantsSubscribed) return;
            Application.wantsToQuit -= ObserveWantsToQuit; wantsSubscribed = false; wantsDetachCount++;
        }
        private void OnDestroy()
        {
            if (!bound) return;
            // This marker is authored only from the actual Unity OnDestroy.
            // No claim that native teardown or object destruction has finished.
            quitting = true; destroyCount++;
            if (!audioClosed) { RecordFailure(new InvalidOperationException("Observer destroyed before pre-quit closure.")); CloseAudio(false); }
            DetachWants(); DetachExit(); AppendSafely("destroyed");
            try
            {
                sink.Complete(new Completion
                {
                    pid = pid, output_directory = output, run_id = runId, main_thread_id = mainThread,
                    stopwatch_frequency = Stopwatch.Frequency, prequit_sha256 = preQuitSha ?? "", prequit_observed = preQuitObserved,
                    exit_entity_id = exitEntityId,
                    exit_attach_count = exitAttachCount, exit_detach_count = exitDetachCount,
                    wants_subscribe_count = wantsSubscribeCount, wants_detach_count = wantsDetachCount, wants_to_quit_count = wantsCount,
                    application_quit_count = quitCount, on_destroy_count = destroyCount, audio_closed = audioClosed,
                    subscribed = subscribed, wants_subscribed = wantsSubscribed, errors = errors.ToArray()
                });
            }
            catch (Exception error) { RecordFailure(error); }
        }
        public void RecordFailure(Exception error)
        {
            if (errors.Count < 16) errors.Add(error.GetType().Name + ": " + error.Message);
        }
        private void AppendSafely(string kind) { try { Append(kind, true); } catch (Exception error) { RecordFailure(error); } }
        private void Append(string kind, bool scalarOnly, string id = "", string name = "", bool suite = false, string result = "")
        {
            if (sequence >= MarkerCapacity) throw new InvalidOperationException("Audio lifecycle marker limit reached.");
            int token = sequence++;
            sink.Append(new Marker { pid = pid, run_id = runId, sequence = token, kind = kind, utc = Utc(),
                clock_ticks = Stopwatch.GetTimestamp(), thread_id = Thread.CurrentThread.ManagedThreadId,
                exit_entity_id = exitEntityId, quit_allowed = kind == "wants-to-quit",
                frame = scalarOnly ? -1 : Time.frameCount, phase_token = token, original_id = id, full_name = name, is_suite = suite, result = result });
            Volatile.Write(ref phaseToken, token);
        }
        private static string Utc() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        [Serializable] public sealed class Marker
        {
            public int schema_version = 1, pid, sequence, thread_id, frame, phase_token;
            public string profile = Profile, run_id, kind, utc, original_id, full_name, result;
            public long clock_ticks;
            public ulong exit_entity_id;
            public bool is_suite, quit_allowed;
        }
        [Serializable] public sealed class PreQuit
        {
            public int schema_version = 1, pid, main_thread_id, subscribe_attempts, unsubscribe_attempts, pending_callbacks;
            public string profile = Profile, output_directory, run_id, journal_sha256;
            public long stopwatch_frequency, callback_count, queue_drops, record_drops, journal_bytes;
            public ulong exit_entity_id;
            public int journal_records, queue_capacity = AudioConfigurationObservationQueue.Capacity, record_capacity = RecordCapacity;
            public bool is_editor, counter_overflow, accepting_callbacks, subscribed;
            public AudioConfigurationTelemetry.ConfigurationData initial, final;
            public AudioConfigurationTelemetry.EventData[] retained;
            public string[] errors;
        }
        [Serializable] public sealed class Completion
        {
            public int schema_version = 1, pid, main_thread_id, exit_attach_count, exit_detach_count, wants_subscribe_count, wants_detach_count;
            public int wants_to_quit_count, application_quit_count, on_destroy_count, journal_records;
            public string profile = Profile, output_directory, run_id, prequit_sha256, journal_sha256;
            public long stopwatch_frequency, journal_bytes;
            public ulong exit_entity_id;
            public bool prequit_observed, audio_closed, subscribed, wants_subscribed;
            public string[] errors;
            public string scope = "Managed passive callbacks, later main-thread configuration and actual managed lifecycle markers only. Not selected Windows stream, continuous audio, audibility, recovery or completed native teardown.";
        }
    }

    public sealed class AudioRunLifecycleFiles : IAudioRunLifecycleSink
    {
        private const int ByteLimit = 512 * 1024;
        private readonly string output;
        private readonly FileStream journal;
        private readonly MemoryStream accepted = new MemoryStream();
        private int records;
        public AudioRunLifecycleFiles(string directory)
        {
            output = RenderedRouteBinding.LocalPath(directory);
            journal = new FileStream(Path.Combine(output, "audio-run-lifecycle.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        public void Append(AudioRunLifecycleObserver.Marker marker)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(marker) + "\n");
            if (records >= AudioRunLifecycleObserver.MarkerCapacity || bytes.Length > 4096 || accepted.Length + bytes.Length > ByteLimit)
                throw new InvalidOperationException("Audio lifecycle journal exceeds its bounds.");
            journal.Write(bytes, 0, bytes.Length); journal.Flush();
            accepted.Write(bytes, 0, bytes.Length); records++;
        }
        private string JournalSha() { using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(accepted.ToArray())); }
        private static string Hex(byte[] value) => BitConverter.ToString(value).Replace("-", "").ToLowerInvariant();
        private void Save(string name, object value)
        {
            string json = JsonUtility.ToJson(value, true);
            if (Encoding.UTF8.GetByteCount(json) > ByteLimit) throw new InvalidOperationException("Audio lifecycle evidence exceeds its bounds.");
            using (var stream = new FileStream(Path.Combine(output, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.WriteLine(json);
        }
        public string SavePreQuit(AudioRunLifecycleObserver.PreQuit value)
        {
            value.journal_sha256 = JournalSha(); value.journal_bytes = accepted.Length; value.journal_records = records;
            Save("audio-run-lifecycle-prequit.json", value);
            return RenderedRouteBinding.Sha(Path.Combine(output, "audio-run-lifecycle-prequit.json"));
        }
        public void Complete(AudioRunLifecycleObserver.Completion value)
        {
            journal.Dispose();
            value.journal_sha256 = JournalSha(); value.journal_bytes = accepted.Length; value.journal_records = records;
            Save("audio-run-lifecycle-completion.json", value); accepted.Dispose();
        }
    }
}
#endif
