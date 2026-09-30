using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine.Rendering;
#endif

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // These tests need the real Unity physics/Input System. Headless C# is not a substitute.
    public sealed class CastleLayoutTraversalTests
    {
        private const string ScenePath = "Assets/Scenes/ForsakenCastle.unity";
        private const float Frame = 1f / 60f;
        private readonly List<Behaviour> suspendedBehaviours = new List<Behaviour>();
        private readonly List<Collider> suspendedColliders = new List<Collider>();
        private Scene scene;
        private Component movement;
        private Component follow;
        private CharacterController character;
        private InputActionAsset input;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputSettings.UpdateMode previousUpdateMode;
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
        private float previousTimeScale;
        private bool settingsCaptured;

        [UnitySetUp]
        public IEnumerator LoadLayout()
        {
            previousUpdateMode = InputSystem.settings.updateMode;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousTimeScale = Time.timeScale;
            settingsCaptured = true;
            // Batch mode has no focused Game View; synthetic input must still use
            // player updates. This does not change PlayerMovement's focus handling.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            Time.timeScale = 1;

            // The test runner may already have SampleScene or the level open. Do not
            // let their player, camera, audio or collision geometry affect this copy.
            foreach (var behaviour in Object.FindObjectsByType<Behaviour>())
                if (behaviour.isActiveAndEnabled && (behaviour is Camera || behaviour is AudioListener ||
                    behaviour.GetType().Name == "PlayerMovement" || behaviour.GetType().Name == "CameraFollow"))
                {
                    suspendedBehaviours.Add(behaviour);
                    behaviour.enabled = false;
                }
            foreach (var collider in Object.FindObjectsByType<Collider>())
                if (collider.enabled)
                {
                    suspendedColliders.Add(collider);
                    collider.enabled = false;
                }

            var previousScenes = new HashSet<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++) previousScenes.Add(SceneManager.GetSceneAt(i));
            var operation = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            Assert.That(operation, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 30;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(operation.isDone, Is.True, "Castle scene load timed out.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var candidate = SceneManager.GetSceneAt(i);
                if (candidate.path == ScenePath && !previousScenes.Contains(candidate)) scene = candidate;
            }
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
            var player = Root("Player");
            character = player.GetComponent<CharacterController>();
            Assert.That(character, Is.Not.Null);
            movement = player.GetComponent(Type.GetType("PlayerMovement, Assembly-CSharp", true));
            follow = Root("Main Camera").GetComponent(Type.GetType("CameraFollow, Assembly-CSharp", true));
            Assert.That(movement, Is.Not.Null);
            Assert.That(follow, Is.Not.Null);

            // Clone the scene's actual bindings and restrict only this test instance
            // to synthetic devices. Never edit the imported action asset.
            var source = (InputActionAsset)movement.GetType().GetField("inputActions").GetValue(movement);
            Assert.That(source, Is.Not.Null, "The scene must explicitly assign its input asset.");
            ((Behaviour)movement).enabled = false;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            input = Object.Instantiate(source);
            input.devices = new InputDevice[] { keyboard, mouse };
            movement.GetType().GetField("inputActions").SetValue(movement, input);
            ((Behaviour)movement).enabled = true;
            Call(movement, "OnApplicationFocus", true);
            Keys();
            Assert.That(InputState.currentUpdateType, Is.EqualTo(InputUpdateType.Manual),
                "Synthetic input must reach a player update in an unfocused batch editor.");
            Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator UnloadLayout()
        {
            bool unloaded = true;
            if (scene.IsValid() && scene.isLoaded)
            {
                var operation = SceneManager.UnloadSceneAsync(scene);
                if (operation != null)
                {
                    float deadline = Time.realtimeSinceStartup + 30;
                    while (!operation.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                    unloaded = operation.isDone;
                }
            }
            if (input != null) Object.DestroyImmediate(input);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (settingsCaptured)
            {
                InputSystem.settings.updateMode = previousUpdateMode;
                InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
                Time.timeScale = previousTimeScale;
            }
            foreach (var collider in suspendedColliders) if (collider != null) collider.enabled = true;
            foreach (var behaviour in suspendedBehaviours) if (behaviour != null) behaviour.enabled = true;
            suspendedColliders.Clear();
            suspendedBehaviours.Clear();
            Assert.That(unloaded, Is.True, "Castle scene unload timed out.");
        }

        [TestCase("Courtyard", "FirstEncounter", false)]
        [TestCase("Courtyard", "FirstEncounter", true)]
        [TestCase("FirstEncounter", "Puzzle", false)]
        [TestCase("FirstEncounter", "Puzzle", true)]
        [TestCase("FirstEncounter", "ThroneRoom", false)]
        [TestCase("FirstEncounter", "ThroneRoom", true)]
        [TestCase("ThroneRoom", "Library", false)]
        [TestCase("ThroneRoom", "Library", true)]
        [TestCase("Library", "Catacombs", false)]
        [TestCase("Library", "Catacombs", true)]
        [TestCase("Catacombs", "FinalArena", false)]
        [TestCase("Catacombs", "FinalArena", true)]
        [TestCase("Catacombs", "BonusRoom", false)]
        [TestCase("Catacombs", "BonusRoom", true)]
        [TestCase("ThroneRoom", "BonusRoom", false)]
        [TestCase("ThroneRoom", "BonusRoom", true)]
        [TestCase("FinalArena", "Exit", false)]
        [TestCase("FinalArena", "Exit", true)]
        public void EveryPassageIsWalkableInBothDirections(string from, string to, bool reverse)
        {
            string passageName = $"Passage_{from}_{to}";
            var passage = Root("Passages").transform.Find(passageName);
            Assert.That(passage, Is.Not.Null, passageName);
            var waypoints = passage.Cast<Transform>().Where(point => point.name.StartsWith("Waypoint_", StringComparison.Ordinal))
                .OrderBy(point => point.name, StringComparer.Ordinal).Select(point => point.position).ToArray();
            Assert.That(waypoints.Length, Is.GreaterThanOrEqualTo(2), passageName);
            if (reverse) Array.Reverse(waypoints);

            // Positioning is fixture setup only. Every following segment uses W/A/D
            // through PlayerMovement and the real CharacterController, with no warps.
            var direction = waypoints[1] - waypoints[0];
            direction.y = 0;
            Assert.That(direction.sqrMagnitude, Is.GreaterThan(0.01f));
            PlacePlayer(waypoints[0], direction, passageName);
            AssertSupported(waypoints[0], passageName + " start");
            if (!reverse && from == "Courtyard" && to == "FirstEncounter") Capture("castle-courtyard");

            for (int i = 1; i < waypoints.Length; i++)
            {
                WalkTo(waypoints[i], passageName + " waypoint " + i);
                Settle();
                AssertSupported(waypoints[i], passageName + " waypoint " + i);
                Assert.That((bool)follow.GetType().GetProperty("HasSafePose").GetValue(follow), Is.True,
                    passageName + " camera after waypoint " + i);
            }
            Assert.That(Root("Main Camera").GetComponent<Camera>().enabled, Is.True);
            if (!reverse && from == "FirstEncounter" && to == "ThroneRoom") Capture("castle-throne");
            if (!reverse && from == "ThroneRoom" && to == "Library") Capture("castle-library");
            if (!reverse && from == "Library" && to == "Catacombs") Capture("castle-catacombs");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void UnityJumpAtCourtyardLandsInsideBounds()
        {
            var center = Root("Rooms").transform.Find("Room_Courtyard").position;
            PlacePlayer(center + Vector3.up * 0.05f, Vector3.forward, "Courtyard jump");
            float initialHeight = movement.transform.position.y;
            float highest = initialHeight;
            for (int frame = 0; frame < 30; frame++)
            {
                Step(Frame, Key.W, Key.Space);
                highest = Mathf.Max(highest, movement.transform.position.y);
                Assert.That(Mathf.Abs(movement.transform.position.x - center.x), Is.LessThan(3.7f));
                Assert.That(Mathf.Abs(movement.transform.position.z - center.z), Is.LessThan(3.7f));
                Assert.That((bool)follow.GetType().GetProperty("HasSafePose").GetValue(follow), Is.True,
                    "Camera lost its safe pose during the courtyard jump.");
            }
            for (int frame = 0; frame < 90; frame++) Step(Frame);
            Assert.That(highest - initialHeight, Is.GreaterThan(0.7f), "The test must perform an actual jump.");
            Assert.That(movement.transform.position.z - center.z, Is.InRange(2.3f, 2.7f), "Forward input did not move the player.");
            Assert.That(character.isGrounded, Is.True, "The player did not land on the courtyard floor.");
            Assert.That(movement.transform.position.y, Is.EqualTo(initialHeight).Within(0.1f));
            Assert.That(Mathf.Abs(movement.transform.position.x - center.x), Is.LessThan(3.7f));
            Assert.That(Mathf.Abs(movement.transform.position.z - center.z), Is.LessThan(3.7f));
            Assert.That((bool)follow.GetType().GetProperty("HasSafePose").GetValue(follow), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void PuzzleNorthWallRejectsWalkingAndJumpingThroughTheMapBoundary()
        {
            var center = Root("Rooms").transform.Find("Room_Puzzle").position;
            PlacePlayer(center + Vector3.up * 0.05f, Vector3.forward, "Puzzle north boundary");
            for (int frame = 0; frame < 120; frame++) Step(Frame, Key.W);
            Assert.That(movement.transform.position.z - center.z, Is.GreaterThan(2.8f),
                "The player must reach the actual wall before checking that it blocks movement.");
            Assert.That(movement.transform.position.z, Is.LessThan(center.z + 3.9f), "Walking escaped through the puzzle wall.");
            float highest = movement.transform.position.y;
            for (int frame = 0; frame < 30; frame++)
            {
                Step(Frame, Key.W, Key.Space);
                highest = Mathf.Max(highest, movement.transform.position.y);
                Assert.That(movement.transform.position.z, Is.LessThan(center.z + 3.9f), "Jump bypassed the puzzle wall.");
            }
            for (int frame = 0; frame < 90; frame++)
            {
                Step(Frame, Key.W);
                Assert.That(movement.transform.position.z, Is.LessThan(center.z + 3.9f), "Player crossed the wall while landing.");
            }
            Assert.That(highest - center.y, Is.GreaterThan(0.7f), "The bypass attempt must include an actual jump.");
            Assert.That(Mathf.Abs(movement.transform.position.x - center.x), Is.LessThan(0.2f));
            Assert.That(character.isGrounded, Is.True);
            Assert.That(movement.transform.position.y, Is.EqualTo(center.y).Within(0.1f));
            Assert.That((bool)follow.GetType().GetProperty("HasSafePose").GetValue(follow), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        private void PlacePlayer(Vector3 feet, Vector3 forward, string context)
        {
            character.enabled = false;
            movement.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(forward));
            character.enabled = true;
            Call(movement, "ResetMotion");
            Keys();
            Physics.SyncTransforms();
            Assert.That((bool)Call(follow, "SnapToTarget"), Is.True, context + " initial camera");
            Settle();
            Assert.That(character.isGrounded, Is.True, context + " starting floor");
        }

        private void WalkTo(Vector3 destination, string context)
        {
            float distance = HorizontalDistance(movement.transform.position, destination);
            float lowestExpectedFloor = Mathf.Min(movement.transform.position.y, destination.y);
            int limit = Mathf.CeilToInt(120 + distance * 60);
            float speed = (float)movement.GetType().GetField("speed").GetValue(movement);
            float turnSpeed = (float)movement.GetType().GetField("rotationSpeed").GetValue(movement);
            Assert.That(speed, Is.GreaterThan(0));
            Assert.That(turnSpeed, Is.GreaterThan(0));
            for (int step = 0; step < limit; step++)
            {
                var offset = destination - movement.transform.position;
                offset.y = 0;
                if (offset.magnitude <= 0.12f) return;
                float angle = Vector3.SignedAngle(movement.transform.forward, offset, Vector3.up);
                if (Mathf.Abs(angle) > 0.5f)
                    Step(Mathf.Min(Frame, Mathf.Abs(angle) / turnSpeed), angle > 0 ? Key.D : Key.A);
                else
                    Step(Mathf.Min(Frame, offset.magnitude / speed), Key.W);
                Assert.That(movement.transform.position.y, Is.GreaterThan(lowestExpectedFloor - 3f),
                    context + ": player fell below the route.");
            }
            Assert.Fail(context + $": blocked after {limit} movement steps at {movement.transform.position}; target {destination}.");
        }

        private void AssertSupported(Vector3 waypoint, string context)
        {
            Assert.That(character.isGrounded, Is.True, context + ": no supporting floor.");
            Assert.That(HorizontalDistance(movement.transform.position, waypoint), Is.LessThan(0.2f), context);
            Assert.That(movement.transform.position.y, Is.EqualTo(waypoint.y - 0.05f).Within(0.3f),
                context + ": reached the wrong floor/elevation.");
        }

        private void Settle()
        {
            for (int i = 0; i < 30; i++) Step(Frame);
        }

        private void Step(float seconds, params Key[] keys)
        {
            Keys(keys);
            Call(movement, "Simulate", seconds);
            Physics.SyncTransforms();
            Call(follow, "Simulate", seconds);
        }

        private void Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private void Capture(string name)
        {
#if UNITY_EDITOR
            string directory = Environment.GetEnvironmentVariable("SHADOWS_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) return;
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "Optional castle captures require graphics; omit SHADOWS_CAPTURE_DIR for -nographics runs.");
            var camera = Root("Main Camera").GetComponent<Camera>();
            Assert.That((bool)Call(follow, "SnapToTarget"), Is.True, "Capture requires the saved camera's safe follow pose.");
            Assert.That(camera.isActiveAndEnabled, Is.True);
            var previousScene = SceneManager.GetActiveScene();
            var originalTarget = camera.targetTexture;
            var originalActive = RenderTexture.active;
            bool originalAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            var otherRenderers = new List<Renderer>();
            var otherLights = new List<Light>();
            RenderTexture texture = null;
            Texture2D pixels = null;
            try
            {
                // Render this saved scene's lighting and geometry only. These
                // temporary presentation changes never alter traversal physics.
                Assert.That(SceneManager.SetActiveScene(scene), Is.True);
                foreach (var renderer in Object.FindObjectsByType<Renderer>())
                    if (renderer.enabled && renderer.gameObject.scene != scene)
                    {
                        otherRenderers.Add(renderer); renderer.enabled = false;
                    }
                foreach (var light in Object.FindObjectsByType<Light>())
                    if (light.enabled && light.gameObject.scene != scene)
                    {
                        otherLights.Add(light); light.enabled = false;
                    }
                // Avoid first-use asynchronous shader placeholders in evidence.
                ShaderUtil.allowAsyncCompilation = false;
                texture = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
                texture.Create();
                var request = new RenderPipeline.StandardRequest { destination = texture };
                Assert.That(RenderPipeline.SupportsRenderRequest(camera, request), Is.True,
                    "The saved scene pipeline must support a camera render request.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = texture;
                pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = originalAsyncCompilation;
                camera.targetTexture = originalTarget;
                RenderTexture.active = originalActive;
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                foreach (var renderer in otherRenderers) if (renderer != null) renderer.enabled = true;
                foreach (var light in otherLights) if (light != null) light.enabled = true;
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            }
#endif
        }

        private GameObject Root(string name) => scene.GetRootGameObjects().Single(root => root.name == name);
        private static float HorizontalDistance(Vector3 first, Vector3 second) =>
            new Vector2(first.x - second.x, first.z - second.z).magnitude;

        private static object Call(Component component, string method, params object[] arguments)
        {
            try
            {
                return component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Invoke(component, arguments);
            }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
    }
}
