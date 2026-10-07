using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public sealed class LevelSessionTests
    {
        private const float Frame = 1f / 60;
        private readonly Vector3 origin = new Vector3(3000, 0, 3000);
        private readonly List<Behaviour> suspended = new List<Behaviour>();
        private Scene scene;
        private LevelProgressionController progression;
        private Component session, movement, health, melee, combat, interactor, enemyHealth, enemyMelee;
        private CharacterController character;
        private Transform spawn;
        private Keyboard keyboard;
        private InputActionAsset input;
        private InputSettings.UpdateMode previousMode;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
        private float previousTimeScale;

        [SetUp]
        public void CreateSessionWithRealControlsAndPhysics()
        {
            previousMode = InputSystem.settings.updateMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousTimeScale = Time.timeScale;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Time.timeScale = 1;
            foreach (var behaviour in Object.FindObjectsByType<Behaviour>())
                if (behaviour.isActiveAndEnabled && (behaviour.GetType().Name == "PlayerMovement" ||
                    behaviour.GetType().Name == "EnemyEncounter" || behaviour.GetType().Name == "LevelRoomTrigger"))
                {
                    suspended.Add(behaviour);
                    behaviour.enabled = false;
                }
            scene = SceneManager.CreateScene("Session fixture " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
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
            map.AddAction("Restart", InputActionType.Button, "<Keyboard>/r");
            progression = Make("Progression", Vector3.zero).AddComponent<LevelProgressionController>();
            Box("Floor", new Vector3(0, -.25f, 5), new Vector3(20, .5f, 30));
            spawn = Make("Safe spawn", new Vector3(0, .05f, 0)).transform;
            var player = Make("Player", new Vector3(0, .05f, 0));
            player.SetActive(false);
            movement = player.AddComponent(RuntimeType("PlayerMovement"));
            Set(movement, "inputActions", input);
            character = player.GetComponent<CharacterController>();
            character.height = 2; character.center = Vector3.up; character.radius = .3f; character.skinWidth = .02f;
            health = player.AddComponent(RuntimeType("ShadowsOfTheForsaken.Combat.CombatHealth"));
            Set(health, "progression", progression);
            melee = player.AddComponent(RuntimeType("ShadowsOfTheForsaken.Combat.MeleeCombat"));
            combat = player.AddComponent(RuntimeType("PlayerCombat"));
            interactor = player.AddComponent(RuntimeType("PlayerInteractor"));
            Set(interactor, "progression", progression);
            player.SetActive(true);
            Call(movement, "OnApplicationFocus", true); Call(combat, "OnApplicationFocus", true);
            var enemy = Make("Bound actor", new Vector3(5, 0, 6));
            enemy.SetActive(false);
            enemy.AddComponent<BoxCollider>().center = Vector3.up;
            enemyHealth = enemy.AddComponent(RuntimeType("ShadowsOfTheForsaken.Combat.CombatHealth"));
            Set(enemyHealth, "progression", progression);
            enemyMelee = enemy.AddComponent(RuntimeType("ShadowsOfTheForsaken.Combat.MeleeCombat"));
            enemy.SetActive(true);
            var sessionObject = Make("Level session", Vector3.zero);
            sessionObject.SetActive(false);
            session = sessionObject.AddComponent(RuntimeType("LevelSessionController"));
            Set(session, "progression", progression); Set(session, "player", movement);
            Set(session, "playerHealth", health); Set(session, "spawn", spawn);
            sessionObject.SetActive(true);
            Assert.That((bool)Call(session, "InitializeSession"), Is.True);
            Call(session, "OnApplicationFocus", true);
            Keys();
            Assert.That(InputState.currentUpdateType, Is.EqualTo(InputUpdateType.Manual));
            Step(3);
        }

        [UnityTearDown]
        public IEnumerator RemoveFixtureAndRestoreInput()
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                var operation = SceneManager.UnloadSceneAsync(scene);
                float deadline = Time.realtimeSinceStartup + 15;
                while (operation != null && !operation.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(operation == null || operation.isDone, Is.True);
            }
            if (input != null) Object.DestroyImmediate(input);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = previousMode;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            Time.timeScale = previousTimeScale;
            foreach (var behaviour in suspended) if (behaviour != null) behaviour.enabled = true;
            suspended.Clear();
        }

        [Test]
        public void SessionEvidenceRecoversFromSnapshotFileContentionAndStopsAfterPersistentFailure()
        {
            const string variable = "SHADOWS_PLAYER_EVIDENCE_DIR";
            string previousDirectory = Environment.GetEnvironmentVariable(variable);
            string directory = Path.Combine(Path.GetTempPath(), "ShadowsEvidenceTest-" + Guid.NewGuid().ToString("N"));
            Component evidence = null;
            try
            {
                Directory.CreateDirectory(directory);
                Environment.SetEnvironmentVariable(variable, directory);
                evidence = session.gameObject.AddComponent(RuntimeType("PlayerValidationEvidence"));
                Set(evidence, "session", session);
                Call(evidence, "Start");
                string status = Path.Combine(directory, "status.json");
                string temporary = Path.Combine(directory, ".status.tmp");
                string original = File.ReadAllText(status);
                Guid token = progression.Snapshot.SessionId;
                int currentHealth = Get<int>(health, "Current");
                using (new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    for (int i = 0; i < 3; i++) Call(evidence, "WriteSnapshot");
                    Assert.That(((Behaviour)evidence).enabled, Is.True);
                    Assert.That(File.ReadAllText(status), Is.EqualTo(original), "A failed write must retain the last complete snapshot.");
                }
                Call(evidence, "WriteSnapshot");
                string recovered = File.ReadAllText(status);
                StringAssert.Contains("\"ioFailureCount\":3", recovered);
                StringAssert.Contains("\"lastIoOperation\":\"snapshot-write\"", recovered);
                StringAssert.Contains("\"lastIoHResult\":\"0x", recovered);
                Assert.That(recovered, Is.Not.EqualTo(original));
                using (new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    for (int i = 0; i < 9; i++) Call(evidence, "WriteSnapshot");
                    Assert.That(((Behaviour)evidence).enabled, Is.True, "Successful publication must reset the consecutive failure budget.");
                    LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                        @"Local player validation evidence disabled after snapshot-write: IOException \(0x[0-9A-F]{8}\)\."));
                    Call(evidence, "WriteSnapshot");
                    Assert.That(((Behaviour)evidence).enabled, Is.False);
                    Assert.That(File.ReadAllText(status), Is.EqualTo(recovered));
                }
                Assert.That(progression.Snapshot.SessionId, Is.EqualTo(token));
                Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
                Assert.That(Get<int>(health, "Current"), Is.EqualTo(currentHealth));
                Assert.That(State, Is.EqualTo("Running"));
            }
            finally
            {
                if (evidence != null) Object.DestroyImmediate(evidence);
                Environment.SetEnvironmentVariable(variable, previousDirectory);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SessionRunningRejectsRestartAndPreservesDormantDamageLock()
        {
            Guid token = progression.Snapshot.SessionId;
            Assert.That((bool)Call(session, "RestartSession"), Is.False);
            Call(enemyHealth, "SetEncounterDamageEnabled", false);
            ((Behaviour)session).enabled = false; ((Behaviour)session).enabled = true;
            Assert.That(State, Is.EqualTo("Running"));
            Assert.That(Get<bool>(health, "DamageEnabled"), Is.True);
            Assert.That(Get<bool>(enemyHealth, "DamageEnabled"), Is.False);
            Assert.That(Damage(enemyHealth, 10), Is.False);
            Assert.That((bool)Call(enemyMelee, "TryAttack"), Is.False);
            Keys(Key.R); Step(3);
            Assert.That(progression.Snapshot.SessionId, Is.EqualTo(token));
        }

        [Test]
        public void SessionDeathStopsMovementDamageAttacksAndRejectsExit()
        {
            Assert.That(Damage(health, 1000), Is.True);
            Assert.That(State, Is.EqualTo("Defeated"));
            Vector3 position = movement.transform.position;
            Keys(Key.W, Key.Space, Key.Enter, Key.E); Step(15);
            Assert.That(Vector3.Distance(position, movement.transform.position), Is.LessThan(.001f));
            Assert.That(Get<bool>(movement, "ControlsEnabled"), Is.False);
            Assert.That((bool)Call(enemyMelee, "TryAttack"), Is.False);
            Assert.That(Damage(enemyHealth, 1), Is.False);
            Assert.That((bool)Call(session, "TryEnterRoom", LevelRoom.Exit), Is.False);
            Assert.That(progression.Snapshot.IsCompleted, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SessionExitRequiresEveryObjectiveAndLivingPlayer(bool defeated)
        {
            Assert.That((bool)Call(session, "TryEnterRoom", LevelRoom.Exit), Is.False);
            CompleteRoutePrerequisites(false);
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.FinalArena));
            if (defeated) Damage(health, 1000);
            Assert.That((bool)Call(session, "TryEnterRoom", LevelRoom.Exit), Is.EqualTo(!defeated));
            Assert.That(State, Is.EqualTo(defeated ? "Defeated" : "Completed"));
            Assert.That(progression.Snapshot.IsCompleted, Is.EqualTo(!defeated));
            Assert.That((bool)Call(session, "TryEnterRoom", LevelRoom.Exit), Is.False, "A terminal exit cannot publish completion again.");
            Assert.That(Damage(health, 1), Is.False);
            Assert.That((bool)Call(enemyMelee, "TryAttack"), Is.False);
        }

        [Test]
        public void SessionRestartRequiresFreshTerminalRAndClearsHeldGameplayInput()
        {
            Guid previous = progression.Snapshot.SessionId;
            Keys(Key.R); Step(1);
            Damage(health, 1000);
            Keys(Key.R); Step(4);
            Assert.That(State, Is.EqualTo("Defeated"));
            Assert.That(progression.Snapshot.SessionId, Is.EqualTo(previous));
            Keys(); Step(1);
            Keys(Key.R, Key.W, Key.Enter); Step(1);
            Assert.That(State, Is.EqualTo("Running"));
            Assert.That(progression.Snapshot.SessionId, Is.Not.EqualTo(previous));
            Vector3 position = movement.transform.position;
            Keys(Key.W, Key.Enter); Step(10);
            Assert.That(new Vector2(movement.transform.position.x - position.x, movement.transform.position.z - position.z).magnitude,
                Is.LessThan(.001f));
            Assert.That(Get<object>(melee, "Phase").ToString(), Is.EqualTo("Ready"));
            Keys(); Step(1); Keys(Key.W); Step(12);
            Assert.That(movement.transform.position.z, Is.GreaterThan(position.z + .5f));
        }

        [TestCase("focus")]
        [TestCase("pause")]
        public void SessionRestartRequiresReleaseAfterFocusOrPause(string reason)
        {
            Damage(health, 1000);
            string callback = reason == "focus" ? "OnApplicationFocus" : "OnApplicationPause";
            Call(movement, callback, reason != "focus");
            Keys(); Step(1); Keys(Key.R); Step(1);
            Assert.That(State, Is.EqualTo("Defeated"));
            Call(movement, callback, reason == "focus");
            Keys(Key.R); Step(1);
            Assert.That(State, Is.EqualTo("Defeated"));
            Keys(); Step(1); Keys(Key.R); Step(1);
            Assert.That(State, Is.EqualTo("Running"));
        }

        [Test]
        public void SessionRepeatedRestartsCreateExactlyOneNewSessionEach()
        {
            int resets = 0;
            progression.Changed += change => { if (change.Kind == ProgressionChangeKind.SessionReset) resets++; };
            var tokens = new HashSet<Guid> { progression.Snapshot.SessionId };
            for (int i = 0; i < 3; i++)
            {
                ((Behaviour)session).enabled = false; ((Behaviour)session).enabled = true;
                Damage(health, 1000);
                Keys(); Step(1); Keys(Key.R); Step(1);
                Assert.That(State, Is.EqualTo("Running"));
                Assert.That(tokens.Add(progression.Snapshot.SessionId), Is.True);
                Assert.That(Get<int>(health, "Current"), Is.EqualTo(Get<int>(health, "Maximum")));
                Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
                Assert.That(resets, Is.EqualTo(i + 1));
            }
        }

        [Test]
        public void SessionRoomVolumeRejectsLowerPassageAndTracksRealMovement()
        {
            var region = Make("First room volume", new Vector3(0, 1.5f, 4));
            var box = region.AddComponent<BoxCollider>(); box.size = new Vector3(4, 3, 2); box.isTrigger = true;
            var trigger = region.AddComponent(RuntimeType("LevelRoomTrigger"));
            Set(trigger, "session", session); Set(trigger, "room", LevelRoom.FirstEncounter);
            character.enabled = false; movement.transform.position = origin + new Vector3(0, -10, 4); character.enabled = true;
            Assert.That((bool)Call(trigger, "EvaluatePlayerPosition"), Is.False, "A lower corridor must not enter the room above it.");
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
            character.enabled = false; movement.transform.position = spawn.position; character.enabled = true;
            Call(movement, "ResetMotion"); Keys(); Step(1); Keys(Key.W);
            for (int i = 0; i < 46; i++)
            {
                Step(1);
                Call(trigger, "EvaluatePlayerPosition");
            }
            Assert.That(movement.transform.position.z - origin.z, Is.InRange(3.4f, 4.2f));
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.FirstEncounter));
            Keys(Key.S);
            var courtyard = Make("Courtyard volume", new Vector3(0, 1.5f, 0));
            courtyard.AddComponent<BoxCollider>().size = new Vector3(4, 3, 4);
            var returnTrigger = courtyard.AddComponent(RuntimeType("LevelRoomTrigger"));
            Set(returnTrigger, "session", session); Set(returnTrigger, "room", LevelRoom.Courtyard);
            for (int i = 0; i < 30; i++) { Step(1); Call(returnTrigger, "EvaluatePlayerPosition"); }
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
        }

        [Test]
        public void SessionResetRelocatesPlayerBeforeClosingOccupiedGate()
        {
            var panel = Box("Puzzle gate", new Vector3(0, 1.5f, 3), new Vector3(4, 3, .4f));
            panel.SetActive(false);
            var gate = panel.AddComponent(RuntimeType("ProgressionGate"));
            Set(gate, "progression", progression);
            panel.SetActive(true);
            Assert.That((bool)Call(session, "TryEnterRoom", LevelRoom.FirstEncounter), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.FirstEnemyDefeated, progression.Snapshot.SessionId), Is.True);
            Keys(Key.W); Step(36);
            Assert.That(Mathf.Abs(movement.transform.position.z - origin.z - 3), Is.LessThan(.2f));
            Assert.That(Get<bool>(gate, "IsOpen"), Is.True);
            Guid oldSession = progression.Snapshot.SessionId, oldLife = Get<Guid>(health, "LifeId");
            Damage(health, 1000);
            Assert.That((bool)Call(session, "RestartSession"), Is.True);
            Assert.That(Get<bool>(gate, "IsOpen"), Is.False);
            Assert.That(panel.GetComponent<Collider>().enabled, Is.True);
            Assert.That(character.bounds.Intersects(panel.GetComponent<Collider>().bounds), Is.False);
            Assert.That(Vector3.Distance(movement.transform.position, spawn.position), Is.LessThan(.001f));
            Assert.That((bool)Call(health, "TryDamage", 1000, oldLife, oldSession), Is.False);
        }

        [UnityTest]
        public IEnumerator SessionExternalResetRestoresWorldAfterNotification()
        {
            Keys(Key.W); Step(20); Keys();
            Assert.That(movement.transform.position.z - origin.z, Is.GreaterThan(1));
            Assert.That(progression.TryResetSession(), Is.True);
            Guid token = progression.Snapshot.SessionId;
            Assert.That(State, Is.EqualTo("Resetting"));
            Assert.That(Get<bool>(movement, "ControlsEnabled"), Is.False);
            yield return null;
            Assert.That(State, Is.EqualTo("Running"));
            Assert.That(progression.Snapshot.SessionId, Is.EqualTo(token), "World restoration must not reset progression inside or after its notification again.");
            Assert.That(Vector3.Distance(movement.transform.position, spawn.position), Is.LessThan(.06f));
        }

        [UnityTest]
        public IEnumerator SessionStaleDeathNotificationCannotDefeatRestoredWorld()
        {
            Guid previous = progression.Snapshot.SessionId;
            Action<HealthChange> resetOnLethalChange = change =>
            {
                if (change.IsDeath) Assert.That(progression.TryResetSession(), Is.True);
            };
            health.GetType().GetEvent("Changed").AddEventHandler(health, resetOnLethalChange);
            Assert.That(Damage(health, 1000), Is.True);
            Assert.That(progression.Snapshot.SessionId, Is.Not.EqualTo(previous));
            Assert.That(State, Is.EqualTo("Resetting"));
            yield return null;
            Assert.That(State, Is.EqualTo("Running"));
            Assert.That(Get<bool>(health, "IsAlive"), Is.True);
            health.GetType().GetEvent("Changed").RemoveEventHandler(health, resetOnLethalChange);
        }

        [UnityTest]
        public IEnumerator SessionTimerExcludesFocusLossAndApplicationPause()
        {
            yield return null;
            foreach (string callback in new[] { "OnApplicationFocus", "OnApplicationPause" })
            {
                bool suspend = callback == "OnApplicationPause";
                Call(session, callback, suspend);
                float before = Get<float>(session, "ElapsedSeconds");
                yield return new WaitForSecondsRealtime(.05f);
                Assert.That(Get<float>(session, "ElapsedSeconds"), Is.EqualTo(before));
                Call(session, callback, !suspend);
                Call(session, "Update");
                Assert.That(Get<float>(session, "ElapsedSeconds"), Is.EqualTo(before),
                    "The first resumed frame must establish a new baseline, not charge the suspended interval.");
                yield return null;
            }
        }

        [Test]
        public void SessionRestartAfterVictoryClearsMandatoryAndOptionalProgress()
        {
            CompleteRoutePrerequisites(true);
            Assert.That((bool)Call(session, "TryEnterRoom", LevelRoom.Exit), Is.True);
            Guid previous = progression.Snapshot.SessionId;
            Assert.That((bool)Call(session, "RestartSession"), Is.True);
            Assert.That(State, Is.EqualTo("Running"));
            Assert.That(progression.Snapshot.SessionId, Is.Not.EqualTo(previous));
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(Get<bool>(movement, "ControlsEnabled"), Is.True);
            Assert.That(Get<bool>(health, "DamageEnabled"), Is.True);
        }

        [Test]
        public void SessionHealthRestorationCannotOverrideTerminalLocks()
        {
            Damage(health, 1000);
            Call(health, "ResetHealth");
            Assert.That(Get<bool>(health, "IsAlive"), Is.True);
            Assert.That(State, Is.EqualTo("Defeated"));
            Assert.That(Get<bool>(movement, "ControlsEnabled"), Is.False);
            Assert.That(Get<bool>(health, "DamageEnabled"), Is.False);
            Assert.That((bool)Call(melee, "TryAttack"), Is.False);
            Assert.That((bool)Call(session, "TryEnterRoom", LevelRoom.FirstEncounter), Is.False);
            Assert.That((bool)Call(session, "RestartSession"), Is.True);
            Assert.That(Get<bool>(movement, "ControlsEnabled"), Is.True);
            Assert.That(Get<bool>(health, "DamageEnabled"), Is.True);
        }

        [Test]
        public void SessionHUDShowsHealthSelectionAndRelicCompletion()
        {
            var hud = MakeHUD();
            var targetObject = Box("Marked lever", new Vector3(0, 1, 1.5f), Vector3.one * .5f);
            targetObject.SetActive(false);
            var target = targetObject.AddComponent(RuntimeType("InteractionTarget"));
            Set(target, "progression", progression); Set(target, "prompt", "Pull marked lever");
            targetObject.SetActive(true);
            Call(interactor, "RefreshTarget");
            Assert.That(Get<string>(hud, "PromptText"), Is.EqualTo("[E] Pull marked lever"));
            Damage(health, 25);
            StringAssert.Contains("75 / 100", Get<string>(hud, "HealthText"));
            Object.DestroyImmediate(targetObject);
            Call(interactor, "RefreshTarget");
            Assert.That(Get<string>(hud, "PromptText"), Is.Empty);
            CompleteRoutePrerequisites(true);
            Assert.That((bool)Call(session, "TryEnterRoom", LevelRoom.Exit), Is.True);
            StringAssert.Contains("Hidden relic discovered", Get<string>(hud, "OutcomeText"));
            var canvas = (Canvas)Get<object>(hud, "DisplayCanvas");
            Assert.That(canvas.isActiveAndEnabled, Is.True);
            Assert.That(canvas.GetComponentsInChildren<CanvasRenderer>().Length, Is.GreaterThan(4));
        }

        [Test]
        public void SessionHUDRestartButtonOnlyRestartsTerminalSession()
        {
            var hud = MakeHUD();
            var button = hud.GetComponentsInChildren<Component>(true).Single(component => component != null && component.GetType().FullName == "UnityEngine.UI.Button");
            var click = (UnityEvent)button.GetType().GetProperty("onClick").GetValue(button);
            Guid initial = progression.Snapshot.SessionId;
            click.Invoke();
            Assert.That(progression.Snapshot.SessionId, Is.EqualTo(initial));
            Damage(health, 1000);
            StringAssert.Contains("You fell", Get<string>(hud, "OutcomeText"));
            Assert.That(button.gameObject.activeInHierarchy, Is.True);
            click.Invoke();
            Assert.That(State, Is.EqualTo("Running"));
            Assert.That(progression.Snapshot.SessionId, Is.Not.EqualTo(initial));
            Assert.That(button.gameObject.activeInHierarchy, Is.False);
            Assert.That(Get<string>(hud, "OutcomeText"), Is.Empty);
        }

        // Arrange late-route state through the authoritative model for focused
        // terminal/UI tests. FullCastleRouteTests proves the actual played route.
        private void CompleteRoutePrerequisites(bool withSecret)
        {
            Guid token = progression.Snapshot.SessionId;
            Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, token), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.FirstEnemyDefeated, token), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.Puzzle, token), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.MainPuzzleSolved, token), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, token), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.ThroneRoom, token), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.MinibossDefeated, token), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.Library, token), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.LibraryOpened, token), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.Catacombs, token), Is.True);
            if (withSecret)
            {
                Assert.That(progression.TryComplete(LevelObjective.SecretLeverPulled, token), Is.True);
                Assert.That(progression.TryEnter(LevelRoom.BonusRoom, token), Is.True);
                Assert.That(progression.TryComplete(LevelObjective.BonusDiscovered, token), Is.True);
                Assert.That(progression.TryEnter(LevelRoom.Catacombs, token), Is.True);
            }
            Assert.That(progression.TryEnter(LevelRoom.FinalArena, token), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.FinalEnemyDefeated, token), Is.True);
        }

        private Component MakeHUD()
        {
            var go = Make("HUD", Vector3.zero); go.SetActive(false);
            var hud = go.AddComponent(RuntimeType("LevelHUD"));
            Set(hud, "session", session); Set(hud, "progression", progression);
            Set(hud, "playerHealth", health); Set(hud, "interactor", interactor);
            go.SetActive(true);
            return hud;
        }
        private string State => Get<object>(session, "State").ToString();
        private bool Damage(Component body, int amount) => (bool)Call(body, "TryDamage", amount, Get<Guid>(body, "LifeId"), Get<Guid>(body, "SessionId"));
        private void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update(); }
        private void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Call(movement, "Simulate", Frame);
                scene.GetPhysicsScene().Simulate(Frame);
            }
        }
        private GameObject Make(string name, Vector3 position)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = origin + position; return go;
        }
        private GameObject Box(string name, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(go, scene);
            go.name = name; go.transform.position = origin + position; go.transform.localScale = scale; return go;
        }
        private static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static void Set(Component target, string name, object value) => target.GetType().GetField(name).SetValue(target, value);
        private static T Get<T>(Component target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);
        private static object Call(Component target, string name, params object[] arguments)
        {
            try { return target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
    }
}
