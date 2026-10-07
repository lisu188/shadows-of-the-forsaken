using System;
using System.IO;
using System.Text;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.Profiling;

// Opt-in observation for a locally driven Windows player. This component never
// sends gameplay commands, changes progression, controls time, or reads input.
[DisallowMultipleComponent]
public sealed class PlayerValidationEvidence : MonoBehaviour
{
    public LevelSessionController session;

    private const int MaximumEvents = 10000;
    private const int MaximumEnemies = 64;
    private const int MaximumSnapshotFailures = 10;
    private const double SnapshotInterval = .2;
    private const int MaximumPerformanceSummaries = 256;
    private const int MaximumRoomSummaries = 256;
    private static readonly UTF8Encoding Encoding = new UTF8Encoding(false);
    private string statusPath, temporaryPath, eventsPath, performancePath, roomPerformancePath;
    private LevelSessionController observedSession;
    private LevelProgressionController observedProgression;
    private bool active, failed, quitting;
    private int eventCount;
    private int consecutiveSnapshotFailures, ioFailureCount;
    private string lastIoOperation = "", lastIoHResult = "";
    private long sequence;
    private double nextSnapshot;
    private PlayerFrameMetrics frameMetrics;
    private HardwareEvidence hardware;
    private MemoryEvidence memory;
    private string performanceSessionId;
    private int performanceSummaries;
    private bool focused, paused, baselineValid, previouslyEligible;
    private double frameBaseline;
    private PlayerRoomFrameMetrics roomMetrics;
    private int roomSummaries;
    private long roomVisit;
    private string roomStartedUtc;
    private RoomConfiguration roomConfiguration;
    private bool roomConfigurationChanged;

    private void Start()
    {
        string directory = Environment.GetEnvironmentVariable("SHADOWS_PLAYER_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) { enabled = false; return; }
        try
        {
            if (session == null || session.progression == null || session.player == null || session.playerHealth == null)
                throw new InvalidOperationException("Evidence requires an assigned scene session.");
            if (!Path.IsPathRooted(directory)) throw new InvalidOperationException("Evidence directory must be absolute.");
            directory = Path.GetFullPath(directory);
            // The explicitly requested evidence is local; do not send snapshots
            // to UNC/network shares through an environment variable.
            if (directory.StartsWith(@"\\", StringComparison.Ordinal) || directory.StartsWith("//", StringComparison.Ordinal))
                throw new InvalidOperationException("Evidence directory must be local.");
            if (new DriveInfo(Path.GetPathRoot(directory)).DriveType == DriveType.Network)
                throw new InvalidOperationException("Evidence directory cannot use a mapped network drive.");
            Directory.CreateDirectory(directory);
            statusPath = Path.Combine(directory, "status.json");
            temporaryPath = Path.Combine(directory, ".status.tmp");
            eventsPath = Path.Combine(directory, "events.jsonl");
            performancePath = Path.Combine(directory, "performance.jsonl");
            roomPerformancePath = Path.Combine(directory, "room-performance.jsonl");
            if (File.Exists(eventsPath))
                using (var reader = new StreamReader(eventsPath, Encoding))
                    while (eventCount < MaximumEvents && reader.ReadLine() != null) eventCount++;
            if (File.Exists(performancePath))
                using (var reader = new StreamReader(performancePath, Encoding))
                    while (performanceSummaries < MaximumPerformanceSummaries && reader.ReadLine() != null) performanceSummaries++;
            if (File.Exists(roomPerformancePath))
                using (var reader = new StreamReader(roomPerformancePath, Encoding))
                    while (roomSummaries < MaximumRoomSummaries && reader.ReadLine() != null) roomSummaries++;
            roomVisit = roomSummaries;
            observedSession = session;
            observedProgression = session.progression;
            hardware = HardwareEvidence.Read();
            focused = Application.isFocused;
            BeginPerformanceSession(observedProgression.Snapshot.SessionId);
            observedSession.StateChanged += SessionChanged;
            observedProgression.Changed += ProgressionChanged;
            active = true;
            BeginRoomVisit(observedProgression.Snapshot);
            AppendEvent(CurrentEvent("started"));
            WriteSnapshot();
        }
        catch (Exception error) { DisableAfterFailure(error, "initialization"); }
    }

    private void Update()
    {
        if (!active || Time.realtimeSinceStartupAsDouble < nextSnapshot) return;
        WriteSnapshot();
    }

    private void LateUpdate()
    {
        if (!active) return;
        double now = Time.realtimeSinceStartupAsDouble;
        bool eligible = focused && !paused && Time.timeScale > 0 && observedSession != null && observedSession.IsRunning;
        PlayerFrameExclusion reason = paused || Time.timeScale <= 0 ? PlayerFrameExclusion.Paused :
            !focused ? PlayerFrameExclusion.Unfocused :
            observedSession == null || !observedSession.IsRunning ? PlayerFrameExclusion.NotRunning :
            !baselineValid || !previouslyEligible ? PlayerFrameExclusion.Transition : PlayerFrameExclusion.None;
        // A frame is eligible only when both boundaries are focused, unpaused,
        // Running. Focus/pause callbacks invalidate the baseline even when no
        // frames ran while suspended, excluding that unobservable interval.
        frameMetrics.Record(baselineValid ? now - frameBaseline : 0, reason);
        frameBaseline = now;
        baselineValid = true;
        previouslyEligible = eligible;
        ObserveRoom(now);
    }

    private void OnApplicationFocus(bool value)
    {
        focused = value; baselineValid = false;
        roomMetrics?.Invalidate(value ? PlayerFrameExclusion.Transition : PlayerFrameExclusion.Unfocused);
    }
    private void OnApplicationPause(bool value)
    {
        paused = value; baselineValid = false;
        roomMetrics?.Invalidate(value ? PlayerFrameExclusion.Paused : PlayerFrameExclusion.Transition);
    }

    private void BeginPerformanceSession(Guid token)
    {
        performanceSessionId = token.ToString();
        frameMetrics = new PlayerFrameMetrics();
        memory = new MemoryEvidence();
        baselineValid = false;
        previouslyEligible = false;
    }

    private void ProgressionChanged(ProgressionChange change)
    {
        if (!active) return;
        if (change.Kind == ProgressionChangeKind.SessionReset)
        {
            if (roomMetrics != null && roomMetrics.SessionId == change.Before.SessionId) EndRoomVisit("reset");
            WritePerformanceSummary("reset"); // Original session token and accumulated frames.
            BeginPerformanceSession(change.After.SessionId);
        }
        else if (change.Kind == ProgressionChangeKind.RoomEntered)
        {
            var current = observedProgression.Snapshot;
            // A stale notification cannot close or relabel a later visit. Check
            // identity, but retain the callback's immutable payload when opening.
            if (change.After.SessionId == current.SessionId && change.After.Room == current.Room &&
                (roomMetrics == null || (roomMetrics.SessionId == change.Before.SessionId && roomMetrics.Room == change.Before.Room.ToString())))
            {
                double boundary = Time.realtimeSinceStartupAsDouble;
                string boundaryUtc = Utc();
                EndRoomVisit(change.After.IsCompleted ? "completion" : "room-exit", boundary, boundaryUtc);
                BeginRoomVisit(change.After, boundary, boundaryUtc);
            }
        }
        // Copy the callback's immutable payload now. An event must not acquire a
        // newer token because another observer starts a different session later.
        AppendEvent(new Milestone
        {
            utc = Utc(), kind = "progression", change = change.Kind.ToString(),
            sessionId = change.After.SessionId.ToString(), beforeSessionId = change.Before.SessionId.ToString(),
            room = change.After.Room.ToString(), beforeRoom = change.Before.Room.ToString(),
            objective = change.Objective.ToString(), objectiveFlags = (int)change.After.CompletedObjectives,
            phase = observedSession != null ? observedSession.State.ToString() : "Missing",
            elapsedSeconds = observedSession != null ? observedSession.ElapsedSeconds : 0
        });
    }

    private void SessionChanged(LevelSessionState state)
    {
        if (!active) return;
        if (state == LevelSessionState.Running) BeginRoomVisit(observedProgression.Snapshot);
        else EndRoomVisit(state == LevelSessionState.Defeated ? "defeat" :
            state == LevelSessionState.Completed ? "completion" : "resetting");
        if (!active) return;
        var milestone = CurrentEvent("session");
        milestone.phase = state.ToString();
        AppendEvent(milestone);
        if (state == LevelSessionState.Defeated) WritePerformanceSummary("defeat");
        else if (state == LevelSessionState.Completed) WritePerformanceSummary("completion");
    }

    private Milestone CurrentEvent(string kind)
    {
        var snapshot = observedProgression.Snapshot;
        return new Milestone
        {
            utc = Utc(), kind = kind, sessionId = snapshot.SessionId.ToString(),
            beforeSessionId = snapshot.SessionId.ToString(), room = snapshot.Room.ToString(),
            beforeRoom = snapshot.Room.ToString(), objective = LevelObjective.None.ToString(),
            objectiveFlags = (int)snapshot.CompletedObjectives, phase = observedSession.State.ToString(),
            elapsedSeconds = observedSession.ElapsedSeconds
        };
    }

    private void AppendEvent(Milestone milestone)
    {
        if (!active || eventCount >= MaximumEvents) return;
        try
        {
            milestone.sequence = ++sequence;
            File.AppendAllText(eventsPath, JsonUtility.ToJson(milestone) + "\n", Encoding);
            eventCount++;
        }
        catch (Exception error) { DisableAfterFailure(error, "milestone-append"); }
    }

    private void WriteSnapshot()
    {
        if (!active) return;
        nextSnapshot = Time.realtimeSinceStartupAsDouble + SnapshotInterval;
        string operation = "snapshot-serialize";
        try
        {
            var progress = observedProgression.Snapshot;
            var player = observedSession.player;
            var health = observedSession.playerHealth;
            var melee = player.GetComponent<MeleeCombat>();
            var interaction = player.GetComponent<PlayerInteractor>();
            var encounters = observedSession.encounters;
            int count = encounters == null ? 0 : Math.Min(encounters.Length, MaximumEnemies);
            var snapshot = new Snapshot
            {
                utc = Utc(), sequence = ++sequence, phase = observedSession.State.ToString(),
                sessionId = progress.SessionId.ToString(), room = progress.Room.ToString(),
                objectives = progress.CompletedObjectives.ToString(), objectiveFlags = (int)progress.CompletedObjectives,
                elapsedSeconds = observedSession.ElapsedSeconds,
                ioFailureCount = ioFailureCount, lastIoOperation = lastIoOperation, lastIoHResult = lastIoHResult,
                performanceSummariesWritten = performanceSummaries,
                performanceSummaryLimitReached = performanceSummaries >= MaximumPerformanceSummaries,
                roomPerformanceSummariesWritten = roomSummaries,
                roomPerformanceSummaryLimitReached = roomSummaries >= MaximumRoomSummaries,
                hardware = hardware, performance = frameMetrics.Snapshot(false), memory = ReadMemory(),
                player = new PlayerSnapshot
                {
                    position = player.transform.position, yaw = player.transform.eulerAngles.y,
                    health = health.Current, maximumHealth = health.Maximum, lifeId = health.LifeId.ToString(),
                    controlsEnabled = player.ControlsEnabled,
                    attackPhase = melee != null ? melee.Phase.ToString() : "Missing",
                    canAttack = melee != null && melee.CanAttack,
                    selectedInteraction = interaction != null && interaction.SelectedTarget != null ? interaction.SelectedTarget.name : "",
                    canInteract = interaction != null && interaction.CanInteract
                },
                enemies = new EnemySnapshot[count]
            };
            for (int i = 0; i < count; i++)
            {
                var encounter = encounters[i];
                if (encounter == null) { snapshot.enemies[i] = new EnemySnapshot { name = "Missing" }; continue; }
                var enemyHealth = encounter.GetComponent<CombatHealth>();
                var enemyMelee = encounter.GetComponent<MeleeCombat>();
                snapshot.enemies[i] = new EnemySnapshot
                {
                    name = encounter.name, objective = encounter.objective.ToString(),
                    position = encounter.transform.position, yaw = encounter.transform.eulerAngles.y,
                    health = enemyHealth != null ? enemyHealth.Current : 0,
                    maximumHealth = enemyHealth != null ? enemyHealth.Maximum : 0,
                    lifeId = enemyHealth != null ? enemyHealth.LifeId.ToString() : "",
                    attackPhase = enemyMelee != null ? enemyMelee.Phase.ToString() : "Missing",
                    encounterPhase = encounter.Phase.ToString(), canAttack = enemyMelee != null && enemyMelee.CanAttack,
                    activated = encounter.IsActivated, credited = encounter.DeathCredited, pendingCredit = encounter.HasPendingDeath
                };
            }
            string json = JsonUtility.ToJson(snapshot);
            operation = "snapshot-write";
            File.WriteAllText(temporaryPath, json, Encoding);
            operation = "snapshot-publish";
            if (File.Exists(statusPath)) File.Replace(temporaryPath, statusPath, null);
            else File.Move(temporaryPath, statusPath);
            consecutiveSnapshotFailures = 0;
        }
        catch (IOException error)
        {
            // File scanners/readers can briefly contend with atomic publication.
            // Keep the last complete status and retry at the normal interval;
            // persistent IO errors stop observation without touching gameplay.
            ioFailureCount++;
            lastIoOperation = operation;
            lastIoHResult = HResult(error);
            if (++consecutiveSnapshotFailures >= MaximumSnapshotFailures)
                DisableAfterFailure(error, operation);
        }
        catch (Exception error) { DisableAfterFailure(error, operation); }
    }

    private void OnApplicationQuit()
    {
        if (!active) return;
        quitting = true;
        EndRoomVisit("normal-exit");
        WritePerformanceSummary("normal-exit");
        WriteLifecycleEvent("normal-exit");
        WriteSnapshot();
        Unsubscribe();
    }

    private void OnDisable()
    {
        if (active && !quitting)
        {
            EndRoomVisit("observer-disabled");
            WritePerformanceSummary("observer-disabled");
            WriteLifecycleEvent("observer-disabled");
            WriteSnapshot();
        }
        Unsubscribe();
    }

    private void WriteLifecycleEvent(string kind)
    {
        // Scene unload may destroy another root before this observer disables.
        // Keep the last rolling snapshot instead of dereferencing a dead source.
        if (!active || observedProgression == null || observedSession == null) return;
        try { AppendEvent(CurrentEvent(kind)); }
        catch (Exception error) { DisableAfterFailure(error, "lifecycle-event"); }
    }

    private MemoryEvidence ReadMemory()
    {
        memory.managedEstimatedBytes = GC.GetTotalMemory(false); // Never force a collection.
        memory.unityAllocatorUsedBytes = Profiler.GetTotalAllocatedMemoryLong();
        memory.unityAllocatorReservedBytes = Profiler.GetTotalReservedMemoryLong();
        memory.unityAllocatorUnusedReservedBytes = Profiler.GetTotalUnusedReservedMemoryLong();
        memory.unityAllocatorAvailable = memory.unityAllocatorReservedBytes > 0;
        memory.peakSampledManagedEstimatedBytes = Math.Max(memory.peakSampledManagedEstimatedBytes, memory.managedEstimatedBytes);
        memory.peakSampledUnityAllocatorUsedBytes = Math.Max(memory.peakSampledUnityAllocatorUsedBytes, memory.unityAllocatorUsedBytes);
        memory.samples++;
        return memory;
    }

    private void WritePerformanceSummary(string reason)
    {
        if (!active || frameMetrics == null || performanceSummaries >= MaximumPerformanceSummaries) return;
        try
        {
            var summary = new PerformanceEvidence
            {
                utc = Utc(), reason = reason, sessionId = performanceSessionId,
                frameIntervals = frameMetrics.Snapshot(true), hardware = hardware, memory = ReadMemory(),
                screenWidth = Screen.width, screenHeight = Screen.height, fullScreenMode = Screen.fullScreenMode.ToString(),
                qualityLevel = QualitySettings.GetQualityLevel(), vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate,
                provisionalResolutionMatches = Screen.width == 1280 && Screen.height == 720
            };
            File.AppendAllText(performancePath, JsonUtility.ToJson(summary) + "\n", Encoding);
            performanceSummaries++;
        }
        catch (Exception error) { DisableAfterFailure(error, "performance-append"); }
    }

    private void BeginRoomVisit(LevelProgressSnapshot snapshot, double? now = null, string utc = null)
    {
        if (!active || roomMetrics != null || roomSummaries >= MaximumRoomSummaries || snapshot.IsCompleted ||
            observedSession == null || observedProgression == null || !observedSession.IsRunning) return;
        var current = observedProgression.Snapshot;
        if (snapshot.SessionId != current.SessionId || snapshot.Room != current.Room) return;
        roomMetrics = new PlayerRoomFrameMetrics(snapshot.SessionId, snapshot.Room.ToString(), ++roomVisit,
            now ?? Time.realtimeSinceStartupAsDouble);
        roomStartedUtc = utc ?? Utc(); roomConfiguration = RoomConfiguration.Read(); roomConfigurationChanged = false;
    }

    private void ObserveRoom(double now)
    {
        if (!active || observedProgression == null || observedSession == null) return;
        var snapshot = observedProgression.Snapshot;
        if (roomMetrics != null && (roomMetrics.SessionId != snapshot.SessionId || roomMetrics.Room != snapshot.Room.ToString()))
            EndRoomVisit("identity-mismatch", now); // A missed callback is never charged to the new identity.
        BeginRoomVisit(snapshot, now);
        if (roomMetrics == null) return;
        roomConfigurationChanged |= !roomConfiguration.MatchesCurrent();
        PlayerFrameExclusion reason = paused || Time.timeScale <= 0 ? PlayerFrameExclusion.Paused :
            !focused ? PlayerFrameExclusion.Unfocused : !observedSession.IsRunning ? PlayerFrameExclusion.NotRunning : PlayerFrameExclusion.None;
        roomMetrics.Record(now, snapshot.SessionId, snapshot.Room.ToString(), reason);
    }

    private void EndRoomVisit(string reason, double? now = null, string utc = null)
    {
        if (!active || roomMetrics == null) return;
        var visit = roomMetrics;
        roomMetrics = null; // Prevent duplicate output when failure/quit invokes OnDisable.
        if (roomSummaries >= MaximumRoomSummaries) return;
        try
        {
            var summary = new RoomPerformanceEvidence
            {
                reason = reason, startedUtc = roomStartedUtc, endedUtc = utc ?? Utc(),
                visit = visit.Close(now ?? Time.realtimeSinceStartupAsDouble), hardware = hardware,
                configurationAtStart = roomConfiguration, configurationAtEnd = RoomConfiguration.Read(),
                configurationChanged = roomConfigurationChanged || !roomConfiguration.MatchesCurrent()
            };
            File.AppendAllText(roomPerformancePath, JsonUtility.ToJson(summary) + "\n", Encoding);
            roomSummaries++;
        }
        catch (Exception error) { DisableAfterFailure(error, "room-performance-append"); }
    }

    private void Unsubscribe()
    {
        if (observedSession != null) observedSession.StateChanged -= SessionChanged;
        if (observedProgression != null) observedProgression.Changed -= ProgressionChanged;
        observedSession = null;
        observedProgression = null;
        active = false;
    }

    private void DisableAfterFailure(Exception error, string operation)
    {
        Unsubscribe();
        if (failed) return;
        failed = true;
        // Do not dump the environment, path, or exception data into game logs.
        Debug.LogWarning("Local player validation evidence disabled after " + operation + ": " +
            error.GetType().Name + " (" + HResult(error) + ").", this);
        enabled = false;
    }

    private static string Utc() => DateTime.UtcNow.ToString("O");
    private static string HResult(Exception error) => "0x" + unchecked((uint)error.HResult).ToString("X8");

    [Serializable]
    private sealed class Snapshot
    {
        public string utc, phase, sessionId, room, objectives, lastIoOperation, lastIoHResult;
        public long sequence;
        public int objectiveFlags, ioFailureCount, performanceSummariesWritten, roomPerformanceSummariesWritten;
        public bool performanceSummaryLimitReached, roomPerformanceSummaryLimitReached;
        public float elapsedSeconds;
        public PlayerSnapshot player;
        public EnemySnapshot[] enemies;
        public HardwareEvidence hardware;
        public PlayerFrameSummary performance;
        public MemoryEvidence memory;
    }
    [Serializable]
    private sealed class PlayerSnapshot
    {
        public Vector3 position;
        public float yaw;
        public int health, maximumHealth;
        public string lifeId, attackPhase, selectedInteraction;
        public bool controlsEnabled, canAttack, canInteract;
    }
    [Serializable]
    private sealed class EnemySnapshot
    {
        public string name, objective, lifeId, attackPhase, encounterPhase;
        public Vector3 position;
        public float yaw;
        public int health, maximumHealth;
        public bool canAttack, activated, credited, pendingCredit;
    }
    [Serializable]
    private sealed class Milestone
    {
        public string utc, kind, change, phase, sessionId, beforeSessionId, room, beforeRoom, objective;
        public long sequence;
        public int objectiveFlags;
        public float elapsedSeconds;
    }

    [Serializable]
    private sealed class PerformanceEvidence
    {
        public string utc, reason, sessionId, fullScreenMode;
        public int screenWidth, screenHeight, qualityLevel, vSyncCount, targetFrameRate;
        public bool provisionalResolutionMatches;
        public string measurement = "Observed realtime frame intervals, not CPU/GPU profiling. Warmup: first 5 eligible seconds; boundary-crossing warmup frame excluded in full. No eligible outliers trimmed.";
        public string provisionalBudget = "Implementation choice: 60 FPS intent at 1280x720; mean and conservative approximate p95 <=33.3 ms. Memory reported without a pass threshold. Not a DOCX requirement or human-playtest result.";
        public PlayerFrameSummary frameIntervals;
        public HardwareEvidence hardware;
        public MemoryEvidence memory;
    }

    [Serializable]
    private sealed class RoomPerformanceEvidence
    {
        public int schemaVersion = 1;
        public string reason, startedUtc, endedUtc;
        public string measurement = "Per-visit observed realtime frame intervals, not CPU/GPU or per-light profiling. Each visit has its own first-5-eligible-seconds warmup. Only eligible endpoints within one room/session can be measured; boundary intervals are excluded and the final partial interval is reported separately. Empty/short visits are not performance qualification.";
        public PlayerRoomFrameSummary visit;
        public HardwareEvidence hardware;
        public RoomConfiguration configurationAtStart, configurationAtEnd;
        public bool configurationChanged;
    }

    [Serializable]
    private sealed class RoomConfiguration
    {
        public int screenWidth, screenHeight, qualityLevel, vSyncCount, targetFrameRate;
        public string fullScreenMode;
        private FullScreenMode mode;
        public static RoomConfiguration Read() => new RoomConfiguration
        {
            screenWidth = Screen.width, screenHeight = Screen.height, qualityLevel = QualitySettings.GetQualityLevel(),
            vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate,
            fullScreenMode = Screen.fullScreenMode.ToString(), mode = Screen.fullScreenMode
        };
        public bool MatchesCurrent() => screenWidth == Screen.width && screenHeight == Screen.height &&
            qualityLevel == QualitySettings.GetQualityLevel() && vSyncCount == QualitySettings.vSyncCount &&
            targetFrameRate == Application.targetFrameRate && mode == Screen.fullScreenMode;
    }

    [Serializable]
    private sealed class HardwareEvidence
    {
        public string unityVersion, platform, operatingSystem, processor, graphicsDevice, graphicsVendor, graphicsApi;
        public int processorCount, processorFrequencyMHz, systemMemoryMB, graphicsMemoryMB;
        public bool isEditor;
        public static HardwareEvidence Read() => new HardwareEvidence
        {
            unityVersion = Application.unityVersion, platform = Application.platform.ToString(), isEditor = Application.isEditor,
            operatingSystem = SystemInfo.operatingSystem, processor = SystemInfo.processorType,
            processorCount = SystemInfo.processorCount, processorFrequencyMHz = SystemInfo.processorFrequency,
            systemMemoryMB = SystemInfo.systemMemorySize, graphicsDevice = SystemInfo.graphicsDeviceName,
            graphicsVendor = SystemInfo.graphicsDeviceVendor, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            graphicsMemoryMB = SystemInfo.graphicsMemorySize
        };
    }

    [Serializable]
    private sealed class MemoryEvidence
    {
        public string meaning = "GC.GetTotalMemory(false) managed estimate; Unity internal allocator used/reserved/unused bytes (0 can mean unavailable), not process working set, complete native memory, GPU usage, or allocation-per-frame. Sampled every snapshot/lifecycle event; peaks may miss shorter spikes.";
        public long samples, managedEstimatedBytes, unityAllocatorUsedBytes, unityAllocatorReservedBytes, unityAllocatorUnusedReservedBytes;
        public long peakSampledManagedEstimatedBytes, peakSampledUnityAllocatorUsedBytes;
        public bool unityAllocatorAvailable;
    }
}
