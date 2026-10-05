using System;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public sealed class PlayerCombat : MonoBehaviour
{
    [SerializeField] private int maximumHealth = 100;
    [SerializeField] private int damage = 25;
    [SerializeField] private float range = 2.25f;
    [SerializeField] private float windup = 0.16f;
    [SerializeField] private float activeWindow = 0.15f;
    [SerializeField] private float recovery = 0.26f;

    public int Health => health == null ? maximumHealth : health.Current;
    public int MaximumHealth => maximumHealth;
    public bool IsDead => health != null && health.IsDead;
    public bool CanFight => CanAct;
    public AttackPhase Phase => attack.Phase;
    public Guid SessionId => health == null ? Guid.Empty : health.SessionId;
    public event Action Died;
    public event Action HealthChanged;
    public event Action AttackStarted;

    private readonly MeleeAttack attack = new MeleeAttack();
    private LevelProgressionController progression;
    private PlayerMovement movement;
    private CombatHealth health;
    private CombatPresentation presentation;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private Vector3 swingForward;
    private Transform weaponVisual;
    private Quaternion weaponRest;
    private bool focused = true;
    private bool paused;

    public void Configure(LevelProgressionController controller, int maxHealth = 100)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        if (maxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
        Unsubscribe();
        progression = controller;
        movement = GetComponent<PlayerMovement>();
        maximumHealth = maxHealth;
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        health = new CombatHealth(maximumHealth, controller.Snapshot.SessionId);
        presentation = new CombatPresentation(gameObject);
        if (isActiveAndEnabled) Subscribe();
    }

    public void SetWeaponVisual(Transform weapon)
    {
        weaponVisual = weapon;
        if (weaponVisual != null) weaponRest = weaponVisual.localRotation;
    }

    private void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        Subscribe();
        if (progression != null && health != null && health.SessionId != progression.Snapshot.SessionId)
            ResetPhysical(progression.Snapshot.SessionId);
    }

    private void OnDisable() { Unsubscribe(); attack.Cancel(); UpdateVisual(); }
    private void OnDestroy() { Unsubscribe(); Died = null; HealthChanged = null; AttackStarted = null; }
    private void OnApplicationFocus(bool focus) { focused = focus; if (!focus) attack.Cancel(); }
    private void OnApplicationPause(bool value) { paused = value; if (value) attack.Cancel(); }
    private void Update() => Simulate(Time.deltaTime);

    private void Subscribe()
    {
        if (movement != null) { movement.AttackRequested -= RequestAttack; movement.AttackRequested += RequestAttack; }
        if (progression != null) { progression.Changed -= OnProgressionChanged; progression.Changed += OnProgressionChanged; }
    }

    private void Unsubscribe()
    {
        if (movement != null) movement.AttackRequested -= RequestAttack;
        if (progression != null) progression.Changed -= OnProgressionChanged;
    }

    public void RequestAttack()
    {
        if (!CanAct || health.SessionId != progression.Snapshot.SessionId) return;
        // The session is captured here, never replaced during the later active-window callback.
        if (!attack.TryBegin(new AttackTiming(windup, activeWindow, recovery), health.SessionId)) return;
        swingForward = transform.forward;
        Publish(AttackStarted);
    }

    public void Simulate(float seconds)
    {
        if (!CanAct || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0)
        {
            attack.Cancel();
            UpdateVisual();
            return;
        }
        attack.Step(Mathf.Min(seconds, 0.1f));
        if (attack.CanHit && attack.SessionId == progression.Snapshot.SessionId)
        {
            foreach (var collider in Physics.OverlapSphere(transform.position, range + 1,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var enemy = collider.GetComponentInParent<EnemyCombat>();
                if (enemy == null || enemy.IsDead) continue;
                Vector3 offset = enemy.transform.position - transform.position;
                if (Mathf.Abs(offset.y) > 1.6f || !MeleeGeometry.Contains(swingForward.x, swingForward.z,
                    offset.x, offset.z, range, 0.45f) || !CombatPresentation.IsClear(transform, enemy.transform)) continue;
                if (attack.TryRegisterHit(enemy.GetInstanceID(), progression.Snapshot.SessionId))
                    enemy.ReceiveDamage(damage, attack.SessionId);
            }
        }
        UpdateVisual();
    }

    public bool ReceiveDamage(int amount, Guid actionSession)
    {
        if (amount <= 0 || health == null || !isActiveAndEnabled || progression == null ||
            !progression.isActiveAndEnabled || progression.Snapshot.IsCompleted || !focused || paused || Time.timeScale <= 0 ||
            actionSession != progression.Snapshot.SessionId ||
            !health.TryDamage(amount, actionSession)) return false;
        Publish(HealthChanged);
        if (health.IsDead)
        {
            attack.Cancel();
            movement.SetControlsEnabled(false);
            if (presentation != null) presentation.Tint(new Color(0.35f, 0.04f, 0.04f), 0.8f);
            Publish(Died);
        }
        return true;
    }

    private bool CanAct => isActiveAndEnabled && health != null && !health.IsDead && progression != null &&
        progression.isActiveAndEnabled && !progression.Snapshot.IsCompleted && movement != null &&
        movement.ControlsEnabled && movement.isActiveAndEnabled && focused && !paused && Time.timeScale > 0;

    private void OnProgressionChanged(ProgressionChange change)
    {
        if (change.Kind == ProgressionChangeKind.SessionReset) ResetPhysical(change.After.SessionId);
        else if (change.After.IsCompleted) { attack.Cancel(); movement.SetControlsEnabled(false); }
    }

    private void ResetPhysical(Guid session)
    {
        health.Reset(session);
        attack.Cancel();
        CombatPresentation.ResetTransform(transform, spawnPosition, spawnRotation);
        if (presentation != null) presentation.SetDefeated(false);
        movement.ResetMotion();
        movement.SetControlsEnabled(true);
        UpdateVisual();
        Publish(HealthChanged);
    }

    private void UpdateVisual()
    {
        if (presentation != null && !IsDead)
            presentation.Tint(new Color(0.65f, 0.8f, 1), Phase == AttackPhase.Active ? 0.65f : Phase == AttackPhase.Windup ? 0.25f : 0);
        if (weaponVisual != null)
        {
            float angle = Phase == AttackPhase.Ready ? 0 : -55 + attack.Progress * 150;
            weaponVisual.localRotation = weaponRest * Quaternion.Euler(0, angle, 0);
        }
    }

    private void Publish(Action listeners)
    {
        if (listeners == null) return;
        foreach (Action listener in listeners.GetInvocationList())
            try { listener(); } catch (Exception error) { Debug.LogException(error, this); }
    }

    private void OnValidate()
    {
        maximumHealth = Mathf.Max(1, maximumHealth);
        damage = Mathf.Max(1, damage);
        range = ValidBalance(range, 2.25f, 0.1f);
        windup = ValidBalance(windup, 0.16f, 0);
        activeWindow = ValidBalance(activeWindow, 0.15f, 0.01f);
        recovery = ValidBalance(recovery, 0.26f, 0);
    }

    private static float ValidBalance(float value, float fallback, float minimum)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, 100);
    }
}
