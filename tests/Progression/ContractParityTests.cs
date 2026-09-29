using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;

namespace ShadowsOfTheForsaken.Tests.Contract
{
    public sealed class ContractParityTests
    {
        private static readonly Dictionary<string, LevelRoom> Rooms = new Dictionary<string, LevelRoom>
        {
            { "courtyard", LevelRoom.Courtyard }, { "first_encounter", LevelRoom.FirstEncounter },
            { "puzzle", LevelRoom.Puzzle }, { "throne_room", LevelRoom.ThroneRoom },
            { "library", LevelRoom.Library }, { "catacombs", LevelRoom.Catacombs },
            { "bonus_room", LevelRoom.BonusRoom }, { "final_arena", LevelRoom.FinalArena },
            { "exit", LevelRoom.Exit }
        };
        private static readonly Dictionary<string, LevelObjective> Objectives = new Dictionary<string, LevelObjective>
        {
            { "first_enemy_defeated", LevelObjective.FirstEnemyDefeated },
            { "main_puzzle_solved", LevelObjective.MainPuzzleSolved },
            { "miniboss_defeated", LevelObjective.MinibossDefeated },
            { "library_opened", LevelObjective.LibraryOpened },
            { "secret_lever_pulled", LevelObjective.SecretLeverPulled },
            { "bonus_discovered", LevelObjective.BonusDiscovered },
            { "final_enemy_defeated", LevelObjective.FinalEnemyDefeated }
        };

        private readonly struct Step
        {
            public readonly LevelRoom Room;
            public readonly LevelObjective Objective;
            public Step(LevelRoom room, LevelObjective objective) { Room = room; Objective = objective; }
            public bool Apply(LevelProgression progress) => Objective == LevelObjective.None
                ? progress.TryEnter(Room, progress.Snapshot.SessionId)
                : progress.TryComplete(Objective, progress.Snapshot.SessionId);
        }

        private static LevelObjective Requirements(JsonElement value) => value.EnumerateArray()
            .Aggregate(LevelObjective.None, (mask, item) => mask | Objectives[item.GetString()]);
        private static bool Meets(LevelObjective flags, JsonElement item)
        {
            var required = Requirements(item.GetProperty("requires"));
            return (flags & required) == required;
        }
        private static LevelProgression Replay(IEnumerable<Step> path)
        {
            var progress = new LevelProgression();
            foreach (var step in path) Assert.That(step.Apply(progress), Is.True, "Failed to replay reachable state.");
            return progress;
        }
        private static (LevelRoom Room, LevelObjective Flags) Key(LevelProgression progress) =>
            (progress.Snapshot.Room, progress.Snapshot.CompletedObjectives);

        [Test]
        public void RuntimeIdentifiersAndRequiredFlagsMatchAuthoritativeContract()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "level-contract.json")));
            var root = document.RootElement;
            Assert.That(root.GetProperty("schema_version").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("rooms").EnumerateArray().Select(x => x.GetProperty("id").GetString()), Is.EquivalentTo(Rooms.Keys));
            Assert.That(root.GetProperty("events").EnumerateArray().Select(x => x.GetProperty("id").GetString()), Is.EquivalentTo(Objectives.Keys));
            Assert.That(Rooms.Values, Is.EquivalentTo(Enum.GetValues(typeof(LevelRoom))));
            Assert.That(Objectives.Values, Is.EquivalentTo(Enum.GetValues(typeof(LevelObjective)).Cast<LevelObjective>().Where(x => x != LevelObjective.None)));
            Assert.That(Requirements(root.GetProperty("required_events")), Is.EqualTo(LevelProgression.RequiredObjectives));
            Assert.That(Rooms[root.GetProperty("start").GetString()], Is.EqualTo(new LevelProgression().Snapshot.Room));
            Assert.That(Rooms[root.GetProperty("exit").GetString()], Is.EqualTo(LevelRoom.Exit));
        }

        [TestCase(false, 29)]
        [TestCase(true, 63)]
        public void EveryReachableStateAndCommandMatchesJsonOracle(bool includeSecrets, int expectedStates)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "level-contract.json")));
            var root = document.RootElement;
            var rooms = root.GetProperty("rooms").EnumerateArray().ToDictionary(x => Rooms[x.GetProperty("id").GetString()]);
            var events = root.GetProperty("events").EnumerateArray().ToDictionary(x => Objectives[x.GetProperty("id").GetString()]);
            var edges = root.GetProperty("connections").EnumerateArray().ToArray();
            var paths = new Dictionary<(LevelRoom Room, LevelObjective Flags), List<Step>>
            {
                { (LevelRoom.Courtyard, LevelObjective.None), new List<Step>() }
            };
            var queue = new Queue<(LevelRoom Room, LevelObjective Flags)>(paths.Keys);
            var comparisons = 0;
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                var steps = new List<(Step Command, bool Allowed, bool Optional)>();
                foreach (var room in rooms)
                {
                    var connected = edges.Any(edge =>
                        ((Rooms[edge.GetProperty("from").GetString()] == state.Room && Rooms[edge.GetProperty("to").GetString()] == room.Key) ||
                         (Rooms[edge.GetProperty("to").GetString()] == state.Room && Rooms[edge.GetProperty("from").GetString()] == room.Key)) && Meets(state.Flags, edge));
                    steps.Add((new Step(room.Key, LevelObjective.None),
                        state.Room != LevelRoom.Exit && connected && Meets(state.Flags, room.Value),
                        room.Value.GetProperty("optional").GetBoolean()));
                }
                foreach (var item in events)
                    steps.Add((new Step(state.Room, item.Key),
                        state.Room != LevelRoom.Exit && (state.Flags & item.Key) == 0 &&
                        Rooms[item.Value.GetProperty("room").GetString()] == state.Room && Meets(state.Flags, item.Value),
                        item.Value.GetProperty("optional").GetBoolean()));
                foreach (var step in steps)
                {
                    var progress = Replay(paths[state]);
                    Assert.That(Key(progress), Is.EqualTo(state));
                    var before = progress.Snapshot;
                    var notifications = 0;
                    progress.Changed += _ => notifications++;
                    var canApply = step.Command.Objective == LevelObjective.None
                        ? progress.CanEnter(step.Command.Room) : progress.CanComplete(step.Command.Objective);
                    var context = $"{state}: {step.Command.Room}/{step.Command.Objective}";
                    Assert.That(canApply, Is.EqualTo(step.Allowed), context);
                    Assert.That(step.Command.Apply(progress), Is.EqualTo(step.Allowed), context);
                    Assert.That(notifications, Is.EqualTo(step.Allowed ? 1 : 0), context);
                    Assert.That(progress.Snapshot.SessionId, Is.EqualTo(before.SessionId));
                    var expected = !step.Allowed ? state : step.Command.Objective == LevelObjective.None
                        ? (step.Command.Room, state.Flags) : (state.Room, state.Flags | step.Command.Objective);
                    Assert.That(Key(progress), Is.EqualTo(expected), context);
                    comparisons++;
                    if (step.Allowed && (includeSecrets || !step.Optional) && !paths.ContainsKey(expected))
                    {
                        var path = new List<Step>(paths[state]) { step.Command };
                        paths.Add(expected, path);
                        queue.Enqueue(expected);
                    }
                }
            }
            Assert.That(paths.Count, Is.EqualTo(expectedStates));
            Assert.That(paths.Keys.Any(state => state.Room == LevelRoom.Exit), Is.True);
            foreach (var state in paths.Keys.Where(state => state.Room == LevelRoom.Exit))
                Assert.That(state.Flags & LevelProgression.RequiredObjectives, Is.EqualTo(LevelProgression.RequiredObjectives));
            TestContext.Out.WriteLine($"JSON parity: {paths.Count} reachable states; {comparisons} commands; includeSecrets={includeSecrets}.");
        }
    }
}
