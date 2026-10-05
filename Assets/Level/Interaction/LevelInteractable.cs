using System;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;

namespace ShadowsOfTheForsaken.Level
{
    public enum LevelInteractionKind { Hint, Rune, Objective }

    [DisallowMultipleComponent]
    public sealed class LevelInteractable : MonoBehaviour
    {
        [SerializeField] private LevelInteractionKind kind;
        [SerializeField] private LevelProgressionController progression;
        [SerializeField] private RunePuzzle runePuzzle;
        [SerializeField] private int runeId;
        [SerializeField] private LevelObjective objective;
        [SerializeField] private string label = "Inspect";
        [SerializeField, TextArea] private string hint;

        public string Prompt => "E — " + label;
        public string Feedback { get; private set; } = "";
        public bool CanInteract
        {
            get
            {
                if (!isActiveAndEnabled) return false;
                switch (kind)
                {
                    case LevelInteractionKind.Hint: return true;
                    case LevelInteractionKind.Rune: return runePuzzle != null && runePuzzle.CanPress;
                    case LevelInteractionKind.Objective: return IsMechanismObjective(objective) && progression != null && progression.CanComplete(objective);
                    default: return false;
                }
            }
        }

        public void ConfigureRune(RunePuzzle puzzle, int id, string readableLabel)
        {
            kind = LevelInteractionKind.Rune;
            runePuzzle = puzzle;
            runeId = id;
            label = readableLabel;
        }

        public void ConfigureObjective(LevelProgressionController controller, LevelObjective goal, string readableLabel)
        {
            if (!IsMechanismObjective(goal))
                throw new ArgumentOutOfRangeException(nameof(goal), "Interaction objectives are the library, secret lever or bonus relic.");
            kind = LevelInteractionKind.Objective;
            progression = controller;
            objective = goal;
            label = readableLabel;
        }

        public void ConfigureHint(string readableHint)
        {
            kind = LevelInteractionKind.Hint;
            hint = readableHint;
            label = "Read the inscription";
        }

        public bool TryInteract(Guid capturedSession)
        {
            if (!CanInteract) return false;
            switch (kind)
            {
                case LevelInteractionKind.Hint:
                    Feedback = hint;
                    return true;
                case LevelInteractionKind.Rune:
                    bool pressed = runePuzzle.TryPress(runeId, capturedSession);
                    Feedback = runePuzzle.Feedback;
                    return pressed;
                case LevelInteractionKind.Objective:
                    if (!progression.TryComplete(objective, capturedSession)) return false;
                    switch (objective)
                    {
                        case LevelObjective.LibraryOpened: Feedback = "The forbidden book releases the hidden door to the catacombs."; break;
                        case LevelObjective.SecretLeverPulled: Feedback = "A concealed branch and the throne-room return passage open."; break;
                        case LevelObjective.BonusDiscovered: Feedback = "You found the forgotten relic. Return to the main route when ready."; break;
                    }
                    return true;
                default: return false;
            }
        }

        private static bool IsMechanismObjective(LevelObjective goal) => goal == LevelObjective.LibraryOpened ||
            goal == LevelObjective.SecretLeverPulled || goal == LevelObjective.BonusDiscovered;
    }
}
