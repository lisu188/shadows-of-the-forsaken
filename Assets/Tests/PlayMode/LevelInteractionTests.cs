using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public sealed class LevelInteractionTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly InputTestFixture inputFixture = new InputTestFixture();
        private LevelProgressionController progression;
        private Component movement;
        private InputActionAsset actions;
        private Type movementType;
        private float timeScale;
        private Keyboard keyboard;
        private InputSettings.UpdateMode previousInputMode;
        private static readonly MethodInfo ManualInputUpdate = typeof(InputSystem).GetMethod("Update",
            BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(InputUpdateType) }, null);

        private Component Add(string name, string type)
        {
            var owner = new GameObject(name);
            objects.Add(owner);
            return owner.AddComponent(Type.GetType("ShadowsOfTheForsaken.Level." + type + ", Assembly-CSharp", true));
        }

        private static object Call(Component target, string method, params object[] args)
        {
            try { return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        private static T Read<T>(Component target, string property) => (T)target.GetType().GetProperty(property).GetValue(target);

        private static void PumpInput()
        {
            Assert.That(ManualInputUpdate, Is.Not.Null, "Pinned Input System must expose its internal typed update pump.");
            try { ManualInputUpdate.Invoke(null, new object[] { InputUpdateType.Manual }); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        [SetUp]
        public void SetUp()
        {
            inputFixture.Setup();
            timeScale = Time.timeScale;
            Time.timeScale = 1;
            previousInputMode = InputSystem.settings.updateMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            keyboard = InputSystem.AddDevice<Keyboard>();
            var session = new GameObject("Interaction progression");
            objects.Add(session);
            progression = session.AddComponent<LevelProgressionController>();
            movementType = Type.GetType("PlayerMovement, Assembly-CSharp", true);
            actions = ScriptableObject.CreateInstance<InputActionAsset>();
            actions.devices = new InputDevice[] { keyboard };
            var map = actions.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            map.AddAction("Jump", InputActionType.Button);
            map.AddAction("Attack", InputActionType.Button);
            map.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            var player = new GameObject("Interaction player");
            objects.Add(player);
            player.SetActive(false);
            movement = player.AddComponent(movementType);
            movementType.GetField("inputActions").SetValue(movement, actions);
            player.SetActive(true);
            Call(movement, "OnApplicationFocus", true);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            PumpInput();
            Assert.That((bool)movementType.GetField("inputUpdated", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(movement),
                Is.True, "Neutral input must reach the movement consumer before E delivery is tested.");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
                objects.Clear();
                Object.DestroyImmediate(actions);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.updateMode = previousInputMode;
                Time.timeScale = timeScale;
            }
            finally
            {
                try { inputFixture.TearDown(); }
                finally { Time.timeScale = timeScale; }
            }
        }

        private void ReachPuzzle()
        {
            var session = progression.Snapshot.SessionId;
            Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, session), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.FirstEnemyDefeated, session), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.Puzzle, session), Is.True);
        }

        [Test]
        public void RuneMechanismRejectsStaleSessionAndAllowsRetryAfterWrongPress()
        {
            ReachPuzzle();
            var puzzle = Add("Rune puzzle", "RunePuzzle");
            Call(puzzle, "Configure", progression, new[] { 0, 1, 2 }, "Moon, thorn, crown");
            var session = progression.Snapshot.SessionId;
            Assert.That(Call(puzzle, "TryPress", 0, session), Is.True);
            Assert.That(Call(puzzle, "TryPress", 2, session), Is.True);
            Assert.That(Read<int>(puzzle, "Progress"), Is.Zero);
            Call(puzzle, "TryPress", 0, session);
            Call(puzzle, "TryPress", 1, session);
            Assert.That(Call(puzzle, "TryPress", 2, session), Is.True);
            Assert.That(progression.IsPassageOpen(LevelRoom.FirstEncounter, LevelRoom.ThroneRoom), Is.True);
            progression.TryResetSession();
            ReachPuzzle();
            Assert.That(Read<int>(puzzle, "Progress"), Is.Zero);
            Assert.That(Call(puzzle, "TryPress", 0, session), Is.False);
            Assert.That(Call(puzzle, "TryPress", 0, progression.Snapshot.SessionId), Is.True);
        }

        [Test]
        public void PhysicalGateOpensOnlyFromProgressionAndClosesAfterResetOrReenable()
        {
            var barrierOwner = new GameObject("Full height barrier");
            objects.Add(barrierOwner);
            var barrier = barrierOwner.AddComponent<BoxCollider>();
            barrier.size = new Vector3(4, 10, 0.5f);
            var gate = Add("Gate observer", "PassageGate");
            Call(gate, "Configure", progression, LevelRoom.FirstEncounter, LevelRoom.Puzzle, barrier, null);
            Assert.That(barrier.enabled, Is.True);
            ReachPuzzle();
            Assert.That(barrier.enabled, Is.False);
            ((Behaviour)gate).enabled = false;
            progression.TryResetSession();
            ((Behaviour)gate).enabled = true;
            Assert.That(barrier.enabled, Is.True);
            Assert.That(Read<bool>(gate, "IsOpen"), Is.False);
        }

        private Component Hint(string name, Vector3 position, string text)
        {
            var hint = Add(name, "LevelInteractable");
            hint.transform.position = position;
            hint.gameObject.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
            Call(hint, "ConfigureHint", text);
            return hint;
        }

        [Test]
        public void InteractionChoosesOneNearestVisibleTargetAndCannotReachThroughWall()
        {
            var interaction = Add("Player interaction", "PlayerInteraction");
            Call(interaction, "Configure", movement, progression, 2.6f);
            var nearest = Hint("Nearest inscription", new Vector3(-0.5f, 0.4f, 1), "Near hint");
            var far = Hint("Far inscription", new Vector3(0.5f, 0.4f, 2), "Far hint");
            Physics.SyncTransforms();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            PumpInput();
            Call(movement, "Simulate", 1f / 60);
            Assert.That(Read<string>(interaction, "LastFeedback"), Is.EqualTo("Near hint"));
            Assert.That(Read<string>(far, "Feedback"), Is.Empty);
            var wall = new GameObject("Opaque wall");
            objects.Add(wall);
            wall.transform.position = new Vector3(0, 0.6f, 0.5f);
            wall.AddComponent<BoxCollider>().size = new Vector3(5, 3, 0.2f);
            Physics.SyncTransforms();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            PumpInput();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            PumpInput();
            Call(movement, "Simulate", 1f / 60);
            Assert.That(Read<Component>(interaction, "Focused"), Is.Null);
            Assert.That(Call(interaction, "TryInteract"), Is.False);
            Assert.That(Read<string>(nearest, "Feedback"), Is.EqualTo("Near hint"));
        }

        [Test]
        public void RejectedCrossingReturnsToSourceAndCannotGrantLockedRoom()
        {
            var session = progression.Snapshot.SessionId;
            progression.TryEnter(LevelRoom.FirstEncounter, session);
            var transition = Add("Puzzle transition", "RoomTransition");
            Call(transition, "Configure", progression, movement, LevelRoom.FirstEncounter, LevelRoom.Puzzle,
                new Vector3(0, 0, -2), new Vector3(0, 0, 2));
            movement.transform.position = new Vector3(0, 2, 0.5f); // A jump cannot bypass a locked route.
            Call(transition, "OnTriggerEnter", movement.GetComponent<CharacterController>());
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.FirstEncounter));
            Assert.That(movement.transform.position, Is.EqualTo(new Vector3(0, 0, -2)));
            progression.TryComplete(LevelObjective.FirstEnemyDefeated, session);
            movement.transform.position = new Vector3(0, 0, -0.5f);
            Call(transition, "OnTriggerEnter", movement.GetComponent<CharacterController>());
            movement.transform.position = new Vector3(0, 0, 0.5f);
            Call(transition, "OnTriggerStay", movement.GetComponent<CharacterController>());
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Puzzle));
            progression.TryResetSession();
            Call(transition, "OnTriggerStay", movement.GetComponent<CharacterController>());
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
        }
    }
}
