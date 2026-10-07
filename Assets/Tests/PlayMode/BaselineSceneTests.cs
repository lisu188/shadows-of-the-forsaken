using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public sealed class BaselineSceneTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private Scene scene;
        private OwnedSceneLoad sceneLoad;

        [UnitySetUp]
        public IEnumerator LoadBaselineScene()
        {
            sceneLoad = null;
            scene = default;
            OwnedSceneLoad.RequireNoPendingCleanup();
            scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                sceneLoad = new OwnedSceneLoad();
                yield return sceneLoad.Load(ScenePath, () => SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive));
                scene = sceneLoad.Scene;
                Assert.That(sceneLoad.LoadedWithinDeadline && scene.IsValid() && scene.isLoaded, Is.True, "Baseline scene load timed out");
            }
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnloadBaselineScene()
        {
            // A scene that was already open belongs to the surrounding editor.
            if (sceneLoad != null) yield return sceneLoad.Cleanup();
            Assert.That(sceneLoad?.CleanupFailure, Is.Null, "Baseline scene cleanup failed");
        }

        [UnityTest]
        public IEnumerator BaselineSceneRunsWithoutMissingComponents()
        {
            for (var frame = 0; frame < 3; frame++)
                yield return null;
            var roots = scene.GetRootGameObjects();
            Assert.That(roots, Is.Not.Empty);
            foreach (var root in roots)
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    Assert.That(component != null, Is.True, $"Missing component under {root.name}");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BaselineCameraAndDirectionalLightAreActive()
        {
            yield return null;
            var roots = scene.GetRootGameObjects();
            var cameras = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .Where(camera => camera.isActiveAndEnabled && camera.CompareTag("MainCamera")).ToArray();
            Assert.That(cameras.Length, Is.EqualTo(1));
            var listener = cameras[0].GetComponent<AudioListener>();
            Assert.That(listener != null && listener.isActiveAndEnabled, Is.True);
            Assert.That(roots.SelectMany(root => root.GetComponentsInChildren<Light>(true))
                .Any(light => light.isActiveAndEnabled && light.type == LightType.Directional), Is.True);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
