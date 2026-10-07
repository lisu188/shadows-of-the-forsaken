using System;
using System.Collections.Generic;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Encounters;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;

public enum LevelSessionState { Running, Defeated, Completed, Resetting }

[DisallowMultipleComponent]
public sealed class LevelSessionController : MonoBehaviour
{
    public LevelProgressionController progression;
    public PlayerMovement player;
    public CombatHealth playerHealth;
    public CameraFollow follow;
    public Transform spawn;
    public EnemyEncounter[] encounters = Array.Empty<EnemyEncounter>();

    public LevelSessionState State { get; private set; } = LevelSessionState.Resetting;
    public bool IsRunning => isActiveAndEnabled && initialized && State == LevelSessionState.Running;
    public bool CanRestart => isActiveAndEnabled && initialized &&
        (State == LevelSessionState.Defeated || State == LevelSessionState.Completed);
    public float ElapsedSeconds { get; private set; }
    public event Action<LevelSessionState> StateChanged;

    private LevelProgressionController observedProgression;
    private PlayerMovement observedPlayer;
    private CombatHealth observedHealth;
    private bool initialized, resetting, notifyingState, externalResetPending;
    private bool focused = true;
    private bool paused, timerBaselineValid;
    private double timerBaseline;
    private Guid worldSession;
    private readonly List<CombatHealth> healths = new List<CombatHealth>();
    private readonly List<ProgressionGate> gates = new List<ProgressionGate>();

    private void OnEnable()
    {
        timerBaselineValid = false;
        observedProgression = progression;
        observedPlayer = player;
        observedHealth = playerHealth;
        if (observedProgression != null) observedProgression.Changed += ProgressChanged;
        if (observedPlayer != null) observedPlayer.RestartRequested += RestartRequested;
        if (observedHealth != null) observedHealth.Died += PlayerDied;
        focused = Application.isFocused;
        if (!initialized) return;
        CollectParticipants();
        if (progression != null && worldSession != progression.Snapshot.SessionId)
        {
            externalResetPending = true;
            ChangeState(LevelSessionState.Resetting);
        }
        else if (playerHealth != null && !playerHealth.IsAlive) ChangeState(LevelSessionState.Defeated);
        else if (progression != null && progression.Snapshot.IsCompleted) ChangeState(LevelSessionState.Completed);
        else ApplyPermissions();
    }

    private void Start() => InitializeSession();

    private void OnDisable()
    {
        timerBaselineValid = false;
        if (observedProgression != null) observedProgression.Changed -= ProgressChanged;
        if (observedPlayer != null) observedPlayer.RestartRequested -= RestartRequested;
        if (observedHealth != null) observedHealth.Died -= PlayerDied;
        observedProgression = null; observedPlayer = null; observedHealth = null;
        ApplyPermissions();
    }

    private void OnDestroy() { StateChanged = null; }
    private void OnApplicationFocus(bool value) { focused = value; timerBaselineValid = false; }
    private void OnApplicationPause(bool value) { paused = value; timerBaselineValid = false; }

    private void Update()
    {
        if (externalResetPending && !resetting && !notifyingState)
        {
            externalResetPending = false;
            ResetWorld(false);
        }
        if (IsRunning && focused && !paused && Time.timeScale > 0)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (timerBaselineValid) ElapsedSeconds += (float)Math.Max(0, now - timerBaseline);
            timerBaseline = now;
            timerBaselineValid = true;
        }
        else timerBaselineValid = false;
    }

    public bool InitializeSession()
    {
        if (initialized || !isActiveAndEnabled) return false;
        if (progression == null || player == null || playerHealth == null || spawn == null ||
            playerHealth.gameObject != player.gameObject || playerHealth.progression != progression ||
            progression.gameObject.scene != gameObject.scene || player.gameObject.scene != gameObject.scene ||
            observedProgression != progression || observedPlayer != player || observedHealth != playerHealth)
        {
            Debug.LogError("Level session requires a scene-owned progression, player, matching health and safe spawn before enable.", this);
            enabled = false;
            return false;
        }
        initialized = true;
        return ResetWorld(true);
    }

    public bool RestartSession()
    {
        if (!CanRestart || resetting || notifyingState) return false;
        return ResetWorld(true);
    }

    public bool TryEnterRoom(LevelRoom room)
    {
        if (!IsRunning || resetting || notifyingState || playerHealth == null || !playerHealth.IsAlive ||
            progression == null || !progression.isActiveAndEnabled ||
            room < LevelRoom.Courtyard || room > LevelRoom.Exit) return false;
        var snapshot = progression.Snapshot;
        if (snapshot.Room == room) return true;
        return progression.TryEnter(room, snapshot.SessionId);
    }

    private void RestartRequested() => RestartSession();

    private void PlayerDied(HealthChange change)
    {
        if (!IsRunning || !change.IsDeath || playerHealth == null || progression == null ||
            change.SessionId != progression.Snapshot.SessionId || change.LifeId != playerHealth.LifeId ||
            playerHealth.IsAlive) return;
        ChangeState(LevelSessionState.Defeated);
    }

    private void ProgressChanged(ProgressionChange change)
    {
        // These notifications may only change presentation/permissions. The
        // progression model rejects mutation from its own Changed callbacks.
        if (change.Kind == ProgressionChangeKind.SessionReset && initialized && !resetting)
        {
            externalResetPending = true;
            ChangeState(LevelSessionState.Resetting);
        }
        else if (change.After.IsCompleted && IsRunning)
            ChangeState(playerHealth != null && playerHealth.IsAlive
                ? LevelSessionState.Completed : LevelSessionState.Defeated);
    }

    private bool ResetWorld(bool createSession)
    {
        if (resetting || !initialized || !isActiveAndEnabled || progression == null || !progression.isActiveAndEnabled)
            return false;
        resetting = true;
        try
        {
            CollectParticipants();
            ChangeState(LevelSessionState.Resetting);
            Time.timeScale = 1;
            // Relocate the player before SessionReset asks gates to close. Actors
            // remain unable to move, attack or take damage throughout this method.
            var character = player.GetComponent<CharacterController>();
            if (character != null) character.enabled = false;
            player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            if (character != null) character.enabled = true;
            player.ResetMotion();
            Physics.SyncTransforms();
            if (createSession && !progression.TryResetSession()) return false;
            worldSession = progression.Snapshot.SessionId;
            // Enabled health components already observed SessionReset. Include
            // disabled actors too, without issuing a second life reset needlessly.
            foreach (var health in healths)
                if (health != null && (health.SessionId != worldSession || !health.IsAlive || health.Current != health.Maximum))
                    health.ResetHealth();
            if (encounters != null)
                foreach (var encounter in encounters) if (encounter != null) encounter.ResetEncounter();
            Physics.SyncTransforms();
            foreach (var gate in gates) if (gate != null) gate.RefreshGate();
            if (follow != null) follow.SnapToTarget();
            player.SetControlsEnabled(true);
            player.ResetMotion();
            ElapsedSeconds = 0;
            externalResetPending = false;
            ChangeState(LevelSessionState.Running);
            return true;
        }
        finally
        {
            resetting = false;
            // If configuration/restoration failed, fail closed in Resetting.
            ApplyPermissions();
        }
    }

    private void CollectParticipants()
    {
        healths.Clear(); gates.Clear();
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            foreach (var health in root.GetComponentsInChildren<CombatHealth>(true))
                if (health.progression == progression) healths.Add(health);
            foreach (var gate in root.GetComponentsInChildren<ProgressionGate>(true))
                if (gate.progression == progression) gates.Add(gate);
        }
    }

    private void ChangeState(LevelSessionState value)
    {
        bool changed = State != value;
        State = value;
        if (changed) timerBaselineValid = false;
        ApplyPermissions();
        if (!changed || StateChanged == null) return;
        notifyingState = true;
        try
        {
            foreach (Action<LevelSessionState> listener in StateChanged.GetInvocationList())
                try { listener(value); }
                catch (Exception error) { Debug.LogException(error, this); }
        }
        finally { notifyingState = false; }
    }

    private void ApplyPermissions()
    {
        bool running = IsRunning && !resetting;
        if (player != null)
        {
            player.SetSessionControlsEnabled(running);
            player.SetRestartEnabled(CanRestart && !resetting);
            if (!running) player.GetComponent<MeleeCombat>()?.SetAttacksEnabled(false);
        }
        foreach (var health in healths) if (health != null) health.SetDamageEnabled(running);
        if (encounters != null)
            foreach (var encounter in encounters) if (encounter != null) encounter.SetSessionRunning(running);
    }
}
