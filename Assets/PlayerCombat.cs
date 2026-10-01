using ShadowsOfTheForsaken.Combat;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(PlayerMovement), typeof(MeleeCombat))]
public sealed class PlayerCombat : MonoBehaviour
{
    private PlayerMovement movement;
    private MeleeCombat melee;
    private bool focused;
    private bool paused;
    private bool disabledForDeath;
    private bool restoreControlsAfterDeath;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>(); melee = GetComponent<MeleeCombat>();
        focused = Application.isFocused;
    }
    private void OnEnable()
    {
        movement.AttackRequested += Attack;
        melee.Health.Changed += HealthChanged;
        ReconcileHealth();
        RefreshPermission();
    }
    private void OnDisable()
    {
        movement.AttackRequested -= Attack;
        melee.Health.Changed -= HealthChanged;
        melee.SetAttacksEnabled(false);
    }
    private void Update() => RefreshPermission();
    private void OnApplicationFocus(bool value) { focused = value; RefreshPermission(); }
    private void OnApplicationPause(bool value) { paused = value; RefreshPermission(); }
    private void Attack() { RefreshPermission(); melee.TryAttack(); }
    private void RefreshPermission()
    {
        if (melee == null || movement == null) return;
        melee.SetAttacksEnabled(isActiveAndEnabled && focused && !paused && Time.timeScale > 0 &&
            movement.isActiveAndEnabled && movement.ControlsEnabled && melee.Health.IsAlive);
    }
    private void HealthChanged(HealthChange change)
    {
        ReconcileHealth();
        RefreshPermission();
    }
    private void ReconcileHealth()
    {
        if (!melee.Health.IsAlive && !disabledForDeath)
        {
            restoreControlsAfterDeath = movement.RequestedControlsEnabled;
            disabledForDeath = true;
            movement.SetControlsEnabled(false);
        }
        if (melee.Health.IsAlive && disabledForDeath)
        {
            disabledForDeath = false;
            movement.SetControlsEnabled(restoreControlsAfterDeath);
            movement.ResetMotion();
        }
    }
}
