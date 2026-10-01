using System;
using NUnit.Framework;
using ShadowsOfTheForsaken.Encounters;

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    public sealed class EncounterStateTests
    {
        private EncounterState state;
        private Guid session, life;

        [SetUp]
        public void SetUp()
        {
            state = new EncounterState();
            session = Guid.NewGuid(); life = Guid.NewGuid();
            state.Reset(session, life);
        }

        [Test]
        public void EncounterRequiresLegitimateRoomAndOriginalIdentityForActivation()
        {
            Assert.That(state.TryActivate(session, life, false), Is.False);
            Assert.That(state.TryActivate(Guid.NewGuid(), life, true), Is.False);
            Assert.That(state.TryActivate(session, Guid.NewGuid(), true), Is.False);
            Assert.That(state.TryActivate(session, life, true), Is.True);
            Assert.That(state.TryActivate(session, life, true), Is.False, "Room re-entry cannot duplicate activation.");
        }

        [Test]
        public void EncounterRejectsDeathBeforeActivationAndForeignLife()
        {
            Assert.That(state.RecordDeath(session, life), Is.False);
            state.TryActivate(session, life, true);
            Assert.That(state.RecordDeath(Guid.NewGuid(), life), Is.False);
            Assert.That(state.RecordDeath(session, Guid.NewGuid()), Is.False);
            Assert.That(state.HasPendingDeath, Is.False);
        }

        [Test]
        public void EncounterQueuesDeathUntilReturningToItsRoomAndCreditsOnce()
        {
            state.TryActivate(session, life, true);
            Assert.That(state.RecordDeath(session, life), Is.True);
            Assert.That(state.RecordDeath(session, life), Is.False);
            Assert.That(state.CanCredit(session, life, false, true), Is.False);
            Assert.That(state.CanCredit(session, life, true, false), Is.False);
            Assert.That(state.CanCredit(session, life, true, true), Is.True);
            Assert.That(state.ConfirmCredit(session, Guid.NewGuid()), Is.False);
            Assert.That(state.ConfirmCredit(session, life), Is.True);
            Assert.That(state.ConfirmCredit(session, life), Is.False);
            Assert.That(state.RecordDeath(session, life), Is.False);
            Assert.That(state.HasPendingDeath, Is.False);
            Assert.That(state.DeathCredited, Is.True);
        }

        [Test]
        public void EncounterResetDiscardsPendingCreditAndRejectsOldCallbacks()
        {
            state.TryActivate(session, life, true);
            state.RecordDeath(session, life);
            state.Reset(Guid.NewGuid(), Guid.NewGuid());
            Assert.That(state.HasPendingDeath, Is.False);
            Assert.That(state.Activated, Is.False);
            Assert.That(state.RecordDeath(session, life), Is.False);
            Assert.That(state.ConfirmCredit(session, life), Is.False);
            Assert.That(state.TryActivate(state.SessionId, state.LifeId, true), Is.True);
            Assert.That(state.RecordDeath(state.SessionId, state.LifeId), Is.True);
        }

        [Test]
        public void EncounterCannotTransferOldDeathToSameSessionRespawn()
        {
            state.TryActivate(session, life, true);
            state.RecordDeath(session, life);
            Assert.That(state.CanCredit(session, Guid.NewGuid(), true, true), Is.False);
            Assert.That(state.CanCredit(Guid.NewGuid(), life, true, true), Is.False);
        }

        [TestCase(false, true, true, EncounterPhase.Dormant)]
        [TestCase(true, false, true, EncounterPhase.Defeated)]
        [TestCase(true, true, false, EncounterPhase.Suspended)]
        public void EncounterLifeAndSessionGatesPrecedePursuit(bool activated, bool alive, bool running, EncounterPhase expected)
        {
            Assert.That(EncounterState.Decide(activated, alive, running, true, true, true, false, false, true, false), Is.EqualTo(expected));
        }

        [TestCase(false, false, true, EncounterPhase.Chasing)]
        [TestCase(false, false, false, EncounterPhase.Blocked)]
        [TestCase(false, true, false, EncounterPhase.Attacking)]
        [TestCase(true, false, false, EncounterPhase.Attacking)]
        public void EncounterPathFailureCannotFallBackToWalkingThroughObstacles(bool attacking, bool inRange, bool path, EncounterPhase expected)
        {
            Assert.That(EncounterState.Decide(true, true, true, true, true, true, attacking, inRange, path, false), Is.EqualTo(expected));
        }

        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        public void EncounterLosesTargetSafelyWithoutLeavingArena(bool available, bool inside, bool detected)
        {
            Assert.That(EncounterState.Decide(true, true, true, available, inside, detected, true, true, true, false), Is.EqualTo(EncounterPhase.Returning));
            Assert.That(EncounterState.Decide(true, true, true, available, inside, detected, true, true, true, true), Is.EqualTo(EncounterPhase.Watching));
            Assert.That(EncounterState.Decide(true, true, true, available, inside, detected, true, true, false, false), Is.EqualTo(EncounterPhase.Blocked));
        }
    }
}
