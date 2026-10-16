#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // DOCX 3, 7-8: diagnose primary HUD visibility at recorded lower-room views.
    // This is presentation setup, not a gameplay route or progression proof.
    // The camera renders normally into one persistent target; no render request,
    // Camera.Render, camera snap or manual gameplay/camera simulation is used.
    public sealed class PrimaryCastleHudRenderingTests
    {
        private const string ScenePath = "Assets/Scenes/ForsakenCastle.unity";
        private const int Width = 1280, Height = 720;
        private const int MinimumChangedPixels = 32;
        private readonly List<Behaviour> suspendedBehaviours = new List<Behaviour>();
        private readonly List<Collider> suspendedColliders = new List<Collider>();
        private readonly List<Renderer> suspendedRenderers = new List<Renderer>();
        private Scene previousScene, scene;
        private OwnedSceneLoad sceneLoad;
        private Camera camera;
        private Canvas canvas;
        private Component movement, follow, hud;
        private RenderTexture target, originalTarget, originalActive;
        private float originalAspect, previousTimeScale;
        private bool settingsCaptured, canvasEnabled;
        private string directory;
#if !UNITY_EDITOR
        private NativeHudRenderProbe nativeRenderProbe;
#endif

        [SetUp]
        public void Prepare()
        {
            previousScene = SceneManager.GetActiveScene();
            previousTimeScale = Time.timeScale;
            settingsCaptured = true;
            Time.timeScale = 1;
            // Existing scene HUDs must not contribute pixels to this copy.
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
            if (!string.IsNullOrWhiteSpace(requested))
                Assert.That(Path.IsPathRooted(requested), Is.True, "SHADOWS_CAPTURE_DIR must be absolute.");
            directory = Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(requested) ? Application.temporaryCachePath : requested,
                "primary-hud-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            TestContext.WriteLine("Primary HUD ordinary-frame evidence: " + directory);
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            try
            {
                RestoreTarget();
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                if (sceneLoad != null) yield return sceneLoad.Cleanup();
            }
            finally
            {
                RestoreTarget();
                if (settingsCaptured) Time.timeScale = previousTimeScale;
                foreach (var item in suspendedColliders) if (item != null) item.enabled = true;
                foreach (var item in suspendedRenderers) if (item != null) item.enabled = true;
                foreach (var item in suspendedBehaviours) if (item != null) item.enabled = true;
                suspendedColliders.Clear(); suspendedRenderers.Clear(); suspendedBehaviours.Clear();
            }
            Assert.That(sceneLoad?.CleanupFailure, Is.Null, "Primary HUD owned-scene cleanup failed.");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator PrimaryHudRendersAtRecordedPuzzleWestWallPose()
        {
            // full-current-230/captures/camera-exploration-4ef9f870115a4a6c99ca1d06eae68b77/05-*.png
            yield return VerifyPose("05-puzzle-west-wall",
                new Vector3(-11.6800261f, .01999974f, 21.9406700f), 29.3383198f,
                new Vector3(-11.4175215f, 2.3006697f, 20.6123257f),
                new Quaternion(-.051230356f, -.263642341f, -.014023145f, .963157117f));
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator PrimaryHudRendersAtRecordedPuzzleNorthwestCornerPose()
        {
            // Same actual exploration receipt, 08-puzzle-northwest-corner.png.
            yield return VerifyPose("08-puzzle-northwest-corner",
                new Vector3(-11.6989841f, .01999986f, 27.6800003f), 357.0531006f,
                new Vector3(-11.0898542f, 3.9041939f, 25.1492062f),
                new Quaternion(.212268561f, -.262756228f, .059376132f, .939348578f));
        }

        private IEnumerator VerifyPose(string name, Vector3 feet, float yaw, Vector3 viewPosition, Quaternion viewRotation)
        {
            yield return Load();
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "Primary HUD verification requires ordinary rendered frames.");
            // Freeze only the presentation fixture's actor/camera transforms.
            // The saved HUD, render mode, depth and materials remain unchanged.
            ((Behaviour)movement).enabled = false;
            ((Behaviour)follow).enabled = false;
            var character = movement.GetComponent<CharacterController>();
            character.enabled = false;
            movement.transform.SetPositionAndRotation(feet, Quaternion.Euler(0, yaw, 0));
            character.enabled = true;
            camera.transform.SetPositionAndRotation(viewPosition, viewRotation);
            Quaternion assignedViewRotation = camera.transform.rotation;
            int frozenActors;
            var actorParts = FreezeActorPresentation(out frozenActors);
            Physics.SyncTransforms();
            originalTarget = camera.targetTexture;
            originalActive = RenderTexture.active;
            originalAspect = camera.aspect;
            canvasEnabled = canvas.enabled;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1, useMipMap = false, autoGenerateMips = false
            };
            Assert.That(target.Create(), Is.True);
            camera.targetTexture = target;
            camera.aspect = (float)Width / Height;
            Assert.That(camera.isActiveAndEnabled && canvas.isActiveAndEnabled, Is.True);

            var texts = new[]
            {
                TextAt("Status/Health"), TextAt("Controls/Controls text")
            };
            var textEnabled = texts.Select(text => text.enabled).ToArray();
            Texture2D visible = null, hidden = null, textHidden = null;
            try
            {
#if !UNITY_EDITOR
                NativePresentationPipelineDiagnostics.Prepare(camera, scene);
                nativeRenderProbe = new NativeHudRenderProbe(camera, target);
#endif
                yield return OrdinaryRenderFrames();
#if !UNITY_EDITOR
                nativeRenderProbe.RecordPhase("hud-visible");
#endif
                int visibleFrame = Time.frameCount;
                var regions = Regions();
                visible = ReadOrdinaryFrame();
                File.WriteAllBytes(Path.Combine(directory, name + "-hud-visible.png"), visible.EncodeToPNG());
                canvas.enabled = false;
                yield return OrdinaryRenderFrames();
#if !UNITY_EDITOR
                nativeRenderProbe.RecordPhase("hud-disabled");
#endif
                int hiddenFrame = Time.frameCount;
                hidden = ReadOrdinaryFrame();
                File.WriteAllBytes(Path.Combine(directory, name + "-hud-disabled.png"), hidden.EncodeToPNG());
                canvas.enabled = true;
                foreach (var text in texts) text.enabled = false;
                yield return OrdinaryRenderFrames();
#if !UNITY_EDITOR
                nativeRenderProbe.RecordPhase("hud-text-disabled");
#endif
                int textHiddenFrame = Time.frameCount;
                textHidden = ReadOrdinaryFrame();
                File.WriteAllBytes(Path.Combine(directory, name + "-hud-text-disabled.png"), textHidden.EncodeToPNG());
                foreach (var region in regions)
                {
                    region.changedPixels = CountChangedPixels(visible, hidden, region.viewportBounds);
                    region.textChangedPixels = CountChangedPixels(visible, textHidden, region.viewportBounds);
                }
                var nearCorners = new Vector3[4];
                camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), camera.nearClipPlane, Camera.MonoOrStereoscopicEye.Mono, nearCorners);
                var evidence = new HudEvidence
                {
                    pose = name, scene = ScenePath, width = Width, height = Height,
                    visibleFrame = visibleFrame, hiddenFrame = hiddenFrame, textHiddenFrame = textHiddenFrame,
                    cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation,
                    recordedCameraRotation = viewRotation, assignedCameraRotation = assignedViewRotation,
                    playerPosition = movement.transform.position, playerYaw = movement.transform.eulerAngles.y,
                    aspect = camera.aspect, fieldOfView = camera.fieldOfView, nearClip = camera.nearClipPlane,
                    planeDistance = canvas.planeDistance, renderMode = canvas.renderMode.ToString(),
                    canvasBounds = Bounds(canvas.GetComponent<RectTransform>()),
                    nearPlaneWorldCorners = nearCorners.Select(point => camera.transform.TransformPoint(point)).ToArray(),
                    worldPixelsWithHudDisabled = CountWorldPixels(hidden),
                    frozenActorCount = frozenActors, frozenActorPartCount = actorParts.Length,
                    frozenActorPartsUnchanged = actorParts.All(part => part.MatchesCurrentPose),
                    healthText = (string)hud.GetType().GetProperty("HealthText").GetValue(hud),
                    textOriginallyEnabled = textEnabled,
                    minimumChangedPixels = MinimumChangedPixels, regions = regions,
#if !UNITY_EDITOR
                    nativeRendering = nativeRenderProbe.Snapshot(),
#endif
                    scope = "Primary saved HUD at recorded camera/player transforms, persistent native-resolution target and ordinary frames. Actor/camera movement frozen as presentation setup; CastleActorPresentation child poses are saved and restored after disabling animation, preventing body/cape animation from contributing paired pixel changes. Canvas-disabled image retains world validation; text-specific image keeps Canvas/panels enabled and disables only actual Health and Controls Text components. Progression is not bypassed or tested. Difference floor establishes text graphic contribution, not human readability. No forced Camera.Render or RenderPipeline render request."
                };
                File.WriteAllText(Path.Combine(directory, name + "-evidence.json"), JsonUtility.ToJson(evidence, true));
                TestContext.WriteLine("Primary HUD rendered regions: " + JsonUtility.ToJson(evidence));

                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
                Assert.That(canvas.worldCamera, Is.SameAs(camera));
                Assert.That(evidence.healthText, Does.Contain("200"));
                Assert.That(hiddenFrame - visibleFrame, Is.GreaterThanOrEqualTo(2));
                Assert.That(textHiddenFrame - hiddenFrame, Is.GreaterThanOrEqualTo(2));
                Assert.That(textEnabled.All(value => value), Is.True, "The production health/control Text components must start enabled.");
                Assert.That(evidence.worldPixelsWithHudDisabled, Is.GreaterThan(1000),
                    "Ordinary frames must actually render the world, rather than an empty target; evidence=" + directory);
                Assert.That(evidence.frozenActorCount, Is.GreaterThan(0), "The real actor presentations must be frozen for the pixel pair.");
                Assert.That(evidence.frozenActorPartsUnchanged, Is.True,
                    "Actor animation must not create a false HUD pixel contribution; evidence=" + directory);
                Assert.That(camera.transform.position, Is.EqualTo(viewPosition), "The diagnostic must retain the recorded view.");
                Assert.That(camera.transform.rotation, Is.EqualTo(assignedViewRotation),
                    "Compare against the actual assigned pose, without recorded-quaternion normalization roundoff.");
                foreach (var region in regions)
                {
                    Assert.That(region.inFrontOfCamera && region.insideViewport, Is.True,
                        name + " " + region.name + " projected bounds; evidence=" + directory);
                    Assert.That(region.changedPixels, Is.GreaterThanOrEqualTo(MinimumChangedPixels),
                        name + " " + region.name + " must contribute actual HUD pixels with the world present; evidence=" + directory);
                    Assert.That(region.textChangedPixels, Is.GreaterThanOrEqualTo(MinimumChangedPixels),
                        name + " " + region.name + " must contribute actual text pixels independently of its panel background; evidence=" + directory);
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
#if !UNITY_EDITOR
                StopNativeRenderObservation();
#endif
                for (int i = 0; i < texts.Length; i++) if (texts[i] != null) texts[i].enabled = textEnabled[i];
                if (canvas != null) canvas.enabled = canvasEnabled;
                if (visible != null) Object.DestroyImmediate(visible);
                if (hidden != null) Object.DestroyImmediate(hidden);
                if (textHidden != null) Object.DestroyImmediate(textHidden);
            }
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
            Assert.That(sceneLoad.LoadedWithinDeadline && scene.IsValid() && scene.isLoaded, Is.True, "Primary HUD scene load exceeded 30 s.");
            Assert.That(SceneManager.SetActiveScene(scene), Is.True);
            yield return null; // Complete ordinary session/HUD startup before presentation setup.
            movement = Find("PlayerMovement"); follow = Find("CameraFollow"); hud = Find("LevelHUD");
            canvas = (Canvas)hud.GetType().GetProperty("DisplayCanvas").GetValue(hud);
            canvasEnabled = canvas.enabled;
            camera = canvas.worldCamera;
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.gameObject.scene, Is.EqualTo(scene));
            Assert.That(Object.FindObjectsByType<Canvas>()
                .Where(item => item.isActiveAndEnabled && item.gameObject.scene != scene)
                .Select(item => item.name).ToArray(), Is.Empty, "Other scene canvases must remain suspended.");
        }

        private static IEnumerator OrdinaryRenderFrames()
        {
            // Hold the bound viewport through normal Update/LateUpdate/render
            // before refreshing Canvas layout, then allow the refresh to render.
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
        }

        private RegionEvidence[] Regions()
        {
            var health = canvas.transform.Find("Status/Health").GetComponent<RectTransform>();
            var controls = canvas.transform.Find("Controls/Controls text").GetComponent<RectTransform>();
            var fullControls = Bounds(controls);
            var left = CopyRegion(fullControls, "Controls left half");
            var right = CopyRegion(fullControls, "Controls right half");
            float middle = fullControls.viewportBounds.center.x;
            left.viewportBounds = Rect.MinMaxRect(fullControls.viewportBounds.xMin, fullControls.viewportBounds.yMin, middle, fullControls.viewportBounds.yMax);
            right.viewportBounds = Rect.MinMaxRect(middle, fullControls.viewportBounds.yMin, fullControls.viewportBounds.xMax, fullControls.viewportBounds.yMax);
            var healthRegion = Bounds(health); healthRegion.name = "Health";
            return new[] { healthRegion, left, right };
        }

        private Behaviour TextAt(string path) => (Behaviour)canvas.transform.Find(path)
            .GetComponent(Type.GetType("UnityEngine.UI.Text, UnityEngine.UI", true));

        private RegionEvidence Bounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var projected = corners.Select(point => camera.WorldToViewportPoint(point)).ToArray();
            var bounds = Rect.MinMaxRect(projected.Min(point => point.x), projected.Min(point => point.y),
                projected.Max(point => point.x), projected.Max(point => point.y));
            return new RegionEvidence { name = rect.name, worldCorners = corners, viewportCorners = projected,
                viewportBounds = bounds, inFrontOfCamera = projected.All(point => point.z > camera.nearClipPlane),
                insideViewport = bounds.xMin >= -.002f && bounds.xMax <= 1.002f && bounds.yMin >= -.002f && bounds.yMax <= 1.002f };
        }

        private static RegionEvidence CopyRegion(RegionEvidence source, string name) => new RegionEvidence
        {
            name = name, worldCorners = source.worldCorners, viewportCorners = source.viewportCorners,
            viewportBounds = source.viewportBounds, inFrontOfCamera = source.inFrontOfCamera, insideViewport = source.insideViewport
        };

        private Texture2D ReadOrdinaryFrame()
        {
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                RenderTexture.active = target;
                pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply();
                return pixels;
            }
            catch
            {
                if (pixels != null) Object.DestroyImmediate(pixels);
                throw;
            }
            finally { RenderTexture.active = previous; }
        }

        private static int CountWorldPixels(Texture2D image) => image.GetPixels32().Count(pixel => pixel.r + pixel.g + pixel.b > 24);

        private ActorPartPose[] FreezeActorPresentation(out int actorCount)
        {
            var actors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(
                Type.GetType("CastleActorPresentation, Assembly-CSharp", true), true)).Cast<Behaviour>()
                .Where(item => item.isActiveAndEnabled).ToArray();
            actorCount = actors.Length;
            var allParts = new List<ActorPartPose>();
            foreach (var actor in actors)
            {
                var parts = actor.GetComponentsInChildren<Transform>(true).Where(part => part != actor.transform)
                    .Select(part => new ActorPartPose(part)).ToArray();
                // OnDisable restores bind poses. Put the saved child pose back
                // after unsubscribing the animation, preserving static geometry.
                actor.enabled = false;
                foreach (var part in parts) part.Restore();
                allParts.AddRange(parts);
            }
            return allParts.ToArray();
        }

        private static int CountChangedPixels(Texture2D visible, Texture2D hidden, Rect viewport)
        {
            int left = Mathf.Clamp(Mathf.CeilToInt(viewport.xMin * Width), 0, Width);
            int right = Mathf.Clamp(Mathf.FloorToInt(viewport.xMax * Width), 0, Width);
            int bottom = Mathf.Clamp(Mathf.CeilToInt(viewport.yMin * Height), 0, Height);
            int top = Mathf.Clamp(Mathf.FloorToInt(viewport.yMax * Height), 0, Height);
            var shown = visible.GetPixels32(); var absent = hidden.GetPixels32();
            int count = 0;
            for (int y = bottom; y < top; y++)
                for (int x = left; x < right; x++)
                {
                    int index = y * Width + x;
                    if (Math.Abs(shown[index].r - absent[index].r) + Math.Abs(shown[index].g - absent[index].g) +
                        Math.Abs(shown[index].b - absent[index].b) > 8) count++;
                }
            return count;
        }

        private void RestoreTarget()
        {
#if !UNITY_EDITOR
            // Teardown also releases subscriptions if UTF abandons the iterator.
            StopNativeRenderObservation();
#endif
            if (canvas != null) canvas.enabled = canvasEnabled;
            if (target == null) return;
            if (camera != null) { camera.targetTexture = originalTarget; camera.aspect = originalAspect; }
            RenderTexture.active = originalActive;
            target.Release(); Object.DestroyImmediate(target); target = null;
        }

        private Component Find(string name) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren(Type.GetType(name + ", Assembly-CSharp", true), true)).Single();

        [Serializable] private sealed class HudEvidence
        {
            public string pose, scene, renderMode, healthText, scope;
            public int width, height, visibleFrame, hiddenFrame, textHiddenFrame, minimumChangedPixels, worldPixelsWithHudDisabled;
            public int frozenActorCount, frozenActorPartCount;
            public bool frozenActorPartsUnchanged;
            public bool[] textOriginallyEnabled;
            public Vector3 cameraPosition, playerPosition;
            public Quaternion cameraRotation, recordedCameraRotation, assignedCameraRotation;
            public float playerYaw, aspect, fieldOfView, nearClip, planeDistance;
            public RegionEvidence canvasBounds;
            public Vector3[] nearPlaneWorldCorners;
            public RegionEvidence[] regions;
#if !UNITY_EDITOR
            public NativeHudRendering nativeRendering;
#endif
        }
#if !UNITY_EDITOR
        private void StopNativeRenderObservation()
        {
            nativeRenderProbe?.Dispose();
            nativeRenderProbe = null;
        }

        // Observe SRP callbacks only. These counts do not establish GPU completion,
        // presentation, frame performance or pixels. PNG assertions remain separate.
        private sealed class NativeHudRenderProbe : IDisposable
        {
            private const int FrameLimit = 256;
            private readonly Camera ownedCamera;
            private readonly RenderTexture ownedTarget;
            private readonly List<int> allBeginFrames = new List<int>(FrameLimit), allEndFrames = new List<int>(FrameLimit);
            private readonly List<int> ownedBeginFrames = new List<int>(FrameLimit), ownedEndFrames = new List<int>(FrameLimit);
            private readonly List<NativeHudRenderPhase> phases = new List<NativeHudRenderPhase>(3);
            private int allBeginCount, allEndCount, ownedBeginCount, ownedEndCount;
            private bool disposed, frameLimitReached;

            public NativeHudRenderProbe(Camera camera, RenderTexture texture)
            {
                ownedCamera = camera; ownedTarget = texture;
                RenderPipelineManager.beginCameraRendering += Begin;
                RenderPipelineManager.endCameraRendering += End;
            }

            private void Begin(ScriptableRenderContext context, Camera camera)
            {
                allBeginCount++; RecordFrame(allBeginFrames);
                if (camera == ownedCamera) { ownedBeginCount++; RecordFrame(ownedBeginFrames); }
            }

            private void End(ScriptableRenderContext context, Camera camera)
            {
                allEndCount++; RecordFrame(allEndFrames);
                if (camera == ownedCamera) { ownedEndCount++; RecordFrame(ownedEndFrames); }
            }

            private void RecordFrame(List<int> frames)
            {
                if (frames.Count < FrameLimit) frames.Add(Time.frameCount);
                else frameLimitReached = true;
            }

            public void RecordPhase(string name)
            {
                if (phases.Count == 3) return;
                phases.Add(new NativeHudRenderPhase
                {
                    name = name, frame = Time.frameCount,
                    cameraEnabled = ownedCamera != null && ownedCamera.enabled,
                    cameraActiveSelf = ownedCamera != null && ownedCamera.gameObject.activeSelf,
                    cameraActiveInHierarchy = ownedCamera != null && ownedCamera.gameObject.activeInHierarchy,
                    cameraIsActiveAndEnabled = ownedCamera != null && ownedCamera.isActiveAndEnabled,
                    targetIsCreated = ownedTarget != null && ownedTarget.IsCreated(),
                    targetMatchesCamera = ownedCamera != null && ownedCamera.targetTexture == ownedTarget,
                    applicationIsFocused = Application.isFocused, applicationRunInBackground = Application.runInBackground,
                    pipelineType = RenderPipelineManager.currentPipeline?.GetType().FullName ?? "<null>",
                    allBeginCount = allBeginCount, allEndCount = allEndCount,
                    ownedBeginCount = ownedBeginCount, ownedEndCount = ownedEndCount
                });
            }

            public NativeHudRendering Snapshot() => new NativeHudRendering
            {
                frameLimit = FrameLimit, frameLimitReached = frameLimitReached,
                allBeginCount = allBeginCount, allEndCount = allEndCount,
                ownedBeginCount = ownedBeginCount, ownedEndCount = ownedEndCount,
                allBeginFrames = allBeginFrames.ToArray(), allEndFrames = allEndFrames.ToArray(),
                ownedBeginFrames = ownedBeginFrames.ToArray(), ownedEndFrames = ownedEndFrames.ToArray(), phases = phases.ToArray()
            };

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                RenderPipelineManager.beginCameraRendering -= Begin;
                RenderPipelineManager.endCameraRendering -= End;
            }
        }

        [Serializable] private sealed class NativeHudRendering
        {
            public string scope = "Native-only read-only SRP camera callbacks and phase state; no render, focus, time, pose or gameplay changes. Callbacks are not GPU completion or display proof.";
            public int frameLimit, allBeginCount, allEndCount, ownedBeginCount, ownedEndCount;
            public bool frameLimitReached;
            public int[] allBeginFrames, allEndFrames, ownedBeginFrames, ownedEndFrames;
            public NativeHudRenderPhase[] phases;
        }

        [Serializable] private sealed class NativeHudRenderPhase
        {
            public string name, pipelineType;
            public int frame, allBeginCount, allEndCount, ownedBeginCount, ownedEndCount;
            public bool cameraEnabled, cameraActiveSelf, cameraActiveInHierarchy, cameraIsActiveAndEnabled;
            public bool targetIsCreated, targetMatchesCamera, applicationIsFocused, applicationRunInBackground;
        }
#endif
        private sealed class ActorPartPose
        {
            private readonly Transform part;
            private readonly Vector3 position, scale;
            private readonly Quaternion rotation;
            public ActorPartPose(Transform part)
            {
                this.part = part; position = part.localPosition; rotation = part.localRotation; scale = part.localScale;
            }
            public void Restore()
            {
                part.localPosition = position; part.localRotation = rotation; part.localScale = scale;
            }
            public bool MatchesCurrentPose => part != null && part.localPosition == position &&
                part.localScale == scale && part.localRotation == rotation;
        }
        [Serializable] private sealed class RegionEvidence
        {
            public string name;
            public Vector3[] worldCorners, viewportCorners;
            public Rect viewportBounds;
            public bool inFrontOfCamera, insideViewport;
            public int changedPixels, textChangedPixels;
        }
    }
}
#endif
