#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using UnityEngine;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // Managed scalars only. The callback's thread is deliberately not assumed
    // to be Unity's main thread, and this queue has no Unity dependency.
    public sealed class AudioConfigurationObservationQueue
    {
        public const int Capacity = 128;
        public readonly struct Entry
        {
            public readonly long sequence, utcTicks, clockTicks;
            public readonly int threadId, phase;
            public readonly bool deviceWasChanged;
            public Entry(long sequence, long utcTicks, long clockTicks, int threadId, int phase, bool changed)
            { this.sequence = sequence; this.utcTicks = utcTicks; this.clockTicks = clockTicks; this.threadId = threadId; this.phase = phase; deviceWasChanged = changed; }
        }
        public readonly struct Counts
        {
            public readonly long observed, dropped;
            public readonly int pending;
            public readonly bool accepting, overflowed;
            public Counts(long observed, long dropped, int pending, bool accepting, bool overflowed)
            { this.observed = observed; this.dropped = dropped; this.pending = pending; this.accepting = accepting; this.overflowed = overflowed; }
        }
        private readonly object gate = new object();
        private readonly Entry[] entries = new Entry[Capacity];
        private int head, count;
        private long observed, dropped;
        private bool accepting = true, overflowed;
        public bool Record(bool changed, long utcTicks, long clockTicks, int threadId, int phase)
        {
            lock (gate)
            {
                if (!accepting) return false;
                if (observed == long.MaxValue) { overflowed = true; return false; }
                observed++;
                if (count == Capacity)
                {
                    if (dropped == long.MaxValue) overflowed = true; else dropped++;
                    return false;
                }
                entries[(head + count) % Capacity] = new Entry(observed, utcTicks, clockTicks, threadId, phase, changed);
                count++;
                return true;
            }
        }
        public int DrainInto(Entry[] destination)
        {
            if (destination == null || destination.Length < Capacity) throw new ArgumentException("A complete bounded drain buffer is required.");
            lock (gate)
            {
                int available = count;
                for (int i = 0; i < available; i++)
                {
                    destination[i] = entries[head]; entries[head] = default;
                    head = (head + 1) % Capacity;
                }
                count = 0;
                return available;
            }
        }
        public void CloseAcceptance() { lock (gate) accepting = false; }
        public Counts Snapshot() { lock (gate) return new Counts(observed, dropped, count, accepting, overflowed); }
    }

    public interface IAudioConfigurationEvents
    {
        void Subscribe(Action<bool> callback);
        void Unsubscribe(Action<bool> callback);
    }
    public interface IAudioConfigurationReader { AudioConfiguration Read(); }

    // Test-only owned component. Only Awake/Update/fixture lifecycle calls read
    // Unity state. No Reset, endpoint selection, PCM change or log suppression.
    public sealed class AudioConfigurationTelemetry : MonoBehaviour
    {
        public const int RecordCapacity = 256, PhaseCapacity = 16;
        private readonly AudioConfigurationObservationQueue queue = new AudioConfigurationObservationQueue();
        private readonly AudioConfigurationObservationQueue.Entry[] drain = new AudioConfigurationObservationQueue.Entry[AudioConfigurationObservationQueue.Capacity];
        private readonly List<EventData> retained = new List<EventData>(RecordCapacity);
        private readonly List<PhaseData> phases = new List<PhaseData>(PhaseCapacity);
        private readonly List<string> errors = new List<string>(8);
        private IAudioConfigurationEvents publisher;
        private IAudioConfigurationReader reader;
        private int mainThread, phaseToken;
        private long recordDrops;
        private bool bound, subscribed, closed, phaseLimit;
        private string phase;
        private Evidence data;
        private void Awake() { mainThread = Thread.CurrentThread.ManagedThreadId; }
        public int MainThreadId => mainThread;
        private void MainThreadOnly()
        {
            if (mainThread == 0 || Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new InvalidOperationException("Audio configuration reads require the thread captured by Unity Awake.");
        }
        public void Begin(string caseName, string outputDirectory, string evidencePath, IAudioConfigurationEvents events, IAudioConfigurationReader configuration)
        {
            MainThreadOnly();
            if (bound || events == null || configuration == null) throw new InvalidOperationException("An observer lease can be bound exactly once.");
            publisher = events; reader = configuration; bound = true;
            int processId;
            using (var process = Process.GetCurrentProcess()) processId = process.Id;
            data = new Evidence
            {
                pid = processId, caseFullName = caseName, outputDirectory = outputDirectory,
                evidencePath = evidencePath, probeSessionId = Guid.NewGuid().ToString("N"), mainThreadId = mainThread,
                beganUtc = Utc(), stopwatchFrequency = Stopwatch.Frequency,
                sceneHandle = gameObject.scene.handle.GetRawData(), scenePath = gameObject.scene.path, sceneName = gameObject.scene.name,
                sceneLoaded = gameObject.scene.isLoaded
            };
            MarkPhase("prepared");
            try
            {
                // Own even an attempted subscription: a publisher may attach
                // before throwing, so exceptional setup must still detach it.
                subscribed = true; publisher.Subscribe(OnConfigurationChanged);
                data.initial = ReadConfiguration();
            }
            catch
            {
                Close();
                throw;
            }
        }
        // Never call Unity, a configuration reader, logging, serialization or
        // file APIs here. Timestamps/thread/phase are immutable managed scalars.
        private void OnConfigurationChanged(bool deviceWasChanged)
        {
            queue.Record(deviceWasChanged, DateTime.UtcNow.Ticks, Stopwatch.GetTimestamp(),
                Thread.CurrentThread.ManagedThreadId, Volatile.Read(ref phaseToken));
        }
        public void MarkPhase(string value)
        {
            MainThreadOnly();
            if (!bound || closed || value == phase) return;
            if (phases.Count == PhaseCapacity) { phaseLimit = true; return; }
            phase = value;
            var boundary = new PhaseData { token = phases.Count, name = value, utc = Utc(), clockTicks = Stopwatch.GetTimestamp(), frame = Time.frameCount };
            phases.Add(boundary); Volatile.Write(ref phaseToken, boundary.token);
        }
        private void Update() { if (bound && !closed) DrainOnMainThread(); }
        public void DrainOnMainThread()
        {
            MainThreadOnly();
            if (!bound || closed) return;
            DrainAccepted();
        }
        private void DrainAccepted()
        {
            int count = queue.DrainInto(drain);
            if (count == 0) return;
            int room = RecordCapacity - retained.Count;
            ConfigurationData observed = null;
            try { if (room > 0) observed = ReadConfiguration(); }
            catch (Exception error)
            {
                recordDrops += count;
                Array.Clear(drain, 0, count);
                AddError(error);
                throw;
            }
            for (int i = 0; i < count; i++)
            {
                var item = drain[i]; drain[i] = default;
                if (retained.Count == RecordCapacity) { recordDrops++; continue; }
                retained.Add(new EventData
                {
                    sequence = item.sequence, callbackUtc = new DateTime(item.utcTicks, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
                    callbackClockTicks = item.clockTicks, callbackThreadId = item.threadId, callbackPhaseToken = item.phase,
                    deviceWasChanged = item.deviceWasChanged, drainedPhaseToken = Volatile.Read(ref phaseToken), configuration = observed,
                    observedDelaySeconds = (observed.readClockTicks - item.clockTicks) / (double)Stopwatch.Frequency
                });
            }
        }
        private ConfigurationData ReadConfiguration()
        {
            MainThreadOnly();
            AudioConfiguration configuration = reader.Read();
            return new ConfigurationData
            {
                readUtc = Utc(), readClockTicks = Stopwatch.GetTimestamp(), readThreadId = Thread.CurrentThread.ManagedThreadId,
                sampleRate = configuration.sampleRate, dspBufferSize = configuration.dspBufferSize,
                numRealVoices = configuration.numRealVoices, numVirtualVoices = configuration.numVirtualVoices,
                speakerMode = configuration.speakerMode.ToString(), frame = Time.frameCount,
                sceneHandle = gameObject.scene.handle.GetRawData(), scenePath = gameObject.scene.path, sceneLoaded = gameObject.scene.isLoaded
            };
        }
        public Evidence Close()
        {
            MainThreadOnly();
            if (!bound) return null;
            if (closed) return data;
            MarkPhase("observer-closing");
            // Close acceptance under the same lock as Record. A callback whose
            // delegate was already captured can run later but cannot alter data.
            queue.CloseAcceptance();
            try
            {
                if (subscribed) { publisher.Unsubscribe(OnConfigurationChanged); subscribed = false; }
            }
            catch (Exception error) { AddError(error); }
            try { DrainAccepted(); } catch (Exception error) { AddError(error); }
            try { data.final = ReadConfiguration(); } catch (Exception error) { AddError(error); }
            var counts = queue.Snapshot();
            data.callbackCount = counts.observed; data.queueDrops = counts.dropped; data.pendingCallbacks = counts.pending;
            data.counterOverflow = counts.overflowed; data.recordDrops = recordDrops; data.phaseLimitReached = phaseLimit;
            data.retained = retained.ToArray(); data.phases = phases.ToArray(); data.errors = errors.ToArray();
            data.subscribed = subscribed; data.acceptingCallbacks = counts.accepting; data.closed = true; data.closedUtc = Utc();
            closed = true;
            return data;
        }
        private void AddError(Exception error) { if (errors.Count < 8) errors.Add(error.GetType().Name + ": " + error.Message); }
        private void OnDestroy() { if (bound && !closed) Close(); }
        private static string Utc() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        public sealed class UnityEvents : IAudioConfigurationEvents
        {
            private AudioSettings.AudioConfigurationChangeHandler handler;
            public void Subscribe(Action<bool> callback)
            {
                if (handler != null) throw new InvalidOperationException("Duplicate owned audio subscription.");
                handler = callback.Invoke; AudioSettings.OnAudioConfigurationChanged += handler;
            }
            public void Unsubscribe(Action<bool> callback)
            {
                if (handler == null) return;
                AudioSettings.OnAudioConfigurationChanged -= handler; handler = null;
            }
        }
        public sealed class UnityReader : IAudioConfigurationReader { public AudioConfiguration Read() => AudioSettings.GetConfiguration(); }

        [Serializable] public sealed class Evidence
        {
            public int schemaVersion = 1, pid, mainThreadId, pendingCallbacks;
            public ulong sceneHandle;
            public string profile = "audio-mixer-passive-configuration", caseFullName, outputDirectory, evidencePath, probeSessionId;
            public string beganUtc, closedUtc, scenePath, sceneName;
            public bool sceneLoaded, subscribed, acceptingCallbacks, closed, counterOverflow, phaseLimitReached, ownedObserverDestroyed;
            public long stopwatchFrequency, callbackCount, queueDrops, recordDrops;
            public int queueCapacity = AudioConfigurationObservationQueue.Capacity, recordCapacity = RecordCapacity;
            public ConfigurationData initial, final;
            public EventData[] retained;
            public PhaseData[] phases;
            public string[] errors;
            public string scope = "Passive test-only audio configuration observations. Event scalars are captured on the actual callback thread; configuration is read later on Unity's main thread. No reset, endpoint selection, simulated native event, stream health or audible recovery is asserted. probeSessionId identifies this observer lease, not a game progression session.";
        }
        [Serializable] public sealed class ConfigurationData
        {
            public string readUtc, speakerMode, scenePath;
            public long readClockTicks;
            public int readThreadId, sampleRate, dspBufferSize, numRealVoices, numVirtualVoices, frame;
            public ulong sceneHandle;
            public bool sceneLoaded;
        }
        [Serializable] public sealed class EventData
        {
            public long sequence, callbackClockTicks;
            public string callbackUtc;
            public int callbackThreadId, callbackPhaseToken, drainedPhaseToken;
            public bool deviceWasChanged;
            public double observedDelaySeconds;
            public ConfigurationData configuration;
        }
        [Serializable] public sealed class PhaseData { public int token, frame; public string name, utc; public long clockTicks; }
    }
}
#endif
