using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    /// <summary>Real scene, colliders, triggers, combat queries and E selection. No direct objective grants.</summary>
    public sealed class ForsakenLevelTests
    {
        private const string ScenePath = "Assets/Scenes/ForsakenRuntimeCastle.unity";
        private readonly WaitForFixedUpdate fixedUpdate = new WaitForFixedUpdate();
        private readonly List<string> visited = new List<string>();
        private readonly List<string> screenshots = new List<string>();
        private readonly List<GameObject> suspendedSceneRoots = new List<GameObject>();
        private readonly InputTestFixture inputFixture = new InputTestFixture();
        private Scene scene;
        private Component level, movement, combat, interaction, follow, puzzle, hud;
        private Component[] enemies;
        private CharacterController character;
        private LevelProgressionController progression;
        private Keyboard keyboard;
        private InputSettings.UpdateMode previousInputMode;
        private float previousTimeScale;
        private float startedAt;
        private int movementSteps;
        private int combatSteps;
        private string evidenceName;
        private bool timedJourney;
        private float travelledMetres;
        private static readonly MethodInfo ManualInputUpdate = typeof(InputSystem).GetMethod("Update",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(InputUpdateType) }, null);

        private static object Call(Component component, string name, params object[] args)
        {
            try { return component.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(component, args); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        private static T Read<T>(Component component, string name) => (T)component.GetType().GetProperty(name).GetValue(component);

        private static void PumpInput()
        {
            Assert.That(ManualInputUpdate, Is.Not.Null, "Pinned Input System must expose its typed update pump.");
            try { ManualInputUpdate.Invoke(null, new object[] { InputUpdateType.Manual }); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        [UnitySetUp]
        public IEnumerator LoadLevel()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1;
            // Isolate authored scene geometry while preserving the test runner and the editor's original scene.
            var levelType = Type.GetType("ShadowsOfTheForsaken.Level.ForsakenLevel, Assembly-CSharp", true);
            suspendedSceneRoots.Clear();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                var otherScene = SceneManager.GetSceneAt(index);
                if (!otherScene.isLoaded || (otherScene.path != ScenePath && otherScene.path != "Assets/Scenes/ForsakenCastle.unity" && otherScene.path != "Assets/Scenes/SampleScene.unity")) continue;
                foreach (var root in otherScene.GetRootGameObjects())
                {
                    if (root.name.IndexOf("TestRunner", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    bool isLevelRoot = root.GetComponent(levelType) != null || root.name == "Shadows of the Forsaken";
                    bool isGeometryRoot = root.GetComponent<Camera>() != null || root.GetComponent<Light>() != null || root.GetComponent<Collider>() != null;
                    if (root.activeSelf && (otherScene.path == "Assets/Scenes/ForsakenCastle.unity" || isLevelRoot || isGeometryRoot))
                    {
                        suspendedSceneRoots.Add(root);
                        root.SetActive(false);
                    }
                }
            }
            // Disconnect any original scene consumers before saving the native input system.
            inputFixture.Setup();
            previousInputMode = InputSystem.settings.updateMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            keyboard = InputSystem.AddDevice<Keyboard>();
            var operation = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            Assert.That(operation, Is.Not.Null, "The authored level must be enabled in build settings.");
            float deadline = Time.realtimeSinceStartup + 60;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(operation.isDone, Is.True, "Level scene load timed out.");
            scene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
            Assert.That(scene.path, Is.EqualTo(ScenePath));
            level = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(levelType, true)).Single();
            progression = Read<LevelProgressionController>(level, "Progression");
            movement = Read<Component>(level, "Movement");
            combat = Read<Component>(level, "Combat");
            interaction = Read<Component>(level, "Interaction");
            follow = Read<Component>(level, "Follow");
            puzzle = Read<Component>(level, "Puzzle");
            hud = level.GetComponent(Type.GetType("ShadowsOfTheForsaken.Level.LevelHud, Assembly-CSharp", true));
            character = movement.GetComponent<CharacterController>();
            enemies = ((IEnumerable)level.GetType().GetField("Enemies").GetValue(level)).Cast<Component>().ToArray();
            Assert.That(enemies.Length, Is.EqualTo(3), "The DOCX route contains first threat, miniboss and final fight.");
            Assert.That(level.GetComponentsInChildren<LevelProgressionController>().Length, Is.EqualTo(1));
            Call(level, "OnApplicationFocus", true);
            Call(level, "OnApplicationPause", false);
            Call(movement, "OnApplicationFocus", true);
            Call(combat, "OnApplicationFocus", true);
            foreach (var enemy in enemies) Call(enemy, "OnApplicationFocus", true);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            PumpInput();
            Assert.That((bool)movement.GetType().GetField("inputUpdated", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(movement),
                Is.True, "Neutral manual input must reach the movement consumer after focus setup.");
            Debug.Log("ForsakenLevelTests setup ready: " + TestContext.CurrentContext.Test.Name);
            yield return fixedUpdate;
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
            Assert.That(Read<int>(combat, "Health"), Is.EqualTo(100));
            Assert.That(Read<bool>(hud, "Ready"), Is.True, "The scene-owned camera Canvas and UI input module must be ready.");
            var hudCanvas = Read<Canvas>(hud, "HudCanvas");
            Assert.That(hudCanvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(hudCanvas.worldCamera, Is.SameAs(follow.GetComponent<Camera>()));
            Assert.That(level.GetComponentsInChildren<Component>(true).Count(component =>
                component != null && component.GetType().FullName == "UnityEngine.EventSystems.EventSystem"), Is.EqualTo(1),
                "The level must own exactly one UI event system.");
            visited.Clear(); screenshots.Clear();
            movementSteps = combatSteps = 0;
            timedJourney = false;
            travelledMetres = 0;
            startedAt = Time.realtimeSinceStartup;
            evidenceName = TestContext.CurrentContext.Test.Name;
            AssertCamera("courtyard spawn");
        }

        [UnityTearDown]
        public IEnumerator UnloadLevel()
        {
            try
            {
                if (scene.IsValid() && scene.isLoaded)
                {
                    var operation = SceneManager.UnloadSceneAsync(scene);
                    float deadline = Time.realtimeSinceStartup + 60;
                    while (operation != null && !operation.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(operation == null || operation.isDone, Is.True, "Level scene unload timed out.");
                }
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.updateMode = previousInputMode;
            }
            finally
            {
                try { inputFixture.TearDown(); }
                finally
                {
                    Time.timeScale = previousTimeScale;
                    foreach (var root in suspendedSceneRoots) if (root != null) root.SetActive(true);
                    suspendedSceneRoots.Clear();
                }
            }
        }

        private void AssertCamera(string waypoint)
        {
            Assert.That(Call(follow, "SnapToTarget"), Is.True, "No safe camera pose at " + waypoint + ": " + movement.transform.position);
            Assert.That(Read<bool>(follow, "HasSafePose"), Is.True, waypoint);
            Assert.That(follow.GetComponent<Camera>().isActiveAndEnabled, Is.True, waypoint);
        }

        private IEnumerator Walk(float x, float z, float expectedY = 0)
        {
            var destination = new Vector3(x, expectedY, z);
            Debug.Log("Forsaken traversal " + evidenceName + ": " + progression.Snapshot.Room + " " + movement.transform.position + " -> " + destination);
            int stationary = 0;
            for (int step = 0; step < 4000; step++)
            {
                if (progression.Snapshot.IsCompleted) break;
                Vector3 offset = destination - movement.transform.position;
                offset.y = 0;
                if (offset.magnitude < .12f) break;
                Assert.That(Read<bool>(level, "Failed"), Is.False, "Player died walking to " + destination);
                Vector3 before = movement.transform.position;
                movement.transform.rotation = Quaternion.LookRotation(offset);
                float distancePerStep = timedJourney ? (float)movement.GetType().GetField("speed").GetValue(movement) * Time.fixedDeltaTime : .35f;
                character.Move(offset.normalized * Mathf.Min(distancePerStep, offset.magnitude) + Vector3.down * .2f);
                movementSteps++;
                yield return fixedUpdate; // The engine dispatches actual trigger contacts here.
                Vector3 moved = movement.transform.position - before;
                moved.y = 0;
                travelledMetres += moved.magnitude;
                stationary = (movement.transform.position - before).sqrMagnitude < .0001f ? stationary + 1 : 0;
                Assert.That(stationary, Is.LessThan(16), "Geometry blocks route to " + destination + " at " + movement.transform.position);
            }
            for (int settle = 0; settle < 12 && !character.isGrounded; settle++)
            {
                character.Move(Vector3.down * .2f);
                yield return fixedUpdate;
            }
            Vector3 actual = movement.transform.position;
            Assert.That(Vector2.Distance(new Vector2(actual.x, actual.z), new Vector2(x, z)), Is.LessThan(.3f), "Did not reach " + destination);
            Assert.That(actual.y, Is.EqualTo(expectedY).Within(.4f), "Unexpected floor height at " + destination);
            AssertCamera(destination.ToString());
            visited.Add(progression.Snapshot.Room + " " + actual.ToString("F2"));
        }

        private IEnumerator Fight(LevelRoom room)
        {
            Assert.That(progression.Snapshot.Room, Is.EqualTo(room), "Encounter must be reached by its real doorway.");
            var enemy = enemies.Single(candidate => Read<LevelRoom>(candidate, "Room") == room);
            int previousHealth = Read<int>(enemy, "Health");
            for (int swing = 0; swing < 20 && !Read<bool>(enemy, "IsDead"); swing++)
            {
                float stepLimit = timedJourney ? (float)movement.GetType().GetField("speed").GetValue(movement) * Time.fixedDeltaTime : .25f;
                Vector3 initialOffset = enemy.transform.position - movement.transform.position;
                initialOffset.y = 0;
                LogCombatState(enemy, "before acquisition " + swing);
                // The final chamber starts farther away than the first encounter. Do not assume the enemy closes that distance.
                int maximumApproachSteps = Mathf.Max(200, Mathf.CeilToInt((initialOffset.magnitude + 4) / stepLimit) + 120);
                for (int approach = 0; approach < maximumApproachSteps; approach++)
                {
                    Vector3 offset = enemy.transform.position - movement.transform.position;
                    offset.y = 0;
                    if (offset.magnitude <= 2.12f && offset.magnitude >= 2.0f) break;
                    Vector3 direction = offset.normalized;
                    float correction = offset.magnitude > 2.12f ? Mathf.Min(stepLimit, offset.magnitude - 2.08f) : -Mathf.Min(stepLimit, 2.08f - offset.magnitude);
                    Vector3 before = movement.transform.position;
                    character.Move(direction * correction + Vector3.down * .1f);
                    yield return fixedUpdate;
                    Vector3 moved = movement.transform.position - before;
                    moved.y = 0;
                    travelledMetres += moved.magnitude;
                    Assert.That(Read<bool>(level, "Failed"), Is.False, "Player died approaching " + enemy.name);
                }
                Vector3 towardEnemy = enemy.transform.position - movement.transform.position;
                towardEnemy.y = 0;
                Assert.That(towardEnemy.magnitude, Is.InRange(2.0f, 2.12f), "Could not physically acquire melee range for " + enemy.name);
                Assert.That(Mathf.Abs(enemy.transform.position.y - movement.transform.position.y), Is.LessThan(1.6f));
                var isClear = Type.GetType("CombatPresentation, Assembly-CSharp", true).GetMethod("IsClear", BindingFlags.Static | BindingFlags.Public);
                Assert.That(isClear.Invoke(null, new object[] { movement.transform, enemy.transform }), Is.True, "Actual combat line of sight must be clear before attacking.");
                Assert.That(Read<bool>(combat, "CanFight"), Is.True);
                Assert.That(combat.GetType().GetProperty("Phase").GetValue(combat).ToString(), Is.EqualTo("Ready"));
                LogCombatState(enemy, "acquired swing " + swing);
                movement.transform.rotation = Quaternion.LookRotation(towardEnemy);
                Call(combat, "RequestAttack");
                Assert.That(combat.GetType().GetProperty("Phase").GetValue(combat).ToString(), Is.Not.EqualTo("Ready"), "The actual attack request must be accepted.");
                for (int tick = 0; tick < (timedJourney ? 200 : 12); tick++)
                {
                    if (!timedJourney)
                    {
                        Call(combat, "Simulate", .05f);
                        foreach (var candidate in enemies) Call(candidate, "Simulate", .05f);
                    }
                    combatSteps++;
                    yield return fixedUpdate;
                    Assert.That(Read<bool>(level, "Failed"), Is.False, "Player died fighting " + enemy.name);
                    if (timedJourney && combat.GetType().GetProperty("Phase").GetValue(combat).ToString() == "Ready") break;
                }
                int currentHealth = Read<int>(enemy, "Health");
                Assert.That(currentHealth, Is.LessThan(previousHealth), "Real melee queries did not hit " + enemy.name + " from " + movement.transform.position);
                previousHealth = currentHealth;
            }
            Assert.That(Read<bool>(enemy, "IsDead"), Is.True, "Encounter was not defeated by player attacks.");
            Assert.That((progression.Snapshot.CompletedObjectives & Read<LevelObjective>(enemy, "Objective")) != 0, Is.True);
            AssertCamera(room + " fight aftermath");
        }

        private void LogCombatState(Component enemy, string stage)
        {
            var visibility = Type.GetType("CombatPresentation, Assembly-CSharp", true).GetMethod("IsClear", BindingFlags.Static | BindingFlags.Public);
            Debug.Log("Forsaken melee " + enemy.name + " " + stage + ": player " + movement.transform.position +
                ", enemy " + enemy.transform.position + ", distance " + Vector3.Distance(movement.transform.position, enemy.transform.position) +
                ", enemy phase " + enemy.GetType().GetProperty("Phase").GetValue(enemy) +
                ", enemy enabled " + ((Behaviour)enemy).isActiveAndEnabled +
                ", enemy focused " + enemy.GetType().GetField("focused", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy) +
                ", enemy paused " + enemy.GetType().GetField("paused", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy) +
                ", player CanFight " + Read<bool>(combat, "CanFight") +
                ", room " + progression.Snapshot.Room + ", session " + progression.Snapshot.SessionId +
                ", LOS " + visibility.Invoke(null, new object[] { movement.transform, enemy.transform }));
        }

        private IEnumerator InteractAt(float x, float z, string expectedTarget)
        {
            yield return Walk(x, z);
            Physics.SyncTransforms();
            Assert.That(Call(interaction, "TryInteract"), Is.True, "E interaction could not reach " + expectedTarget);
            var target = Read<Component>(interaction, "Focused");
            Assert.That(target.name, Is.EqualTo(expectedTarget), "E chose an unexpected target.");
            Assert.That(Read<string>(interaction, "LastFeedback"), Is.Not.Empty);
        }

        private IEnumerator ReachCatacombs()
        {
            Assert.That(Call(level, "TryRestart"), Is.False, "Restart is terminal-only.");
            Guid originalSession = progression.Snapshot.SessionId;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R));
            PumpInput();
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            PumpInput();
            Assert.That(progression.Snapshot.SessionId, Is.EqualTo(originalSession), "R must not reset an ongoing journey.");
            yield return Walk(0, 20);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.FirstEncounter));
            yield return Fight(LevelRoom.FirstEncounter);
            if (timedJourney) yield return Capture("first-encounter");
            yield return Walk(0, 24);
            yield return Walk(-12, 24);
            yield return Walk(-12, 33);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Puzzle));
            yield return InteractAt(-15, 36.2f, "Crescent");
            if (!timedJourney)
            {
                yield return InteractAt(-9, 36.2f, "Flame"); // Wrong input has no permanent cost.
                Assert.That(Read<int>(puzzle, "Progress"), Is.Zero);
                Assert.That((progression.Snapshot.CompletedObjectives & LevelObjective.MainPuzzleSolved) == 0, Is.True);
                yield return InteractAt(-15, 36.2f, "Crescent");
            }
            yield return InteractAt(-12, 36.2f, "Crown");
            yield return InteractAt(-9, 36.2f, "Flame");
            Assert.That(Read<bool>(puzzle, "Solved"), Is.True);
            yield return Walk(-12, 34);
            yield return Walk(-12, 27);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.FirstEncounter));
            yield return Walk(-12, 24);
            yield return Walk(12, 24);
            yield return Walk(12, 44);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.ThroneRoom));
            yield return Fight(LevelRoom.ThroneRoom);
            if (timedJourney) yield return Capture("throne-miniboss");
            yield return Walk(12, 53);
            yield return Walk(24, 53); // Navigate around the solid fallen columns and throne dais.
            yield return Walk(24, 58);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Library));
            if (timedJourney) yield return Capture("cursed-library");
            yield return InteractAt(24, 60.5f, "Forbidden book");
            Assert.That((progression.Snapshot.CompletedObjectives & LevelObjective.LibraryOpened) != 0, Is.True);
            yield return Walk(24, 69);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Catacombs));
        }

        private IEnumerator FinishLevel()
        {
            yield return Walk(24, 72);
            yield return Walk(12, 72);
            yield return Walk(12, 82);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.FinalArena));
            Assert.That(progression.IsPassageOpen(LevelRoom.FinalArena, LevelRoom.Exit), Is.False);
            Assert.That(progression.Snapshot.IsCompleted, Is.False);
            yield return Fight(LevelRoom.FinalArena);
            Assert.That(progression.IsPassageOpen(LevelRoom.FinalArena, LevelRoom.Exit), Is.True);
            yield return Walk(12, 102.3f);
            Assert.That(progression.Snapshot.IsCompleted, Is.True);
            Assert.That(Read<bool>(movement, "ControlsEnabled"), Is.False);
        }

        private IEnumerator AssertReset(Guid oldSession, bool viaButton)
        {
            Call(hud, "LateUpdate");
            Assert.That(Read<bool>(hud, "Ready"), Is.True);
            var restartButton = Read<Component>(hud, "RestartButton");
            Assert.That(((Behaviour)restartButton).isActiveAndEnabled, Is.True, "The real terminal restart button must be visible.");
            Assert.That(Read<bool>(restartButton, "interactable"), Is.True);
            if (viaButton)
            {
                // Queue a genuine R callback from the old session, then restart through the Button before Update.
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R));
                PumpInput();
                var restart = (InputAction)level.GetType().GetField("restart", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(level);
                Assert.That(restart.WasPerformedThisFrame(), Is.True);
                // Invoke the real Button's UnityEvent and its production listener, rather than calling TryRestart.
                var click = restartButton.GetType().GetProperty("onClick").GetValue(restartButton);
                click.GetType().GetMethod("Invoke", Type.EmptyTypes).Invoke(click, null);
                Guid buttonSession = progression.Snapshot.SessionId;
                Assert.That(buttonSession, Is.Not.EqualTo(oldSession), "The real Button listener must reset the terminal session.");
                yield return null;
                Assert.That(progression.Snapshot.SessionId, Is.EqualTo(buttonSession), "The pending R callback captured before the Button reset must not reset the new session.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                PumpInput();
            }
            else
            {
                // Unfocused input, paused input, and a focus loss after queuing all reject a real delivered R press.
                for (int guard = 0; guard < 3; guard++)
                {
                    Call(level, "OnApplicationFocus", guard != 0);
                    Call(level, "OnApplicationPause", guard == 1);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R));
                    PumpInput();
                    if (guard == 2) Call(level, "OnApplicationFocus", false);
                    yield return null;
                    Assert.That(progression.Snapshot.SessionId, Is.EqualTo(oldSession), "Focus and pause guards must reject terminal R, including a queued press before focus loss.");
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    PumpInput();
                    Call(level, "OnApplicationPause", false);
                    Call(level, "OnApplicationFocus", true);
                    yield return null;
                    Assert.That(progression.Snapshot.SessionId, Is.EqualTo(oldSession), "Restoring focus must not replay a blocked R press.");
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R));
                PumpInput();
                var restart = (InputAction)level.GetType().GetField("restart", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(level);
                Assert.That(restart.WasPerformedThisFrame(), Is.True, "The actual R action must receive the terminal press.");
                yield return null; // ForsakenLevel.Update performs the queued production restart.
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                PumpInput();
            }
            Assert.That(progression.Snapshot.SessionId, Is.Not.EqualTo(oldSession));
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(Read<int>(combat, "Health"), Is.EqualTo(Read<int>(combat, "MaximumHealth")));
            Assert.That(Read<bool>(combat, "IsDead"), Is.False);
            Assert.That(Read<bool>(level, "Failed"), Is.False);
            Assert.That(Read<bool>(movement, "ControlsEnabled"), Is.True);
            Assert.That(Read<int>(puzzle, "Progress"), Is.Zero);
            Assert.That(Read<bool>(puzzle, "Solved"), Is.False);
            Assert.That(Vector3.Distance(movement.transform.position, new Vector3(0, .08f, -51)), Is.LessThan(.2f));
            foreach (var enemy in enemies)
            {
                Assert.That(Read<int>(enemy, "Health"), Is.EqualTo(Read<int>(enemy, "MaximumHealth")), enemy.name);
                Assert.That(Read<bool>(enemy, "IsDead"), Is.False, enemy.name);
                Assert.That(enemy.GetComponent<CharacterController>().enabled, Is.True, enemy.name);
            }
            var gateType = Type.GetType("ShadowsOfTheForsaken.Level.PassageGate, Assembly-CSharp", true);
            var gates = level.GetComponentsInChildren(gateType, true);
            Assert.That(gates.Length, Is.EqualTo(10)); // Nine logical passages plus the shortcut entrance seal.
            Assert.That(gates.Count(gate => Read<bool>(gate, "IsOpen")), Is.EqualTo(1));
            Assert.That(Call(combat, "ReceiveDamage", 100, oldSession), Is.False);
            Call(hud, "LateUpdate");
            Assert.That(((Behaviour)restartButton).isActiveAndEnabled, Is.False, "Restart returns to the ordinary HUD.");
            Assert.That(Read<bool>(hud, "Ready"), Is.True);
            AssertCamera("restart spawn");
        }

        [UnityTest]
        public IEnumerator MainRouteWinsWithoutSecretAndVictoryRestartRestoresTheWholeScene()
        {
            timedJourney = true;
            yield return Capture("courtyard-introduction");
            yield return ReachCatacombs();
            yield return Capture("catacombs-main-route");
            yield return FinishLevel();
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelProgression.RequiredObjectives));
            Assert.That(Read<int>(combat, "Health"), Is.GreaterThan(0), "The timed route must finish with the normal vulnerable player alive.");
            yield return Capture("exit-main-route");
            SaveEvidence();
            Assert.That(Read<float>(level, "ElapsedSeconds"), Is.InRange(120f, 180f),
                "Configured-speed automated basic route missed the DOCX timing target. This is automated timing evidence, not a human playtest.");
            yield return AssertReset(progression.Snapshot.SessionId, true);
        }

        [UnityTest]
        public IEnumerator OptionalRelicAndPhysicalConcealedRampReturnStillRequireTheFinalFight()
        {
            yield return ReachCatacombs();
            yield return InteractAt(26, 71.5f, "Concealed cult lever");
            Assert.That(progression.IsPassageOpen(LevelRoom.Catacombs, LevelRoom.BonusRoom), Is.True);
            yield return Walk(24, 72);
            yield return Walk(-12, 72);
            yield return Walk(-12, 82);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.BonusRoom));
            yield return InteractAt(-12, 94, "Relic of the fallen kingdom");
            yield return Capture("bonus-relic");
            yield return Walk(-8, 94);
            yield return Walk(-8, 96);
            yield return Walk(-4.5f, 96);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.ThroneRoom), "The actual concealed return crossing must lead to the throne route.");
            yield return Walk(0, 96);
            yield return Walk(0, 80, -6);
            yield return Walk(-6, 80, -6);
            yield return Walk(-6, 50, -6);
            yield return Walk(-6, 48, -6);
            yield return Walk(8, 48);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.ThroneRoom));
            yield return Capture("throne-secret-return");
            yield return Walk(8, 53);
            yield return Walk(24, 53);
            yield return Walk(24, 58);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Library));
            yield return Walk(24, 69);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Catacombs));
            Assert.That((progression.Snapshot.CompletedObjectives & LevelObjective.FinalEnemyDefeated) == 0, Is.True);
            yield return FinishLevel();
            Assert.That((progression.Snapshot.CompletedObjectives & (LevelObjective.SecretLeverPulled | LevelObjective.BonusDiscovered)),
                Is.EqualTo(LevelObjective.SecretLeverPulled | LevelObjective.BonusDiscovered));
            yield return Capture("exit-with-relic");
            SaveEvidence();
        }

        [UnityTest]
        public IEnumerator EnemyInflictedDefeatAndRestartRestoreHealthFoesAndRejectOldDamage()
        {
            yield return Walk(0, 20); // Wait within the arena; do not try to walk through the living demon's collider.
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.FirstEncounter));
            var enemy = enemies.Single(candidate => Read<LevelRoom>(candidate, "Room") == LevelRoom.FirstEncounter);
            var session = progression.Snapshot.SessionId;
            for (int tick = 0; tick < 600 && !Read<bool>(level, "Failed"); tick++)
            {
                Call(enemy, "Simulate", .1f);
                combatSteps++;
                yield return fixedUpdate;
            }
            Assert.That(Read<bool>(level, "Failed"), Is.True, "The actual first enemy must be able to defeat an idle player.");
            Assert.That(Read<int>(combat, "Health"), Is.Zero);
            Assert.That(Read<bool>(movement, "ControlsEnabled"), Is.False);
            Assert.That(progression.Snapshot.IsCompleted, Is.False);
            yield return Capture("defeat-before-restart");
            SaveEvidence();
            yield return AssertReset(session, false);
        }

        [UnityTest]
        public IEnumerator ClosedPuzzleGateStopsPhysicalJumpCrossingsBeforeFirstEnemyDefeat()
        {
            yield return Walk(0, 21);
            yield return Walk(-12, 24);
            yield return Walk(-12, 28);
            float startHeight = movement.transform.position.y;
            Call(movement, "ResetMotion"); // This fixture supplies the entire physical jump, including gravity.
            float maximumHeight = startHeight;
            float verticalSpeed = 5;
            for (int tick = 0; tick < 80; tick++)
            {
                verticalSpeed -= 9.81f * Time.fixedDeltaTime;
                character.Move(Vector3.forward * .13f + Vector3.up * verticalSpeed * Time.fixedDeltaTime);
                movementSteps++;
                maximumHeight = Mathf.Max(maximumHeight, movement.transform.position.y);
                yield return fixedUpdate;
            }
            Assert.That(maximumHeight, Is.GreaterThan(startHeight + .5f), "The fixture must actually attempt an airborne crossing.");
            Assert.That(movement.transform.position.z, Is.LessThan(30));
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.FirstEncounter));
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            AssertCamera("closed puzzle door");
            SaveEvidence();
        }

        private string EvidenceDirectory => Path.Combine(Path.GetDirectoryName(Application.dataPath), "artifacts", "level-completion", "traversal");

        private IEnumerator Capture(string suffix)
        {
            yield return null;
            yield return null;
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null), "Scene delivery tests require rendered visual evidence.");
            Directory.CreateDirectory(EvidenceDirectory);
            var camera = follow.GetComponent<Camera>();
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var destination = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1, useMipMap = false, autoGenerateMips = false
            };
            Texture2D image = null;
            try
            {
                Assert.That(destination.Create(), Is.True, "Could not create the visual-evidence render target.");
                camera.targetTexture = destination;
                Canvas.ForceUpdateCanvases();
                Assert.That(Read<bool>(hud, "Ready"), Is.True);
                Vector2 healthPoint = Read<Vector2>(hud, "HealthBarViewportPoint");
                // URP 17's supported synchronous request renders in batch mode without end-of-frame coroutines.
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = destination });
                RenderTexture.active = destination;
                image = new Texture2D(destination.width, destination.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, destination.width, destination.height), 0, 0);
                image.Apply();
                string path = Path.Combine(EvidenceDirectory, evidenceName + "-" + suffix + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                screenshots.Add(path);
                var pixels = image.GetPixels32();
                var first = pixels[0];
                Assert.That(pixels.Where((pixel, index) => index % 64 == 0).Any(pixel =>
                    Math.Abs(pixel.r - first.r) + Math.Abs(pixel.g - first.g) + Math.Abs(pixel.b - first.b) > 8),
                    Is.True, "The camera request produced a uniform image; visual evidence is invalid.");
                if (Read<int>(combat, "Health") > 0)
                {
                    Assert.That(healthPoint.x, Is.InRange(0f, 1f));
                    Assert.That(healthPoint.y, Is.InRange(0f, 1f));
                    Color32 healthPixel = image.GetPixel(Mathf.Clamp(Mathf.RoundToInt(healthPoint.x * destination.width), 0, destination.width - 1),
                        Mathf.Clamp(Mathf.RoundToInt(healthPoint.y * destination.height), 0, destination.height - 1));
                    Assert.That(healthPixel.r, Is.GreaterThan(40));
                    Assert.That(healthPixel.r, Is.GreaterThan(healthPixel.g * 1.5f), "The actual health graphic must be present in the camera PNG.");
                    Assert.That(healthPixel.r, Is.GreaterThan(healthPixel.b * 1.5f), "The actual health graphic must be present in the camera PNG.");
                }
            }
            finally
            {
                camera.targetTexture = previousTarget;
                Canvas.ForceUpdateCanvases();
                RenderTexture.active = previousActive;
                if (image != null) Object.Destroy(image);
                destination.Release();
                Object.Destroy(destination);
            }
        }

        [Serializable]
        private sealed class TraversalEvidence
        {
            public string test, unityVersion, graphicsDevice, room;
            public string[] waypoints, screenshots;
            public int movementSteps, combatSteps, health;
            public float realElapsedSeconds, gameElapsedSeconds;
            public float travelledMetres, configuredSpeedMetresPerSecond;
            public bool completed;
            public bool configuredSpeedTraversal;
            public string method;
            public string captureMethod = "URP SingleCameraRequest to 1280x720 RenderTexture plus ReadPixels. Includes the scene-owned ScreenSpaceCamera UGUI HUD; living-player captures verify the actual health graphic's pixels.";
        }

        private void SaveEvidence()
        {
            Directory.CreateDirectory(EvidenceDirectory);
            var data = new TraversalEvidence
            {
                test = evidenceName, unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                room = progression.Snapshot.Room.ToString(), waypoints = visited.ToArray(), screenshots = screenshots.ToArray(),
                movementSteps = movementSteps, combatSteps = combatSteps, health = Read<int>(combat, "Health"),
                realElapsedSeconds = Time.realtimeSinceStartup - startedAt, gameElapsedSeconds = Read<float>(level, "ElapsedSeconds"),
                completed = progression.Snapshot.IsCompleted, travelledMetres = travelledMetres,
                configuredSpeedMetresPerSecond = (float)movement.GetType().GetField("speed").GetValue(movement), configuredSpeedTraversal = timedJourney,
                method = timedJourney ? "Automated CharacterController.Move at configured speed times real Time.fixedDeltaTime, actual doorway trigger callbacks, E range/LOS selection, actual Update-driven player/enemy attack timing. No direct progression grants or compulsory waits. Automated timing evidence, not human/manual acceptance or a benchmark."
                    : "Accelerated CharacterController.Move (maximum 0.35m per real FixedUpdate), actual doorway trigger callbacks, E range/LOS selection and player/enemy melee simulation. No direct progression grants. Regression harness; not human/manual timing acceptance or a benchmark."
            };
            File.WriteAllText(Path.Combine(EvidenceDirectory, evidenceName + ".json"), JsonUtility.ToJson(data, true));
        }
    }
}
