using System;

namespace ShadowsOfTheForsaken.Puzzles
{
    public enum RunePressResult { Incorrect, Accepted, Solved, AlreadySolved }

    /// <summary>The rune mechanism only: wrong input clears the sequence without consuming a rune.</summary>
    public sealed class RuneSequence
    {
        private readonly int[] sequence;
        public int Progress { get; private set; }
        public int Length => sequence.Length;
        public bool Solved => Progress == sequence.Length;

        public RuneSequence(int[] orderedRunes)
        {
            if (orderedRunes == null) throw new ArgumentNullException(nameof(orderedRunes));
            if (orderedRunes.Length == 0) throw new ArgumentException("A rune sequence cannot be empty.", nameof(orderedRunes));
            sequence = (int[])orderedRunes.Clone();
        }

        public RunePressResult Press(int rune)
        {
            if (Solved) return RunePressResult.AlreadySolved;
            if (rune != sequence[Progress])
            {
                Progress = 0;
                return RunePressResult.Incorrect;
            }
            Progress++;
            return Solved ? RunePressResult.Solved : RunePressResult.Accepted;
        }

        public void Reset() => Progress = 0;
    }
}
