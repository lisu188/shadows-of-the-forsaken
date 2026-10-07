using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    public sealed class ProjectBaselineTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void EditorVersionMatchesPin()
        {
            Assert.That(Application.unityVersion, Is.EqualTo("6000.6.3f1"));
        }

        [Test]
        public void BaselineSceneIsEnabledInBuildSettings()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null);
            Assert.That(EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == ScenePath), Is.True);
        }

        [TestCase("Assets/PlayerMovement.cs", "PlayerMovement", "963b3f782c6d71942ad1e73d117b2820")]
        [TestCase("Assets/CameraFollow.cs", "CameraFollow", "9191262690f98974abc0076595479fd6")]
        public void RuntimeScriptImportsWithOriginalGuid(string path, string className, string guid)
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Assert.That(script, Is.Not.Null);
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            var type = script.GetClass();
            Assert.That(type, Is.Not.Null);
            Assert.That(type.Name, Is.EqualTo(className));
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(type), Is.True);
        }

        [Test]
        public void BaselineSceneHasNoMissingComponents()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.That(roots, Is.Not.Empty);
                foreach (var root in roots)
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                        Assert.That(component != null, Is.True, $"Missing component under {root.name}");
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void MaterialsHaveResolvableShaders()
        {
            var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            Assert.That(guids, Is.Not.Empty);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.That(material, Is.Not.Null, path);
                Assert.That(material.shader != null, Is.True, path);
                Assert.That(material.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"), path);
            }
        }
    }
}
