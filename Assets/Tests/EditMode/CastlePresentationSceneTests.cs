using System;
using System.Linq;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    public sealed class CastlePresentationSceneTests
    {
        private Scene scene;
        private bool opened;
        private const string Environment = "Assets/LevelPresentation/Environment/";

        [SetUp]
        public void OpenScene()
        {
            const string path = "Assets/Scenes/ForsakenCastle.unity";
            scene = SceneManager.GetSceneByPath(path); opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }
        [TearDown]
        public void CloseScene() { if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); }

        [Test]
        public void GothicEnvironmentUsesSavedDetailedMeshesWithinItsBudget()
        {
            var presentation = scene.GetRootGameObjects().Single(o => o.name == "Presentation");
            Assert.That(presentation.GetComponentsInChildren<Collider>(true), Is.Empty,
                "Presentation must leave collision and collider-based navigation unchanged.");
            Assert.That(presentation.GetComponentsInChildren<Component>(true).Any(c => c != null &&
                (c.GetType().Name == "NavMeshSurface" || c.GetType().Name == "NavMeshModifier")), Is.False);
            var meshes = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<MeshFilter>(true))
                .Where(f => AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(Environment, StringComparison.Ordinal)).ToArray();
            Assert.That(meshes.Length, Is.InRange(30, 180));
            int triangles = meshes.Sum(f => f.sharedMesh.triangles.Length / 3);
            Assert.That(triangles, Is.InRange(10000, 180000));
            foreach (var mesh in meshes)
            {
                Assert.That(mesh.sharedMesh.uv.Length, Is.EqualTo(mesh.sharedMesh.vertexCount), mesh.name);
                Assert.That(mesh.sharedMesh.normals.Length, Is.EqualTo(mesh.sharedMesh.vertexCount), mesh.name);
                Assert.That(mesh.GetComponent<Renderer>().sharedMaterial, Is.Not.Null, mesh.name);
            }
            Assert.That(meshes.Any(f => f.name.StartsWith("Mountains_", StringComparison.Ordinal)), Is.True);
            Assert.That(meshes.Any(f => f.name.StartsWith("PortalCrowns_", StringComparison.Ordinal)), Is.True);
            // The final room's eastern floor and wall meet at (12,-4,64).
            // Opposing chamfers previously exposed the blue background here.
            // Intersect saved visible triangles, not the unchanged colliders.
            foreach (float offset in new[] { -.02f, 0, .02f })
            {
                var ray = new Ray(new Vector3(11, -3 + offset, 64), new Vector3(1, -1, 0).normalized);
                Assert.That(HitsVisibleMesh(meshes, ray, 1.6f), Is.True,
                    "The visible floor/wall envelope leaks through the final room's eastern join, offset " + offset);
            }
        }

        private static bool HitsVisibleMesh(MeshFilter[] meshes, Ray ray, float maximumDistance)
        {
            foreach (var filter in meshes)
            {
                var renderer = filter.GetComponent<Renderer>();
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    !renderer.bounds.IntersectRay(ray, out float near) || near > maximumDistance) continue;
                var mesh = filter.sharedMesh; var vertices = mesh.vertices; var indices = mesh.triangles;
                var matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = matrix.MultiplyPoint3x4(vertices[indices[i]]);
                    Vector3 edge1 = matrix.MultiplyPoint3x4(vertices[indices[i + 1]]) - a;
                    Vector3 edge2 = matrix.MultiplyPoint3x4(vertices[indices[i + 2]]) - a;
                    Vector3 p = Vector3.Cross(ray.direction, edge2);
                    float determinant = Vector3.Dot(edge1, p);
                    if (determinant <= .0000001f) continue; // Reject backfaces, as the material does.
                    Vector3 relative = ray.origin - a;
                    float u = Vector3.Dot(relative, p) / determinant;
                    if (u < -.00001f || u > 1.00001f) continue;
                    Vector3 q = Vector3.Cross(relative, edge1);
                    float v = Vector3.Dot(ray.direction, q) / determinant;
                    if (v < -.00001f || u + v > 1.00001f) continue;
                    float distance = Vector3.Dot(edge2, q) / determinant;
                    if (distance >= 0 && distance <= maximumDistance) return true;
                }
            }
            return false;
        }

        [Test]
        public void GothicStoneSurfacesUseRepeatableMipmappedOriginalAlbedos()
        {
            foreach (var pair in new[] { new[] { "Limestone", "WeatheredLimestone" }, new[] { "Flagstone", "WornFlagstone" } })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(Environment + pair[0] + ".mat");
                Assert.That(material, Is.Not.Null);
                string path = "Assets/LevelPresentation/Textures/" + pair[1] + ".png";
                Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), Is.EqualTo(path));
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.maxTextureSize, Is.LessThanOrEqualTo(1024));
                Assert.That(importer.mipmapEnabled, Is.True); Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            }
        }

        [Test]
        public void GothicEmissiveMaterialsRetainTheirKeywordsAfterReimport()
        {
            const string actors = "Assets/LevelPresentation/Actors/Materials/";
            foreach (string path in new[] { actors + "Ember eyes.mat", actors + "Cursed violet.mat",
                Environment + "Flame.mat", Environment + "GlassBlue.mat", Environment + "GlassWine.mat", Environment + "Rune.mat",
                actors + "Worn silver.mat", Environment + "Limestone.mat" })
            {
                bool emissive = !path.EndsWith("Worn silver.mat", StringComparison.Ordinal) &&
                    !path.EndsWith("Limestone.mat", StringComparison.Ordinal);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.That(material, Is.Not.Null, path);
                // Exercise the actual shader validation that can remove an
                // inconsistent keyword during later imports or player builds.
                var editor = UnityEditor.Editor.CreateEditor(material) as MaterialEditor;
                try
                {
                    Assert.That(editor, Is.Not.Null, path);
                    Assert.That(editor.customShaderGUI, Is.Not.Null, path);
                    editor.customShaderGUI.ValidateMaterial(material);
                    Assert.That(material.GetColor("_EmissionColor").maxColorComponent > 0, Is.EqualTo(emissive), path);
                    Assert.That((material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive) != 0,
                        Is.EqualTo(emissive), path);
                    Assert.That((material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) != 0,
                        Is.EqualTo(!emissive), path);
                    Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.EqualTo(emissive), path);
                }
                finally { if (editor != null) UnityEngine.Object.DestroyImmediate(editor); }
            }
        }

        [Test]
        public void GothicGateAndRelicMeshesRemainBoundToTheirExistingStateComponents()
        {
            var components = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
            foreach (var gate in components.Where(c => c != null && c.GetType().Name == "ProgressionGate"))
            {
                var data = new SerializedObject(gate); var visuals = data.FindProperty("closedVisuals");
                var from = (LevelRoom)data.FindProperty("from").intValue;
                var to = (LevelRoom)data.FindProperty("to").intValue;
                bool Connects(LevelRoom a, LevelRoom b) => (from == a && to == b) || (from == b && to == a);
                var materials = new System.Collections.Generic.List<string>();
                Assert.That(visuals.arraySize, Is.GreaterThanOrEqualTo(2), gate.name);
                for (int i = 0; i < visuals.arraySize; i++)
                {
                    var renderer = visuals.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
                    Assert.That(renderer, Is.Not.Null); Assert.That(renderer.transform.IsChildOf(gate.transform), Is.True);
                    Assert.That(renderer.enabled, Is.EqualTo(gate.GetComponent<BoxCollider>().enabled));
                    materials.Add(AssetDatabase.GetAssetPath(renderer.sharedMaterial));
                }
                Assert.That(gate.transform.Find("Gate panel").GetComponent<Renderer>().enabled, Is.False);
                if (Connects(LevelRoom.Library, LevelRoom.Catacombs))
                {
                    Assert.That(materials, Does.Contain(Environment + "Oak.mat"), "The library passage is concealed by a bookcase.");
                    Assert.That(materials, Does.Contain(Environment + "Pages.mat"));
                    Assert.That(materials, Does.Not.Contain(Environment + "Iron.mat"));
                }
                if (Connects(LevelRoom.Catacombs, LevelRoom.BonusRoom) || Connects(LevelRoom.BonusRoom, LevelRoom.ThroneRoom))
                {
                    Assert.That(materials, Does.Contain(Environment + "Limestone.mat"), "The secret branches are concealed by masonry.");
                    Assert.That(materials, Does.Contain(Environment + "CarvedStone.mat"));
                    Assert.That(materials, Does.Not.Contain(Environment + "Iron.mat"));
                }
            }
            foreach (var mechanism in components.Where(c => c != null && c.GetType().Name == "MechanismFeedback"))
            {
                var moving = mechanism.transform.Find("Moving mechanism");
                Assert.That(moving, Is.Not.Null);
                var originals = moving.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.name == "Marked part" || r.name == "Rune plaque" || r.name.StartsWith("Rune ", StringComparison.Ordinal)).ToArray();
                Assert.That(originals, Is.Not.Empty, mechanism.name);
                Assert.That(originals.All(r => !r.enabled), Is.True,
                    "Old mechanism primitives must remain hidden even when authored under inactive Gameplay: " + mechanism.name);
            }
            var relic = components.Single(c => c != null && c.GetType().Name == "MechanismFeedback" &&
                new SerializedObject(c).FindProperty("hideWhenConsumed").boolValue);
            var rewards = new SerializedObject(relic).FindProperty("rewardVisuals");
            Assert.That(rewards.arraySize, Is.GreaterThanOrEqualTo(2));
            for (int i = 0; i < rewards.arraySize; i++)
                Assert.That(rewards.GetArrayElementAtIndex(i).objectReferenceValue as Renderer, Is.Not.Null);
        }
    }
}
