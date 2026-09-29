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
        private bool loadedForTest;

        [UnitySetUp]
        public IEnumerator LoadBaselineScene()
        {
            scene = SceneManager.GetSceneByPath(ScenePath);
            loadedForTest = !scene.IsValid() || !scene.isLoaded;
            if (loadedForTest)
            {
                var operation = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
                Assert.That(operation, Is.Not.Null);
                var deadline = Time.realtimeSinceStartup + 30f;
                while (!operation.isDone && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(operation.isDone, Is.True, "Baseline scene load timed out");
                scene = SceneManager.GetSceneByPath(ScenePath);
            }
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnloadBaselineScene()
        {
            if (!loadedForTest || !scene.IsValid() || !scene.isLoaded)
                yield break;
            var operation = SceneManager.UnloadSceneAsync(scene);
            Assert.That(operation, Is.Not.Null);
            var deadline = Time.realtimeSinceStartup + 30f;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(operation.isDone, Is.True, "Baseline scene unload timed out");
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
