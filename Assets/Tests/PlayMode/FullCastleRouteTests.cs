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
    // DOCX 3, 4 and 6-8: saved-scene routes driven by input and normal Unity updates.
    // No progression command, teleport, damage injection or special enemy tuning is used.
    public sealed class FullCastleRouteTests
    {
        private readonly List<Behaviour> suspendedBehaviours = new List<Behaviour>();
        private readonly List<Collider> suspendedColliders = new List<Collider>();
        private readonly List<Renderer> suspendedRenderers = new List<Renderer>();
        private Scene scene, previousScene;
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
            routeTrace.Clear(); inputTrace.Clear(); recordingInputs = false; lastInput = null;
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
                    item.GetType().Name == "Volume" || item.GetType().Name == "NavMeshSurface" || item.GetType().Name == "NavMeshAgent" || item.GetType().Assembly.GetName().Name == "Assembly-CSharp" ||
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
            bool unloaded = true;
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded)
            {
                var operation = SceneManager.UnloadSceneAsync(scene);
                float deadline = Time.realtimeSinceStartup + 30;
                while (operation != null && !operation.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                unloaded = operation == null || operation.isDone;
            }
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
            Assert.That(unloaded, Is.True, "Demo unload timed out.");
        }

        private LevelProgressionController progression;
        private Component session, playerHealth;
        private readonly List<string> routeTrace = new List<string>();
        private readonly List<InputFrame> inputTrace = new List<InputFrame>();
        private bool recordingInputs;
        private float inputStarted;
        private string lastInput;
        private LevelObjective defeatAt;
        [Serializable] private sealed class InputFrame { public float time; public string[] keys; public bool attack; }
        [Serializable] private sealed class RouteEvidence { public float seconds; public bool secret; public int health; public string[] progression; public InputFrame[] inputs; }

        [UnityTest]
        [Timeout(420000)]
        public IEnumerator CastleMainRouteCompletesThroughControlsWithoutSecret()
        {
            yield return RunRoute(false);
        }

        [UnityTest]
        [Timeout(540000)]
        public IEnumerator CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight()
        {
            yield return RunRoute(true);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CastleFirstFightDeathRestartsTheSavedWorld()
        {
            yield return RunRoute(false, LevelObjective.FirstEnemyDefeated);
        }

        [UnityTest, Timeout(360000)]
        public IEnumerator CastleThroneFightDeathRestartsTheSavedWorld()
        {
            yield return RunRoute(false, LevelObjective.MinibossDefeated);
        }

        [UnityTest, Timeout(420000)]
        public IEnumerator CastleFinalFightDeathRestartsTheSavedWorld()
        {
            yield return RunRoute(false, LevelObjective.FinalEnemyDefeated);
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator CastleLockedMainGatesRejectWalkingAndJumping()
        {
            yield return Load("Assets/Scenes/ForsakenCastle.unity");
            session = Find("LevelSessionController"); progression = Field<LevelProgressionController>(session,"progression");
            playerHealth = Field<Component>(session,"playerHealth");
            Call(session,"OnApplicationFocus",true);
            foreach (var enemy in FindAll("ShadowsOfTheForsaken.Encounters.EnemyEncounter")) Call(enemy,"OnApplicationFocus",true);
            yield return MoveTo(new Vector3(0,0,16));
            yield return MoveTo(new Vector3(8,0,16));
            yield return MoveTo(new Vector3(8,0,27));
            yield return PushClosedGate(28);
            Assert.That(progression.CanEnter(LevelRoom.ThroneRoom), Is.False);
            yield return MoveTo(new Vector3(8,0,16));
            yield return MoveTo(new Vector3(-8,0,16));
            yield return MoveTo(new Vector3(-8,0,19));
            yield return PushClosedGate(20);
            Assert.That(progression.CanEnter(LevelRoom.Puzzle), Is.False);
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator PushClosedGate(float boundary)
        {
            float until = Time.time + 1.4f;
            while (Time.time < until) { Input(false,Key.W,Key.Space); yield return null; }
            Input(); yield return null;
            Assert.That(movement.transform.position.z, Is.LessThan(boundary-.2f), Diagnostic("Jumping at a locked main gate"));
            Assert.That(Get<bool>(session,"IsRunning"), Is.True);
        }

        private IEnumerator RunRoute(bool secret, LevelObjective plannedDeath = LevelObjective.None)
        {
            defeatAt = plannedDeath;
            yield return Load("Assets/Scenes/ForsakenCastle.unity");
            session = Find("LevelSessionController");
            progression = Field<LevelProgressionController>(session, "progression");
            playerHealth = Field<Component>(session, "playerHealth");
            Call(session, "OnApplicationFocus", true);
            foreach (var enemy in FindAll("ShadowsOfTheForsaken.Encounters.EnemyEncounter"))
                Call(enemy, "OnApplicationFocus", true);
            Assert.That(Get<bool>(session, "IsRunning"), Is.True);
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Guid originalSession = progression.Snapshot.SessionId;
            inputStarted = Time.time; recordingInputs = true; Input();
            progression.Changed += change => routeTrace.Add(change.After.Room + ":" + change.After.CompletedObjectives);
            Capture("route-courtyard");
            yield return MoveTo(new Vector3(0, 0, 10));
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(FindAll("ShadowsOfTheForsaken.Encounters.EnemyEncounter").All(e => !Get<bool>(e, "IsActivated")), Is.True,
                "The extended courtyard must remain calm before the encounter threshold.");
            yield return MoveTo(new Vector3(0, 0, 15));
            yield return Fight(LevelObjective.FirstEnemyDefeated);
            if (defeatAt == LevelObjective.FirstEnemyDefeated) { yield return AssertDeathRestart(originalSession); yield break; }
            yield return MoveTo(new Vector3(0, 0, 16));
            yield return MoveTo(new Vector3(-8, 0, 16));
            yield return MoveTo(new Vector3(-8, 0, 24));
            var damaged = FindAll("InteractionTarget").Single(t => Field<LevelObjective>(t, "objective") == LevelObjective.None);
            yield return Use(damaged);
            Assert.That(Has(LevelObjective.MainPuzzleSolved), Is.False, "An incorrect mechanism must not solve the puzzle.");
            yield return Use(ObjectiveTarget(LevelObjective.MainPuzzleSolved));
            Assert.That(Has(LevelObjective.MainPuzzleSolved), Is.True);
            Capture("route-rune-lever");
            yield return MoveTo(new Vector3(-8, 0, 24));
            yield return MoveTo(new Vector3(-8, 0, 16));
            yield return MoveTo(new Vector3(0, 0, 16));
            yield return MoveTo(new Vector3(8, 0, 16));
            yield return MoveTo(new Vector3(8, 0, 30));
            yield return Fight(LevelObjective.MinibossDefeated);
            if (defeatAt == LevelObjective.MinibossDefeated) { yield return AssertDeathRestart(originalSession); yield break; }
            Capture("route-throne");
            yield return MoveTo(new Vector3(8, 0, 32));
            yield return MoveTo(new Vector3(16, 0, 32));
            yield return MoveTo(new Vector3(16, 0, 36));
            yield return MoveTo(new Vector3(16, -2, 39));
            yield return Use(ObjectiveTarget(LevelObjective.LibraryOpened));
            Assert.That(Has(LevelObjective.LibraryOpened), Is.True);
            Capture("route-library");
            yield return MoveTo(new Vector3(16, -4, 44));
            yield return MoveTo(new Vector3(16, -4, 48));
            if (secret)
            {
                yield return Use(ObjectiveTarget(LevelObjective.SecretLeverPulled));
                yield return MoveTo(new Vector3(16, -4, 48));
                yield return MoveTo(new Vector3(-8, -4, 48));
                yield return MoveTo(new Vector3(-8, -4, 64));
                yield return Use(ObjectiveTarget(LevelObjective.BonusDiscovered));
                Assert.That(Has(LevelObjective.BonusDiscovered), Is.True);
                Capture("route-relic");
                yield return MoveTo(new Vector3(-8, -4, 64));
                foreach (var point in new[] { new Vector3(-12,-4,64), new Vector3(-28,-12,64),
                    new Vector3(-30,-12,64), new Vector3(-30,-12,32), new Vector3(-24,-12,32),
                    new Vector3(0,0,32), new Vector3(8,0,32) }) yield return MoveTo(point);
                Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.ThroneRoom), Diagnostic("Secret return"));
                Assert.That(Has(LevelObjective.FinalEnemyDefeated), Is.False);
                Assert.That(progression.CanEnter(LevelRoom.Exit), Is.False);
                foreach (var point in new[] { new Vector3(16,0,32), new Vector3(16,0,36),
                    new Vector3(16,-2,40), new Vector3(16,-4,44), new Vector3(16,-4,48) }) yield return MoveTo(point);
            }
            yield return MoveTo(new Vector3(8, -4, 48));
            yield return MoveTo(new Vector3(8, -4, 62));
            Assert.That(progression.CanEnter(LevelRoom.Exit), Is.False, "The final fight remains mandatory.");
            Capture("route-final-ready");
            yield return Fight(LevelObjective.FinalEnemyDefeated);
            if (defeatAt == LevelObjective.FinalEnemyDefeated) { yield return AssertDeathRestart(originalSession); yield break; }
            yield return MoveTo(new Vector3(8, -4, 70));
            yield return MoveTo(new Vector3(8, -4, 76), true);
            Input(); yield return null;
            Assert.That(progression.Snapshot.IsCompleted, Is.True, Diagnostic("Exit"));
            Assert.That(Get<object>(session, "State").ToString(), Is.EqualTo("Completed"));
            Assert.That(Has(LevelObjective.BonusDiscovered), Is.EqualTo(secret));
            Assert.That(Get<bool>(playerHealth, "IsAlive"), Is.True);
            Capture(secret ? "route-complete-secret" : "route-complete-main");
            recordingInputs = false;
            string evidenceDirectory = Environment.GetEnvironmentVariable("SHADOWS_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(evidenceDirectory))
            {
                var evidence = new RouteEvidence { seconds = Get<float>(session, "ElapsedSeconds"), secret = secret,
                    health = Get<int>(playerHealth, "Current"), progression = routeTrace.ToArray(), inputs = inputTrace.ToArray() };
                File.WriteAllText(Path.Combine(evidenceDirectory, TestContext.CurrentContext.Test.Name + "-input.json"), JsonUtility.ToJson(evidence, true));
            }
            string diagnostic = "Full castle input route " + (secret ? "with secret" : "without secret") + ": " + string.Join(" -> ", routeTrace);
            LogAssert.Expect(LogType.Log, diagnostic);
            Debug.Log(diagnostic);

            // Restart through the actual terminal R action, then inspect the
            // saved world's reset state. Never call the reset API from this test.
            Input(); yield return null;
            Input(false, Key.R); yield return null;
            Input(); yield return null;
            Assert.That(progression.Snapshot.SessionId, Is.Not.EqualTo(originalSession));
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(Get<bool>(session, "IsRunning"), Is.True);
            Assert.That(Get<int>(playerHealth, "Current"), Is.EqualTo(Get<int>(playerHealth, "Maximum")));
            Assert.That(Vector3.Distance(movement.transform.position, Field<Transform>(session, "spawn").position), Is.LessThan(.3f));
            foreach (var enemy in FindAll("ShadowsOfTheForsaken.Encounters.EnemyEncounter"))
            {
                Assert.That(Get<bool>(enemy, "IsActivated"), Is.False);
                Assert.That(Get<bool>(enemy, "HasPendingDeath"), Is.False);
                Assert.That(Get<bool>(enemy.GetComponent(RuntimeType("ShadowsOfTheForsaken.Combat.CombatHealth")), "IsAlive"), Is.True);
            }
            foreach (var target in FindAll("InteractionTarget")) Assert.That(Get<bool>(target, "IsConsumed"), Is.False);
            foreach (var gate in FindAll("ProgressionGate"))
                Assert.That(Get<bool>(gate, "IsOpen"), Is.EqualTo(progression.IsPassageOpen(Field<LevelRoom>(gate,"from"), Field<LevelRoom>(gate,"to"))));
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator MoveTo(Vector3 target, bool acceptCompletion = false)
        {
            float deadline = Time.realtimeSinceStartup + 50;
            while (FlatDistance(movement.transform.position, target) > .3f)
            {
                if (acceptCompletion && progression.Snapshot.IsCompleted) { Input(); yield break; }
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), Diagnostic("Walk toward " + target));
                Assert.That(Get<bool>(session, "IsRunning"), Is.True, Diagnostic("Walk interrupted"));
                Vector3 offset = target - movement.transform.position; offset.y = 0;
                float angle = Vector3.SignedAngle(movement.transform.forward, offset, Vector3.up);
                if (Mathf.Abs(angle) > 2.5f) Input(false, angle > 0 ? Key.D : Key.A);
                else Input(false, Key.W);
                yield return null;
                Assert.That(movement.transform.position.y, Is.GreaterThan(Mathf.Min(target.y, -12) - 3), Diagnostic("Fall"));
            }
            Input(); yield return null;
        }

        private IEnumerator Face(Vector3 target)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (true)
            {
                Vector3 offset = target - movement.transform.position; offset.y = 0;
                float angle = Vector3.SignedAngle(movement.transform.forward, offset, Vector3.up);
                if (Mathf.Abs(angle) < 3) break;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), Diagnostic("Turn"));
                Input(false, angle > 0 ? Key.D : Key.A); yield return null;
            }
            Input(); yield return null;
        }

        private IEnumerator Use(Component target)
        {
            Vector3 offset = target.transform.position - movement.transform.position; offset.y = 0;
            if (offset.magnitude > 2)
                yield return MoveTo(target.transform.position - offset.normalized * 1.7f);
            yield return Face(target.transform.position);
            var interactor = Find("PlayerInteractor");
            Input(); yield return null;
            Assert.That(Get<Component>(interactor, "SelectedTarget"), Is.SameAs(target), Diagnostic("Select " + target.name));
            Assert.That(Get<bool>(interactor, "CanInteract"), Is.True, Diagnostic("Use " + target.name));
            Input(false, Key.E); yield return null;
            Input(); yield return null;
            Assert.That(Get<bool>(target, "IsConsumed"), Is.True, Diagnostic("Consume " + target.name));
        }

        private IEnumerator Fight(LevelObjective objective)
        {
            var enemy = FindAll("ShadowsOfTheForsaken.Encounters.EnemyEncounter").Single(e => Field<LevelObjective>(e, "objective") == objective);
            var enemyHealth = enemy.GetComponent(RuntimeType("ShadowsOfTheForsaken.Combat.CombatHealth"));
            var enemyMelee = enemy.GetComponent(RuntimeType("ShadowsOfTheForsaken.Combat.MeleeCombat"));
            var playerMelee = playerHealth.GetComponent(RuntimeType("ShadowsOfTheForsaken.Combat.MeleeCombat"));
            if (defeatAt == objective)
            {
                yield return Face(enemy.transform.position);
                float deathDeadline = Time.realtimeSinceStartup + 70;
                while (Get<bool>(playerHealth, "IsAlive"))
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deathDeadline), Diagnostic("Await real enemy defeat " + objective));
                    Vector3 offset = enemy.transform.position - movement.transform.position; offset.y = 0;
                    float angle = Vector3.SignedAngle(movement.transform.forward, offset, Vector3.up);
                    if (Mathf.Abs(angle) > 4) Input(false, angle > 0 ? Key.D : Key.A);
                    else Input(false, offset.magnitude > 1.45f ? new[] { Key.W } : Array.Empty<Key>());
                    yield return null;
                }
                Input(); yield return null;
                Assert.That(Has(objective), Is.False, "Death must not award the undefeated encounter.");
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + 70;
            bool pressedLastFrame = false, capturedTelegraph = false;
            while (!Has(objective))
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), Diagnostic("Fight " + objective + " enemyHP=" + Get<int>(enemyHealth,"Current")));
                Assert.That(Get<bool>(playerHealth, "IsAlive"), Is.True, Diagnostic("Defeated during " + objective));
                Vector3 offset = enemy.transform.position - movement.transform.position; offset.y = 0;
                float distance = offset.magnitude;
                float angle = Vector3.SignedAngle(movement.transform.forward, offset, Vector3.up);
                var phase = Get<AttackPhase>(playerMelee, "Phase");
                var enemyPhase = Get<AttackPhase>(enemyMelee, "Phase");
                if (!capturedTelegraph && enemyPhase == AttackPhase.Windup)
                {
                    Capture("route-fight-" + objective);
                    capturedTelegraph = true;
                }
                bool attack = false;
                if (!Get<bool>(enemyHealth, "IsAlive"))
                {
                    if (progression.Snapshot.Room != Field<LevelRoom>(enemy, "encounterRoom"))
                        yield return MoveTo(Field<Vector3>(enemy, "arenaCenter"));
                    Input();
                }
                else if (Mathf.Abs(angle) > 4) Input(false, angle > 0 ? Key.D : Key.A);
                else if (phase == AttackPhase.Windup || phase == AttackPhase.Active) Input();
                else if (phase != AttackPhase.Ready || enemyPhase == AttackPhase.Windup || enemyPhase == AttackPhase.Active)
                    Input(false, distance < 3 ? new[] { Key.S } : Array.Empty<Key>());
                else if (distance > 1.85f) Input(false, Key.W);
                else
                {
                    attack = !pressedLastFrame;
                    Input(attack);
                }
                pressedLastFrame = attack;
                yield return null;
            }
            Input(); yield return null;
            Assert.That(Get<bool>(enemy, "DeathCredited"), Is.True);
        }

        private IEnumerator AssertDeathRestart(Guid previousSession)
        {
            Assert.That(Get<object>(session, "State").ToString(), Is.EqualTo("Defeated"));
            Capture("route-defeat-" + defeatAt);
            Input(); yield return null;
            Input(false, Key.R); yield return null;
            Input(); yield return null;
            recordingInputs = false;
            Assert.That(progression.Snapshot.SessionId, Is.Not.EqualTo(previousSession));
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(Get<bool>(session, "IsRunning"), Is.True);
            Assert.That(Get<int>(playerHealth, "Current"), Is.EqualTo(Get<int>(playerHealth, "Maximum")));
            Assert.That(Vector3.Distance(movement.transform.position, Field<Transform>(session,"spawn").position), Is.LessThan(.3f));
            foreach (var enemy in FindAll("ShadowsOfTheForsaken.Encounters.EnemyEncounter"))
            {
                var health = enemy.GetComponent(RuntimeType("ShadowsOfTheForsaken.Combat.CombatHealth"));
                Assert.That(Get<int>(health,"Current"), Is.EqualTo(Get<int>(health,"Maximum")));
                Assert.That(Get<bool>(enemy,"IsActivated"), Is.False);
                Assert.That(Get<bool>(enemy,"HasPendingDeath"), Is.False);
            }
            foreach (var target in FindAll("InteractionTarget")) Assert.That(Get<bool>(target,"IsConsumed"), Is.False);
            foreach (var gate in FindAll("ProgressionGate"))
                Assert.That(Get<bool>(gate,"IsOpen"), Is.EqualTo(progression.IsPassageOpen(Field<LevelRoom>(gate,"from"),Field<LevelRoom>(gate,"to"))));
            LogAssert.NoUnexpectedReceived();
        }

        private bool Has(LevelObjective objective) => (progression.Snapshot.CompletedObjectives & objective) == objective;
        private Component ObjectiveTarget(LevelObjective objective) => FindAll("InteractionTarget").Single(t => Field<LevelObjective>(t,"objective") == objective);
        private Component[] FindAll(string name) => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(RuntimeType(name), true)).ToArray();
        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x-b.x,a.z-b.z).magnitude;
        private string Diagnostic(string action) => action + "; position=" + movement.transform.position + "; room=" + progression.Snapshot.Room +
            "; objectives=" + progression.Snapshot.CompletedObjectives + "; session=" + progression.Snapshot.SessionId + "; health=" + Get<int>(playerHealth,"Current");

        private IEnumerator Load(string path)
        {
            var before = new HashSet<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++) before.Add(SceneManager.GetSceneAt(i));
            var operation = EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            Assert.That(operation, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 30;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var candidate = SceneManager.GetSceneAt(i);
                if (candidate.path == path && !before.Contains(candidate)) scene = candidate;
            }
            Assert.That(operation.isDone && scene.IsValid() && scene.isLoaded, Is.True, "Saved demo did not load: " + path);
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
            Input(); yield return null; Input();
            Physics.SyncTransforms();
        }

        private void Input(bool attack = false, params Key[] keys)
        {
            if (recordingInputs)
            {
                string signature = attack + ":" + string.Join(",", keys);
                if (signature != lastInput)
                {
                    inputTrace.Add(new InputFrame { time = Time.time - inputStarted, keys = keys.Select(key => key.ToString()).ToArray(), attack = attack });
                    lastInput = signature;
                }
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.QueueStateEvent(mouse, attack ? new MouseState().WithButton(MouseButton.Left) : new MouseState());
            InputSystem.Update();
            Assert.That(InputState.currentUpdateType, Is.EqualTo(InputUpdateType.Manual));
        }

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
            var occlusion = follow.GetComponent(RuntimeType("CameraPlayerOcclusion"));
            if (occlusion != null) Call(occlusion, "RefreshVisibility");
            var originalTarget = camera.targetTexture;
            var originalActive = RenderTexture.active;
            bool originalAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            var canvases = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Canvas>()).ToArray();
            var modes = canvases.Select(c => c.renderMode).ToArray();
            var cameras = canvases.Select(c => c.worldCamera).ToArray();
            var distances = canvases.Select(c => c.planeDistance).ToArray();
            var texture = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            Texture2D pixels = null;
            try
            {
                // A first-use asynchronous shader placeholder is not material or
                // combat-feedback evidence. Compile needed variants synchronously
                // for this render only; preserve the editor's surrounding policy.
                ShaderUtil.allowAsyncCompilation = false;
                texture.Create();
                for (int i = 0; i < canvases.Length; i++)
                {
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = camera;
                    canvases[i].planeDistance = camera.nearClipPlane + .1f;
                }
                Canvas.ForceUpdateCanvases();
                var request = new RenderPipeline.StandardRequest { destination = texture };
                Assert.That(RenderPipeline.SupportsRenderRequest(camera, request), Is.True, "The scene pipeline must support a camera render request.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = texture;
                pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, TestContext.CurrentContext.Test.Name + "-" + name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                for (int i = 0; i < canvases.Length; i++)
                {
                    canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i]; canvases[i].planeDistance = distances[i];
                }
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
