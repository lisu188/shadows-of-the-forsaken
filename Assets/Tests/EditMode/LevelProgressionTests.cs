using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    public sealed class LevelProgressionTests
    {
        private LevelProgression progress;

        [SetUp]
        public void SetUp() { progress = new LevelProgression(); }

        private void Enter(LevelRoom room)
        {
            Assert.That(progress.TryEnter(room, progress.Snapshot.SessionId), Is.True, room.ToString());
        }

        private void Complete(LevelObjective objective)
        {
            Assert.That(progress.TryComplete(objective, progress.Snapshot.SessionId), Is.True, objective.ToString());
        }

        private void ReachCatacombs()
        {
            Enter(LevelRoom.FirstEncounter);
            Complete(LevelObjective.FirstEnemyDefeated);
            Enter(LevelRoom.Puzzle);
            Complete(LevelObjective.MainPuzzleSolved);
            Enter(LevelRoom.FirstEncounter);
            Enter(LevelRoom.ThroneRoom);
            Complete(LevelObjective.MinibossDefeated);
            Enter(LevelRoom.Library);
            Complete(LevelObjective.LibraryOpened);
            Enter(LevelRoom.Catacombs);
        }

        private void Win()
        {
            Enter(LevelRoom.FinalArena);
            Complete(LevelObjective.FinalEnemyDefeated);
            Enter(LevelRoom.Exit);
        }

        [Test]
        public void StartsInCourtyardWithEmptySession()
        {
            Assert.That(progress.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
            Assert.That(progress.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(progress.Snapshot.SessionId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(progress.Snapshot.IsCompleted, Is.False);
        }

        [Test]
        public void MainRouteCompletesWithoutOptionalObjectives()
        {
            ReachCatacombs();
            Win();
            Assert.That(progress.Snapshot.IsCompleted, Is.True);
            Assert.That(progress.Snapshot.CompletedObjectives, Is.EqualTo(LevelProgression.RequiredObjectives));
        }

        [Test]
        public void BonusRouteReturnsThroughThroneWithoutBypassingLibraryOrFinal()
        {
            ReachCatacombs();
            Complete(LevelObjective.SecretLeverPulled);
            Enter(LevelRoom.BonusRoom);
            Complete(LevelObjective.BonusDiscovered);
            Enter(LevelRoom.ThroneRoom);
            Enter(LevelRoom.BonusRoom);
            Enter(LevelRoom.Catacombs);
            Assert.That(progress.CanEnter(LevelRoom.Exit), Is.False);
            Win();
            Assert.That(progress.HasCompleted(LevelObjective.BonusDiscovered), Is.True);
        }

        [Test]
        public void PuzzleRequiresReturningToTheJunction()
        {
            Enter(LevelRoom.FirstEncounter);
            Assert.That(progress.CanEnter(LevelRoom.Puzzle), Is.False);
            Complete(LevelObjective.FirstEnemyDefeated);
            Assert.That(progress.CanEnter(LevelRoom.ThroneRoom), Is.False);
            Enter(LevelRoom.Puzzle);
            Complete(LevelObjective.MainPuzzleSolved);
            Assert.That(progress.CanEnter(LevelRoom.ThroneRoom), Is.False);
            Enter(LevelRoom.FirstEncounter);
            Enter(LevelRoom.ThroneRoom);
        }

        [Test]
        public void BonusStaysClosedUntilLeverIsUsed()
        {
            ReachCatacombs();
            Assert.That(progress.IsPassageOpen(LevelRoom.Catacombs, LevelRoom.BonusRoom), Is.False);
            Assert.That(progress.IsPassageOpen(LevelRoom.ThroneRoom, LevelRoom.BonusRoom), Is.False);
            Complete(LevelObjective.SecretLeverPulled);
            Assert.That(progress.IsPassageOpen(LevelRoom.Catacombs, LevelRoom.BonusRoom), Is.True);
            Assert.That(progress.IsPassageOpen(LevelRoom.ThroneRoom, LevelRoom.BonusRoom), Is.True);
        }

        [Test]
        public void FinalEnemyMustBeDefeatedBeforeExit()
        {
            ReachCatacombs();
            Enter(LevelRoom.FinalArena);
            Assert.That(progress.TryEnter(LevelRoom.Exit, progress.Snapshot.SessionId), Is.False);
            Complete(LevelObjective.FinalEnemyDefeated);
            Assert.That(progress.Snapshot.IsCompleted, Is.False);
            Enter(LevelRoom.Exit);
            Assert.That(progress.Snapshot.IsCompleted, Is.True);
        }

        [Test]
        public void WrongRoomAndWrongOrderAreRejectedWithoutNotifications()
        {
            var count = 0;
            progress.Changed += _ => count++;
            foreach (LevelObjective objective in Enum.GetValues(typeof(LevelObjective)))
                if (objective != LevelObjective.None)
                    Assert.That(progress.TryComplete(objective, progress.Snapshot.SessionId), Is.False);
            Assert.That(progress.TryEnter(LevelRoom.Exit, progress.Snapshot.SessionId), Is.False);
            Assert.That(progress.TryEnter(LevelRoom.Courtyard, progress.Snapshot.SessionId), Is.False);
            Assert.That(count, Is.Zero);
            Assert.That(progress.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
        }

        [Test]
        public void DuplicateObjectiveChangesNothingAndDoesNotNotifyAgain()
        {
            Enter(LevelRoom.FirstEncounter);
            var changes = new List<ProgressionChange>();
            progress.Changed += changes.Add;
            Complete(LevelObjective.FirstEnemyDefeated);
            Assert.That(progress.TryComplete(LevelObjective.FirstEnemyDefeated, progress.Snapshot.SessionId), Is.False);
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Kind, Is.EqualTo(ProgressionChangeKind.ObjectiveCompleted));
            Assert.That(changes[0].Objective, Is.EqualTo(LevelObjective.FirstEnemyDefeated));
            Assert.That(changes[0].Before.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(changes[0].After.CompletedObjectives, Is.EqualTo(LevelObjective.FirstEnemyDefeated));
        }

        [Test]
        public void NotificationsContainCommittedImmutableSnapshots()
        {
            var original = progress.Snapshot;
            progress.Changed += change =>
            {
                Assert.That(change.Before.SessionId, Is.EqualTo(original.SessionId));
                Assert.That(change.After.Room, Is.EqualTo(progress.Snapshot.Room));
                Assert.That(change.Kind, Is.EqualTo(ProgressionChangeKind.RoomEntered));
            };
            Enter(LevelRoom.FirstEncounter);
            Assert.That(original.Room, Is.EqualTo(LevelRoom.Courtyard));
        }

        [Test]
        public void CompletionIsTerminalAndEmittedOnlyOnce()
        {
            ReachCatacombs();
            var count = 0;
            progress.Changed += change => { if (change.After.IsCompleted) count++; };
            Win();
            foreach (LevelRoom room in Enum.GetValues(typeof(LevelRoom)))
                Assert.That(progress.TryEnter(room, progress.Snapshot.SessionId), Is.False);
            foreach (LevelObjective objective in Enum.GetValues(typeof(LevelObjective)))
                if (objective != LevelObjective.None)
                    Assert.That(progress.TryComplete(objective, progress.Snapshot.SessionId), Is.False);
            Assert.That(count, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ResetClearsStateAndReclosesGates(bool afterCompletion)
        {
            ReachCatacombs();
            Complete(LevelObjective.SecretLeverPulled);
            Enter(LevelRoom.BonusRoom);
            Complete(LevelObjective.BonusDiscovered);
            Enter(LevelRoom.Catacombs);
            if (afterCompletion) Win();
            var oldSession = progress.Snapshot.SessionId;
            var changes = new List<ProgressionChange>();
            progress.Changed += changes.Add;
            progress.Reset();
            Assert.That(progress.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
            Assert.That(progress.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(progress.Snapshot.SessionId, Is.Not.EqualTo(oldSession));
            Assert.That(progress.IsPassageOpen(LevelRoom.Library, LevelRoom.Catacombs), Is.False);
            Assert.That(progress.IsPassageOpen(LevelRoom.ThroneRoom, LevelRoom.BonusRoom), Is.False);
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Kind, Is.EqualTo(ProgressionChangeKind.SessionReset));
            Assert.That(changes[0].Before.SessionId, Is.EqualTo(oldSession));
            Assert.That(changes[0].After.IsCompleted, Is.False);
        }

        [Test]
        public void LateSignalsFromPreviousSessionAreRejectedEvenInMatchingRoom()
        {
            Enter(LevelRoom.FirstEncounter);
            var old = progress.Snapshot.SessionId;
            progress.Reset();
            Assert.That(progress.TryEnter(LevelRoom.FirstEncounter, old), Is.False);
            Enter(LevelRoom.FirstEncounter);
            Assert.That(progress.TryComplete(LevelObjective.FirstEnemyDefeated, old), Is.False);
            Complete(LevelObjective.FirstEnemyDefeated);
        }

        [Test]
        public void DifferentInstancesDoNotShareStateListenersOrSessionTokens()
        {
            var other = new LevelProgression();
            var count = 0;
            progress.Changed += _ => count++;
            Assert.That(other.TryEnter(LevelRoom.FirstEncounter, progress.Snapshot.SessionId), Is.False);
            Assert.That(other.TryEnter(LevelRoom.FirstEncounter, other.Snapshot.SessionId), Is.True);
            Assert.That(progress.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
            Assert.That(count, Is.Zero);
        }

        [Test]
        public void UnsubscribedListenerDoesNotReceiveLaterChanges()
        {
            var count = 0;
            Action<ProgressionChange> listener = _ => count++;
            progress.Changed += listener;
            Enter(LevelRoom.FirstEncounter);
            progress.Changed -= listener;
            Complete(LevelObjective.FirstEnemyDefeated);
            progress.Reset();
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void RejectsReentrantMutationsButAllowsQueries()
        {
            progress.Changed += _ =>
            {
                Assert.That(progress.CanComplete(LevelObjective.FirstEnemyDefeated), Is.True);
                Assert.Throws<InvalidOperationException>(() => progress.Reset());
                Assert.Throws<InvalidOperationException>(() => progress.TryEnter(LevelRoom.Courtyard, progress.Snapshot.SessionId));
                Assert.Throws<InvalidOperationException>(() => progress.TryComplete(LevelObjective.FirstEnemyDefeated, progress.Snapshot.SessionId));
            };
            Enter(LevelRoom.FirstEncounter);
            Assert.That(progress.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
        }

        [Test]
        public void ListenerFailureDoesNotHideChangeFromOtherListenersOrLeaveGuardLocked()
        {
            Action<ProgressionChange> broken = _ => throw new InvalidOperationException("listener failed");
            var count = 0;
            progress.Changed += broken;
            progress.Changed += _ => count++;
            Assert.Throws<AggregateException>(() => progress.TryEnter(LevelRoom.FirstEncounter, progress.Snapshot.SessionId));
            Assert.That(progress.Snapshot.Room, Is.EqualTo(LevelRoom.FirstEncounter));
            Assert.That(count, Is.EqualTo(1));
            progress.Changed -= broken;
            Complete(LevelObjective.FirstEnemyDefeated);
            Assert.That(count, Is.EqualTo(2));
        }

        [TestCase(-1)]
        [TestCase(9)]
        [TestCase(int.MaxValue)]
        public void InvalidRoomIsRejected(int value)
        {
            var room = (LevelRoom)value;
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.CanEnter(room));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.TryEnter(room, progress.Snapshot.SessionId));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.IsPassageOpen(room, LevelRoom.Courtyard));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.IsPassageOpen(LevelRoom.Courtyard, room));
            Assert.That(progress.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(3)]
        [TestCase(127)]
        [TestCase(128)]
        public void InvalidOrCompositeObjectiveIsRejected(int value)
        {
            var objective = (LevelObjective)value;
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.CanComplete(objective));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.TryComplete(objective, progress.Snapshot.SessionId));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.HasCompleted(objective));
            Assert.That(progress.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
        }

        [Test]
        public void AllPassagesRemainSymmetricAndUnconnectedRoomsStayBlocked()
        {
            ReachCatacombs();
            Complete(LevelObjective.SecretLeverPulled);
            foreach (LevelRoom from in Enum.GetValues(typeof(LevelRoom)))
                foreach (LevelRoom to in Enum.GetValues(typeof(LevelRoom)))
                    Assert.That(progress.IsPassageOpen(from, to), Is.EqualTo(progress.IsPassageOpen(to, from)));
            Assert.That(progress.IsPassageOpen(LevelRoom.Puzzle, LevelRoom.ThroneRoom), Is.False);
        }
    }
}
