using System;
using System.IO;
using System.Text;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;

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
    private static readonly UTF8Encoding Encoding = new UTF8Encoding(false);
    private string statusPath, temporaryPath, eventsPath;
    private LevelSessionController observedSession;
    private LevelProgressionController observedProgression;
    private bool active, failed, quitting;
    private int eventCount;
    private int consecutiveSnapshotFailures, ioFailureCount;
    private string lastIoOperation = "", lastIoHResult = "";
    private long sequence;
    private double nextSnapshot;

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
            if (File.Exists(eventsPath))
                using (var reader = new StreamReader(eventsPath, Encoding))
                    while (eventCount < MaximumEvents && reader.ReadLine() != null) eventCount++;
            observedSession = session;
            observedProgression = session.progression;
            observedSession.StateChanged += SessionChanged;
            observedProgression.Changed += ProgressionChanged;
            active = true;
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

    private void ProgressionChanged(ProgressionChange change)
    {
        if (!active) return;
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
        var milestone = CurrentEvent("session");
        milestone.phase = state.ToString();
        AppendEvent(milestone);
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
        WriteLifecycleEvent("normal-exit");
        WriteSnapshot();
        Unsubscribe();
    }

    private void OnDisable()
    {
        if (active && !quitting)
        {
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
        public int objectiveFlags, ioFailureCount;
        public float elapsedSeconds;
        public PlayerSnapshot player;
        public EnemySnapshot[] enemies;
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
}
