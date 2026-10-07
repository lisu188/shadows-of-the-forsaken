#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Progression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // Saved-scene integration using synthetic input and real physics. Optional
    // camera captures are rendered evidence, not a manual controls/UI playthrough.
    public sealed class GameplayDemoTests
    {
        private const float Frame = 1f / 60;
        private readonly List<Behaviour> suspendedBehaviours = new List<Behaviour>();
        private readonly List<Collider> suspendedColliders = new List<Collider>();
        private readonly List<Renderer> suspendedRenderers = new List<Renderer>();
        private Scene scene, previousScene;
        private OwnedSceneLoad sceneLoad;
        private Component movement, follow;
        private Camera camera;
        private InputActionAsset input;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputSettings.UpdateMode previousMode;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
        private float previousTimeScale;

        [SetUp]
        public void Prepare()
        {
            sceneLoad = null;
            scene = default;
            previousScene = SceneManager.GetActiveScene();
            previousMode = InputSystem.settings.updateMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousTimeScale = Time.timeScale;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            Time.timeScale = 1;
            // Keep the test runner alive while isolating pre-existing gameplay,
            // rendering, lighting and collisions from the additive demo copy.
            foreach (var item in Object.FindObjectsByType<Behaviour>())
                if (item.isActiveAndEnabled && (item is Camera || item is AudioListener || item is Light ||
                    item.GetType().Name == "Volume" || item.GetType().Assembly.GetName().Name == "Assembly-CSharp" ||
                    item.GetType().Namespace?.StartsWith("ShadowsOfTheForsaken", StringComparison.Ordinal) == true))
                {
                    suspendedBehaviours.Add(item);
                    item.enabled = false;
                }
            foreach (var item in Object.FindObjectsByType<Collider>())
                if (item.enabled) { suspendedColliders.Add(item); item.enabled = false; }
            foreach (var item in Object.FindObjectsByType<Renderer>())
                if (item.enabled) { suspendedRenderers.Add(item); item.enabled = false; }
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            try
            {
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                if (sceneLoad != null) yield return sceneLoad.Cleanup();
            }
            finally
            {
                if (input != null) Object.DestroyImmediate(input);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                InputSystem.settings.updateMode = previousMode;
                InputSystem.settings.backgroundBehavior = previousBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
                Time.timeScale = previousTimeScale;
                foreach (var item in suspendedColliders) if (item != null) item.enabled = true;
                foreach (var item in suspendedRenderers) if (item != null) item.enabled = true;
                foreach (var item in suspendedBehaviours) if (item != null) item.enabled = true;
                suspendedColliders.Clear(); suspendedRenderers.Clear(); suspendedBehaviours.Clear();
            }
            Assert.That(sceneLoad?.CleanupFailure, Is.Null, "Demo scene cleanup failed.");
        }

        [UnityTest]
        public IEnumerator InteractionDemoSavedSceneOpensGateThroughInputAndRestarts()
        {
            yield return Load("Assets/Interactions/Demo/InteractionDemo.unity");
            var demo = Find("InteractionDemoSession");
            var progression = Field<LevelProgressionController>(demo, "progression");
            var gate = Field<Component>(demo, "gate");
            var interactor = Field<Component>(demo, "interactor");
            var spawn = Field<Transform>(demo, "spawn");
            Assert.That(Field<Component>(demo, "player"), Is.EqualTo(movement));
            Assert.That(Field<LevelProgressionController>(interactor, "progression"), Is.SameAs(progression));
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Puzzle));
            Assert.That(Get<bool>(gate, "IsOpen"), Is.False);
            Assert.That(gate.GetComponent<BoxCollider>().enabled, Is.True);
            Guid initialSession = progression.Snapshot.SessionId;
            Capture("interaction-closed");

            Input(false, Key.E);
            Call(movement, "Simulate", Frame);
            Assert.That(Get<bool>(gate, "IsOpen"), Is.True, "The saved lever must open its wired gate through E.");
            Assert.That(gate.GetComponent<BoxCollider>().enabled, Is.False);
            Assert.That(progression.Snapshot.CompletedObjectives & LevelObjective.MainPuzzleSolved, Is.EqualTo(LevelObjective.MainPuzzleSolved));
            Capture("interaction-open");
            Input(false, Key.W);
            Walk(72);
            Assert.That(movement.transform.position.z, Is.GreaterThan(gate.transform.position.z + 1), "The opened gate must permit real CharacterController traversal.");

            Call(demo, "RestartDemo");
            Assert.That(progression.Snapshot.SessionId, Is.Not.EqualTo(initialSession));
            Assert.That(Vector3.Distance(movement.transform.position, spawn.position), Is.LessThan(.01f));
            Assert.That(Get<bool>(gate, "IsOpen"), Is.False);
            Assert.That(gate.GetComponent<BoxCollider>().enabled, Is.True);
            Input(); Input(false, Key.E); Call(movement, "Simulate", Frame);
            Assert.That(Get<bool>(gate, "IsOpen"), Is.True, "Restart must reset one-shot use and input suspension.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CombatDemoSavedSceneAttacksThroughInputAndRestarts()
        {
            yield return Load("Assets/Combat/Demo/CombatDemo.unity");
            var hud = Find("ShadowsOfTheForsaken.Combat.CombatDemoHUD");
            var health = Field<Component>(hud, "player");
            var target = Field<Component>(hud, "target");
            var progression = Field<LevelProgressionController>(hud, "progression");
            var melee = health.GetComponent(RuntimeType("ShadowsOfTheForsaken.Combat.MeleeCombat"));
            Assert.That(health.gameObject, Is.EqualTo(movement.gameObject));
            Assert.That(Field<LevelProgressionController>(target, "progression"), Is.SameAs(progression));
            Assert.That(Field<LevelProgressionController>(health, "progression"), Is.SameAs(progression));
            Guid initialSession = progression.Snapshot.SessionId;
            int maximum = Get<int>(target, "Maximum");
            int damage = Field<int>(melee, "damage");
            Assert.That(maximum, Is.GreaterThan(damage));
            Input(false, Key.W); Walk(21); Input();
            Capture("combat-ready");

            Input(true); Call(movement, "Simulate", Frame);
            Assert.That(Get<AttackPhase>(melee, "Phase"), Is.EqualTo(AttackPhase.Windup));
            Capture("combat-windup");
            Call(melee, "Simulate", .25f);
            Assert.That(Get<int>(target, "Current"), Is.EqualTo(maximum - damage));
            Capture("combat-hit");
            Call(melee, "Simulate", 1f);
            Input(true); Call(movement, "Simulate", Frame); Call(melee, "Simulate", .25f);
            Assert.That(Get<int>(target, "Current"), Is.EqualTo(maximum - damage), "Held LMB must not issue another attack after cooldown.");
            int remainingStrikes = Mathf.CeilToInt((float)(maximum - damage) / damage);
            Assert.That(remainingStrikes, Is.InRange(1, 10), "Keep this demonstration bounded.");
            for (int i = 0; i < remainingStrikes; i++)
            {
                Input(); Input(true); Call(movement, "Simulate", Frame);
                Call(melee, "Simulate", .25f); Call(melee, "Simulate", 1f);
            }
            Assert.That(Get<bool>(target, "IsAlive"), Is.False);
            Capture("combat-death");

            Call(hud, "ResetArena");
            Assert.That(progression.Snapshot.SessionId, Is.Not.EqualTo(initialSession));
            Assert.That(Get<int>(target, "Current"), Is.EqualTo(maximum));
            Assert.That(Get<int>(health, "Current"), Is.EqualTo(Get<int>(health, "Maximum")));
            Assert.That(Vector3.Distance(movement.transform.position, Field<Vector3>(hud, "spawn")), Is.LessThan(.01f));
            Input(); Input(false, Key.W); Walk(21); Input(); Input(true);
            Call(movement, "Simulate", Frame); Call(melee, "Simulate", .25f);
            Assert.That(Get<int>(target, "Current"), Is.EqualTo(maximum - damage), "A fresh session must permit a new input-driven attack.");
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator Load(string path)
        {
            sceneLoad = new OwnedSceneLoad();
            yield return sceneLoad.Load(path, () => EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive)));
            scene = sceneLoad.Scene;
            Assert.That(sceneLoad.LoadedWithinDeadline && scene.IsValid() && scene.isLoaded, Is.True, "Saved demo did not load: " + path);
            SceneManager.SetActiveScene(scene);
            yield return null; // Let the saved demo's Start initialize its session.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    Assert.That(component != null, Is.True, "Missing component under " + root.name);
            movement = Find("PlayerMovement"); follow = Find("CameraFollow");
            camera = follow.GetComponent<Camera>();
            Assert.That(camera != null && camera.isActiveAndEnabled, Is.True);
            Assert.That(Field<Transform>(follow, "player"), Is.EqualTo(movement.transform));
            var source = Field<InputActionAsset>(movement, "inputActions");
            Assert.That(source, Is.Not.Null, "The saved player must assign its real input asset.");
            ((Behaviour)movement).enabled = false;
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            input = Object.Instantiate(source);
            input.devices = new InputDevice[] { keyboard, mouse };
            movement.GetType().GetField("inputActions").SetValue(movement, input);
            ((Behaviour)movement).enabled = true;
            Call(movement, "OnApplicationFocus", true);
            var bridge = movement.GetComponent(RuntimeType("PlayerCombat"));
            if (bridge != null) Call(bridge, "OnApplicationFocus", true);
            Input(); Walk(12); Input();
            Physics.SyncTransforms();
        }

        private void Input(bool attack = false, params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.QueueStateEvent(mouse, attack ? new MouseState().WithButton(MouseButton.Left) : new MouseState());
            InputSystem.Update();
            Assert.That(InputState.currentUpdateType, Is.EqualTo(InputUpdateType.Manual));
        }
        private void Walk(int frames) { for (int i = 0; i < frames; i++) Call(movement, "Simulate", Frame); }

        private void Capture(string name)
        {
            string directory = Environment.GetEnvironmentVariable("SHADOWS_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) return;
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null), "Optional captures require graphics; omit SHADOWS_CAPTURE_DIR for -nographics runs.");
            Assert.That((bool)Call(follow, "SnapToTarget"), Is.True, "Capture requires the scene camera's safe follow pose.");
            // Refresh presentation for the manually advanced action without adding
            // an uncontrolled Update frame or advancing the combat clock twice.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var feedback in root.GetComponentsInChildren(RuntimeType("ShadowsOfTheForsaken.Combat.CombatFeedback")))
                    Call(feedback, "Apply");
            var originalTarget = camera.targetTexture;
            var originalActive = RenderTexture.active;
            bool originalAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            var texture = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            Texture2D pixels = null;
            try
            {
                // A first-use asynchronous shader placeholder is not material or
                // combat-feedback evidence. Compile needed variants synchronously
                // for this render only; preserve the editor's surrounding policy.
                ShaderUtil.allowAsyncCompilation = false;
                texture.Create();
                var request = new RenderPipeline.StandardRequest { destination = texture };
                Assert.That(RenderPipeline.SupportsRenderRequest(camera, request), Is.True, "The scene pipeline must support a camera render request.");
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
                texture.Release(); Object.DestroyImmediate(texture);
            }
        }

        private Component Find(string typeName)
        {
            var matches = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(RuntimeType(typeName), true)).ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), "Expected one saved " + typeName);
            return matches[0];
        }
        private static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static T Field<T>(Component component, string name) => (T)component.GetType().GetField(name).GetValue(component);
        private static T Get<T>(Component component, string name) => (T)component.GetType().GetProperty(name).GetValue(component);
        private static object Call(Component component, string method, params object[] arguments)
        {
            try { return component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(component, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
    }
}
#endif
