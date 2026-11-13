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
            // DOCX 4: sample the saved luminous inscription itself. Its center
            // stem and ring are visible, while spaces between strokes expose
            // the existing floor instead of filling a solid blockout square.
            var cult = meshes.Single(f => f.name == "Masonry_0_1_Upper_Rune");
            var cultCenter = new Vector3(18.75f, -3.967f, 46.5f);
            foreach (var offset in new[] { Vector3.zero, Vector3.right * .42f })
                Assert.That(HitsVisibleMesh(new[] { cult }, new Ray(cultCenter + offset + Vector3.up, Vector3.down), 1.1f),
                    Is.True, "The cult inscription must retain its stem and enclosing ring.");
            foreach (var offset in new[] { new Vector3(.25f, 0, -.24f), new Vector3(.43f, 0, .43f) })
                Assert.That(HitsVisibleMesh(new[] { cult }, new Ray(cultCenter + offset + Vector3.up, Vector3.down), 1.1f),
                    Is.False, "The cult inscription must have visible spaces between its strokes.");

            // DOCX 7: the terminal threshold frames an outside view. Check
            // rendered triangles separately from the unchanged solid end wall.
            var exit = meshes.Single(f => f.name == "Masonry_0_3_Upper_Limestone");
            Assert.That(HitsVisibleMesh(new[] { exit }, new Ray(new Vector3(8, -2, 76), Vector3.forward), 5), Is.False,
                "The exit's central sightline must be an opening rather than a blank visible wall.");
            foreach (var origin in new[] { new Vector3(9.8f, -2, 76), new Vector3(8, .35f, 76) })
                Assert.That(HitsVisibleMesh(new[] { exit }, new Ray(origin, Vector3.forward), 5), Is.True,
                    "Stone jambs and the pointed opening's upper masonry must still frame the exit.");
            Assert.That(HitsVisibleMesh(new[] { exit }, new Ray(new Vector3(8, 1.5f, 78.2f), Vector3.down), 1.1f), Is.True,
                "The opening wall's upper masonry must have a visible top cap between its jambs.");
            var geometry = scene.GetRootGameObjects().Single(o => o.name == "Geometry");
            var exitCollider = geometry.GetComponentsInChildren<BoxCollider>(true)
                .Single(c => c.name == "UpperStructure_Wall_027");
            Assert.That(exitCollider.enabled, Is.True, "The visual opening must preserve the physical world boundary.");
            Assert.That(exitCollider.transform.position, Is.EqualTo(new Vector3(8, -1.75f, 78.2f)));
            Assert.That(Vector3.Scale(exitCollider.size, exitCollider.transform.lossyScale), Is.EqualTo(new Vector3(4, 4.5f, .4f)));
        }

        [Test]
        public void ThroneRuinedColumnsHaveFracturedTopsInsideOriginalPhysicalBounds()
        {
            // DOCX 4 explicitly asks for destroyed throne-room columns. Sample
            // actual upward-facing saved triangles, not generator parameters,
            // collider tops or every vertex on the old decorative bevel.
            var geometry = scene.GetRootGameObjects().Single(o => o.name == "Geometry");
            var stone = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<MeshFilter>(true))
                .Single(f => f.name == "Throne_CarvedStone");
            Assert.That(AssetDatabase.GetAssetPath(stone.sharedMesh), Is.EqualTo(Environment + "Throne_CarvedStone.asset"));
            foreach (float x in new[] { 5.2f, 10.8f })
            {
                string name = x < 8 ? "Throne_Column_-2.8_-2.8" : "Throne_Column_2.8_-2.8";
                var physical = geometry.GetComponentsInChildren<BoxCollider>(true).Single(c => c.name == name);
                Assert.That(physical.enabled && physical.gameObject.activeInHierarchy, Is.True, name);
                var bounds = physical.bounds;
                Assert.That(Vector3.Distance(bounds.center, new Vector3(x, 1.2f, 29.2f)), Is.LessThan(.0001f), name);
                Assert.That(Vector3.Distance(bounds.size, new Vector3(.5f, 1.8f, .5f)), Is.LessThan(.0001f),
                    "The existing physical pillar must retain its complete bounds: " + name);

                float originY = bounds.max.y + .2f;
                var heights = new float[8];
                for (int i = 0; i < heights.Length; i++)
                {
                    float angle = i * Mathf.PI / 4;
                    var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (bounds.extents.x * .48f);
                    var origin = new Vector3(bounds.center.x, originY, bounds.center.z) + offset;
                    float distance = NearestVisibleMeshHit(new[] { stone }, new Ray(origin, Vector3.down), bounds.size.y + .2f);
                    Assert.That(float.IsPositiveInfinity(distance), Is.False, "The fractured shaft needs a solid visible top: " + name);
                    heights[i] = originY - distance;
                    Assert.That(heights[i], Is.InRange(bounds.min.y + bounds.size.y * .6f, bounds.max.y + .0001f), name);
                }
                TestContext.WriteLine(name + " sampled top range: " + heights.Min() + " to " + heights.Max());
                Assert.That(heights.Max() - heights.Min(), Is.GreaterThan(.12f),
                    "A broken top must be visibly uneven around the shaft, rather than a flat symmetrical capital: " + name);

                // All samples lie on the same radial ring. The original intact
                // capital covers this wide upper rim; a broken shaft must not.
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4;
                    var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (bounds.extents.x * .9f);
                    var origin = new Vector3(bounds.center.x, originY, bounds.center.z) + offset;
                    Assert.That(HitsVisibleMesh(new[] { stone }, new Ray(origin, Vector3.down), .2f + bounds.size.y * .32f),
                        Is.False, "The shortened column must not retain its enlarged upper capital: " + name);
                }
                var upperVertices = stone.sharedMesh.vertices.Select(stone.transform.TransformPoint)
                    .Where(p => Mathf.Abs(p.x - bounds.center.x) < 1 && Mathf.Abs(p.z - bounds.center.z) < 1 &&
                        p.y > bounds.min.y + .4f).ToArray();
                Assert.That(upperVertices, Is.Not.Empty);
                foreach (var point in upperVertices)
                    Assert.That(bounds.Contains(point) || bounds.SqrDistance(point) < .00000001f, Is.True,
                        "Fractured stone must stay inside the original physical pillar footprint: " + name + " / " + point);
            }

            // The two taller columns retain their original flat, wide capitals.
            foreach (float x in new[] { 5.2f, 10.8f })
            {
                string name = x < 8 ? "Throne_Column_-2.8_2.8" : "Throne_Column_2.8_2.8";
                var physical = geometry.GetComponentsInChildren<BoxCollider>(true).Single(c => c.name == name);
                var bounds = physical.bounds;
                Assert.That(Vector3.Distance(bounds.center, new Vector3(x, 2.05f, 34.8f)), Is.LessThan(.0001f), name);
                Assert.That(Vector3.Distance(bounds.size, new Vector3(.5f, 3.5f, .5f)), Is.LessThan(.0001f), name);
                foreach (var offset in new[] { Vector3.zero, Vector3.left * .225f, Vector3.right * .225f,
                    Vector3.forward * .225f, Vector3.back * .225f })
                {
                    var origin = new Vector3(bounds.center.x, bounds.max.y + .2f, bounds.center.z) + offset;
                    Assert.That(NearestVisibleMeshHit(new[] { stone }, new Ray(origin, Vector3.down), .25f),
                        Is.EqualTo(.2f).Within(.0001f), "The taller column's complete capital must remain unchanged: " + name);
                }
            }
        }

        private static bool HitsVisibleMesh(MeshFilter[] meshes, Ray ray, float maximumDistance) =>
            !float.IsPositiveInfinity(NearestVisibleMeshHit(meshes, ray, maximumDistance));

        private static float NearestVisibleMeshHit(MeshFilter[] meshes, Ray ray, float maximumDistance)
        {
            float nearest = float.PositiveInfinity;
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
                    if (distance >= 0 && distance <= maximumDistance && distance < nearest) nearest = distance;
                }
            }
            return nearest;
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
