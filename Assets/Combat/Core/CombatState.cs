using System;
using System.Collections.Generic;

namespace ShadowsOfTheForsaken.Combat
{
    public enum DamageOutcome { Rejected, Hurt, Killed }
    public enum AttackPhase { Ready, Windup, Active, Recovery }

    public readonly struct HealthChange
    {
        public int Previous { get; }
        public int Current { get; }
        public int Maximum { get; }
        public Guid LifeId { get; }
        public Guid SessionId { get; }
        public bool IsReset { get; }
        public bool IsDeath => !IsReset && Previous > 0 && Current == 0;
        public HealthChange(int previous, int current, int maximum, Guid lifeId, Guid sessionId, bool isReset)
        {
            Previous = previous; Current = current; Maximum = maximum;
            LifeId = lifeId; SessionId = sessionId; IsReset = isReset;
        }
    }

    // Identity belongs to this particular life as well as the containing scene
    // session. Delayed damage cannot affect an actor that has already respawned.
    public sealed class HealthState
    {
        public int Maximum { get; private set; }
        public int Current { get; private set; }
        public Guid LifeId { get; private set; }
        public Guid SessionId { get; private set; }
        public bool IsAlive => Current > 0;

        public HealthState(int maximum, Guid sessionId) { Reset(maximum, sessionId); }

        public void Reset(int maximum, Guid sessionId)
        {
            if (maximum < 1 || maximum > 1000000) throw new ArgumentOutOfRangeException(nameof(maximum));
            Maximum = Current = maximum;
            SessionId = sessionId;
            LifeId = Guid.NewGuid();
        }

        public DamageOutcome TryDamage(int damage, Guid expectedLife, Guid expectedSession)
        {
            if (damage <= 0 || !IsAlive || expectedLife != LifeId || expectedSession != SessionId)
                return DamageOutcome.Rejected;
            Current -= Math.Min(Current, damage);
            return IsAlive ? DamageOutcome.Hurt : DamageOutcome.Killed;
        }
    }

    public readonly struct AttackTiming
    {
        public double Windup { get; }
        public double Active { get; }
        public double Recovery { get; }
        public double Duration => Windup + Active + Recovery;
        public AttackTiming(double windup, double active, double recovery)
        {
            Validate(windup, nameof(windup), false);
            Validate(active, nameof(active), true);
            Validate(recovery, nameof(recovery), false);
            Windup = windup; Active = active; Recovery = recovery;
        }
        private static void Validate(double value, string name, bool positive)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 60 || (positive && value == 0))
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public sealed class MeleeAttackState<TTargetId>
    {
        private readonly HashSet<TTargetId> hitTargets = new HashSet<TTargetId>();
        private AttackTiming timing;
        private double elapsed;
        private bool started;
        private bool interrupted;
        public Guid LifeId { get; private set; }
        public Guid SessionId { get; private set; }
        public AttackPhase Phase { get; private set; }
        public bool CanDealDamage { get; private set; }

        public bool TryStart(AttackTiming settings, Guid lifeId, Guid sessionId)
        {
            if (Phase != AttackPhase.Ready || settings.Active <= 0) return false;
            timing = settings; elapsed = 0; started = true; interrupted = false;
            LifeId = lifeId; SessionId = sessionId; hitTargets.Clear();
            CanDealDamage = false;
            Phase = timing.Windup > 0 ? AttackPhase.Windup : AttackPhase.Active;
            return true;
        }

        // A frame spanning the entire active window still samples it once. The
        // caller queries current physics for this simulation interval, not later
        // from an animation callback. Target identity remains deduplicated.
        public bool Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            CanDealDamage = false;
            if (!started || seconds == 0) return false;
            double next = Math.Min(timing.Duration, elapsed + seconds);
            CanDealDamage = !interrupted && elapsed < timing.Windup + timing.Active && next >= timing.Windup;
            elapsed = next;
            if (elapsed >= timing.Duration)
            {
                Phase = AttackPhase.Ready;
                started = false;
            }
            else if (interrupted || elapsed >= timing.Windup + timing.Active) Phase = AttackPhase.Recovery;
            else if (elapsed >= timing.Windup) Phase = AttackPhase.Active;
            else Phase = AttackPhase.Windup;
            return CanDealDamage;
        }

        public bool TryRegisterHit(TTargetId targetId)
        {
            return CanDealDamage && !EqualityComparer<TTargetId>.Default.Equals(targetId, default) && hitTargets.Add(targetId);
        }

        public void Interrupt()
        {
            interrupted = true; CanDealDamage = false;
            if (started) Phase = AttackPhase.Recovery;
        }

        public void Reset()
        {
            started = interrupted = CanDealDamage = false;
            elapsed = 0; LifeId = SessionId = Guid.Empty;
            Phase = AttackPhase.Ready; hitTargets.Clear();
        }
    }
}
