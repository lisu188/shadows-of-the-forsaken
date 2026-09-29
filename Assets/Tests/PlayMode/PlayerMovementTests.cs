using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public class PlayerMovementTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private InputActionAsset asset;
        private Keyboard keyboard;
        private Mouse mouse;
        private Component movement;
        private CharacterController character;
        private Type movementType;
        private InputSettings.UpdateMode previousUpdateMode;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousUpdateMode = InputSystem.settings.updateMode;
            previousTimeScale = Time.timeScale;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            Time.timeScale = 1;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.devices = new InputDevice[] { keyboard, mouse };
            var map = asset.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2")
                .AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            map.AddAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            map.AddAction("Interact", InputActionType.Button, "<Keyboard>/e", interactions: "Hold");
            movementType = Type.GetType("PlayerMovement, Assembly-CSharp", true);
            Box("Ground", new Vector3(0, -0.25f, 0), new Vector3(40, 0.5f, 40));
            var player = new GameObject("Movement test player");
            objects.Add(player);
            player.SetActive(false);
            player.transform.position = new Vector3(0, 1.01f, 0);
            movement = player.AddComponent(movementType);
            character = player.GetComponent<CharacterController>();
            Assert.That(character, Is.Not.Null, "RequireComponent must install CharacterController");
            character.height = 2;
            character.radius = 0.3f;
            character.center = Vector3.zero;
            character.skinWidth = 0.02f;
            character.minMoveDistance = 0;
            character.stepOffset = 0.3f;
            movementType.GetField("inputActions").SetValue(movement, asset);
            player.SetActive(true);
            Call("OnApplicationFocus", true);
            Physics.SyncTransforms();
            Keys();
            Advance(0.2f);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            if (asset != null) Object.DestroyImmediate(asset);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.updateMode = previousUpdateMode;
            Time.timeScale = previousTimeScale;
        }

        private void Call(string method, params object[] args)
        {
            try { movementType.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(movement, args); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        private void Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private void Advance(float seconds, int fps = 60)
        {
            int frames = Mathf.RoundToInt(seconds * fps);
            for (int i = 0; i < frames; i++) Call("Simulate", 1f / fps);
        }

        private GameObject Box(string name, Vector3 position, Vector3 size)
        {
            var box = new GameObject(name);
            objects.Add(box);
            box.transform.position = position;
            box.AddComponent<BoxCollider>().size = size;
            Physics.SyncTransforms();
            return box;
        }

        private float Velocity => (float)movementType.GetProperty("VerticalVelocity").GetValue(movement);

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void GroundMovementAtRepresentativeFrameRates(int fps)
        {
            Assert.That(character.isGrounded, Is.True);
            float start = movement.transform.position.z;
            Keys(Key.W);
            Advance(1, fps);
            Assert.That(movement.transform.position.z - start, Is.EqualTo(5).Within(0.02));
            Assert.That(character.isGrounded, Is.True);
        }

        [Test]
        public void AAndDOnlyRotateAndDoNotStrafe()
        {
            var start = movement.transform.position;
            Keys(Key.D);
            Advance(1);
            Assert.That(movement.transform.eulerAngles.y, Is.EqualTo(20).Within(0.02));
            Assert.That(movement.transform.position.x, Is.EqualTo(start.x).Within(0.001));
            Assert.That(movement.transform.position.z, Is.EqualTo(start.z).Within(0.001));
        }

        [Test]
        public void WallStopsForwardMovement()
        {
            Box("Wall", new Vector3(0, 2, 2), new Vector3(10, 4, 0.25f));
            Keys(Key.W);
            Advance(1);
            Assert.That(movement.transform.position.z, Is.InRange(1.4f, 1.7f));
        }

        [Test]
        public void LowCeilingCancelsJumpAndPlayerLands()
        {
            Box("Ceiling", new Vector3(0, 2.4f, 0), new Vector3(10, 0.2f, 10));
            Keys(Key.Space);
            Advance(0.2f);
            Assert.That(Velocity, Is.LessThanOrEqualTo(0));
            Assert.That(movement.transform.position.y, Is.LessThan(1.35f));
            Keys();
            Advance(1);
            Assert.That(character.isGrounded, Is.True);
        }

        [Test]
        public void HeldJumpDoesNotJumpAgainAfterLanding()
        {
            Keys(Key.Space);
            Advance(2);
            Assert.That(character.isGrounded, Is.True);
            Assert.That(movement.transform.position.y, Is.EqualTo(1).Within(0.06));
        }

        [Test]
        public void CharacterCanClimbConfiguredSteps()
        {
            Box("First step", new Vector3(0, 0.1f, 1), new Vector3(2, 0.2f, 0.6f));
            Box("Second step", new Vector3(0, 0.2f, 1.6f), new Vector3(2, 0.4f, 0.6f));
            Box("Landing", new Vector3(0, 0.3f, 3), new Vector3(2, 0.6f, 2.2f));
            Keys(Key.W);
            Advance(0.6f);
            Assert.That(movement.transform.position.z, Is.GreaterThan(2.5f));
            Assert.That(movement.transform.position.y, Is.GreaterThan(1.5f));
        }

        [Test]
        public void NarrowPassageRemainsTraversable()
        {
            Box("Left wall", new Vector3(-0.6f, 2, 2), new Vector3(0.2f, 4, 6));
            Box("Right wall", new Vector3(0.6f, 2, 2), new Vector3(0.2f, 4, 6));
            Keys(Key.W);
            Advance(1);
            Assert.That(movement.transform.position.z, Is.EqualTo(5).Within(0.03));
        }

        [Test]
        public void FocusLossDropsMovementAndRequiresReleasedControls()
        {
            Keys(Key.W);
            Advance(0.2f);
            Call("OnApplicationFocus", false);
            var start = movement.transform.position;
            Advance(0.2f);
            Assert.That(movement.transform.position, Is.EqualTo(start));
            Call("OnApplicationFocus", true);
            InputSystem.Update();
            Advance(0.2f);
            Assert.That(movement.transform.position.z, Is.EqualTo(start.z));
            Keys();
            Keys(Key.W);
            Advance(0.2f);
            Assert.That(movement.transform.position.z, Is.GreaterThan(start.z + 0.9f));
        }

        [Test]
        public void DisabledControlsFlushQueuedJumpAndCannotFireActions()
        {
            int attacks = 0;
            movementType.GetEvent("AttackRequested").AddEventHandler(movement, (Action)(() => attacks++));
            Keys(Key.Space);
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            InputSystem.Update();
            Call("SetControlsEnabled", false);
            var start = movement.transform.position;
            Advance(0.2f);
            Assert.That(movement.transform.position, Is.EqualTo(start));
            Assert.That(attacks, Is.Zero);
            Call("SetControlsEnabled", true);
            InputSystem.Update();
            Advance(0.2f);
            Assert.That(attacks, Is.Zero);
            Assert.That(Velocity, Is.LessThanOrEqualTo(0));
        }

        [Test]
        public void InteractFiresOnPressDespiteTemplateHoldInteraction()
        {
            int interactions = 0;
            movementType.GetEvent("InteractRequested").AddEventHandler(movement, (Action)(() => interactions++));
            Keys(Key.E);
            Advance(0.1f);
            Assert.That(interactions, Is.EqualTo(1));
            InputSystem.Update();
            Advance(0.1f);
            Assert.That(interactions, Is.EqualTo(1));
            Keys();
            Keys(Key.E);
            Advance(0.1f);
            Assert.That(interactions, Is.EqualTo(2));
        }

        [Test]
        public void PressAndReleaseWithinOneInputUpdateIsNotLost()
        {
            int interactions = 0;
            movementType.GetEvent("InteractRequested").AddEventHandler(movement, (Action)(() => interactions++));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            Advance(0.1f);
            Assert.That(interactions, Is.EqualTo(1));
        }

        [Test]
        public void DisablingComponentDoesNotDisableOrRewriteSourceActions()
        {
            var source = asset.FindActionMap("Player");
            string original = source.ToJson();
            source.Enable();
            ((Behaviour)movement).enabled = false;
            Assert.That(source.enabled, Is.True);
            Assert.That(source.ToJson(), Is.EqualTo(original));
            ((Behaviour)movement).enabled = true;
            Call("OnApplicationFocus", true);
            Keys();
            Keys(Key.W);
            float start = movement.transform.position.z;
            Advance(0.2f);
            Assert.That(movement.transform.position.z - start, Is.EqualTo(1).Within(0.02));
        }

        [Test]
        public void MissingRequiredActionDisablesComponentWithOneError()
        {
            ((Behaviour)movement).enabled = false;
            asset.FindAction("Player/Jump").RemoveAction();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("PlayerMovement input configuration:"));
            ((Behaviour)movement).enabled = true;
            Assert.That(((Behaviour)movement).enabled, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void PausingClearsQueuedInputUntilControlsReturnToNeutral()
        {
            Keys(Key.Space);
            Call("OnApplicationPause", true);
            var start = movement.transform.position;
            Advance(0.1f);
            Assert.That(movement.transform.position, Is.EqualTo(start));
            Call("OnApplicationPause", false);
            InputSystem.Update();
            Advance(0.1f);
            Assert.That(Velocity, Is.LessThanOrEqualTo(0));
        }

        [Test]
        public void DisabledCharacterControllerIsNotMoved()
        {
            Keys(Key.W);
            character.enabled = false;
            var start = movement.transform.position;
            Advance(0.1f);
            Assert.That(movement.transform.position, Is.EqualTo(start));
            character.enabled = true;
            Keys();
            Keys(Key.W);
            Advance(0.1f);
            Assert.That(movement.transform.position.z, Is.GreaterThan(start.z));
        }
    }
}
