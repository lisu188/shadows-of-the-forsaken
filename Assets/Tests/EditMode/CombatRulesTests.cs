using System;
using NUnit.Framework;
using ShadowsOfTheForsaken.Combat.CastleRules;

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    public class CombatRulesTests
    {
        private static readonly AttackTiming Timing = new AttackTiming(0.2f, 0.15f, 0.3f);

        [Test]
        public void HealthClampsLethalDamageAndDeathCannotBeRepeated()
        {
            var session = Guid.NewGuid();
            var health = new CombatHealth(60, session);
            Assert.That(health.TryDamage(25, session), Is.True);
            Assert.That(health.Current, Is.EqualTo(35));
            Assert.That(health.TryDamage(int.MaxValue, session), Is.True);
            Assert.That(health.Current, Is.Zero);
            Assert.That(health.IsDead, Is.True);
            Assert.That(health.TryDamage(1, session), Is.False);
        }

        [Test]
        public void ResetRevivesAndRejectsDamageFromPreviousSession()
        {
            var oldSession = Guid.NewGuid();
            var newSession = Guid.NewGuid();
            var health = new CombatHealth(100, oldSession);
            health.TryDamage(100, oldSession);
            health.Reset(newSession);
            Assert.That(health.Current, Is.EqualTo(100));
            Assert.That(health.IsDead, Is.False);
            Assert.That(health.TryDamage(25, oldSession), Is.False);
            Assert.That(health.Current, Is.EqualTo(100));
            Assert.That(health.TryDamage(25, newSession), Is.True);
        }

        [Test]
        public void HealthRejectsInvalidBalanceAndTokens()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatHealth(0, Guid.NewGuid()));
            Assert.Throws<ArgumentException>(() => new CombatHealth(100, Guid.Empty));
            var health = new CombatHealth(100, Guid.NewGuid());
            Assert.Throws<ArgumentOutOfRangeException>(() => health.TryDamage(0, health.SessionId));
            Assert.Throws<ArgumentOutOfRangeException>(() => health.TryDamage(-1, health.SessionId));
        }

        [Test]
        public void AttackHasWindupActiveRecoveryAndCannotQueueAnotherSwing()
        {
            var attack = new MeleeAttack();
            var session = Guid.NewGuid();
            Assert.That(attack.TryBegin(Timing, session), Is.True);
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Windup));
            Assert.That(attack.TryBegin(Timing, session), Is.False);
            attack.Step(0.21f);
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Active));
            attack.Step(0.15f);
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Recovery));
            Assert.That(attack.TryBegin(Timing, session), Is.False);
            attack.Step(0.31f);
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Ready));
            Assert.That(attack.TryBegin(Timing, session), Is.True);
        }

        [Test]
        public void TargetCanBeHitOncePerSwingIncludingCompoundColliders()
        {
            var attack = new MeleeAttack();
            var session = Guid.NewGuid();
            attack.TryBegin(Timing, session);
            Assert.That(attack.TryRegisterHit(7, session), Is.False, "No damage in windup");
            attack.Step(0.21f);
            Assert.That(attack.TryRegisterHit(7, session), Is.True);
            Assert.That(attack.TryRegisterHit(7, session), Is.False);
            Assert.That(attack.TryRegisterHit(8, session), Is.True);
            attack.Step(0.05f);
            Assert.That(attack.TryRegisterHit(7, session), Is.False);
            attack.Step(1);
            attack.TryBegin(Timing, session);
            attack.Step(0.21f);
            Assert.That(attack.TryRegisterHit(7, session), Is.True);
        }

        [Test]
        public void HashCollisionsDoNotMergeDistinctTargets()
        {
            var first = new CollidingTarget();
            var second = new CollidingTarget();
            var attack = new MeleeAttack<CollidingTarget>();
            var session = Guid.NewGuid();
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            attack.TryBegin(Timing, session);
            attack.Step(0.21f);
            Assert.That(attack.TryRegisterHit(first, session), Is.True);
            Assert.That(attack.TryRegisterHit(first, session), Is.False);
            Assert.That(attack.TryRegisterHit(second, session), Is.True);
            Assert.That(attack.TryRegisterHit(second, session), Is.False);
        }

        private sealed class CollidingTarget
        {
            public override int GetHashCode() => 7;
        }

        [Test]
        public void SlowFrameCrossingActiveWindowCannotLoseHit()
        {
            var attack = new MeleeAttack();
            var session = Guid.NewGuid();
            attack.TryBegin(Timing, session);
            attack.Step(1);
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Ready));
            Assert.That(attack.TryRegisterHit(7, session), Is.True);
            attack.Step(0.01f);
            Assert.That(attack.TryRegisterHit(8, session), Is.False);
        }

        [Test]
        public void OldAttackNeverReplacesCapturedSession()
        {
            var attack = new MeleeAttack();
            var oldSession = Guid.NewGuid();
            attack.TryBegin(Timing, oldSession);
            attack.Step(0.21f);
            Assert.That(attack.TryRegisterHit(7, Guid.NewGuid()), Is.False);
            Assert.That(attack.SessionId, Is.EqualTo(oldSession));
            Assert.That(attack.TryRegisterHit(7, oldSession), Is.True);
        }

        [Test]
        public void CancelClearsActiveWindowAndSession()
        {
            var attack = new MeleeAttack();
            var session = Guid.NewGuid();
            attack.TryBegin(Timing, session);
            attack.Step(0.21f);
            attack.Cancel();
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Ready));
            Assert.That(attack.SessionId, Is.EqualTo(Guid.Empty));
            Assert.That(attack.TryRegisterHit(7, session), Is.False);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void SwingHitsExactlyOnceAtRepresentativeFrameRates(int fps)
        {
            var attack = new MeleeAttack();
            var session = Guid.NewGuid();
            attack.TryBegin(Timing, session);
            int hits = 0;
            for (int i = 0; i < fps; i++)
            {
                attack.Step(1f / fps);
                if (attack.TryRegisterHit(7, session)) hits++;
            }
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Ready));
        }

        [Test]
        public void TimingRejectsZeroActiveAndNonFiniteDurations()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AttackTiming(0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AttackTiming(-1, 0.1f, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AttackTiming(0, float.NaN, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AttackTiming(0, 0.1f, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AttackTiming(float.MaxValue, float.MaxValue, 0));
            var attack = new MeleeAttack();
            Assert.Throws<ArgumentException>(() => attack.TryBegin(default, Guid.NewGuid()));
            Assert.Throws<ArgumentException>(() => attack.TryBegin(Timing, Guid.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => attack.Step(float.NaN));
        }

        [Test]
        public void DirectionalConeRejectsBehindOutsideRangeAndInvalidValues()
        {
            Assert.That(MeleeGeometry.Contains(0, 1, 0, 2, 2.25f, 0.45f), Is.True);
            Assert.That(MeleeGeometry.Contains(0, 1, 0, -1, 2.25f, 0.45f), Is.False);
            Assert.That(MeleeGeometry.Contains(0, 1, 0, 3, 2.25f, 0.45f), Is.False);
            Assert.That(MeleeGeometry.Contains(0, 1, 2, 0, 2.25f, 0.45f), Is.False);
            Assert.That(MeleeGeometry.Contains(0, 1, 0, 0, 2.25f, 0.45f), Is.True);
            Assert.That(MeleeGeometry.Contains(0, 0, 0, 1, 2.25f, 0.45f), Is.False);
            Assert.That(MeleeGeometry.Contains(0, 1, float.NaN, 1, 2.25f, 0.45f), Is.False);
        }
    }
}
