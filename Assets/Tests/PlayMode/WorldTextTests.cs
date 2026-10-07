using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    /// <summary>DOCX §4/7/8: room inscriptions belong to the visible castle geometry.</summary>
    public sealed class WorldTextTests
    {
        private readonly ForsakenLevelTests foundation = new ForsakenLevelTests();

        [UnitySetUp]
        public IEnumerator LoadLevel() { yield return foundation.LoadLevel(); }

        [UnityTearDown]
        public IEnumerator UnloadLevel() { yield return foundation.UnloadLevel(); }

        private T SceneField<T>(string name) => (T)typeof(ForsakenLevelTests)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(foundation);

        private static Color32[] Capture(Camera camera, string path)
        {
            var destination = new RenderTexture(1280, 720, 24);
            var priorTarget = camera.targetTexture;
            var priorActive = RenderTexture.active;
            Texture2D image = null;
            try
            {
                destination.Create();
                camera.targetTexture = destination;
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera,
                    new UniversalRenderPipeline.SingleCameraRequest { destination = destination });
                RenderTexture.active = destination;
                image = new Texture2D(destination.width, destination.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, destination.width, destination.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                return image.GetPixels32();
            }
            finally
            {
                camera.targetTexture = priorTarget;
                RenderTexture.active = priorActive;
                if (image != null) Object.Destroy(image);
                destination.Release();
                Object.Destroy(destination);
                Canvas.ForceUpdateCanvases();
            }
        }

        private static int DifferentPixels(Color32[] withText, Color32[] withoutText)
        {
            Assert.That(withText.Length, Is.EqualTo(withoutText.Length));
            return withText.Where((pixel, index) =>
                Math.Abs(pixel.r - withoutText[index].r) + Math.Abs(pixel.g - withoutText[index].g) +
                Math.Abs(pixel.b - withoutText[index].b) > 8).Count();
        }

        [UnityTest]
        public IEnumerator WorldInscriptionRendersNearbyAndIsOccludedByTheClosedLibraryDoor()
        {
            var level = SceneField<Component>("level");
            var progression = SceneField<LevelProgressionController>("progression");
            var token = progression.Snapshot.SessionId;
            // Test-only viewpoint setup: leave LibraryOpened unset so its opaque exit remains closed.
            Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, token), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.FirstEnemyDefeated, token), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.Puzzle, token), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.MainPuzzleSolved, token), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, token), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.ThroneRoom, token), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.MinibossDefeated, token), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.Library, token), Is.True);

            var inscription = level.GetComponentsInChildren<TextMesh>().Single(text => text.text == "Concealed cult lever");
            var renderer = inscription.GetComponent<MeshRenderer>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Shadows/WorldText"));
            Assert.That(renderer.sharedMaterial, Is.Not.SameAs(font.material));
            Assert.That(renderer.sharedMaterial.mainTexture, Is.SameAs(font.material.mainTexture),
                "The scene-owned material must follow the dynamic font atlas.");

            var camera = SceneField<Component>("follow").GetComponent<Camera>();
            var originalPosition = camera.transform.position;
            var originalRotation = camera.transform.rotation;
            float oldScale = Time.timeScale;
            int occludedDifferences = -1, visibleDifferences = -1;
            string output = Path.Combine(Application.dataPath, "..", "artifacts", "world-text-render");
            Directory.CreateDirectory(output);
            try
            {
                Time.timeScale = 0;
                camera.transform.position = new Vector3(24, 2.73f, 54.12f);
                camera.transform.LookAt(renderer.bounds.center);
                Physics.SyncTransforms();
                var offset = renderer.bounds.center - camera.transform.position;
                var barriers = Physics.RaycastAll(camera.transform.position, offset.normalized, offset.magnitude,
                    ~0, QueryTriggerInteraction.Ignore);
                Assert.That(barriers.Any(hit => hit.collider.name == "Hidden stone door"), Is.True,
                    "The occlusion capture must contain the actual closed library exit between camera and label.");
                var occludedWithText = Capture(camera, Path.Combine(output, "library-door-with-label.png"));
                renderer.enabled = false;
                var occludedWithoutText = Capture(camera, Path.Combine(output, "library-door-without-label.png"));
                occludedDifferences = DifferentPixels(occludedWithText, occludedWithoutText);

                camera.transform.position = new Vector3(27.5f, 1.8f, 69);
                camera.transform.LookAt(renderer.bounds.center);
                renderer.enabled = true;
                var nearbyWithText = Capture(camera, Path.Combine(output, "nearby-with-label.png"));
                renderer.enabled = false;
                var nearbyWithoutText = Capture(camera, Path.Combine(output, "nearby-without-label.png"));
                visibleDifferences = DifferentPixels(nearbyWithText, nearbyWithoutText);
                Assert.That(visibleDifferences, Is.GreaterThan(40),
                    "Depth testing must preserve readable nearby text; hiding every inscription is not a fix.");
                Assert.That(occludedDifferences, Is.EqualTo(0),
                    "Toggling a fully occluded inscription must not alter the GPU-rendered closed-door view.");
            }
            finally
            {
                renderer.enabled = true;
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                Time.timeScale = oldScale;
                File.WriteAllText(Path.Combine(output, "world-text.json"), JsonUtility.ToJson(new Evidence
                {
                    unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                    occludedLabelChangedPixels = occludedDifferences, nearbyLabelChangedPixels = visibleDifferences
                }, true));
            }
            yield return null;
        }

        [Serializable]
        private sealed class Evidence
        {
            public string unityVersion, graphicsDevice;
            public int occludedLabelChangedPixels, nearbyLabelChangedPixels;
            public string method = "Actual ForsakenRuntimeCastle scene and closed library door. Paired same-frame URP GPU render requests differ only by the concealed lever TextMesh renderer. Test-only direct progression setup and camera viewpoints; no progression or timing acceptance claim.";
        }
    }
}
