using System;
using System.Collections.Generic;

namespace ShadowsOfTheForsaken.Combat
{
    public enum AttackPhase { Ready, Windup, Active, Recovery }

    /// <summary>Balance choices live in the scene adapter; this core has no Unity dependency.</summary>
    public readonly struct AttackTiming
    {
        public float Windup { get; }
        public float Active { get; }
        public float Recovery { get; }
        public float Duration => Windup + Active + Recovery;

        public AttackTiming(float windup, float active, float recovery)
        {
            Validate(windup, nameof(windup));
            Validate(active, nameof(active));
            Validate(recovery, nameof(recovery));
            if (active <= 0) throw new ArgumentOutOfRangeException(nameof(active));
            if ((double)windup + active + recovery > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(recovery), "Total duration must remain finite.");
            Windup = windup; Active = active; Recovery = recovery;
        }

        private static void Validate(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public sealed class CombatHealth
    {
        public int Maximum { get; }
        public int Current { get; private set; }
        public bool IsDead => Current == 0;
        public Guid SessionId { get; private set; }

        public CombatHealth(int maximum, Guid sessionId)
        {
            if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            Maximum = maximum;
            Reset(sessionId);
        }

        public bool TryDamage(int damage, Guid sessionId)
        {
            if (damage <= 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (sessionId != SessionId || IsDead) return false;
            Current = Math.Max(0, Current - damage);
            return true;
        }

        public void Reset(Guid sessionId)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("A session token is required.", nameof(sessionId));
            SessionId = sessionId;
            Current = Maximum;
        }
    }

    /// <summary>One directional swing, with one damage registration per target even for compound colliders.</summary>
    public sealed class MeleeAttack
    {
        private readonly HashSet<int> hitTargets = new HashSet<int>();
        private AttackTiming timing;
        private double elapsed;
        private bool running;
        private bool visitedActiveWindow;
        public Guid SessionId { get; private set; }
        public AttackPhase Phase { get; private set; } = AttackPhase.Ready;
        public bool IsRunning => running;
        public bool CanHit => visitedActiveWindow;
        public float Progress => running ? (float)(elapsed / timing.Duration) : 0;

        public bool TryBegin(AttackTiming settings, Guid sessionId)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("A session token is required.", nameof(sessionId));
            if (settings.Active <= 0) throw new ArgumentException("An active window is required.", nameof(settings));
            if (running) return false;
            timing = settings;
            SessionId = sessionId;
            elapsed = 0;
            running = true;
            visitedActiveWindow = settings.Windup == 0;
            hitTargets.Clear();
            Phase = settings.Windup == 0 ? AttackPhase.Active : AttackPhase.Windup;
            return true;
        }

        public void Step(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            visitedActiveWindow = false;
            if (!running) return;
            double previous = elapsed;
            elapsed += seconds;
            // A slow frame crossing the entire active interval still gets one collision query.
            visitedActiveWindow = previous < timing.Windup + timing.Active && elapsed >= timing.Windup;
            if (elapsed >= timing.Duration) { running = false; Phase = AttackPhase.Ready; }
            else if (elapsed >= timing.Windup + timing.Active) Phase = AttackPhase.Recovery;
            else if (elapsed >= timing.Windup) Phase = AttackPhase.Active;
            else Phase = AttackPhase.Windup;
        }

        public bool TryRegisterHit(int targetId, Guid currentSession)
        {
            return CanHit && currentSession == SessionId && hitTargets.Add(targetId);
        }

        public void Cancel()
        {
            running = false;
            visitedActiveWindow = false;
            elapsed = 0;
            Phase = AttackPhase.Ready;
            SessionId = Guid.Empty;
            hitTargets.Clear();
        }
    }

    public static class MeleeGeometry
    {
        /// <summary>Horizontal cone; vertical and obstruction checks belong to physics in the Unity adapter.</summary>
        public static bool Contains(float forwardX, float forwardZ, float offsetX, float offsetZ,
            float range, float minimumDot)
        {
            if (!Finite(forwardX) || !Finite(forwardZ) || !Finite(offsetX) || !Finite(offsetZ) ||
                !Finite(range) || range <= 0 || !Finite(minimumDot) || minimumDot < -1 || minimumDot > 1)
                return false;
            double distanceSquared = (double)offsetX * offsetX + (double)offsetZ * offsetZ;
            double headingSquared = (double)forwardX * forwardX + (double)forwardZ * forwardZ;
            if (distanceSquared > (double)range * range || headingSquared <= 0) return false;
            if (distanceSquared == 0) return true;
            double dot = ((double)forwardX * offsetX + (double)forwardZ * offsetZ) /
                Math.Sqrt(distanceSquared * headingSquared);
            return dot >= minimumDot;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
