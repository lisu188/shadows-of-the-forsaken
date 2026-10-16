#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if !UNITY_EDITOR
using UnityEngine.Rendering.Universal;
#endif

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // DOCX 3/7 test evidence only. Preserve the exact inactive-pass guards used
    // by FullCastleRouteTests without changing its route/capture implementation.
    internal static class NativePresentationPipelineDiagnostics
    {
        public static void Prepare(Camera camera, Scene scene)
        {
#if !UNITY_EDITOR
            var additional = camera.GetComponent<UniversalAdditionalCameraData>();
            Assert.That(additional, Is.Not.Null, "The saved camera must have URP camera data.");
            Assert.That(additional.renderPostProcessing, Is.False,
                "Inactive-pass startup diagnostics cannot be expected for a post-processing camera.");
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(pipeline, Is.Not.Null, "The saved scene must use its pinned URP pipeline.");
            Assert.That(GraphicsSettings.TryGetRenderPipelineSettings<URPDefaultVolumeProfileSettings>(out var defaults), Is.True);
            Assert.That(defaults.volumeProfile, Is.Not.Null, "The configured URP default volume profile must exist.");
            AssertEffectsInactive(defaults.volumeProfile, true);
            AssertEffectsInactive(pipeline.volumeProfile, false);
            foreach (var volume in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Volume>(true)))
                if (volume.isActiveAndEnabled && volume.weight > 0)
                    AssertEffectsInactive(volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile, false);

            // An automatic render may already have constructed URP and its
            // renderers. Never enqueue first-use messages for an existing pipeline.
            if (RenderPipelineManager.currentPipeline != null)
            {
                Assert.That(RenderPipelineManager.currentPipeline, Is.TypeOf<UniversalRenderPipeline>());
                Assert.That(UniversalRenderPipeline.asset, Is.SameAs(pipeline));
                Assert.That(pipeline.renderers.Length, Is.GreaterThan(0));
                for (int i = 0; i < pipeline.renderers.Length; i++)
                    Assert.That(pipeline.renderers[i], Is.Not.Null, "An existing URP pipeline must have initialized renderers.");
                return;
            }

            // Pinned URP17.6 constructs inactive pass materials on first use.
            // Expect only the observed exact LogType.Log count, and only for an
            // unavailable shader. Missing profiles, active effects and other logs fail.
            ExpectUnavailableShader("Hidden/Universal Render Pipeline/GaussianDepthOfField", "GaussianDepthOfField", 2);
            ExpectUnavailableShader("Hidden/Universal Render Pipeline/BokehDepthOfField", "BokehDepthOfField", 1);
            ExpectUnavailableShader("Hidden/Universal Render Pipeline/PaniniProjection", "PaniniProjection", 1);
#endif
        }

#if !UNITY_EDITOR
        private static void AssertEffectsInactive(VolumeProfile profile, bool requireEffects)
        {
            if (profile == null) { Assert.That(requireEffects, Is.False); return; }
            bool hasDepthOfField = profile.TryGet<DepthOfField>(out var depthOfField);
            bool hasPanini = profile.TryGet<PaniniProjection>(out var panini);
            if (requireEffects)
            {
                Assert.That(hasDepthOfField, Is.True, profile.name + " default DepthOfField");
                Assert.That(hasPanini, Is.True, profile.name + " default PaniniProjection");
            }
            if (hasDepthOfField)
            {
                Assert.That(depthOfField.mode.value, Is.EqualTo(DepthOfFieldMode.Off), profile.name + " DepthOfField mode");
                Assert.That(depthOfField.IsActive(), Is.False, profile.name + " DepthOfField must be inactive");
            }
            if (hasPanini)
            {
                Assert.That(panini.distance.value, Is.EqualTo(0f), profile.name + " Panini distance");
                Assert.That(panini.IsActive(), Is.False, profile.name + " Panini must be inactive");
            }
        }

        private static void ExpectUnavailableShader(string name, string pass, int count)
        {
            var shader = Shader.Find(name);
            bool unavailable = shader == null || !shader.isSupported;
            TestContext.WriteLine("Player capture pipeline shader: " + name + "; unavailable=" + unavailable);
            if (!unavailable) return;
            string message = "Shader '" + name + "' is not supported or has been stripped from the build (in '" + pass + "'). PostProcessing render passes will not execute.";
            for (int i = 0; i < count; i++) LogAssert.Expect(LogType.Log, message);
        }
#endif
    }
}
#endif
