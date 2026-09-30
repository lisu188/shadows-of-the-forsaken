using System;
using System.Linq;
using NUnit.Framework;
using ShadowsOfTheForsaken.Interactions;

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    public sealed class InteractionRulesTests
    {
        [Test]
        public void NearestVisibleCandidateInsideConeWins()
        {
            var candidates = new[] {
                new InteractionCandidate(1, 1, 1, false), new InteractionCandidate(2, 2, 0.2f, true),
                new InteractionCandidate(3, 4, 0.8f, true), new InteractionCandidate(4, 5, 1, true) };
            Assert.That(InteractionSelection.Select(candidates, 2.5f, 0.5f), Is.EqualTo(2));
        }

        [Test]
        public void RangeAndConeBoundariesAreInclusive()
        {
            Assert.That(InteractionSelection.Select(new[] { new InteractionCandidate(1, 6.25f, 0.5f, true) }, 2.5f, 0.5f), Is.Zero);
            Assert.That(InteractionSelection.Select(new[] { new InteractionCandidate(1, 6.26f, 0.5f, true) }, 2.5f, 0.5f), Is.EqualTo(-1));
        }

        [Test]
        public void FacingThenAuthoredOrderThenIdBreakTiesRegardlessOfEnumeration()
        {
            var candidates = new[] {
                new InteractionCandidate(1, 1, 0.6f, true, -50), new InteractionCandidate(2, 1, 0.8f, true, 1),
                new InteractionCandidate(3, 1, 0.8f, true, 0), new InteractionCandidate(-4, 1, 0.8f, true, 0) };
            foreach (var items in new[] { candidates, candidates.Reverse().ToArray() })
                Assert.That(items[InteractionSelection.Select(items, 2, 0)].Id, Is.EqualTo(-4));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        public void MalformedCandidateDistanceCannotBecomeASelection(float distance)
        {
            Assert.That(InteractionSelection.Select(new[] { new InteractionCandidate(1, distance, 1, true) }, 2, 0), Is.EqualTo(-1));
        }

        [TestCase(0f, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(float.NaN, 0f)]
        [TestCase(2f, 1.1f)]
        [TestCase(2f, float.NaN)]
        public void InvalidSelectionSettingsAreRejected(float range, float facing)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => InteractionSelection.Select(Array.Empty<InteractionCandidate>(), range, facing));
        }

        [Test]
        public void NoCandidatesMeansNoSelection()
        {
            Assert.That(InteractionSelection.Select(Array.Empty<InteractionCandidate>(), 2, 0), Is.EqualTo(-1));
        }

        [Test]
        public void OneShotRejectsRepeatAndStaleTokensAfterReset()
        {
            var use = new OneShotInteraction();
            var first = Guid.NewGuid(); var second = Guid.NewGuid();
            Assert.That(use.TryConsume(Guid.Empty), Is.False);
            use.Reset(first);
            Assert.That(use.TryConsume(first), Is.True);
            Assert.That(use.TryConsume(first), Is.False);
            use.Reset(second);
            Assert.That(use.TryConsume(first), Is.False);
            Assert.That(use.TryConsume(second), Is.True);
        }

        [Test]
        public void EmptySessionCannotResetAOneShot()
        {
            Assert.Throws<ArgumentException>(() => new OneShotInteraction().Reset(Guid.Empty));
        }

        [Test]
        public void OccupiedOpenGateDefersClosureUntilClear()
        {
            var gate = new GateClosure();
            gate.Refresh(false, true);
            Assert.That(gate.IsOpen && gate.ClosePending, Is.True);
            gate.Refresh(false, false);
            Assert.That(gate.IsOpen || gate.ClosePending, Is.False);
        }

        [Test]
        public void ApproachingAClosedGateCannotUnlockIt()
        {
            var gate = new GateClosure();
            gate.Refresh(false, false);
            gate.Refresh(false, true);
            Assert.That(gate.IsOpen || gate.ClosePending, Is.False);
        }

        [Test]
        public void ReopeningCancelsPendingClosure()
        {
            var gate = new GateClosure();
            gate.Refresh(false, true);
            gate.Refresh(true, false);
            Assert.That(gate.IsOpen, Is.True);
            Assert.That(gate.ClosePending, Is.False);
        }
    }
}
