using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    /// <summary>Actual scene-owned Canvas layout and rendered timer at supported viewport shapes.</summary>
    public sealed class LevelHudLayoutTests
    {
        private const string ScenePath = "Assets/Scenes/ForsakenCastle.unity";
        private readonly List<GameObject> suspendedRoots = new List<GameObject>();
        private readonly InputTestFixture inputFixture = new InputTestFixture();
        private InputSettings.UpdateMode previousInputMode;
        private Scene scene;
        private Canvas canvas;
        private Camera camera;

        [UnitySetUp]
        public IEnumerator LoadCastle()
        {
            var levelType = Type.GetType("ShadowsOfTheForsaken.Level.ForsakenLevel, Assembly-CSharp", true);
            suspendedRoots.Clear();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                var otherScene = SceneManager.GetSceneAt(index);
                if (!otherScene.isLoaded || (otherScene.path != ScenePath && otherScene.path != "Assets/Scenes/SampleScene.unity")) continue;
                foreach (var root in otherScene.GetRootGameObjects())
                {
                    if (root.name.IndexOf("TestRunner", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    bool isLevelRoot = root.GetComponent(levelType) != null || root.name == "Shadows of the Forsaken";
                    bool isGeometryRoot = root.GetComponent<Camera>() != null || root.GetComponent<Light>() != null || root.GetComponent<Collider>() != null;
                    if (root.activeSelf && (isLevelRoot || isGeometryRoot))
                    {
                        suspendedRoots.Add(root);
                        root.SetActive(false);
                    }
                }
            }
            inputFixture.Setup();
            previousInputMode = InputSystem.settings.updateMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            var operation = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            Assert.That(operation, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 60;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(operation.isDone, Is.True, "Castle scene load timed out.");
            scene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
            Assert.That(scene.path, Is.EqualTo(ScenePath));
            var level = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(levelType, true)).Single();
            canvas = level.GetComponentInChildren<Canvas>();
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            camera = canvas.worldCamera;
            Assert.That(camera, Is.Not.Null);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnloadCastle()
        {
            try
            {
                if (scene.IsValid() && scene.isLoaded)
                {
                    var operation = SceneManager.UnloadSceneAsync(scene);
                    float deadline = Time.realtimeSinceStartup + 60;
                    while (operation != null && !operation.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(operation == null || operation.isDone, Is.True, "Castle scene unload timed out.");
                }
                InputSystem.settings.updateMode = previousInputMode;
            }
            finally
            {
                try { inputFixture.TearDown(); }
                finally
                {
                    foreach (var root in suspendedRoots) if (root != null) root.SetActive(true);
                    suspendedRoots.Clear();
                }
            }
        }

        [UnityTest]
        public IEnumerator ControlsAndRenderedTimerFitFourByThreeViewport() => VerifyViewport(1024, 768);

        [UnityTest]
        public IEnumerator ControlsAndRenderedTimerFitSixteenByTenViewport() => VerifyViewport(1280, 800);

        [UnityTest]
        public IEnumerator ControlsAndRenderedTimerFitSixteenByNineViewport() => VerifyViewport(1280, 720);

        [UnityTest]
        public IEnumerator ControlsAndRenderedTimerFitUltrawideViewport() => VerifyViewport(2560, 1080);

        private IEnumerator VerifyViewport(int width, int height)
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "HUD layout verification requires the actual rendered camera Canvas.");
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var destination = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1, useMipMap = false, autoGenerateMips = false
            };
            Texture2D visible = null, hidden = null;
            Behaviour timer = null;
            bool timerEnabled = false;
            try
            {
                Assert.That(destination.Create(), Is.True);
                camera.targetTexture = destination;
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();

                var canvasRect = canvas.GetComponent<RectTransform>();
                Assert.That(canvasRect.rect.width / canvasRect.rect.height,
                    Is.EqualTo((float)width / height).Within(.002f), "The real Canvas must use the requested camera viewport.");
                var controls = canvas.transform.Find("Controls").GetComponent<RectTransform>();
                var keys = controls.Find("Keys").GetComponent<RectTransform>();
                var elapsed = controls.Find("Elapsed time").GetComponent<RectTransform>();
                var edge = controls.Find("Gilt edge").GetComponent<RectTransform>();
                Rect controlsBounds = ViewportBounds(controls);
                Rect keysBounds = ViewportBounds(keys);
                Rect elapsedBounds = ViewportBounds(elapsed);
                Rect edgeBounds = ViewportBounds(edge);

                Assert.That(controlsBounds.xMin, Is.EqualTo(0).Within(.002f), "Controls begin at the viewport's left edge.");
                Assert.That(controlsBounds.xMax, Is.EqualTo(1).Within(.002f), "Controls fill the viewport width.");
                AssertInsideViewport(controlsBounds, "Controls");
                AssertInsideViewport(keysBounds, "Keys");
                AssertInsideViewport(elapsedBounds, "Elapsed time");
                Assert.That(keysBounds.xMax, Is.LessThan(elapsedBounds.xMin), "Control hints must leave a gap before the timer.");
                Assert.That(edgeBounds.xMin, Is.EqualTo(controlsBounds.xMin).Within(.002f));
                Assert.That(edgeBounds.xMax, Is.EqualTo(controlsBounds.xMax).Within(.002f), "The gilt edge must resize with Controls.");

                timer = (Behaviour)elapsed.GetComponent(Type.GetType("UnityEngine.UI.Text, UnityEngine.UI", true));
                timerEnabled = timer.enabled;
                Assert.That(timerEnabled, Is.True);
                Assert.That((string)timer.GetType().GetProperty("text").GetValue(timer), Does.StartWith("Time "));
                visible = Capture(destination);
                timer.enabled = false;
                Canvas.ForceUpdateCanvases();
                hidden = Capture(destination); // Same frame and camera; only the actual timer graphic changes.
                int changedPixels = CountChangedPixels(visible, hidden, elapsedBounds);
                Assert.That(changedPixels, Is.GreaterThan(8), "The elapsed text must actually render inside its on-screen rectangle.");
                Debug.Log("HUD viewport " + width + "x" + height + ": timer bounds " + elapsedBounds +
                    ", rendered timer pixels " + changedPixels);
                string evidence = Path.Combine(Path.GetDirectoryName(Application.dataPath), "artifacts", "review-fixes-20261006", "hud-layout");
                Directory.CreateDirectory(evidence);
                File.WriteAllBytes(Path.Combine(evidence, "hud-" + width + "x" + height + ".png"), visible.EncodeToPNG());
            }
            finally
            {
                if (timer != null) timer.enabled = timerEnabled;
                camera.targetTexture = previousTarget;
                Canvas.ForceUpdateCanvases();
                RenderTexture.active = previousActive;
                if (visible != null) Object.Destroy(visible);
                if (hidden != null) Object.Destroy(hidden);
                destination.Release();
                Object.Destroy(destination);
            }
        }

        private Rect ViewportBounds(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var points = corners.Select(corner => camera.WorldToViewportPoint(corner)).ToArray();
            Assert.That(points.All(point => point.z > 0), Is.True, rect.name + " must lie in front of its Canvas camera.");
            return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y),
                points.Max(point => point.x), points.Max(point => point.y));
        }

        private static void AssertInsideViewport(Rect bounds, string name)
        {
            Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(-.002f), name + " left edge");
            Assert.That(bounds.xMax, Is.LessThanOrEqualTo(1.002f), name + " right edge");
            Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(-.002f), name + " bottom edge");
            Assert.That(bounds.yMax, Is.LessThanOrEqualTo(1.002f), name + " top edge");
        }

        private Texture2D Capture(RenderTexture destination)
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = destination });
            RenderTexture.active = destination;
            var image = new Texture2D(destination.width, destination.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, destination.width, destination.height), 0, 0);
            image.Apply();
            return image;
        }

        private static int CountChangedPixels(Texture2D visible, Texture2D hidden, Rect bounds)
        {
            int left = Mathf.Clamp(Mathf.FloorToInt(bounds.xMin * visible.width) - 2, 0, visible.width - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt(bounds.xMax * visible.width) + 2, 0, visible.width - 1);
            int bottom = Mathf.Clamp(Mathf.FloorToInt(bounds.yMin * visible.height) - 2, 0, visible.height - 1);
            int top = Mathf.Clamp(Mathf.CeilToInt(bounds.yMax * visible.height) + 2, 0, visible.height - 1);
            var shownPixels = visible.GetPixels32();
            var hiddenPixels = hidden.GetPixels32();
            int changed = 0;
            for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                {
                    int index = y * visible.width + x;
                    var shown = shownPixels[index];
                    var absent = hiddenPixels[index];
                    if (Math.Abs(shown.r - absent.r) + Math.Abs(shown.g - absent.g) + Math.Abs(shown.b - absent.b) > 8) changed++;
                }
            return changed;
        }
    }
}
