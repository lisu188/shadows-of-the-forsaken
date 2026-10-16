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
    // The unsnapped route never manually advances the camera: real updates,
    // W/A/D/Space/LMB/E and saved collisions run throughout. The separate saved
    // controller regression below explicitly uses direct component simulation.
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
        private bool sceneLeaseReleased = true, directComponentEvidence;
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
        private string evidenceCaseName;
        private int observedFrames, unsafeFrames, warningCount;
        private double started, ended, nextPeriodicSample;
#if !UNITY_EDITOR
        private NativeExplorationRenderProbe nativeRenderProbe;
#endif

        [SetUp]
        public void Prepare()
        {
            // NUnit reuses this fixture. Do not inherit another case's evidence
            // or discard a scene lease whose teardown did not finish.
            directory = null; // A setup guard failure must not rewrite the preceding case's bundle.
#if !UNITY_EDITOR
            if (nativeRenderProbe != null)
                throw new InvalidOperationException("An earlier native exploration render observer still owns subscriptions.");
#endif
            OwnedSceneLoad.RequireNoPendingCleanup();
            Assert.That(sceneLeaseReleased && suspendedBehaviours.Count == 0 && suspendedColliders.Count == 0 &&
                suspendedRenderers.Count == 0 && observer == null && input == null &&
                (keyboard == null || !keyboard.added) && (mouse == null || !mouse.added), Is.True,
                "An earlier camera fixture still owns resources; refusing to overwrite its cleanup state.");
            samples.Clear(); episodes.Clear(); warnings.Clear(); inputs.Clear(); captures.Clear();
            currentEpisode = null; lastSafe = null; viewport = null;
            observedFrames = unsafeFrames = warningCount = 0;
            settingsCaptured = observing = recordedInitial = completed = false;
            started = ended = nextPeriodicSample = 0;
            directory = phase = currentInput = pendingCapture = null;
            sceneLoad = null; previousScene = scene = default;
            movement = follow = session = health = null; progression = null;
            camera = null; observer = null; input = null; keyboard = null; mouse = null;
            safePoseProperty = radiusProperty = null;
            evidenceCaseName = TestContext.CurrentContext.Test.Name;
            directComponentEvidence = evidenceCaseName == nameof(PrimarySavedControllerRetainsGroundContactAtMicrosteps);
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
                (directComponentEvidence ? "saved-controller-microstep-" : "camera-exploration-") + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            TestContext.WriteLine((directComponentEvidence ? "Direct saved-component setup evidence: " : "Unsnapped camera evidence: ") + directory);
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
#if !UNITY_EDITOR
                nativeRenderProbe?.Dispose();
#endif
                Application.logMessageReceived -= OnLog;
                if (observer != null) { observer.AfterLateUpdate = null; Object.DestroyImmediate(observer.gameObject); }
                try { SaveEvidence(); }
                catch (Exception error) { evidenceFailure = error; }
                if (camera != null && viewport != null) camera.aspect = viewport.originalAspect;
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                if (sceneLoad != null)
                {
                    yield return sceneLoad.Cleanup();
                    sceneLeaseReleased = sceneLoad.CleanupFailure == null;
                }
            }
            finally
            {
                observing = false;
#if !UNITY_EDITOR
                nativeRenderProbe?.Dispose();
                nativeRenderProbe = null;
#endif
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

        [UnityTest, Timeout(60000)]
        public IEnumerator PrimarySavedControllerRetainsGroundContactAtMicrosteps()
        {
            // DOCX 3: stable movement and jump controls on the actual saved floor.
            // This is direct engine-component simulation, not an input-route,
            // ordinary-render or performance test. Never replace saved geometry,
            // move the player, or override CharacterController.minMoveDistance.
            yield return Load();
            phase = "saved controller direct-component microstep diagnostic";
            var character = movement.GetComponent<CharacterController>();
            Assert.That(character, Is.Not.Null);
            float minimumMoveDistance = character.minMoveDistance;
            for (int i = 0; i < 60; i++)
            {
                Input();
                Call(movement, "Simulate", 1f / 60f);
            }
            Assert.That(character.isGrounded, Is.True, "Normal steps must first settle on the saved courtyard floor.");
            float settledY = movement.transform.position.y;
            float settledVelocity = Get<float>(movement, "VerticalVelocity");
            const float microstep = .0001f;
            const int neutralSteps = 32;
            int lostGroundSamples = 0, firstLostGroundStep = -1;
            for (int i = 0; i < neutralSteps; i++)
            {
                Input();
                Call(movement, "Simulate", microstep);
                if (!character.isGrounded)
                {
                    lostGroundSamples++;
                    if (firstLostGroundStep < 0) firstLostGroundStep = i;
                }
            }
            float neutralY = movement.transform.position.y;
            Input(false, Key.Space);
            Call(movement, "Simulate", microstep);
            float jumpVelocity = Get<float>(movement, "VerticalVelocity");
            const int jumpSteps = 2000;
            float highestY = movement.transform.position.y;
            for (int i = 0; i < jumpSteps; i++)
            {
                Input();
                Call(movement, "Simulate", microstep);
                highestY = Mathf.Max(highestY, movement.transform.position.y);
            }
            string diagnostic = "Direct saved-component simulation; minMoveDistance=" + minimumMoveDistance.ToString("R") +
                "; step=" + microstep.ToString("R") + "; neutralSteps=" + neutralSteps +
                "; lostGroundSamples=" + lostGroundSamples + "; firstLostGroundStep=" + firstLostGroundStep +
                "; settledY=" + settledY.ToString("R") + "; neutralY=" + neutralY.ToString("R") +
                "; settledVelocity=" + settledVelocity.ToString("R") + "; jumpVelocity=" + jumpVelocity.ToString("R") +
                "; jumpSteps=" + jumpSteps + "; highestY=" + highestY.ToString("R") +
                "; physicalJumpLift=" + (highestY - neutralY).ToString("R");
            TestContext.WriteLine(diagnostic);
            Assert.That(lostGroundSamples, Is.Zero, "Neutral positive microsteps must retain saved-floor contact. " + diagnostic);
            Assert.That(settledVelocity, Is.LessThan(0), "Normal steps must actually advance the production movement component. " + diagnostic);
            Assert.That(jumpVelocity, Is.GreaterThan(0), "A fresh jump after neutral microsteps must be accepted. " + diagnostic);
            Assert.That(highestY - neutralY, Is.GreaterThan(.5f), "Microsteps must physically lift the saved controller, not only its motor velocity. " + diagnostic);
            Assert.That(character.minMoveDistance, Is.EqualTo(minimumMoveDistance), "The regression must retain the authored controller setting.");
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator Load()
        {
            sceneLeaseReleased = false;
            sceneLoad = new OwnedSceneLoad();
#if UNITY_EDITOR
            yield return sceneLoad.Load(ScenePath, () => EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive)));
#else
            Assert.That(SceneUtility.GetBuildIndexByScenePath(ScenePath), Is.GreaterThanOrEqualTo(0));
            // A visible embedded player can render the newly activated scene
            // before this coroutine resumes. Prepare the existing exact inactive
            // pass expectations in sceneLoaded, before its first ordinary render.
            var previousScenes = new HashSet<Scene>(Enumerable.Range(0, SceneManager.sceneCount)
                .Select(index => SceneManager.GetSceneAt(index)));
            int preparedSceneCount = 0;
            Scene preparedScene = default;
            Exception pipelinePreparationFailure = null;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> prepareNativePipeline = (loaded, mode) =>
            {
                if (loaded.path != ScenePath || previousScenes.Contains(loaded)) return;
                try
                {
                    preparedSceneCount++;
                    Assert.That(preparedSceneCount, Is.EqualTo(1), "Prepare exactly one newly loaded castle scene.");
                    Assert.That(mode, Is.EqualTo(LoadSceneMode.Additive));
                    Assert.That(loaded.IsValid() && loaded.isLoaded, Is.True);
                    var savedCameras = loaded.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
                    Assert.That(savedCameras.Length, Is.EqualTo(1), "The owned saved castle has one scene camera.");
                    Assert.That(savedCameras[0].gameObject.scene, Is.EqualTo(loaded));
                    NativePresentationPipelineDiagnostics.Prepare(savedCameras[0], loaded);
                    preparedScene = loaded;
                }
                catch (Exception error)
                {
                    pipelinePreparationFailure = pipelinePreparationFailure == null
                        ? error : new AggregateException(pipelinePreparationFailure, error);
                }
            };
            SceneManager.sceneLoaded += prepareNativePipeline;
            try
            {
                yield return sceneLoad.Load(ScenePath, () => SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive));
            }
            finally
            {
                SceneManager.sceneLoaded -= prepareNativePipeline;
            }
            if (pipelinePreparationFailure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(pipelinePreparationFailure).Throw();
            Assert.That(preparedSceneCount, Is.EqualTo(1), "The native castle load must prepare its first ordinary render.");
            Assert.That(preparedScene, Is.EqualTo(sceneLoad.Scene), "Prepare only this fixture's owned scene.");
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
#if !UNITY_EDITOR
            if (!directComponentEvidence)
                nativeRenderProbe = new NativeExplorationRenderProbe(camera, started, () => phase);
#endif
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
#if !UNITY_EDITOR
            nativeRenderProbe?.ObserveLateFrame();
#endif
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
#if !UNITY_EDITOR
            bool nativeCaptureBracketEntered = false;
#endif
            try
            {
#if !UNITY_EDITOR
                if (nativeRenderProbe != null)
                {
                    nativeRenderProbe.BeginExplicitCapture(name);
                    nativeCaptureBracketEntered = true;
                }
#endif
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
                NativePresentationPipelineDiagnostics.Prepare(camera, scene, false);
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
#if !UNITY_EDITOR
                try
                {
#endif
#if UNITY_EDITOR
                ShaderUtil.allowAsyncCompilation = originalAsyncCompilation;
#endif
                camera.targetTexture = originalTarget; RenderTexture.active = originalActive;
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
#if !UNITY_EDITOR
                }
                finally
                {
                    if (nativeCaptureBracketEntered) nativeRenderProbe.EndExplicitCapture();
                }
#endif
            }
            Assert.That(camera.transform.position, Is.EqualTo(sample.cameraPosition), "Capture must not move the camera.");
            Assert.That(camera.transform.rotation, Is.EqualTo(sample.cameraRotation), "Capture must not turn the camera.");
            return result;
        }

        private void SaveEvidence()
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            var evidence = new Evidence { completed = completed, observedFrames = observedFrames, unsafeFrames = unsafeFrames,
#if !UNITY_EDITOR
                nativeRendering = nativeRenderProbe?.Snapshot(),
#endif
                blockedWarnings = warningCount, elapsedObservedSeconds = observedFrames == 0 ? 0 : (observing ? Time.realtimeSinceStartupAsDouble : ended) - started,
                viewport = viewport, samples = samples.ToArray(), inputs = inputs.ToArray(), episodes = episodes.ToArray(), warnings = warnings.ToArray(), captures = captures.ToArray(),
                limitations = directComponentEvidence
                    ? "Direct saved-component diagnostic " + typeof(CastleCameraExplorationTests).FullName + "." + evidenceCaseName + ": real PlayerMovement.Simulate/CharacterController microsteps. These camera arrays retain shared saved-spawn setup observations only; the synchronous movement measurements are in NUnit output. This is not completed input-route, ordinary-render or performance evidence."
                    : "Focused real-input lower-room Editor/Unity-test observation; no manual camera advance or snap. Warning callback snapshots are during CameraFollow.LateUpdate; ordinary samples are after LateUpdate. First human warnings were unlocated. No human pacing/art/audio/performance acceptance is established. A disabled camera is never forcibly rendered." };
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
#if !UNITY_EDITOR
            public NativeExplorationRendering nativeRendering;
#endif
        }
#if !UNITY_EDITOR
        // Read-only SRP observation. An unbracketed callback with an active,
        // unbound owned camera is an ordinary-render candidate, not GPU/display
        // proof. Every native capture is bracketed throughout its preparation,
        // request and target restoration; the Editor branch remains unchanged.
        private sealed class NativeExplorationRenderProbe : IDisposable
        {
            private const int SampleLimit = 256;
            private readonly Camera ownedCamera;
            private readonly double origin;
            private readonly Func<string> currentPhase;
            private readonly NativeExplorationRenderCounts all = new NativeExplorationRenderCounts();
            private readonly NativeExplorationRenderCounts ordinaryUnbound = new NativeExplorationRenderCounts();
            private readonly NativeExplorationRenderCounts explicitCapture = new NativeExplorationRenderCounts();
            private readonly NativeExplorationRenderCounts outsideUnbound = new NativeExplorationRenderCounts();
            private readonly List<NativeExplorationRenderSample> callbacks = new List<NativeExplorationRenderSample>(SampleLimit);
            private readonly List<NativeExplorationRenderSample> lateSamples = new List<NativeExplorationRenderSample>(SampleLimit);
            private long callbackSamplesDropped, lateSamplesDropped, lateFrames, ordinaryUnboundLateFrames;
            private long explicitCaptureLateFrames, outsideUnboundLateFrames, captureBegins, captureEnds;
            private double nextLateSample;
            private int captureDepth;
            private string captureName;
            private bool disposed;

            public NativeExplorationRenderProbe(Camera camera, double startedAt, Func<string> phase)
            {
                ownedCamera = camera; origin = startedAt; currentPhase = phase;
                try
                {
                    RenderPipelineManager.beginContextRendering += BeginContext;
                    RenderPipelineManager.endContextRendering += EndContext;
                    RenderPipelineManager.beginCameraRendering += BeginCamera;
                    RenderPipelineManager.endCameraRendering += EndCamera;
                }
                catch { Dispose(); throw; }
            }

            public void BeginExplicitCapture(string name)
            {
                if (disposed || captureDepth != 0)
                    throw new InvalidOperationException("Invalid native exploration capture observation bracket.");
                captureBegins = checked(captureBegins + 1);
                captureName = name; captureDepth = 1;
            }

            public void EndExplicitCapture()
            {
                if (captureDepth != 1)
                    throw new InvalidOperationException("Unbalanced native exploration capture observation bracket.");
                try { captureEnds = checked(captureEnds + 1); }
                finally { captureDepth = 0; captureName = null; }
            }

            private NativeExplorationRenderCounts Classify(out string name)
            {
                if (captureDepth != 0) { name = "explicit-capture"; return explicitCapture; }
                if (ownedCamera != null && ownedCamera.isActiveAndEnabled && ownedCamera.targetTexture == null)
                {
                    name = "unbracketed-owned-camera-active-and-unbound"; return ordinaryUnbound;
                }
                name = "outside-owned-active-unbound-interval"; return outsideUnbound;
            }

            private void BeginContext(ScriptableRenderContext context, List<Camera> cameras) => Record("context-begin", cameras, null);
            private void EndContext(ScriptableRenderContext context, List<Camera> cameras) => Record("context-end", cameras, null);
            private void BeginCamera(ScriptableRenderContext context, Camera camera) => Record("camera-begin", null, camera);
            private void EndCamera(ScriptableRenderContext context, Camera camera) => Record("camera-end", null, camera);

            private void Record(string kind, List<Camera> cameras, Camera callbackCamera)
            {
                if (disposed) return;
                var category = Classify(out string classification);
                bool owned = callbackCamera != null && callbackCamera == ownedCamera;
                all.Increment(kind, owned); category.Increment(kind, owned);
                if (callbacks.Count >= SampleLimit)
                {
                    callbackSamplesDropped = checked(callbackSamplesDropped + 1); return;
                }
                callbacks.Add(Sample(kind, classification, cameras?.Count ?? -1,
                    cameras != null && cameras.Contains(ownedCamera), owned));
            }

            public void ObserveLateFrame()
            {
                if (disposed) return;
                var category = Classify(out string classification);
                lateFrames = checked(lateFrames + 1);
                if (category == ordinaryUnbound) ordinaryUnboundLateFrames = checked(ordinaryUnboundLateFrames + 1);
                else if (category == explicitCapture) explicitCaptureLateFrames = checked(explicitCaptureLateFrames + 1);
                else outsideUnboundLateFrames = checked(outsideUnboundLateFrames + 1);
                double now = Time.realtimeSinceStartupAsDouble - origin;
                if (now < nextLateSample) return;
                nextLateSample = now + 1;
                if (lateSamples.Count < SampleLimit) lateSamples.Add(Sample("late-update", classification, -1, false, false));
                else lateSamplesDropped = checked(lateSamplesDropped + 1);
            }

            private NativeExplorationRenderSample Sample(string kind, string classification, int cameraCount, bool present, bool owned) =>
                new NativeExplorationRenderSample
                {
                    kind = kind, classification = classification, frame = Time.frameCount,
                    seconds = Time.realtimeSinceStartupAsDouble - origin, phase = currentPhase(), capture = captureName,
                    contextCameraCount = cameraCount, ownedCameraPresentInContext = present, callbackCameraIsOwned = owned,
                    ownedCameraActiveAndEnabled = ownedCamera != null && ownedCamera.isActiveAndEnabled,
                    ownedCameraTargetIsNull = ownedCamera != null && ownedCamera.targetTexture == null
                };

            public NativeExplorationRendering Snapshot() => new NativeExplorationRendering
            {
                sampleLimitPerArray = SampleLimit, disposed = disposed, activeCaptureDepth = captureDepth,
                allCallbacks = all.Copy(), ordinaryUnboundCallbacks = ordinaryUnbound.Copy(),
                explicitCaptureCallbacks = explicitCapture.Copy(), outsideUnboundCallbacks = outsideUnbound.Copy(),
                lateFrames = lateFrames, ordinaryUnboundLateFrames = ordinaryUnboundLateFrames,
                explicitCaptureLateFrames = explicitCaptureLateFrames, outsideUnboundLateFrames = outsideUnboundLateFrames,
                captureBegins = captureBegins, captureEnds = captureEnds,
                callbackSamplesDropped = callbackSamplesDropped, lateSamplesDropped = lateSamplesDropped,
                callbackSamples = callbacks.ToArray(), lateSamples = lateSamples.ToArray()
            };

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                RenderPipelineManager.beginContextRendering -= BeginContext;
                RenderPipelineManager.endContextRendering -= EndContext;
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                RenderPipelineManager.endCameraRendering -= EndCamera;
            }
        }

        [Serializable] private sealed class NativeExplorationRenderCounts
        {
            public long contextBegin, contextEnd, allCameraBegin, allCameraEnd, ownedCameraBegin, ownedCameraEnd;
            public void Increment(string kind, bool owned)
            {
                switch (kind)
                {
                    case "context-begin": contextBegin = checked(contextBegin + 1); break;
                    case "context-end": contextEnd = checked(contextEnd + 1); break;
                    case "camera-begin":
                        allCameraBegin = checked(allCameraBegin + 1);
                        if (owned) ownedCameraBegin = checked(ownedCameraBegin + 1);
                        break;
                    case "camera-end":
                        allCameraEnd = checked(allCameraEnd + 1);
                        if (owned) ownedCameraEnd = checked(ownedCameraEnd + 1);
                        break;
                    default: throw new InvalidOperationException("Unknown native exploration render callback.");
                }
            }
            public NativeExplorationRenderCounts Copy() => new NativeExplorationRenderCounts
            {
                contextBegin = contextBegin, contextEnd = contextEnd, allCameraBegin = allCameraBegin,
                allCameraEnd = allCameraEnd, ownedCameraBegin = ownedCameraBegin, ownedCameraEnd = ownedCameraEnd
            };
        }

        [Serializable] private sealed class NativeExplorationRendering
        {
            public string scope = "Native-only read-only SRP callbacks during unsnapped exploration. Ordinary-unbound counts classify observed callback state outside this fixture's explicit capture brackets while its owned camera is active with no targetTexture; other cameras are counted separately. Deferred/asynchronous request callbacks outside the bracket cannot be proved to originate from ordinary rendering. Native capture brackets include preparation/request/target restoration. Callback presence, LateUpdate counts and active/unbound state are not GPU completion, display, FPS or human acceptance proof.";
            public int sampleLimitPerArray, activeCaptureDepth;
            public bool disposed;
            public long lateFrames, ordinaryUnboundLateFrames, explicitCaptureLateFrames, outsideUnboundLateFrames;
            public long captureBegins, captureEnds, callbackSamplesDropped, lateSamplesDropped;
            public NativeExplorationRenderCounts allCallbacks, ordinaryUnboundCallbacks, explicitCaptureCallbacks, outsideUnboundCallbacks;
            public NativeExplorationRenderSample[] callbackSamples, lateSamples;
        }

        [Serializable] private sealed class NativeExplorationRenderSample
        {
            public string kind, classification, phase, capture;
            public int frame, contextCameraCount;
            public double seconds;
            public bool ownedCameraPresentInContext, callbackCameraIsOwned, ownedCameraActiveAndEnabled, ownedCameraTargetIsNull;
        }
#endif
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
