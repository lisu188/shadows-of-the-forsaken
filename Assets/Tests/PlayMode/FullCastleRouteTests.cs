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
            sceneLoad = null; scene = default;
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
            Assert.That(sceneLoad?.CleanupFailure, Is.Null);
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
            float windupObservedAt = -1f;
            float telegraphDelay = Mathf.Min(.12f, Mathf.Max(0f, Field<float>(enemyMelee, "windup")) * .3f);
            while (!Has(objective))
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), Diagnostic("Fight " + objective + " enemyHP=" + Get<int>(enemyHealth,"Current")));
                Assert.That(Get<bool>(playerHealth, "IsAlive"), Is.True, Diagnostic("Defeated during " + objective));
                Vector3 offset = enemy.transform.position - movement.transform.position; offset.y = 0;
                float distance = offset.magnitude;
                float angle = Vector3.SignedAngle(movement.transform.forward, offset, Vector3.up);
                var phase = Get<AttackPhase>(playerMelee, "Phase");
                var enemyPhase = Get<AttackPhase>(enemyMelee, "Phase");
                if (enemyPhase != AttackPhase.Windup) windupObservedAt = -1f;
                else if (windupObservedAt < 0f) windupObservedAt = Time.time;
                if (!capturedTelegraph && windupObservedAt >= 0f && Time.time - windupObservedAt >= telegraphDelay)
                {
                    // Observe ordinary updates long enough for the actor's
                    // LateUpdate windup pose; never advance combat for a capture.
                    var framing = Capture("route-fight-" + objective, enemy);
                    // An implementation floor against a mostly hidden attacker,
                    // not a claim about lighting, animation quality or human play.
                    // Sample the real windup meshes before making assertions so
                    // a failed view retains the same PNG and numeric diagnosis.
                    Assert.That(framing.geometryFailure, Is.Null, Diagnostic(framing.geometryFailure));
                    foreach (var region in framing.regions)
                    {
                        Assert.That(region.silhouetteSamples, Is.GreaterThanOrEqualTo(12),
                            Diagnostic(objective + " has insufficient visible-mesh samples for " + region.name));
                        Assert.That(region.visibleFraction, Is.GreaterThanOrEqualTo(.5f),
                            Diagnostic(objective + " player obscures most of the " + region.name +
                                "; framing=" + JsonUtility.ToJson(framing)));
                    }
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
            Assert.That(capturedTelegraph, Is.True, "The normal fight must expose a windup for combat-framing regression: " + objective);
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
            sceneLoad = new OwnedSceneLoad();
            yield return sceneLoad.Load(path, () => EditorSceneManager.LoadSceneAsyncInPlayMode(path,
                new LoadSceneParameters(LoadSceneMode.Additive)));
            scene = sceneLoad.Scene;
            Assert.That(sceneLoad.LoadedWithinDeadline && scene.IsValid() && scene.isLoaded, Is.True,
                "Saved castle did not load within 30 s: " + path);
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

        private FramingEvidence Capture(string name, Component framingEnemy = null)
        {
            string directory = Environment.GetEnvironmentVariable("SHADOWS_CAPTURE_DIR");
            bool saveImage = !string.IsNullOrWhiteSpace(directory);
            if (!saveImage && framingEnemy == null) return null;
            if (saveImage)
                Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null), "Optional captures require graphics; omit SHADOWS_CAPTURE_DIR for -nographics runs.");
            // Refresh presentation for the manually advanced action without adding
            // an uncontrolled Update frame or advancing the combat clock twice.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var feedback in root.GetComponentsInChildren(RuntimeType("ShadowsOfTheForsaken.Combat.CombatFeedback")))
                    Call(feedback, "Apply");
            var occlusion = follow.GetComponent(RuntimeType("CameraPlayerOcclusion"));
            var originalTarget = camera.targetTexture;
            var originalActive = RenderTexture.active;
            bool originalAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            var canvases = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Canvas>()).ToArray();
            var modes = canvases.Select(c => c.renderMode).ToArray();
            var cameras = canvases.Select(c => c.worldCamera).ToArray();
            var distances = canvases.Select(c => c.planeDistance).ToArray();
            var texture = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            Texture2D pixels = null;
            FramingEvidence framing = null;
            try
            {
                // A first-use asynchronous shader placeholder is not material or
                // combat-feedback evidence. Compile needed variants synchronously
                // for this render only; preserve the editor's surrounding policy.
                if (saveImage)
                {
                    ShaderUtil.allowAsyncCompilation = false;
                    texture.Create();
                }
                // Establish the destination before camera clearance and UI layout.
                // URP otherwise assigns it only inside SubmitRenderRequest, after
                // a 4:3 editor canvas has already been sized for this 16:9 image.
                camera.targetTexture = texture;
                Assert.That((bool)Call(follow, "SnapToTarget"), Is.True, "Capture requires the scene camera's safe follow pose.");
                if (occlusion != null) Call(occlusion, "RefreshVisibility");
                if (framingEnemy != null)
                {
                    framing = MeasureFraming(framingEnemy);
                    string json = JsonUtility.ToJson(framing, true);
                    TestContext.WriteLine("Combat framing: " + JsonUtility.ToJson(framing));
                    if (saveImage)
                    {
                        Directory.CreateDirectory(directory);
                        File.WriteAllText(Path.Combine(directory, TestContext.CurrentContext.Test.Name + "-" + name + "-framing.json"), json);
                    }
                }
                if (!saveImage) return framing;
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
                Call(follow, "SnapToTarget");
                if (occlusion != null) Call(occlusion, "RefreshVisibility");
                Canvas.ForceUpdateCanvases();
                RenderTexture.active = originalActive;
                if (pixels != null) Object.DestroyImmediate(pixels);
                texture.Release(); Object.DestroyImmediate(texture);
            }
            return framing;
        }

        [Serializable] private sealed class FramingRegion
        {
            public string name;
            public int silhouetteSamples, visibleSamples, playerBlockedSamples, outsideViewSamples;
            public float visibleFraction;
        }
        [Serializable] private sealed class FramingEvidence
        {
            public string scope = "Actual front-facing actor triangles at the observed windup; player obscuration and viewport only. Not environment, lighting or human acceptance.";
            public string encounter, test;
            public string geometryFailure;
            public Vector3 playerPosition, enemyPosition, cameraPosition;
            public Quaternion cameraRotation;
            public float cameraDistance, cameraHeight, cameraPivotHeight, lookHeightOffset, shoulderOffset, shoulderAimFraction, fieldOfView, aspect;
            public double measurementMilliseconds;
            public int columns = 48, rows = 64, sampledRays, playerMeshes, enemyMeshes;
            public FramingRegion[] regions;
        }
        private sealed class FramingMesh
        {
            public Bounds bounds;
            public Vector3[] vertices;
            public int[] indices;
            public int region;
        }

        private FramingEvidence MeasureFraming(Component enemy)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var presentation = enemy.GetComponent(RuntimeType("CastleActorPresentation"));
            var head = Field<Transform>(presentation, "head");
            var torso = Field<Transform>(presentation, "torso");
            var rightArm = Field<Transform>(presentation, "rightArm");
            var leftArm = Field<Transform>(presentation, "leftArm");
            var cloak = Field<Transform>(presentation, "cloak");
            bool demon = Field<bool>(presentation, "demonic");
            var names = demon ? new[] { "head", "torso", "right attack arm", "left attack arm" }
                : new[] { "head", "torso", "right attack arm" };
            var result = new FramingEvidence
            {
                encounter = enemy.name, test = TestContext.CurrentContext.Test.Name,
                playerPosition = movement.transform.position, enemyPosition = enemy.transform.position,
                cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation,
                cameraDistance = Field<float>(follow, "distance"), cameraHeight = Field<float>(follow, "height"),
                cameraPivotHeight = Field<float>(follow, "pivotHeight"), lookHeightOffset = Field<float>(follow, "lookHeightOffset"),
                shoulderOffset = Field<float>(follow, "shoulderOffset"), shoulderAimFraction = Field<float>(follow, "shoulderAimFraction"),
                fieldOfView = camera.fieldOfView, aspect = camera.aspect,
                regions = names.Select(value => new FramingRegion { name = value }).ToArray()
            };
            // Assign by the articulated hierarchy, excluding nested arms/head
            // and cloak from torso. Other enemy meshes still self-occlude.
            int Region(Transform part)
            {
                if (part.IsChildOf(head)) return 0;
                if (part.IsChildOf(rightArm)) return 2;
                if (part.IsChildOf(leftArm)) return demon ? 3 : -1;
                if (cloak != null && part.IsChildOf(cloak)) return -1;
                return part.IsChildOf(torso) ? 1 : -1;
            }
            var enemies = FramingMeshes(enemy.transform, Region);
            var players = FramingMeshes(movement.transform, _ => -1);
            result.enemyMeshes = enemies.Length; result.playerMeshes = players.Length;
            if (enemies.Length == 0)
            {
                result.geometryFailure = "No rendered enemy meshes to measure.";
                result.measurementMilliseconds = watch.Elapsed.TotalMilliseconds;
                return result;
            }
            Vector2 lower = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 upper = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var mesh in enemies)
                foreach (var vertex in mesh.vertices)
                {
                    Vector3 point = camera.WorldToViewportPoint(vertex);
                    if (point.z <= camera.nearClipPlane)
                    {
                        result.geometryFailure = "The windup actor intersects the camera near plane.";
                        result.measurementMilliseconds = watch.Elapsed.TotalMilliseconds;
                        return result;
                    }
                    lower = Vector2.Min(lower, point); upper = Vector2.Max(upper, point);
                }
            // Uniform projected-area samples avoid triangle-count bias between
            // the cape, armour and detailed horns. Frontmost enemy intersections
            // supply the unoccluded silhouette; no capsules or bounds stand in
            // for visible surfaces. Bounds only accelerate exact triangle rays.
            for (int y = 0; y < result.rows; y++)
                for (int x = 0; x < result.columns; x++)
                {
                    var point = new Vector2(Mathf.Lerp(lower.x, upper.x, (x + .5f) / result.columns),
                        Mathf.Lerp(lower.y, upper.y, (y + .5f) / result.rows));
                    var ray = camera.ViewportPointToRay(point);
                    result.sampledRays++;
                    float distance = NearestFramingSurface(enemies, ray, camera.farClipPlane, out int regionIndex);
                    if (regionIndex < 0) continue;
                    var region = result.regions[regionIndex]; region.silhouetteSamples++;
                    if (point.x < 0 || point.x > 1 || point.y < 0 || point.y > 1) region.outsideViewSamples++;
                    else if (!float.IsPositiveInfinity(NearestFramingSurface(players, ray, distance - .0001f, out _)))
                        region.playerBlockedSamples++;
                    else region.visibleSamples++;
                }
            foreach (var region in result.regions)
                region.visibleFraction = region.silhouetteSamples == 0 ? 0 : (float)region.visibleSamples / region.silhouetteSamples;
            result.measurementMilliseconds = watch.Elapsed.TotalMilliseconds;
            return result;
        }

        private FramingMesh[] FramingMeshes(Transform actor, Func<Transform, int> region)
        {
            var result = new List<FramingMesh>();
            foreach (var filter in actor.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                    (camera.cullingMask & (1 << filter.gameObject.layer)) == 0) continue;
                var matrix = filter.transform.localToWorldMatrix;
                result.Add(new FramingMesh { bounds = renderer.bounds, region = region(filter.transform),
                    vertices = filter.sharedMesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray(),
                    indices = filter.sharedMesh.triangles });
            }
            return result.ToArray();
        }

        private static float NearestFramingSurface(FramingMesh[] meshes, Ray ray, float maximum, out int region)
        {
            float closest = maximum; region = -1;
            bool found = false;
            foreach (var mesh in meshes)
            {
                if (!mesh.bounds.IntersectRay(ray, out float near) || near > closest) continue;
                for (int i = 0; i < mesh.indices.Length; i += 3)
                {
                    Vector3 a = mesh.vertices[mesh.indices[i]];
                    Vector3 edge1 = mesh.vertices[mesh.indices[i + 1]] - a;
                    Vector3 edge2 = mesh.vertices[mesh.indices[i + 2]] - a;
                    Vector3 p = Vector3.Cross(ray.direction, edge2);
                    float determinant = Vector3.Dot(edge1, p);
                    if (determinant <= .0000001f) continue; // Match opaque actor materials' backface culling.
                    Vector3 relative = ray.origin - a;
                    float u = Vector3.Dot(relative, p) / determinant;
                    if (u < 0 || u > 1) continue;
                    Vector3 q = Vector3.Cross(relative, edge1);
                    float v = Vector3.Dot(ray.direction, q) / determinant;
                    if (v < 0 || u + v > 1) continue;
                    float distance = Vector3.Dot(edge2, q) / determinant;
                    if (distance < 0 || distance >= closest) continue;
                    closest = distance; region = mesh.region; found = true;
                }
            }
            // A finite maximum is a search bound, never evidence of a hit.
            // Comparing that rounded return value with a repeated subtraction
            // can misclassify a miss when Mono keeps the latter at higher precision.
            // Region -1 is also a legitimate unlabelled/player surface.
            return found ? closest : float.PositiveInfinity;
        }

        [Test]
        public void FramingRayMissesRemainInfiniteInsideAndOutsideMeshBounds()
        {
            var mesh = FramingTriangle(5.123456f, -1);
            foreach (var meshes in new[] { Array.Empty<FramingMesh>(), new[] { mesh } })
                foreach (var origin in new[] { new Vector3(2, 0, 0), new Vector3(.9f, .9f, 0) })
                {
                    // The second ray crosses the bounds but misses the triangle.
                    float distance = NearestFramingSurface(meshes, new Ray(origin, Vector3.forward), 5.223456f - .0001f, out int region);
                    Assert.That(float.IsPositiveInfinity(distance), Is.True, "A miss must not return the finite search limit.");
                    Assert.That(region, Is.EqualTo(-1));
                }
        }

        [Test]
        public void FramingRayRejectsBackfacesAndHitsAtTheExclusiveDistanceLimit()
        {
            var mesh = FramingTriangle(5.123456f, 2);
            var ray = new Ray(Vector3.zero, Vector3.forward);
            Assert.That(float.IsPositiveInfinity(NearestFramingSurface(new[] { mesh }, ray, 5.123456f, out _)), Is.True);
            Assert.That(NearestFramingSurface(new[] { mesh }, ray, 5.123556f, out int region), Is.EqualTo(5.123456f).Within(.000001f));
            Assert.That(region, Is.EqualTo(2));
            mesh.indices = new[] { 0, 2, 1 };
            Assert.That(float.IsPositiveInfinity(NearestFramingSurface(new[] { mesh }, ray, 6, out _)), Is.True,
                "The opaque actor material does not render the reverse triangle face.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FramingRaySelectsNearestSurfaceIncludingUnlabelledPlayer(bool nearFirst)
        {
            var near = FramingTriangle(3.25f, -1);
            var far = FramingTriangle(5.125f, 2);
            var meshes = nearFirst ? new[] { near, far } : new[] { far, near };
            float distance = NearestFramingSurface(meshes, new Ray(Vector3.zero, Vector3.forward), 6, out int region);
            Assert.That(distance, Is.EqualTo(3.25f).Within(.000001f));
            Assert.That(region, Is.EqualTo(-1), "Player meshes are unlabelled but still count as real occluders.");
            Assert.That(float.IsPositiveInfinity(distance), Is.False);
        }

        private static FramingMesh FramingTriangle(float depth, int region) => new FramingMesh
        {
            bounds = new Bounds(new Vector3(0, 0, depth), new Vector3(2, 2, .01f)),
            vertices = new[] { new Vector3(-1, -1, depth), new Vector3(0, 1, depth), new Vector3(1, -1, depth) },
            indices = new[] { 0, 1, 2 }, region = region
        };

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
