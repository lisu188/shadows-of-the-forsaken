#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Progression;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // DOCX 3, 7-8: explore the lower rooms visited by the first human attempt.
    // Unlike point-capture fixtures, this never snaps or manually advances the
    // camera. Real updates, W/A/D/Space/LMB/E and saved collisions run throughout.
    public sealed class CastleCameraExplorationTests
    {
        private const string ScenePath = "Assets/Scenes/ForsakenCastle.unity";
        private const string BlockedMessage = "CameraFollow: no safe visible camera pose; rendering suspended until recovery.";
        private const float TestAspect = 16f / 9f;
        private readonly List<Behaviour> suspendedBehaviours = new List<Behaviour>();
        private readonly List<Collider> suspendedColliders = new List<Collider>();
        private readonly List<Renderer> suspendedRenderers = new List<Renderer>();
        private readonly List<PoseSample> samples = new List<PoseSample>();
        private readonly List<UnsafeEpisode> episodes = new List<UnsafeEpisode>();
        private readonly List<WarningSample> warnings = new List<WarningSample>();
        private readonly List<InputSample> inputs = new List<InputSample>();
        private readonly List<CaptureSample> captures = new List<CaptureSample>();
        private Scene previousScene, scene;
        private OwnedSceneLoad sceneLoad;
        private InputSettings.UpdateMode previousMode;
        private InputSettings.BackgroundBehavior previousBackground;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
#endif
        private float previousTimeScale;
        private bool settingsCaptured, observing, recordedInitial, completed;
        private Component movement, follow, session, health;
        private LevelProgressionController progression;
        private Camera camera;
        private CameraExplorationLateObserver observer;
        private InputActionAsset input;
        private Keyboard keyboard;
        private Mouse mouse;
        private PropertyInfo safePoseProperty, radiusProperty;
        private UnsafeEpisode currentEpisode;
        private PoseSample lastSafe;
        private ViewportSetup viewport;
        private string directory, phase, currentInput, pendingCapture;
        private int observedFrames, unsafeFrames, warningCount;
        private double started, ended, nextPeriodicSample;

        [SetUp]
        public void Prepare()
        {
            previousScene = SceneManager.GetActiveScene();
            previousMode = InputSystem.settings.updateMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
#if UNITY_EDITOR
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
#endif
            previousTimeScale = Time.timeScale;
            settingsCaptured = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            Time.timeScale = 1;
            foreach (var item in Object.FindObjectsByType<Behaviour>())
                if (item.isActiveAndEnabled && (item is Camera || item is Canvas || item is AudioListener || item is Light ||
                    item.GetType().Name == "Volume" || item.GetType().Name == "NavMeshSurface" || item.GetType().Name == "NavMeshAgent" ||
                    item.GetType().Assembly.GetName().Name == "Assembly-CSharp" ||
                    item.GetType().Namespace?.StartsWith("ShadowsOfTheForsaken", StringComparison.Ordinal) == true))
                {
                    suspendedBehaviours.Add(item); item.enabled = false;
                }
            foreach (var item in Object.FindObjectsByType<Collider>())
                if (item.enabled) { suspendedColliders.Add(item); item.enabled = false; }
            foreach (var item in Object.FindObjectsByType<Renderer>())
                if (item.enabled) { suspendedRenderers.Add(item); item.enabled = false; }
            string requested = Environment.GetEnvironmentVariable("SHADOWS_CAPTURE_DIR");
            directory = Path.Combine(string.IsNullOrWhiteSpace(requested) ? Application.temporaryCachePath : requested,
                "camera-exploration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            TestContext.WriteLine("Unsnapped camera evidence: " + directory);
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            Exception evidenceFailure = null;
            try
            {
                // Stop held input even after an assertion. Briefly observe any
                // recovery; an unsafe frame/warning still fails the test.
                if (keyboard != null && keyboard.added && mouse != null && mouse.added) Input();
                if (observing && (unsafeFrames != 0 || warningCount != 0))
                {
                    double until = Time.realtimeSinceStartupAsDouble + .5;
                    while (Time.realtimeSinceStartupAsDouble < until) yield return null;
                }
                ended = Time.realtimeSinceStartupAsDouble;
                observing = false;
                Application.logMessageReceived -= OnLog;
                if (observer != null) { observer.AfterLateUpdate = null; Object.DestroyImmediate(observer.gameObject); }
                try { SaveEvidence(); }
                catch (Exception error) { evidenceFailure = error; }
                if (camera != null && viewport != null) camera.aspect = viewport.originalAspect;
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                if (sceneLoad != null) yield return sceneLoad.Cleanup();
            }
            finally
            {
                observing = false;
                Application.logMessageReceived -= OnLog;
                if (observer != null) Object.DestroyImmediate(observer.gameObject);
                if (camera != null && viewport != null) camera.aspect = viewport.originalAspect;
                if (input != null) Object.DestroyImmediate(input);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (settingsCaptured)
                {
                    InputSystem.settings.updateMode = previousMode;
                    InputSystem.settings.backgroundBehavior = previousBackground;
#if UNITY_EDITOR
                    InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
#endif
                    Time.timeScale = previousTimeScale;
                }
                foreach (var item in suspendedColliders) if (item != null) item.enabled = true;
                foreach (var item in suspendedRenderers) if (item != null) item.enabled = true;
                foreach (var item in suspendedBehaviours) if (item != null) item.enabled = true;
                suspendedColliders.Clear(); suspendedRenderers.Clear(); suspendedBehaviours.Clear();
            }
            Assert.That(sceneLoad?.CleanupFailure, Is.Null, "Camera exploration scene cleanup failed.");
            Assert.That(evidenceFailure, Is.Null, "Camera exploration evidence could not be retained.");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator LowerRoomsRetainVisibleCameraDuringUnsnappedInputExploration()
        {
            yield return Load();
            Guid originalSession = progression.Snapshot.SessionId;
            phase = "courtyard west wall";
            yield return MoveTo(new Vector3(-3.4f, 0, -82));
            yield return PressWall(270, .5f);
            Assert.That(movement.transform.position.x, Is.InRange(-3.95f, -3.5f), Diagnostic("Reach west wall"));
            yield return Checkpoint("01-courtyard-wall");
            yield return TurnThroughCircle(Key.D);
            yield return TurnThroughCircle(Key.A);
            yield return JumpAgainstWall(270);

            phase = "courtyard narrow entrance";
            yield return MoveTo(new Vector3(0, 0, -82));
            yield return MoveTo(new Vector3(0, 0, 8));
            yield return PressWall(270, .5f);
            Assert.That(movement.transform.position.x, Is.InRange(-1.95f, -1.5f), Diagnostic("Reach entrance wall"));
            yield return TurnThroughCircle(Key.D);
            yield return TurnThroughCircle(Key.A);
            yield return Checkpoint("02-entrance-wall");
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(FindAll("ShadowsOfTheForsaken.Encounters.EnemyEncounter").All(e => !Get<bool>(e, "IsActivated")), Is.True);

            phase = "first fight without a camera capture reset";
            yield return MoveTo(new Vector3(0, 0, 8));
            yield return MoveTo(new Vector3(0, 0, 15));
            yield return FirstFight();
            Assert.That(Has(LevelObjective.FirstEnemyDefeated), Is.True);
            yield return MoveTo(new Vector3(0, 0, 16));
            phase = "encounter north wall and lower junction";
            yield return PressWall(0, 1);
            yield return TurnThroughCircle(Key.D);
            yield return Checkpoint("03-encounter-north-wall");
            yield return MoveTo(new Vector3(0, 0, 16));
            yield return MoveTo(new Vector3(-8, 0, 16));
            yield return PressWall(270, .5f);
            yield return TurnThroughCircle(Key.A);
            yield return MoveTo(new Vector3(-8, 0, 16));
            yield return MoveTo(new Vector3(-8, 0, 24));

            phase = "rune lever through the normal E interaction";
            yield return Use(FindAll("InteractionTarget").Single(t => Field<LevelObjective>(t, "objective") == LevelObjective.MainPuzzleSolved));
            Assert.That(Has(LevelObjective.MainPuzzleSolved), Is.True);
            yield return Checkpoint("04-rune-lever-unsnapped");
            yield return MoveTo(new Vector3(-8, 0, 24));

            // The human's final status was near this west wall at yaw 32.29.
            // Its seven warnings had no positions/times: this is a credible
            // exploration condition, not an asserted historical warning site.
            phase = "puzzle west wall near retained human final position";
            yield return MoveTo(new Vector3(-8, 0, 21.88f));
            yield return PressWall(270, 1);
            Assert.That(movement.transform.position.x, Is.InRange(-11.95f, -11.5f), Diagnostic("Reach puzzle west wall"));
            yield return FaceYaw(32.29f);
            yield return Hold(.4f);
            yield return Checkpoint("05-human-final-position-vicinity");
            yield return TurnThroughCircle(Key.D);
            yield return TurnThroughCircle(Key.A);
            yield return JumpAgainstWall(270);

            phase = "puzzle southwest inside corner";
            yield return PressWall(180, .6f);
            Assert.That(movement.transform.position.z, Is.InRange(20.15f, 20.6f), Diagnostic("Reach southwest corner"));
            yield return TurnThroughCircle(Key.D);
            yield return Checkpoint("06-puzzle-southwest-corner");

            // This 1 m channel fits the saved player capsule but is narrower
            // than the conservative near-plane clearance sphere's diameter.
            phase = "puzzle west wall beside rune plinth";
            yield return FaceYaw(0);
            yield return HoldUntilZ(25.5f);
            yield return TurnThroughCircle(Key.A);
            yield return TurnThroughCircle(Key.D);
            yield return JumpAgainstWall(270);
            yield return Checkpoint("07-west-plinth-channel");
            yield return HoldUntilZ(27.4f);
            yield return PressWall(0, .4f);
            phase = "puzzle northwest inside corner";
            yield return TurnThroughCircle(Key.D);
            yield return Checkpoint("08-puzzle-northwest-corner");

            Input(); yield return null;
            Assert.That(observedFrames, Is.GreaterThan(100));
            Assert.That(viewport.observedAspect, Is.EqualTo(TestAspect).Within(.0001f));
            Assert.That(viewport.observedRadius, Is.EqualTo(viewport.requestedRadius).Within(.0001f),
                "Natural CameraFollow update must use the requested widescreen clearance.");
            Assert.That(unsafeFrames, Is.Zero, Diagnostic("No sampled rendering suspension"));
            Assert.That(warningCount, Is.Zero, Diagnostic("No camera warning"));
            Assert.That(progression.Snapshot.SessionId, Is.EqualTo(originalSession));
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Puzzle));
            Assert.That(progression.Snapshot.CompletedObjectives,
                Is.EqualTo(LevelObjective.FirstEnemyDefeated | LevelObjective.MainPuzzleSolved));
            Assert.That(Get<bool>(health, "IsAlive"), Is.True);
            Assert.That(Get<bool>(session, "IsRunning"), Is.True);
            LogAssert.NoUnexpectedReceived();
            completed = true;
            SaveEvidence();
        }

        private IEnumerator Load()
        {
            sceneLoad = new OwnedSceneLoad();
#if UNITY_EDITOR
            yield return sceneLoad.Load(ScenePath, () => EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive)));
#else
            Assert.That(SceneUtility.GetBuildIndexByScenePath(ScenePath), Is.GreaterThanOrEqualTo(0));
            yield return sceneLoad.Load(ScenePath, () => SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive));
#endif
            scene = sceneLoad.Scene;
            Assert.That(sceneLoad.LoadedWithinDeadline && scene.IsValid() && scene.isLoaded, Is.True, "Saved castle load exceeded 30 s.");
            Assert.That(SceneManager.SetActiveScene(scene), Is.True);
            yield return null;
            Assert.That(Object.FindObjectsByType<Canvas>()
                .Where(item => item.isActiveAndEnabled && item.gameObject.scene != scene)
                .Select(item => item.gameObject.scene.name + "/" + item.name).ToArray(), Is.Empty,
                "Pre-existing scene canvases must be suspended before the owned castle HUD is rendered.");
            movement = Find("PlayerMovement"); follow = Find("CameraFollow");
            session = Find("LevelSessionController"); progression = Field<LevelProgressionController>(session, "progression");
            health = Field<Component>(session, "playerHealth"); camera = follow.GetComponent<Camera>();
            safePoseProperty = follow.GetType().GetProperty("HasSafePose");
            radiusProperty = follow.GetType().GetProperty("EffectiveCollisionRadius");
            Assert.That(camera != null && camera.isActiveAndEnabled, Is.True);
            Assert.That(Field<Transform>(follow, "player"), Is.SameAs(movement.transform));
            // Viewport setup only: match the 1280x720 human/native aspect before
            // observation or synthetic input. The following natural LateUpdate
            // derives clearance from this viewport; never snap or advance it.
            viewport = new ViewportSetup { originalAspect = camera.aspect, requestedAspect = TestAspect,
                originalRadius = (float)radiusProperty.GetValue(follow),
                reason = "Match the actual human/native 1280x720 (16:9) viewport; a hidden Editor's default aspect can reduce near-plane clearance and mask reproduction." };
            camera.aspect = TestAspect;
            Assert.That(camera.aspect, Is.EqualTo(TestAspect).Within(.0001f), "Comparable widescreen viewport precondition.");
            var corners = new Vector3[4];
            camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), camera.nearClipPlane, Camera.MonoOrStereoscopicEye.Mono, corners);
            viewport.requestedRadius = Mathf.Max(Field<float>(follow, "collisionRadius"), corners.Max(corner => corner.magnitude));
            var source = Field<InputActionAsset>(movement, "inputActions");
            Assert.That(source, Is.Not.Null, "Use the saved controls asset.");
            ((Behaviour)movement).enabled = false;
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            input = Object.Instantiate(source); input.devices = new InputDevice[] { keyboard, mouse };
            movement.GetType().GetField("inputActions").SetValue(movement, input);
            ((Behaviour)movement).enabled = true;
            Call(movement, "OnApplicationFocus", true);
            var bridge = movement.GetComponent(RuntimeType("PlayerCombat"));
            if (bridge != null) Call(bridge, "OnApplicationFocus", true);
            Call(session, "OnApplicationFocus", true);
            foreach (var enemy in FindAll("ShadowsOfTheForsaken.Encounters.EnemyEncounter")) Call(enemy, "OnApplicationFocus", true);
            phase = "saved spawn"; started = Time.realtimeSinceStartupAsDouble;
            var helper = new GameObject("Unsnapped camera LateUpdate observation");
            SceneManager.MoveGameObjectToScene(helper, scene);
            observer = helper.AddComponent<CameraExplorationLateObserver>();
            observer.AfterLateUpdate = Observe;
            Application.logMessageReceived += OnLog;
            observing = true;
            Input(); yield return null;
            yield return Checkpoint("00-saved-spawn");
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(progression.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
        }

        private IEnumerator MoveTo(Vector3 target)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 45;
            while (FlatDistance(movement.transform.position, target) > .25f)
            {
                CheckRunning(deadline, "Walk to " + target);
                Vector3 offset = target - movement.transform.position; offset.y = 0;
                float angle = Vector3.SignedAngle(movement.transform.forward, offset, Vector3.up);
                Input(false, Mathf.Abs(angle) > 2.5f ? new[] { angle > 0 ? Key.D : Key.A } : new[] { Key.W });
                yield return null;
            }
            Input(); yield return null;
        }

        private IEnumerator FaceYaw(float yaw)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 6;
            while (Mathf.Abs(Mathf.DeltaAngle(movement.transform.eulerAngles.y, yaw)) > 3)
            {
                CheckRunning(deadline, "Face " + yaw);
                Input(false, Mathf.DeltaAngle(movement.transform.eulerAngles.y, yaw) > 0 ? Key.D : Key.A);
                yield return null;
            }
            Input(); yield return null;
        }

        private IEnumerator TurnThroughCircle(Key direction)
        {
            float previous = movement.transform.eulerAngles.y, turn = 0;
            double deadline = Time.realtimeSinceStartupAsDouble + 12;
            while (turn < 360)
            {
                CheckRunning(deadline, "Full turn " + direction);
                Input(false, direction); yield return null;
                float yaw = movement.transform.eulerAngles.y;
                turn += Mathf.Abs(Mathf.DeltaAngle(previous, yaw)); previous = yaw;
            }
            Input(); yield return null;
        }

        private IEnumerator PressWall(float yaw, float seconds)
        {
            yield return FaceYaw(yaw);
            yield return Hold(seconds, Key.W);
        }

        private IEnumerator JumpAgainstWall(float yaw)
        {
            yield return FaceYaw(yaw);
            float initial = movement.transform.position.y, highest = initial;
            double deadline = Time.realtimeSinceStartupAsDouble + 2;
            Input(false, Key.W, Key.Space); yield return null;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                CheckRunning(deadline + 1, "Jump against wall");
                highest = Mathf.Max(highest, movement.transform.position.y);
                Input(false, Key.W); yield return null;
            }
            Input(); yield return null;
            Assert.That(highest - initial, Is.GreaterThan(.5f), Diagnostic("Actual jump must occur"));
            Assert.That(movement.GetComponent<CharacterController>().isGrounded, Is.True, Diagnostic("Jump must land"));
        }

        private IEnumerator Hold(float seconds, params Key[] keys)
        {
            double until = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < until)
            {
                CheckRunning(until + 1, "Hold " + string.Join(",", keys));
                Input(false, keys); yield return null;
            }
            Input(); yield return null;
        }

        private IEnumerator HoldUntilZ(float z)
        {
            yield return FaceYaw(0);
            double deadline = Time.realtimeSinceStartupAsDouble + 6;
            while (movement.transform.position.z < z)
            {
                CheckRunning(deadline, "Follow west wall to z=" + z);
                Input(false, Key.W); yield return null;
            }
            Input(); yield return null;
        }

        private IEnumerator Use(Component target)
        {
            Vector3 offset = target.transform.position - movement.transform.position; offset.y = 0;
            if (offset.magnitude > 2) yield return MoveTo(target.transform.position - offset.normalized * 1.7f);
            offset = target.transform.position - movement.transform.position; offset.y = 0;
            yield return FaceYaw(Quaternion.LookRotation(offset).eulerAngles.y);
            var interactor = Find("PlayerInteractor");
            Input(); yield return null;
            Assert.That(Get<Component>(interactor, "SelectedTarget"), Is.SameAs(target), Diagnostic("Select rune lever"));
            Assert.That(Get<bool>(interactor, "CanInteract"), Is.True);
            Input(false, Key.E); yield return null;
            Input(); yield return null;
            Assert.That(Get<bool>(target, "IsConsumed"), Is.True);
        }

        private IEnumerator FirstFight()
        {
            var enemy = FindAll("ShadowsOfTheForsaken.Encounters.EnemyEncounter")
                .Single(e => Field<LevelObjective>(e, "objective") == LevelObjective.FirstEnemyDefeated);
            var enemyHealth = enemy.GetComponent(RuntimeType("ShadowsOfTheForsaken.Combat.CombatHealth"));
            var enemyMelee = enemy.GetComponent(RuntimeType("ShadowsOfTheForsaken.Combat.MeleeCombat"));
            var playerMelee = health.GetComponent(RuntimeType("ShadowsOfTheForsaken.Combat.MeleeCombat"));
            double deadline = Time.realtimeSinceStartupAsDouble + 70;
            bool pressedLast = false, observedWindup = false;
            while (!Has(LevelObjective.FirstEnemyDefeated))
            {
                CheckRunning(deadline, "Real first fight, enemy HP=" + Get<int>(enemyHealth, "Current"));
                Vector3 offset = enemy.transform.position - movement.transform.position; offset.y = 0;
                float angle = Vector3.SignedAngle(movement.transform.forward, offset, Vector3.up);
                var playerPhase = Get<AttackPhase>(playerMelee, "Phase");
                var enemyPhase = Get<AttackPhase>(enemyMelee, "Phase");
                observedWindup |= enemyPhase == AttackPhase.Windup;
                bool attack = false;
                if (!Get<bool>(enemyHealth, "IsAlive"))
                {
                    if (progression.Snapshot.Room != LevelRoom.FirstEncounter) yield return MoveTo(Field<Vector3>(enemy, "arenaCenter"));
                    Input();
                }
                else if (Mathf.Abs(angle) > 4) Input(false, angle > 0 ? Key.D : Key.A);
                else if (playerPhase == AttackPhase.Windup || playerPhase == AttackPhase.Active) Input();
                else if (playerPhase != AttackPhase.Ready || enemyPhase == AttackPhase.Windup || enemyPhase == AttackPhase.Active)
                    Input(false, offset.magnitude < 3 ? new[] { Key.S } : Array.Empty<Key>());
                else if (offset.magnitude > 1.85f) Input(false, Key.W);
                else { attack = !pressedLast; Input(attack); }
                pressedLast = attack;
                yield return null;
            }
            Input(); yield return null;
            Assert.That(observedWindup && Get<bool>(enemy, "DeathCredited"), Is.True, "Observe and defeat the active saved demon.");
        }

        private IEnumerator Checkpoint(string name)
        {
            pendingCapture = name;
            int previousFrame = observedFrames;
            double deadline = Time.realtimeSinceStartupAsDouble + 3;
            while (pendingCapture != null || observedFrames == previousFrame)
            {
                CheckRunning(deadline, "Observe " + name); Input(); yield return null;
            }
            SaveEvidence();
        }

        private void CheckRunning(double deadline, string action)
        {
            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), Diagnostic(action + " timed out"));
            Assert.That(Get<bool>(session, "IsRunning") && Get<bool>(health, "IsAlive"), Is.True, Diagnostic(action + " interrupted"));
            Assert.That(movement.transform.position.y, Is.GreaterThan(-.5f), Diagnostic(action + " fell off the lower floor"));
            Assert.That(unsafeFrames, Is.Zero, Diagnostic(action + " lost safe camera; see evidence.json"));
            Assert.That(warningCount, Is.Zero, Diagnostic(action + " received a camera warning; see evidence.json"));
        }

        private void Input(bool attack = false, params Key[] keys)
        {
            string signature = (attack ? "LMB" : "-") + ":" + string.Join(",", keys);
            if (observing && signature != currentInput)
                inputs.Add(new InputSample { frame = Time.frameCount, seconds = Time.realtimeSinceStartupAsDouble - started, phase = phase, keys = keys.Select(key => key.ToString()).ToArray(), attack = attack });
            currentInput = signature;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.QueueStateEvent(mouse, attack ? new MouseState().WithButton(MouseButton.Left) : new MouseState());
            InputSystem.Update();
            Assert.That(InputState.currentUpdateType, Is.EqualTo(InputUpdateType.Manual));
        }

        private void Observe()
        {
            if (!observing || follow == null || movement == null) return;
            observedFrames++;
            var sample = Sample();
            if (observedFrames == 1)
            {
                viewport.observedAspect = sample.aspect;
                viewport.observedRadius = sample.effectiveRadius;
            }
            bool unsafePose = !sample.hasSafePose || !sample.cameraEnabled;
            if (unsafePose)
            {
                unsafeFrames++;
                if (currentEpisode == null)
                {
                    sample.nearbyColliders = NearbyColliders(sample);
                    currentEpisode = new UnsafeEpisode { first = sample, lastSafe = lastSafe };
                    episodes.Add(currentEpisode);
                    samples.Add(sample);
                    captures.Add(CaptureUnsnapped("first-unsafe-view-" + episodes.Count, sample));
                }
                currentEpisode.frames++;
                currentEpisode.sampledUnsafeSeconds += Time.unscaledDeltaTime;
                currentEpisode.last = sample;
                if (currentEpisode.frames == 1) SaveEvidence();
            }
            else
            {
                if (currentEpisode != null)
                {
                    currentEpisode.recovered = true; currentEpisode.recoveryFrame = sample.frame;
                    currentEpisode.wallSecondsUntilRecovery = sample.seconds - currentEpisode.first.seconds;
                    currentEpisode = null;
                    samples.Add(sample); SaveEvidence();
                }
                lastSafe = sample;
            }
            if (!recordedInitial || sample.seconds >= nextPeriodicSample)
            {
                samples.Add(sample); recordedInitial = true; nextPeriodicSample = sample.seconds + .1;
            }
            if (pendingCapture != null)
            {
                string name = pendingCapture; pendingCapture = null;
                captures.Add(CaptureUnsnapped(name, sample));
                SaveEvidence();
            }
        }

        private PoseSample Sample() => new PoseSample
        {
            frame = Time.frameCount, seconds = Time.realtimeSinceStartupAsDouble - started,
            phase = phase, input = currentInput, position = movement.transform.position,
            yaw = movement.transform.eulerAngles.y, cameraPosition = camera.transform.position,
            cameraRotation = camera.transform.rotation, hasSafePose = (bool)safePoseProperty.GetValue(follow),
            cameraEnabled = camera.isActiveAndEnabled, effectiveRadius = (float)radiusProperty.GetValue(follow),
            aspect = camera.aspect, room = progression.Snapshot.Room.ToString(),
            objectives = progression.Snapshot.CompletedObjectives.ToString()
        };

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!observing || condition != BlockedMessage) return;
            warningCount++;
            var sample = Sample(); sample.nearbyColliders = NearbyColliders(sample);
            warnings.Add(new WarningSample { message = condition, type = type.ToString(), poseDuringCameraLateUpdate = sample });
            SaveEvidence();
            // Deliberately neither expected nor suppressed. Unity's normal log
            // assertion and our explicit count both retain this as a failure.
        }

        private ColliderSample[] NearbyColliders(PoseSample sample)
        {
            Vector3 pivot = sample.position + Vector3.up * Field<float>(follow, "pivotHeight");
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Collider>())
                .Where(item => item.enabled && !item.isTrigger && !item.transform.IsChildOf(movement.transform) &&
                    (Vector3.Distance(item.ClosestPoint(pivot), pivot) <= 2 ||
                     Vector3.Distance(item.ClosestPoint(sample.cameraPosition), sample.cameraPosition) <= sample.effectiveRadius + .1f))
                .OrderBy(item => Vector3.Distance(item.ClosestPoint(pivot), pivot)).Take(24)
                .Select(item => new ColliderSample { name = item.name, type = item.GetType().Name,
                    center = item.bounds.center, size = item.bounds.size, closestToPivot = item.ClosestPoint(pivot) }).ToArray();
        }

        private CaptureSample CaptureUnsnapped(string name, PoseSample sample)
        {
            var result = new CaptureSample { name = name, pose = sample };
            if (!sample.cameraEnabled || !sample.hasSafePose)
            {
                result.status = "not rendered: camera disabled or unsafe; no forced render"; return result;
            }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                result.status = "not rendered: graphics device unavailable"; return result;
            }
            var originalTarget = camera.targetTexture;
            var originalActive = RenderTexture.active;
#if UNITY_EDITOR
            bool originalAsyncCompilation = ShaderUtil.allowAsyncCompilation;
#endif
            RenderTexture texture = null;
            Texture2D pixels = null;
            try
            {
                // Match the observed camera aspect; never recompute its clearance
                // or presentation pose for a capture. URP includes the saved
                // camera-space HUD; no UI mode/layout conversion is performed.
                int width = 960, height = Mathf.Clamp(Mathf.RoundToInt(width / sample.aspect), 128, 1920);
                texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); texture.Create();
#if UNITY_EDITOR
                ShaderUtil.allowAsyncCompilation = false;
#endif
                var request = new RenderPipeline.StandardRequest { destination = texture };
#if !UNITY_EDITOR
                NativePresentationPipelineDiagnostics.Prepare(camera, scene);
#endif
                Assert.That(RenderPipeline.SupportsRenderRequest(camera, request), Is.True, "Saved pipeline must support an unsnapped view capture.");
                RenderPipeline.SubmitRenderRequest(camera, request);
#if UNITY_EDITOR
                // Resolve already queued first-use emission variants without
                // changing pose, input, actor feedback or the simulation clock.
                foreach (var material in scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Renderer>())
                    .Where(item => item.enabled).SelectMany(item => item.sharedMaterials)
                    .Where(item => item != null && item.IsKeywordEnabled("_EMISSION")).Distinct())
                    for (int pass = 0; pass < material.passCount; pass++)
                        if ((material.GetPassName(pass) == "ForwardLit" || material.GetPassName(pass) == "GBuffer") &&
                            material.GetShaderPassEnabled(material.GetPassName(pass)))
                        {
                            ShaderUtil.CompilePass(material, pass, true);
                            Assert.That(ShaderUtil.IsPassCompiled(material, pass) && !ShaderUtil.ShaderHasError(material.shader), Is.True,
                                "Unsnapped capture emission pass: " + material.name + "/" + material.GetPassName(pass));
                        }
                RenderPipeline.SubmitRenderRequest(camera, request);
#endif
                RenderTexture.active = texture;
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                result.file = name + ".png"; result.width = width; result.height = height;
                File.WriteAllBytes(Path.Combine(directory, result.file), pixels.EncodeToPNG());
                result.status = "rendered from sampled LateUpdate pose without snap; scene world and saved camera-space HUD via URP, no UI mode/layout conversion";
            }
            finally
            {
#if UNITY_EDITOR
                ShaderUtil.allowAsyncCompilation = originalAsyncCompilation;
#endif
                camera.targetTexture = originalTarget; RenderTexture.active = originalActive;
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
            }
            Assert.That(camera.transform.position, Is.EqualTo(sample.cameraPosition), "Capture must not move the camera.");
            Assert.That(camera.transform.rotation, Is.EqualTo(sample.cameraRotation), "Capture must not turn the camera.");
            return result;
        }

        private void SaveEvidence()
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            var evidence = new Evidence { completed = completed, observedFrames = observedFrames, unsafeFrames = unsafeFrames,
                blockedWarnings = warningCount, elapsedObservedSeconds = observedFrames == 0 ? 0 : (observing ? Time.realtimeSinceStartupAsDouble : ended) - started,
                viewport = viewport, samples = samples.ToArray(), inputs = inputs.ToArray(), episodes = episodes.ToArray(), warnings = warnings.ToArray(), captures = captures.ToArray(),
                limitations = "Focused real-input lower-room Editor/Unity-test observation; no manual camera advance or snap. Warning callback snapshots are during CameraFollow.LateUpdate; ordinary samples are after LateUpdate. First human warnings were unlocated. No human pacing/art/audio/performance acceptance is established. A disabled camera is never forcibly rendered." };
            File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonUtility.ToJson(evidence, true));
        }

        private bool Has(LevelObjective objective) => (progression.Snapshot.CompletedObjectives & objective) == objective;
        private Component Find(string name) => FindAll(name).Single();
        private Component[] FindAll(string name) => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(RuntimeType(name), true)).ToArray();
        private static float FlatDistance(Vector3 first, Vector3 second) => new Vector2(first.x - second.x, first.z - second.z).magnitude;
        private string Diagnostic(string action)
        {
            var first = episodes.Count != 0 ? episodes[0].first : warnings.Count != 0 ? warnings[0].poseDuringCameraLateUpdate : null;
            return action + "; phase=" + phase + "; position=" + movement.transform.position +
                "; yaw=" + movement.transform.eulerAngles.y + "; room=" + progression.Snapshot.Room +
                (first == null ? "" : "; FIRST UNSAFE frame=" + first.frame + "; phase=" + first.phase +
                    "; feet=" + first.position.ToString("F6") + "; yaw=" + first.yaw.ToString("F6") +
                    "; radius=" + first.effectiveRadius.ToString("F6") + "; aspect=" + first.aspect.ToString("F6") +
                    "; sampled unsafe frames=" + unsafeFrames +
                    (episodes.Count == 0 ? "" : "; first episode sampled seconds=" + episodes[0].sampledUnsafeSeconds.ToString("F6"))) +
                "; camera evidence=" + directory;
        }
        private static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static T Field<T>(Component item, string name) => (T)item.GetType().GetField(name).GetValue(item);
        private static T Get<T>(Component item, string name) => (T)item.GetType().GetProperty(name).GetValue(item);
        private static object Call(Component item, string method, params object[] arguments)
        {
            try { return item.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(item, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        [Serializable] private sealed class Evidence
        {
            public bool completed;
            public int observedFrames, unsafeFrames, blockedWarnings;
            public double elapsedObservedSeconds;
            public string limitations;
            public ViewportSetup viewport;
            public PoseSample[] samples;
            public InputSample[] inputs;
            public UnsafeEpisode[] episodes;
            public WarningSample[] warnings;
            public CaptureSample[] captures;
        }
        [Serializable] private sealed class ViewportSetup
        {
            public float originalAspect, requestedAspect, observedAspect, originalRadius, requestedRadius, observedRadius;
            public string reason;
        }
        [Serializable] private sealed class PoseSample
        {
            public int frame;
            public double seconds;
            public string phase, input, room, objectives;
            public Vector3 position, cameraPosition;
            public Quaternion cameraRotation;
            public float yaw, effectiveRadius, aspect;
            public bool hasSafePose, cameraEnabled;
            public ColliderSample[] nearbyColliders;
        }
        [Serializable] private sealed class ColliderSample { public string name, type; public Vector3 center, size, closestToPivot; }
        [Serializable] private sealed class UnsafeEpisode
        {
            public PoseSample first, last, lastSafe;
            public int frames, recoveryFrame;
            public bool recovered;
            public double sampledUnsafeSeconds, wallSecondsUntilRecovery;
        }
        [Serializable] private sealed class WarningSample { public string message, type; public PoseSample poseDuringCameraLateUpdate; }
        [Serializable] private sealed class InputSample { public int frame; public double seconds; public string phase; public string[] keys; public bool attack; }
        [Serializable] private sealed class CaptureSample { public string name, file, status; public int width, height; public PoseSample pose; }
    }

    // CameraFollow uses ordinary LateUpdate; body occlusion is order 100. This
    // observer samples both only after their natural updates have completed.
    [DefaultExecutionOrder(32000)]
    public sealed class CameraExplorationLateObserver : MonoBehaviour
    {
        public Action AfterLateUpdate;
        private void LateUpdate() => AfterLateUpdate?.Invoke();
    }
}
#endif
