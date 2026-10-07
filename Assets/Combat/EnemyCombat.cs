using System;
using ShadowsOfTheForsaken.Combat.CastleRules;
using CastlePlayerCombat = ShadowsOfTheForsaken.LevelCombat.PlayerCombat;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class EnemyCombat : MonoBehaviour
{
    [SerializeField] private float attackRange = 1.85f;
    [SerializeField] private float windup = 0.68f;
    [SerializeField] private float activeWindow = 0.16f;
    [SerializeField] private float recovery = 0.85f;

    public int Health => health == null ? 0 : health.Current;
    public int MaximumHealth => health == null ? 0 : health.Maximum;
    public bool IsDead => health != null && health.IsDead;
    public AttackPhase Phase => attack.Phase;
    public LevelRoom Room => room;
    public LevelObjective Objective => objective;
    public event Action Died;

    private readonly MeleeAttack<EntityId> attack = new MeleeAttack<EntityId>();
    private LevelProgressionController progression;
    private CastlePlayerCombat target;
    private CharacterController motor;
    private CombatHealth health;
    private CombatPresentation presentation;
    private LevelRoom room;
    private LevelObjective objective;
    private Bounds arena;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private Vector3 swingForward;
    private int damage;
    private float moveSpeed;
    private float verticalVelocity;
    private bool reportedDefeat;
    private bool focused = true;
    private bool paused;

    public void Configure(LevelProgressionController controller, CastlePlayerCombat player, LevelRoom encounterRoom,
        LevelObjective defeatObjective, Bounds arenaBounds, int maxHealth = 60, int attackDamage = 15, float speed = 2.2f)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (maxHealth <= 0 || attackDamage <= 0 || float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0)
            throw new ArgumentOutOfRangeException(nameof(maxHealth), "Combat balance must be finite and positive.");
        if (!ValidEncounter(encounterRoom, defeatObjective)) throw new ArgumentException("Encounter objective must match its room.");
        if (!Finite(arenaBounds.center) || !Finite(arenaBounds.size) || arenaBounds.size.x <= 1 || arenaBounds.size.z <= 1)
            throw new ArgumentException("A finite encounter arena is required.");
        Unsubscribe();
        progression = controller; target = player; room = encounterRoom; objective = defeatObjective;
        arena = arenaBounds; damage = attackDamage; moveSpeed = speed;
        spawnPosition = transform.position; spawnRotation = transform.rotation;
        motor = GetComponent<CharacterController>();
        // High frame rates produce small valid moves; the engine's default threshold discards them.
        motor.minMoveDistance = 0;
        health = new CombatHealth(maxHealth, controller.Snapshot.SessionId);
        presentation = new CombatPresentation(gameObject);
        reportedDefeat = false;
        if (isActiveAndEnabled) Subscribe();
    }

    private static bool ValidEncounter(LevelRoom value, LevelObjective defeat)
    {
        return value == LevelRoom.FirstEncounter && defeat == LevelObjective.FirstEnemyDefeated ||
            value == LevelRoom.ThroneRoom && defeat == LevelObjective.MinibossDefeated ||
            value == LevelRoom.FinalArena && defeat == LevelObjective.FinalEnemyDefeated;
    }

    private void OnEnable()
    {
        motor = GetComponent<CharacterController>();
        Subscribe();
        if (health != null && progression != null && health.SessionId != progression.Snapshot.SessionId)
            ResetPhysical(progression.Snapshot.SessionId);
    }
    private void OnDisable() { Unsubscribe(); attack.Cancel(); }
    private void OnDestroy() { Unsubscribe(); Died = null; }
    private void OnApplicationFocus(bool focus) { focused = focus; if (!focus) attack.Cancel(); }
    private void OnApplicationPause(bool value) { paused = value; if (value) attack.Cancel(); }
    private void Update() => Simulate(Time.deltaTime);

    private void Subscribe()
    {
        if (progression == null) return;
        progression.Changed -= OnProgressionChanged;
        progression.Changed += OnProgressionChanged;
    }
    private void Unsubscribe() { if (progression != null) progression.Changed -= OnProgressionChanged; }

    public void Simulate(float seconds)
    {
        if (!isActiveAndEnabled || health == null || progression == null || !progression.isActiveAndEnabled) return;
        if (IsDead)
        {
            // This retry is outside Changed: no synchronous progression mutation from an observer.
            if (!reportedDefeat && health.SessionId == progression.Snapshot.SessionId)
                reportedDefeat = progression.TryComplete(objective, health.SessionId);
            return;
        }
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0 || Time.timeScale <= 0 || !focused || paused)
        {
            attack.Cancel();
            if (presentation != null) presentation.Tint(Color.white, 0);
            return;
        }
        if (progression.Snapshot.Room != room || target == null || !target.CanFight ||
            target.SessionId != health.SessionId || !target.isActiveAndEnabled ||
            progression.Snapshot.IsCompleted || !PlayerInsideArena())
        {
            attack.Cancel();
            if (presentation != null) presentation.Tint(Color.white, 0);
            return;
        }
        float dt = Mathf.Min(seconds, 0.1f);
        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0;
        if (!attack.IsRunning)
        {
            if (offset.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(offset);
            if (offset.magnitude <= attackRange - 0.15f && CombatPresentation.IsClear(transform, target.transform))
            {
                attack.TryBegin(new AttackTiming(windup, activeWindow, recovery), health.SessionId);
                swingForward = transform.forward;
            }
            else MoveTowards(offset, dt);
        }
        attack.Step(dt);
        if (attack.CanHit && attack.SessionId == progression.Snapshot.SessionId &&
            Mathf.Abs(target.transform.position.y - transform.position.y) <= 1.6f &&
            MeleeGeometry.Contains(swingForward.x, swingForward.z, offset.x, offset.z, attackRange, 0.6f) &&
            CombatPresentation.IsClear(transform, target.transform) &&
            attack.TryRegisterHit(target.GetEntityId(), progression.Snapshot.SessionId))
            target.ReceiveDamage(damage, attack.SessionId);
        if (presentation != null)
            presentation.Tint(Phase == AttackPhase.Active ? Color.red : new Color(1, 0.55f, 0.08f),
                Phase == AttackPhase.Windup ? 0.75f : Phase == AttackPhase.Active ? 0.9f : 0);
    }

    private bool PlayerInsideArena()
    {
        var point = target.transform.position;
        return point.x >= arena.min.x && point.x <= arena.max.x && point.z >= arena.min.z && point.z <= arena.max.z;
    }

    private void MoveTowards(Vector3 offset, float seconds)
    {
        if (motor == null || !motor.enabled) return;
        Vector3 movement = offset.normalized * (moveSpeed * seconds);
        Vector3 desired = transform.position + movement;
        desired.x = Mathf.Clamp(desired.x, arena.min.x + motor.radius, arena.max.x - motor.radius);
        desired.z = Mathf.Clamp(desired.z, arena.min.z + motor.radius, arena.max.z - motor.radius);
        movement = desired - transform.position;
        if (motor.isGrounded && verticalVelocity < 0) verticalVelocity = -2;
        verticalVelocity = Mathf.Max(-30, verticalVelocity - 9.81f * seconds);
        movement.y = verticalVelocity * seconds;
        var collisions = motor.Move(movement);
        if ((collisions & CollisionFlags.Below) != 0) verticalVelocity = -2;
    }

    public bool ReceiveDamage(int amount, Guid actionSession)
    {
        if (amount <= 0 || health == null || !isActiveAndEnabled || progression == null ||
            !progression.isActiveAndEnabled || progression.Snapshot.Room != room ||
            actionSession != progression.Snapshot.SessionId || !health.TryDamage(amount, actionSession)) return false;
        if (health.IsDead)
        {
            attack.Cancel();
            if (presentation != null) presentation.SetDefeated(true);
            reportedDefeat = progression.TryComplete(objective, actionSession);
            var listeners = Died;
            if (listeners != null)
                foreach (Action listener in listeners.GetInvocationList())
                    try { listener(); } catch (Exception error) { Debug.LogException(error, this); }
        }
        return true;
    }

    private void OnProgressionChanged(ProgressionChange change)
    {
        if (change.Kind == ProgressionChangeKind.SessionReset) ResetPhysical(change.After.SessionId);
        else if (change.After.Room != room) { attack.Cancel(); if (presentation != null) presentation.Tint(Color.white, 0); }
    }

    private void ResetPhysical(Guid session)
    {
        health.Reset(session); attack.Cancel(); reportedDefeat = false; verticalVelocity = 0;
        CombatPresentation.ResetTransform(transform, spawnPosition, spawnRotation);
        if (presentation != null) presentation.SetDefeated(false);
    }

    private static bool Finite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private void OnValidate()
    {
        attackRange = ValidBalance(attackRange, 1.85f, 0.1f);
        windup = ValidBalance(windup, 0.68f, 0);
        activeWindow = ValidBalance(activeWindow, 0.16f, 0.01f);
        recovery = ValidBalance(recovery, 0.85f, 0);
    }

    private static float ValidBalance(float value, float fallback, float minimum)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, 100);
    }
}
