using System;
using ShadowsOfTheForsaken.Progression;
using ShadowsOfTheForsaken.Puzzles;
using UnityEngine;

namespace ShadowsOfTheForsaken.Level
{
    [DisallowMultipleComponent]
    public sealed class RunePuzzle : MonoBehaviour
    {
        [SerializeField] private LevelProgressionController progression;
        [SerializeField] private int[] orderedRunes = { 0, 1, 2 };
        [SerializeField] private string hint = "Moon, thorn, crown: the fallen king's oath.";
        private RuneSequence sequence;
        private Guid puzzleSession;
        private LevelProgressionController subscribed;

        public string Hint => hint;
        public string Feedback { get; private set; } = "";
        public int Progress => sequence == null ? 0 : sequence.Progress;
        public bool Solved => progression != null &&
            (progression.Snapshot.CompletedObjectives & LevelObjective.MainPuzzleSolved) != 0;
        public bool CanPress => isActiveAndEnabled && progression != null &&
            progression.CanComplete(LevelObjective.MainPuzzleSolved);

        public void Configure(LevelProgressionController controller, int[] runes, string readableHint)
        {
            if (runes == null) throw new ArgumentNullException(nameof(runes));
            if (runes.Length == 0) throw new ArgumentException("A rune sequence cannot be empty.", nameof(runes));
            Unsubscribe();
            progression = controller;
            orderedRunes = (int[])runes.Clone();
            hint = readableHint;
            ResetMechanism();
            Subscribe();
        }

        private void Awake() => ResetMechanism();
        private void OnEnable()
        {
            if (progression != null && puzzleSession != progression.Snapshot.SessionId) ResetMechanism();
            Subscribe();
        }
        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (!isActiveAndEnabled || progression == null || subscribed == progression) return;
            subscribed = progression;
            subscribed.Changed += Observe;
        }

        private void Unsubscribe()
        {
            if (subscribed != null) subscribed.Changed -= Observe;
            subscribed = null;
        }

        private void Observe(ProgressionChange change)
        {
            if (change.Kind == ProgressionChangeKind.SessionReset) ResetMechanism();
        }

        private void ResetMechanism()
        {
            sequence = new RuneSequence(orderedRunes);
            puzzleSession = progression != null ? progression.Snapshot.SessionId : Guid.Empty;
            Feedback = "";
        }

        public bool TryPress(int runeId, Guid capturedSession)
        {
            if (!CanPress || capturedSession != puzzleSession || capturedSession != progression.Snapshot.SessionId) return false;
            var result = sequence.Press(runeId);
            switch (result)
            {
                case RunePressResult.Incorrect:
                    Feedback = "The runes dim. Begin again. " + hint;
                    return true;
                case RunePressResult.Accepted:
                    Feedback = "The seal remembers (" + sequence.Progress + "/" + sequence.Length + ").";
                    return true;
                case RunePressResult.Solved:
                    if (progression.TryComplete(LevelObjective.MainPuzzleSolved, capturedSession))
                    {
                        Feedback = "The throne-room seal is broken. Return to the lower junction.";
                        return true;
                    }
                    sequence.Reset();
                    Feedback = "The seal waits for you to return to its chamber.";
                    return false;
                default: return false;
            }
        }
    }
}
