using System;
using NUnit.Framework;
using ShadowsOfTheForsaken.Combat;

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    public sealed class CombatStateTests
    {
        private static readonly AttackTiming Timing = new AttackTiming(.25, .25, .5);

        [Test]
        public void DamageClampsHealthAndDeathCanOnlyHappenOnce()
        {
            var health = new HealthState(100, Guid.NewGuid());
            Assert.That(health.TryDamage(25, health.LifeId, health.SessionId), Is.EqualTo(DamageOutcome.Hurt));
            Assert.That(health.Current, Is.EqualTo(75));
            Assert.That(health.TryDamage(int.MaxValue, health.LifeId, health.SessionId), Is.EqualTo(DamageOutcome.Killed));
            Assert.That(health.Current, Is.Zero);
            Assert.That(health.IsAlive, Is.False);
            Assert.That(health.TryDamage(1, health.LifeId, health.SessionId), Is.EqualTo(DamageOutcome.Rejected));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(1000001)]
        public void InvalidMaximumIsRejected(int maximum)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HealthState(maximum, Guid.Empty));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void NonpositiveDamageDoesNotHealOrDamage(int amount)
        {
            var health = new HealthState(100, Guid.Empty);
            Assert.That(health.TryDamage(amount, health.LifeId, health.SessionId), Is.EqualTo(DamageOutcome.Rejected));
            Assert.That(health.Current, Is.EqualTo(100));
        }

        [Test]
        public void ResetRestoresConfiguredHealthAndInvalidatesOldLifeAndSession()
        {
            Guid firstSession = Guid.NewGuid();
            var health = new HealthState(100, firstSession);
            Guid firstLife = health.LifeId;
            health.TryDamage(100, firstLife, firstSession);
            health.Reset(150, Guid.NewGuid());
            Assert.That(health.Current, Is.EqualTo(150));
            Assert.That(health.Maximum, Is.EqualTo(150));
            Assert.That(health.LifeId, Is.Not.EqualTo(firstLife));
            Assert.That(health.TryDamage(20, firstLife, health.SessionId), Is.EqualTo(DamageOutcome.Rejected));
            Assert.That(health.TryDamage(20, health.LifeId, firstSession), Is.EqualTo(DamageOutcome.Rejected));
            Assert.That(health.TryDamage(20, health.LifeId, health.SessionId), Is.EqualTo(DamageOutcome.Hurt));
        }

        [Test]
        public void ResetWithinSameSessionAlsoInvalidatesDelayedDamage()
        {
            var health = new HealthState(100, Guid.Empty);
            Guid life = health.LifeId;
            health.Reset(100, Guid.Empty);
            Assert.That(health.TryDamage(100, life, Guid.Empty), Is.EqualTo(DamageOutcome.Rejected));
            Assert.That(health.IsAlive, Is.True);
        }

        [Test]
        public void WindupActiveRecoveryAndCooldownBoundariesAreEnforced()
        {
            var attack = new MeleeAttackState<int>();
            Assert.That(attack.TryStart(Timing, Guid.NewGuid(), Guid.Empty), Is.True);
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Windup));
            Assert.That(attack.Advance(.125), Is.False);
            Assert.That(attack.TryRegisterHit(1), Is.False);
            Assert.That(attack.Advance(.125), Is.True);
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Active));
            Assert.That(attack.TryRegisterHit(1), Is.True);
            attack.Advance(.25);
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Recovery));
            Assert.That(attack.Advance(.125), Is.False);
            Assert.That(attack.TryRegisterHit(2), Is.False);
            Assert.That(attack.TryStart(Timing, Guid.NewGuid(), Guid.Empty), Is.False);
            attack.Advance(.375);
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Ready));
            Assert.That(attack.TryStart(Timing, Guid.NewGuid(), Guid.Empty), Is.True);
        }

        [TestCase(.1)]
        [TestCase(.3)]
        [TestCase(.6)]
        public void RepeatedAttackRequestsCannotRestartAnAction(double elapsed)
        {
            var attack = new MeleeAttackState<int>();
            Guid life = Guid.NewGuid();
            attack.TryStart(Timing, life, Guid.Empty);
            attack.Advance(elapsed);
            for (int i = 0; i < 20; i++) Assert.That(attack.TryStart(Timing, Guid.NewGuid(), Guid.Empty), Is.False);
            Assert.That(attack.LifeId, Is.EqualTo(life));
        }

        [Test]
        public void CompoundTargetsAreHitOncePerActionAndAgainOnNextAction()
        {
            var attack = new MeleeAttackState<int>();
            attack.TryStart(Timing, Guid.NewGuid(), Guid.Empty); attack.Advance(.25);
            Assert.That(attack.TryRegisterHit(11), Is.True);
            Assert.That(attack.TryRegisterHit(11), Is.False);
            Assert.That(attack.TryRegisterHit(12), Is.True);
            attack.Advance(.125);
            Assert.That(attack.TryRegisterHit(11), Is.False);
            attack.Advance(1);
            attack.TryStart(Timing, Guid.NewGuid(), Guid.Empty); attack.Advance(.25);
            Assert.That(attack.TryRegisterHit(11), Is.True);
        }

        [Test]
        public void TargetIdentityUsesEqualityInsteadOfHashCodes()
        {
            var attack = new MeleeAttackState<CollidingTargetId>();
            var first = new CollidingTargetId(1);
            var second = new CollidingTargetId(0x100000001UL);
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            attack.TryStart(Timing, Guid.NewGuid(), Guid.Empty); attack.Advance(.25);
            Assert.That(attack.TryRegisterHit(default), Is.False);
            Assert.That(attack.TryRegisterHit(first), Is.True);
            Assert.That(attack.TryRegisterHit(second), Is.True);
            Assert.That(attack.TryRegisterHit(first), Is.False);
            Assert.That(attack.TryRegisterHit(second), Is.False);
        }

        private readonly struct CollidingTargetId : IEquatable<CollidingTargetId>
        {
            private readonly ulong value;
            public CollidingTargetId(ulong value) { this.value = value; }
            public bool Equals(CollidingTargetId other) => value == other.value;
            public override bool Equals(object other) => other is CollidingTargetId id && Equals(id);
            public override int GetHashCode() => 1;
        }

        [Test]
        public void LargeStepSamplesAnEntireCrossedWindowExactlyOnce()
        {
            var attack = new MeleeAttackState<int>();
            attack.TryStart(Timing, Guid.NewGuid(), Guid.Empty);
            Assert.That(attack.Advance(5), Is.True);
            Assert.That(attack.TryRegisterHit(1), Is.True);
            Assert.That(attack.TryRegisterHit(1), Is.False);
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Ready));
            Assert.That(attack.Advance(.1), Is.False);
            Assert.That(attack.TryRegisterHit(2), Is.False);
        }

        [TestCase(.1)]
        [TestCase(.3)]
        [TestCase(.6)]
        public void InterruptCancelsPendingDamageWithoutBypassingRecovery(double elapsed)
        {
            var attack = new MeleeAttackState<int>();
            attack.TryStart(Timing, Guid.NewGuid(), Guid.Empty); attack.Advance(elapsed);
            attack.Interrupt();
            Assert.That(attack.TryRegisterHit(1), Is.False);
            Assert.That(attack.TryStart(Timing, Guid.NewGuid(), Guid.Empty), Is.False);
            Assert.That(attack.Advance(2), Is.False);
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Ready));
        }

        [Test]
        public void ResetDiscardsAnOldAttackAndCapturedTokens()
        {
            var attack = new MeleeAttackState<int>();
            attack.TryStart(Timing, Guid.NewGuid(), Guid.NewGuid()); attack.Advance(.25);
            attack.TryRegisterHit(1); attack.Reset();
            Assert.That(attack.LifeId, Is.EqualTo(Guid.Empty));
            Assert.That(attack.SessionId, Is.EqualTo(Guid.Empty));
            Assert.That(attack.Advance(2), Is.False);
            Assert.That(attack.TryRegisterHit(1), Is.False);
            Assert.That(attack.TryStart(Timing, Guid.NewGuid(), Guid.NewGuid()), Is.True);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void OneHitAndFullRecoveryAtRepresentativeFrameRates(int fps)
        {
            var attack = new MeleeAttackState<int>();
            attack.TryStart(Timing, Guid.NewGuid(), Guid.Empty);
            int hits = 0; double firstHit = -1;
            for (int i = 0; i < fps * 2; i++)
                if (attack.Advance(1d / fps) && attack.TryRegisterHit(1))
                {
                    hits++; firstHit = (i + 1d) / fps;
                }
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(firstHit, Is.InRange(.25, .25 + 1d / fps + 1e-9));
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Ready));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-1)]
        public void InvalidSimulationTimeIsRejected(double value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeAttackState<int>().Advance(value));
        }

        [TestCase(-1, .1, .1)]
        [TestCase(0, 0, .1)]
        [TestCase(0, .1, -1)]
        [TestCase(double.NaN, .1, .1)]
        [TestCase(0, double.PositiveInfinity, .1)]
        public void InvalidAttackTimingIsRejected(double windup, double active, double recovery)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AttackTiming(windup, active, recovery));
        }
    }
}
