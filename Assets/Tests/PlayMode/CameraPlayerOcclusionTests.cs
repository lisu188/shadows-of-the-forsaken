using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public sealed class CameraPlayerOcclusionTests
    {
        private Scene scene;
        private GameObject player, rig;
        private Component follow, occlusion;
        private Renderer body;

        [SetUp]
        public void SetUp()
        {
            scene = SceneManager.CreateScene("Close-body camera " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            player = new GameObject("Player"); SceneManager.MoveGameObjectToScene(player, scene);
            var character = player.AddComponent<CharacterController>(); character.height = 2; character.radius = .3f; character.center = Vector3.up;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(player.transform, false); visual.transform.localPosition = Vector3.up;
            body = visual.GetComponent<Renderer>();
            rig = new GameObject("Camera"); rig.SetActive(false); SceneManager.MoveGameObjectToScene(rig, scene);
            rig.AddComponent<Camera>().nearClipPlane = .1f;
            follow = rig.AddComponent(Type.GetType("CameraFollow, Assembly-CSharp", true));
            Set(follow, "player", player.transform); Set(follow, "findTaggedPlayer", false); Set(follow, "height", 1f);
            occlusion = rig.AddComponent(Type.GetType("CameraPlayerOcclusion, Assembly-CSharp", true));
            Set(occlusion, "playerVisuals", new[] { body }); rig.SetActive(true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (rig != null) Object.DestroyImmediate(rig);
            if (player != null) Object.DestroyImmediate(player);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public void CloseBodyCameraHysteresisKeepsPhysicsAndRestoresDistantView()
        {
            Pose(1.1f); Assert.That(body.forceRenderingOff, Is.True);
            Assert.That(player.GetComponent<CharacterController>().enabled, Is.True);
            Pose(1.4f); Assert.That(body.forceRenderingOff, Is.True);
            Pose(1.7f); Assert.That(body.forceRenderingOff, Is.False);
            Pose(1.4f); Assert.That(body.forceRenderingOff, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CloseBodyCameraDisableRestoresThenReenableHidesAgain()
        {
            Pose(1.1f); ((Behaviour)occlusion).enabled = false;
            Assert.That(body.forceRenderingOff, Is.False);
            ((Behaviour)occlusion).enabled = true; Call(occlusion, "RefreshVisibility");
            Assert.That(body.forceRenderingOff, Is.True);
        }

        [Test]
        public void CloseBodyCameraPreservesPreexistingFlagsAndIgnoresForeignVisuals()
        {
            var foreign = rig.AddComponent<MeshRenderer>(); foreign.forceRenderingOff = false;
            body.forceRenderingOff = true; Set(occlusion, "playerVisuals", new[] { body, foreign });
            Pose(1.1f); Assert.That(foreign.forceRenderingOff, Is.False);
            Pose(2); Assert.That(body.forceRenderingOff, Is.True);
            Assert.That(foreign.forceRenderingOff, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CloseBodyCameraTargetLossRestoresOriginalVisuals(bool replace)
        {
            Pose(1.1f);
            Set(follow, "player", replace ? rig.transform : null);
            Call(occlusion, "RefreshVisibility");
            Assert.That(body.forceRenderingOff, Is.False);
        }

        private void Pose(float distance)
        {
            Set(follow, "distance", distance);
            Assert.That((bool)Call(follow, "SnapToTarget"), Is.True);
            Call(occlusion, "RefreshVisibility");
        }
        private static void Set(Component item, string name, object value) => item.GetType().GetField(name).SetValue(item, value);
        private static object Call(Component item, string name) => item.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public).Invoke(item, null);
    }
}
