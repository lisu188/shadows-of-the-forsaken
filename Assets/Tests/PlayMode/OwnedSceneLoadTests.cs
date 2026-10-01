using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public sealed class OwnedSceneLoadTests
    {
        private OwnedSceneLoad sceneLoad;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (sceneLoad == null) yield break;
            yield return sceneLoad.Cleanup();
            Assert.That(sceneLoad.CleanupFailure, Is.Null);
        }

        [UnityTest]
        public IEnumerator TimedOutRealSceneLoadIsDrainedWithoutUnloadingPreexistingScenes()
        {
            const string path = "Assets/Scenes/SampleScene.unity";
            var before = new HashSet<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++) before.Add(SceneManager.GetSceneAt(i));
            sceneLoad = new OwnedSceneLoad();
            yield return sceneLoad.Load(path, () =>
            {
                var operation = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
                operation.allowSceneActivation = false;
                return operation;
            }, 0f);
            Assert.That(sceneLoad.LoadedWithinDeadline, Is.False, "An activation-held load must fail its deadline.");

            yield return sceneLoad.Cleanup();
            Assert.That(sceneLoad.CleanupFailure, Is.Null);
            Assert.That(sceneLoad.Scene.IsValid() && sceneLoad.Scene.isLoaded, Is.False);
            var after = new HashSet<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++) after.Add(SceneManager.GetSceneAt(i));
            Assert.That(after.SetEquals(before), Is.True, "Only the fixture's newly loaded scene may be unloaded.");
            OwnedSceneLoad.RequireNoPendingCleanup();
            LogAssert.NoUnexpectedReceived();
        }
    }
}
