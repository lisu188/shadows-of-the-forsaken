using System;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;

namespace ShadowsOfTheForsaken.Combat
{
    [DisallowMultipleComponent]
    public sealed class CombatHealth : MonoBehaviour
    {
        [Min(1)] public int maximumHealth = 100;
        public LevelProgressionController progression;
        private LevelProgressionController observedProgression;
        private HealthState state;
        public event Action<HealthChange> Changed;
        public event Action<HealthChange> Died;
        public int Current => State.Current;
        public int Maximum => State.Maximum;
        public bool IsAlive => State.IsAlive;
        public Guid LifeId => State.LifeId;
        public Guid SessionId => State.SessionId;
        private HealthState State => state ?? (state = new HealthState(SafeMaximum(), CurrentSession));
        private Guid CurrentSession => progression != null ? progression.Snapshot.SessionId : Guid.Empty;

        private void Awake() { _ = State; }
        private void OnEnable()
        {
            observedProgression = progression;
            if (observedProgression != null) observedProgression.Changed += ProgressChanged;
            if (State.SessionId != CurrentSession) ResetHealth();
        }
        private void OnDisable()
        {
            if (observedProgression != null) observedProgression.Changed -= ProgressChanged;
            observedProgression = null;
        }
        private void OnDestroy() { Changed = null; Died = null; }
        private int SafeMaximum() => Mathf.Clamp(maximumHealth, 1, 1000000);
        private void OnValidate() { maximumHealth = SafeMaximum(); }

        public bool TryDamage(int damage, Guid expectedLife, Guid expectedSession)
        {
            if (!isActiveAndEnabled || (progression != null && !progression.isActiveAndEnabled) ||
                expectedSession != CurrentSession) return false;
            int previous = Current;
            var outcome = State.TryDamage(damage, expectedLife, expectedSession);
            if (outcome == DamageOutcome.Rejected) return false;
            // Capture payload before notifying anyone: a listener may reset or
            // destroy this actor. The death must retain its original tokens.
            var change = new HealthChange(previous, Current, Maximum, LifeId, SessionId, false);
            Publish(Changed, change);
            if (change.IsDeath) Publish(Died, change);
            return true;
        }

        public void ResetHealth()
        {
            int previous = Current;
            State.Reset(SafeMaximum(), CurrentSession);
            Publish(Changed, new HealthChange(previous, Current, Maximum, LifeId, SessionId, true));
        }

        private void ProgressChanged(ProgressionChange change)
        {
            // Reset physical health only, never mutate progression from Changed.
            if (change.Kind == ProgressionChangeKind.SessionReset) ResetHealth();
        }
        private void Publish(Action<HealthChange> listeners, HealthChange change)
        {
            if (listeners == null) return;
            foreach (Action<HealthChange> listener in listeners.GetInvocationList())
                try { listener(change); }
                catch (Exception error) { Debug.LogException(error, this); }
        }
    }
}
