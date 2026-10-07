using System;
using NUnit.Framework;
using ShadowsOfTheForsaken.Puzzles;

namespace ShadowsOfTheForsaken.Tests.Puzzles
{
    public sealed class RuneSequenceTests
    {
        [Test]
        public void VisibleOrderedHintCorrespondsToExactlyOneSuccessfulSequence()
        {
            var sequence = new RuneSequence(new[] { 0, 1, 2 });
            Assert.That(sequence.Press(0), Is.EqualTo(RunePressResult.Accepted));
            Assert.That(sequence.Press(1), Is.EqualTo(RunePressResult.Accepted));
            Assert.That(sequence.Press(2), Is.EqualTo(RunePressResult.Solved));
            Assert.That(sequence.Solved, Is.True);
            Assert.That(sequence.Press(2), Is.EqualTo(RunePressResult.AlreadySolved));
            Assert.That(sequence.Progress, Is.EqualTo(3));
        }

        [Test]
        public void WrongRuneAtEveryStepAllowsCompleteRetryWithoutConsumedRunes()
        {
            for (int failAt = 0; failAt < 3; failAt++)
            {
                var sequence = new RuneSequence(new[] { 0, 1, 2 });
                for (int i = 0; i < failAt; i++) sequence.Press(i);
                Assert.That(sequence.Press(99), Is.EqualTo(RunePressResult.Incorrect));
                Assert.That(sequence.Progress, Is.Zero);
                sequence.Press(0);
                sequence.Press(1);
                Assert.That(sequence.Press(2), Is.EqualTo(RunePressResult.Solved));
            }
        }

        [Test]
        public void RepeatedFirstRuneIsAnErrorAndDoesNotPartiallyRestartSequence()
        {
            var sequence = new RuneSequence(new[] { 0, 1, 2 });
            sequence.Press(0);
            Assert.That(sequence.Press(0), Is.EqualTo(RunePressResult.Incorrect));
            Assert.That(sequence.Progress, Is.Zero);
            Assert.That(sequence.Press(1), Is.EqualTo(RunePressResult.Incorrect));
        }

        [Test]
        public void ResetClearsSolvedAndPartialMechanisms()
        {
            var sequence = new RuneSequence(new[] { 0, 1 });
            sequence.Press(0);
            sequence.Reset();
            Assert.That(sequence.Progress, Is.Zero);
            sequence.Press(0);
            sequence.Press(1);
            sequence.Reset();
            Assert.That(sequence.Solved, Is.False);
            Assert.That(sequence.Press(0), Is.EqualTo(RunePressResult.Accepted));
        }

        [Test]
        public void CopiesConfigurationAndRejectsMissingSequence()
        {
            var source = new[] { 4 };
            var sequence = new RuneSequence(source);
            source[0] = 8;
            Assert.That(sequence.Press(4), Is.EqualTo(RunePressResult.Solved));
            Assert.Throws<ArgumentNullException>(() => new RuneSequence(null));
            Assert.Throws<ArgumentException>(() => new RuneSequence(Array.Empty<int>()));
        }
    }
}
