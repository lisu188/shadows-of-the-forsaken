using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public sealed class InteractionComponentTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly Vector3 origin = new Vector3(2000, 0, 2000);
        private LevelProgressionController progression;
        private Component movement, interactor;
        private CharacterController character;
        private Keyboard keyboard;
        private InputActionAsset input;
        private InputSettings.UpdateMode previousMode;
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousMode = InputSystem.settings.updateMode;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousTimeScale = Time.timeScale;
            // Batch editors have no focused Game View; preserve the actor's own
            // focus gate while routing these synthetic devices to player updates.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            Time.timeScale = 1;
            keyboard = InputSystem.AddDevice<Keyboard>();
            input = ScriptableObject.CreateInstance<InputActionAsset>();
            input.devices = new InputDevice[] { keyboard };
            var map = input.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2")
                .AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            map.AddAction("Attack", InputActionType.Button, "<Keyboard>/enter");
            map.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            progression = Make("Session", Vector3.zero).AddComponent<LevelProgressionController>();
            Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, progression.Snapshot.SessionId), Is.True);
            Box("Floor", new Vector3(0, -0.25f, 2), new Vector3(16, 0.5f, 20));
            var player = Make("Player", new Vector3(0, 0.05f, 0));
            player.SetActive(false);
            movement = player.AddComponent(RuntimeType("PlayerMovement"));
            Set(movement, "inputActions", input);
            character = player.GetComponent<CharacterController>();
            character.center = Vector3.up; character.height = 2; character.radius = 0.3f;
            character.skinWidth = 0.02f; character.minMoveDistance = 0;
            interactor = player.AddComponent(RuntimeType("PlayerInteractor"));
            Set(interactor, "progression", progression);
            player.SetActive(true);
            Call(movement, "OnApplicationFocus", true);
            Keys();
            Assert.That(InputState.currentUpdateType, Is.EqualTo(InputUpdateType.Manual),
                "Synthetic input must reach a player update in an unfocused batch editor.");
            Physics.SyncTransforms();
            Advance(0.2f);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            if (input != null) Object.DestroyImmediate(input);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = previousMode;
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
            Time.timeScale = previousTimeScale;
        }

        [Test]
        public void InteractionRejectsOutsideRangeBehindActorAndBehindWall()
        {
            var target = Target(new Vector3(0, 1, 3));
            Assert.That(Interact(), Is.False);
            target.transform.position = origin + new Vector3(0, 1, -1);
            Assert.That(Interact(), Is.False);
            target.transform.position = origin + new Vector3(0, 1, 2);
            Box("Wall", new Vector3(0, 1, 1), new Vector3(4, 3, 0.2f));
            Assert.That(Interact(), Is.False);
            Assert.That(Get<Component>(interactor, "SelectedTarget"), Is.Null);
        }

        [Test]
        public void InteractionRevalidatesMovedWallAndTargetAtPressTime()
        {
            var target = Target(new Vector3(0, 1, 2));
            Call(interactor, "RefreshTarget");
            Assert.That(Get<bool>(interactor, "CanInteract"), Is.True);
            var wall = Box("Moving wall", new Vector3(5, 1, 1), new Vector3(1, 3, 0.2f));
            wall.transform.position = origin + new Vector3(0, 1, 1);
            Assert.That(Interact(), Is.False, "Use must synchronize and recheck a newly moved wall.");
            wall.transform.position = origin + new Vector3(5, 1, 1);
            target.transform.position = origin + new Vector3(0, 1, 5);
            Assert.That(Interact(), Is.False, "A cached selection must not work after moving out of range.");
        }

        [Test]
        public void InteractionRejectsRayOriginInsideSolidWallButIgnoresOwnBody()
        {
            Target(new Vector3(0, 1, 2));
            Call(interactor, "RefreshTarget");
            Assert.That(Get<bool>(interactor, "CanInteract"), Is.True, "CharacterController must not occlude its own actor.");
            Box("Embedded wall", new Vector3(0, 1, 0), Vector3.one);
            Assert.That(Interact(), Is.False, "A ray starting in a wall alone would miss this obstruction.");
        }

        [Test]
        public void InteractionSelectsOneCompoundTargetAndExposesUnavailablePrompt()
        {
            var far = Target(new Vector3(0.6f, 1, 2));
            var near = Target(new Vector3(-0.4f, 1, 1.5f));
            var extra = Make("Second collider", Vector3.zero);
            extra.transform.SetParent(near.transform, false);
            extra.transform.localPosition = Vector3.right * 0.1f;
            extra.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;
            Set(near, "available", false); Set(near, "prompt", "Locked rune");
            Call(interactor, "RefreshTarget");
            Assert.That(Get<Component>(interactor, "SelectedTarget"), Is.EqualTo(near));
            Assert.That(Get<string>(interactor, "Prompt"), Is.EqualTo("Locked rune"));
            Assert.That(Get<bool>(interactor, "CanInteract"), Is.False);
            Assert.That(Interact(), Is.False, "An unavailable selected mechanism must not silently activate a different one.");
            Assert.That(Get<bool>(far, "IsConsumed"), Is.False);
            Set(near, "available", true);
            int activations = 0;
            AddActivation(near, _ => activations++);
            Assert.That(Interact(), Is.True);
            Assert.That(activations, Is.EqualTo(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InteractionEqualTargetsUseAuthoredOrderIndependentlyOfColliderEnumeration(bool authoredOrder)
        {
            var first = Target(new Vector3(0, 1, 1.5f));
            var second = Target(new Vector3(0, 1, 1.5f));
            first.GetComponent<Collider>().isTrigger = second.GetComponent<Collider>().isTrigger = true;
            Set(first, "selectionOrder", authoredOrder ? 10 : 0); Set(second, "selectionOrder", authoredOrder ? -10 : 0);
            var expected = authoredOrder || second.GetEntityId().CompareTo(first.GetEntityId()) < 0 ? second : first;
            for (int i = 0; i < 3; i++)
            {
                first.gameObject.SetActive(false); first.gameObject.SetActive(true);
                Call(interactor, "RefreshTarget");
                Assert.That(Get<Component>(interactor, "SelectedTarget"), Is.EqualTo(expected));
            }
        }

        [Test]
        public void InteractionHeldRepeatedAndReenabledInputPublishesOneCapturedSession()
        {
            var target = Target(new Vector3(0, 1, 1.5f));
            int calls = 0; Guid observed = Guid.Empty;
            AddActivation(target, session => { calls++; observed = session; });
            for (int i = 0; i < 30; i++) { Keys(Key.E); Advance(1f / 60); }
            Keys(); Keys(Key.E); Advance(1f / 60);
            ((Behaviour)interactor).enabled = false; ((Behaviour)interactor).enabled = true;
            target.gameObject.SetActive(false); target.gameObject.SetActive(true);
            Keys(); Keys(Key.E); Advance(1f / 60);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(observed, Is.EqualTo(progression.Snapshot.SessionId));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InteractionResetRejectsOldTokenAndAllowsExactlyOneNewUse(bool disableTarget)
        {
            var target = Target(new Vector3(0, 1, 1.5f));
            var old = progression.Snapshot.SessionId;
            Assert.That(Interact(), Is.True);
            if (disableTarget) target.gameObject.SetActive(false);
            Assert.That(progression.TryResetSession(), Is.True);
            if (disableTarget) target.gameObject.SetActive(true);
            Assert.That((bool)Call(target, "TryActivate", old), Is.False);
            Assert.That(Interact(), Is.True);
            Assert.That(Interact(), Is.False);
        }

        [Test]
        public void InteractionDestroyedDisabledOrMissingTargetClearsSelection()
        {
            Assert.That(Interact(), Is.False);
            var target = Target(new Vector3(0, 1, 1.5f));
            Call(interactor, "RefreshTarget");
            Assert.That(Get<Component>(interactor, "SelectedTarget"), Is.EqualTo(target));
            ((Behaviour)target).enabled = false;
            Assert.That(Interact(), Is.False);
            Assert.That(Get<string>(interactor, "Prompt"), Is.Empty);
            ((Behaviour)target).enabled = true;
            Object.DestroyImmediate(target.gameObject);
            Assert.That(Interact(), Is.False);
            Assert.That(Get<Component>(interactor, "SelectedTarget"), Is.Null);
            Assert.That(Get<bool>(interactor, "CanInteract"), Is.False);
        }

        [Test]
        public void InteractionDisabledPlayerCannotUseAVisibleTarget()
        {
            Target(new Vector3(0, 1, 1.5f));
            Call(movement, "SetControlsEnabled", false);
            Assert.That(Interact(), Is.False);
            Assert.That(Get<Component>(interactor, "SelectedTarget"), Is.Null);
        }

        [Test]
        public void InteractionProgressionObjectiveMustBeAvailableAndCompletesOnce()
        {
            var target = Target(new Vector3(0, 1, 1.5f), LevelObjective.MainPuzzleSolved);
            Assert.That(Interact(), Is.False);
            var session = progression.Snapshot.SessionId;
            Assert.That(progression.TryComplete(LevelObjective.FirstEnemyDefeated, session), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.Puzzle, session), Is.True);
            Assert.That(Interact(), Is.True);
            Assert.That(progression.Snapshot.CompletedObjectives & LevelObjective.MainPuzzleSolved, Is.EqualTo(LevelObjective.MainPuzzleSolved));
            Assert.That(Interact(), Is.False);
        }

        [Test]
        public void InteractionGateClosedBlocksAndOpenAllowsRealCharacterMovement()
        {
            var gate = Gate();
            var renderer = gate.GetComponent<Renderer>(); var barrier = gate.GetComponent<BoxCollider>();
            Assert.That(Get<bool>(gate, "IsOpen"), Is.False);
            Assert.That(renderer.enabled && barrier.enabled, Is.True);
            Keys(Key.W); Advance(1);
            Assert.That(character.transform.position.z - origin.z, Is.InRange(2.3f, 2.6f));
            var target = Target(new Vector3(1, 1, 2.8f), LevelObjective.FirstEnemyDefeated);
            // The player faces the gate; use the trusted target command to isolate
            // gate physics here. Input routing is exercised in the interaction tests.
            Assert.That((bool)Call(target, "TryActivate", progression.Snapshot.SessionId), Is.True);
            Assert.That(Get<bool>(gate, "IsOpen"), Is.True);
            Assert.That(renderer.enabled || barrier.enabled, Is.False);
            Advance(1);
            Assert.That(character.transform.position.z - origin.z, Is.GreaterThan(5));
        }

        [UnityTest]
        public IEnumerator InteractionGateResetWaitsForTeleportedOccupantAndClosesAfterEscape()
        {
            var gate = Gate();
            var renderer = gate.GetComponent<Renderer>(); var barrier = gate.GetComponent<BoxCollider>();
            Assert.That(progression.TryComplete(LevelObjective.FirstEnemyDefeated, progression.Snapshot.SessionId), Is.True);
            character.transform.position = origin + new Vector3(0, 0, 3);
            // Deliberately do not synchronize: reset may follow teleport in one frame.
            Assert.That(progression.TryResetSession(), Is.True);
            Assert.That(Get<bool>(gate, "IsOpen") && Get<bool>(gate, "ClosePending"), Is.True);
            Assert.That(renderer.enabled || barrier.enabled, Is.False);
            Keys(Key.W); Advance(0.5f);
            Assert.That(character.transform.position.z - origin.z, Is.GreaterThan(5));
            float doorwayEnd = barrier.transform.TransformPoint(barrier.center).z +
                barrier.size.z * Mathf.Abs(barrier.transform.lossyScale.z) * 0.5f + 0.05f;
            Assert.That(character.transform.position.z - character.radius, Is.GreaterThan(doorwayEnd),
                "The complete player capsule must leave the safety volume, not just its pivot.");
            Assert.That(progression.IsPassageOpen(LevelRoom.FirstEncounter, LevelRoom.Puzzle), Is.False);
            // Advance() above exercises real Move calls, but all run in one frame.
            // Let the physics world consume its controller movement before requiring
            // an empty-overlap result. The immediate reset safety assertion stays above.
            for (int tick = 0; tick < 3 && Get<bool>(gate, "IsOpen"); tick++)
            {
                yield return new WaitForFixedUpdate();
                Call(gate, "RefreshGate");
            }
            Assert.That(character.bounds.min.z, Is.GreaterThan(doorwayEnd), "The physical capsule did not escape.");
            Assert.That(Get<bool>(gate, "IsOpen") || Get<bool>(gate, "ClosePending"), Is.False,
                $"Gate did not close after three physics ticks; player={character.transform.position}, " +
                $"capsule={character.bounds}, doorway end={doorwayEnd}.");
            Assert.That(renderer.enabled && barrier.enabled, Is.True);
        }

        [Test]
        public void InteractionGateApproachCannotOpenLockedPassageAndReenablePreservesState()
        {
            var gate = Gate();
            for (int i = 0; i < 3; i++) { ((Behaviour)gate).enabled = false; ((Behaviour)gate).enabled = true; }
            Keys(Key.W); Advance(1);
            Call(gate, "RefreshGate");
            Assert.That(Get<bool>(gate, "IsOpen"), Is.False);
            Assert.That(gate.GetComponent<BoxCollider>().enabled, Is.True);
        }

        private Component Gate()
        {
            var owner = Box("Gate", new Vector3(0, 1.5f, 3), new Vector3(3, 3, 0.4f));
            owner.SetActive(false);
            var gate = owner.AddComponent(RuntimeType("ProgressionGate"));
            Set(gate, "progression", progression);
            Set(gate, "closedVisuals", new Renderer[] { owner.GetComponent<Renderer>() });
            owner.SetActive(true);
            return gate;
        }

        private Component Target(Vector3 position, LevelObjective objective = LevelObjective.None)
        {
            var owner = Box("Mechanism", position, Vector3.one * 0.3f);
            owner.SetActive(false);
            var target = owner.AddComponent(RuntimeType("InteractionTarget"));
            Set(target, "progression", progression); Set(target, "objective", objective);
            owner.SetActive(true);
            return target;
        }

        private GameObject Make(string name, Vector3 position)
        {
            var go = new GameObject(name); objects.Add(go); go.transform.position = origin + position; return go;
        }
        private GameObject Box(string name, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(go);
            go.name = name; go.transform.position = origin + position; go.transform.localScale = size; return go;
        }
        private bool Interact() => (bool)Call(interactor, "TryInteract");
        private void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update(); }
        private void Advance(float seconds)
        {
            for (int i = 0; i < Mathf.RoundToInt(seconds * 60); i++) Call(movement, "Simulate", 1f / 60);
        }
        private static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static void Set(Component component, string field, object value) => component.GetType().GetField(field).SetValue(component, value);
        private static T Get<T>(Component component, string property) => (T)component.GetType().GetProperty(property).GetValue(component);
        private static void AddActivation(Component component, Action<Guid> handler) => component.GetType().GetEvent("Activated").AddEventHandler(component, handler);
        private static object Call(Component component, string method, params object[] arguments)
        {
            try { return component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(component, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
    }
}
