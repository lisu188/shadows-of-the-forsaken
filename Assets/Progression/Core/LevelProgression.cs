using System;
using System.Collections.Generic;

namespace ShadowsOfTheForsaken.Progression
{
    public enum LevelRoom
    {
        Courtyard = 0,
        FirstEncounter = 1,
        Puzzle = 2,
        ThroneRoom = 3,
        Library = 4,
        Catacombs = 5,
        BonusRoom = 6,
        FinalArena = 7,
        Exit = 8
    }

    [Flags]
    public enum LevelObjective
    {
        None = 0,
        FirstEnemyDefeated = 1,
        MainPuzzleSolved = 2,
        MinibossDefeated = 4,
        LibraryOpened = 8,
        SecretLeverPulled = 16,
        BonusDiscovered = 32,
        FinalEnemyDefeated = 64
    }

    public enum ProgressionChangeKind
    {
        RoomEntered,
        ObjectiveCompleted,
        SessionReset
    }

    public readonly struct LevelProgressSnapshot
    {
        public LevelRoom Room { get; }
        public LevelObjective CompletedObjectives { get; }
        public Guid SessionId { get; }
        public bool IsCompleted => Room == LevelRoom.Exit;

        internal LevelProgressSnapshot(LevelRoom room, LevelObjective objectives, Guid sessionId)
        {
            Room = room;
            CompletedObjectives = objectives;
            SessionId = sessionId;
        }
    }

    public readonly struct ProgressionChange
    {
        public LevelProgressSnapshot Before { get; }
        public LevelProgressSnapshot After { get; }
        public ProgressionChangeKind Kind { get; }
        public LevelObjective Objective { get; }

        internal ProgressionChange(LevelProgressSnapshot before, LevelProgressSnapshot after,
            ProgressionChangeKind kind, LevelObjective objective)
        {
            Before = before;
            After = after;
            Kind = kind;
            Objective = objective;
        }
    }

    public sealed class LevelProgression
    {
        public const LevelObjective RequiredObjectives = LevelObjective.FirstEnemyDefeated |
            LevelObjective.MainPuzzleSolved | LevelObjective.MinibossDefeated |
            LevelObjective.LibraryOpened | LevelObjective.FinalEnemyDefeated;

        private readonly struct Passage
        {
            public readonly LevelRoom From;
            public readonly LevelRoom To;
            public readonly LevelObjective Requires;

            public Passage(LevelRoom from, LevelRoom to, LevelObjective requires)
            {
                From = from;
                To = to;
                Requires = requires;
            }
        }

        private static readonly Passage[] Passages =
        {
            new Passage(LevelRoom.Courtyard, LevelRoom.FirstEncounter, LevelObjective.None),
            new Passage(LevelRoom.FirstEncounter, LevelRoom.Puzzle, LevelObjective.FirstEnemyDefeated),
            new Passage(LevelRoom.FirstEncounter, LevelRoom.ThroneRoom,
                LevelObjective.FirstEnemyDefeated | LevelObjective.MainPuzzleSolved),
            new Passage(LevelRoom.ThroneRoom, LevelRoom.Library, LevelObjective.MinibossDefeated),
            new Passage(LevelRoom.Library, LevelRoom.Catacombs, LevelObjective.LibraryOpened),
            new Passage(LevelRoom.Catacombs, LevelRoom.FinalArena, LevelObjective.LibraryOpened),
            new Passage(LevelRoom.Catacombs, LevelRoom.BonusRoom,
                LevelObjective.LibraryOpened | LevelObjective.SecretLeverPulled),
            new Passage(LevelRoom.ThroneRoom, LevelRoom.BonusRoom,
                LevelObjective.MinibossDefeated | LevelObjective.LibraryOpened | LevelObjective.SecretLeverPulled),
            new Passage(LevelRoom.FinalArena, LevelRoom.Exit, RequiredObjectives)
        };

        private bool publishing;
        public LevelProgressSnapshot Snapshot { get; private set; } =
            new LevelProgressSnapshot(LevelRoom.Courtyard, LevelObjective.None, Guid.NewGuid());
        public event Action<ProgressionChange> Changed;

        public bool HasCompleted(LevelObjective objective)
        {
            ValidateObjective(objective);
            return HasAll(objective);
        }

        public bool IsPassageOpen(LevelRoom from, LevelRoom to)
        {
            ValidateRoom(from);
            ValidateRoom(to);
            foreach (var passage in Passages)
                if ((passage.From == from && passage.To == to) ||
                    (passage.From == to && passage.To == from))
                    return HasAll(passage.Requires | EntryRequirements(from) | EntryRequirements(to));
            return false;
        }

        public bool CanEnter(LevelRoom destination)
        {
            ValidateRoom(destination);
            return !Snapshot.IsCompleted && IsPassageOpen(Snapshot.Room, destination);
        }

        public bool TryEnter(LevelRoom destination, Guid sessionId)
        {
            EnsureNotPublishing();
            ValidateRoom(destination);
            if (sessionId != Snapshot.SessionId || !CanEnter(destination))
                return false;
            Commit(new LevelProgressSnapshot(destination, Snapshot.CompletedObjectives, sessionId),
                ProgressionChangeKind.RoomEntered, LevelObjective.None);
            return true;
        }

        public bool CanComplete(LevelObjective objective)
        {
            ValidateObjective(objective);
            if (Snapshot.IsCompleted || HasAll(objective))
                return false;
            switch (objective)
            {
                case LevelObjective.FirstEnemyDefeated:
                    return Snapshot.Room == LevelRoom.FirstEncounter;
                case LevelObjective.MainPuzzleSolved:
                    return Snapshot.Room == LevelRoom.Puzzle && HasAll(LevelObjective.FirstEnemyDefeated);
                case LevelObjective.MinibossDefeated:
                    return Snapshot.Room == LevelRoom.ThroneRoom &&
                        HasAll(LevelObjective.FirstEnemyDefeated | LevelObjective.MainPuzzleSolved);
                case LevelObjective.LibraryOpened:
                    return Snapshot.Room == LevelRoom.Library && HasAll(LevelObjective.MinibossDefeated);
                case LevelObjective.SecretLeverPulled:
                    return Snapshot.Room == LevelRoom.Catacombs && HasAll(LevelObjective.LibraryOpened);
                case LevelObjective.BonusDiscovered:
                    return Snapshot.Room == LevelRoom.BonusRoom && HasAll(LevelObjective.SecretLeverPulled);
                case LevelObjective.FinalEnemyDefeated:
                    return Snapshot.Room == LevelRoom.FinalArena &&
                        HasAll(LevelObjective.LibraryOpened | LevelObjective.MinibossDefeated);
                default:
                    throw new ArgumentOutOfRangeException(nameof(objective));
            }
        }

        public bool TryComplete(LevelObjective objective, Guid sessionId)
        {
            EnsureNotPublishing();
            ValidateObjective(objective);
            if (sessionId != Snapshot.SessionId || !CanComplete(objective))
                return false;
            Commit(new LevelProgressSnapshot(Snapshot.Room, Snapshot.CompletedObjectives | objective, sessionId),
                ProgressionChangeKind.ObjectiveCompleted, objective);
            return true;
        }

        public void Reset()
        {
            EnsureNotPublishing();
            Commit(new LevelProgressSnapshot(LevelRoom.Courtyard, LevelObjective.None, Guid.NewGuid()),
                ProgressionChangeKind.SessionReset, LevelObjective.None);
        }

        private bool HasAll(LevelObjective required) => (Snapshot.CompletedObjectives & required) == required;

        private static LevelObjective EntryRequirements(LevelRoom room)
        {
            switch (room)
            {
                case LevelRoom.Puzzle: return LevelObjective.FirstEnemyDefeated;
                case LevelRoom.ThroneRoom: return LevelObjective.FirstEnemyDefeated | LevelObjective.MainPuzzleSolved;
                case LevelRoom.Library: return LevelObjective.MinibossDefeated;
                case LevelRoom.Catacombs:
                case LevelRoom.FinalArena: return LevelObjective.LibraryOpened;
                case LevelRoom.BonusRoom: return LevelObjective.LibraryOpened | LevelObjective.SecretLeverPulled;
                case LevelRoom.Exit: return RequiredObjectives;
                default: return LevelObjective.None;
            }
        }

        private static void ValidateRoom(LevelRoom room)
        {
            if (room < LevelRoom.Courtyard || room > LevelRoom.Exit)
                throw new ArgumentOutOfRangeException(nameof(room), room, "Unknown level room.");
        }

        private static void ValidateObjective(LevelObjective objective)
        {
            var value = (int)objective;
            if (value < 1 || value > (int)LevelObjective.FinalEnemyDefeated || (value & (value - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(objective), objective, "A single known objective is required.");
        }

        private void EnsureNotPublishing()
        {
            if (publishing)
                throw new InvalidOperationException("Progression cannot be mutated during change notification.");
        }

        private void Commit(LevelProgressSnapshot next, ProgressionChangeKind kind, LevelObjective objective)
        {
            var change = new ProgressionChange(Snapshot, next, kind, objective);
            Snapshot = next;
            var listeners = Changed;
            if (listeners == null)
                return;
            List<Exception> errors = null;
            publishing = true;
            try
            {
                foreach (Action<ProgressionChange> listener in listeners.GetInvocationList())
                {
                    try { listener(change); }
                    catch (Exception error)
                    {
                        if (errors == null) errors = new List<Exception>();
                        errors.Add(error);
                    }
                }
            }
            finally { publishing = false; }
            if (errors != null)
                throw new AggregateException("State committed, but a progression listener failed.", errors);
        }
    }
}
