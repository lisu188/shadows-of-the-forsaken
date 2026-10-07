using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public class CameraFollowTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private Component follow;
        private Type followType;
        private Camera camera;
        private GameObject player;
        private const string Missing = "CameraFollow: assign player or tag exactly one active character Player.";
        private const string Blocked = "CameraFollow: no safe visible camera pose; rendering suspended until recovery.";

        [SetUp]
        public void SetUp()
        {
            followType = Type.GetType("CameraFollow, Assembly-CSharp", true);
            player = Make("Camera test player");
            var character = player.AddComponent<CharacterController>();
            character.center = Vector3.up;
            character.height = 2f;
            character.radius = 0.3f;
            var rig = Make("Camera test rig");
            rig.SetActive(false);
            follow = rig.AddComponent(followType);
            camera = rig.GetComponent<Camera>();
            camera.nearClipPlane = 0.1f;
            camera.fieldOfView = 60f;
            camera.aspect = 16f / 9f;
            Set("player", player.transform);
            Set("findTaggedPlayer", false);
            rig.SetActive(true);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        private GameObject Make(string name)
        {
            var result = new GameObject(name);
            objects.Add(result);
            return result;
        }

        private GameObject Box(Vector3 position, Vector3 size)
        {
            var result = Make("Camera obstacle");
            result.transform.position = position;
            result.AddComponent<BoxCollider>().size = size;
            Physics.SyncTransforms();
            return result;
        }

        private void Set(string field, object value) => followType.GetField(field).SetValue(follow, value);
        private T Get<T>(string property) => (T)followType.GetProperty(property).GetValue(follow);
        private object Call(string method, params object[] args)
        {
            try { return followType.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(follow, args); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
        private bool Step(float dt = 1f / 60f)
        {
            Physics.SyncTransforms();
            return (bool)Call("Simulate", dt);
        }
        private void AssertClear(Collider obstacle)
        {
            float radius = Get<float>("EffectiveCollisionRadius");
            float clearance = Vector3.Distance(obstacle.ClosestPoint(camera.transform.position), camera.transform.position);
            Assert.That(clearance, Is.GreaterThanOrEqualTo(radius - 0.001f));
            Assert.That(Get<bool>("HasSafePose"), Is.True);
        }

        [Test]
        public void OriginalSerializedFieldsAndDefaultsRemainAvailable()
        {
            Assert.That(followType.GetField("player").FieldType, Is.EqualTo(typeof(Transform)));
            Assert.That(followType.GetField("distance").GetValue(follow), Is.EqualTo(5f));
            Assert.That(followType.GetField("height").GetValue(follow), Is.EqualTo(2f));
            Assert.That(followType.GetField("smoothSpeed").GetValue(follow), Is.EqualTo(2f));
            Assert.That(followType.GetField("shoulderOffset").GetValue(follow), Is.EqualTo(0f));
            Assert.That(followType.GetField("shoulderAimFraction").GetValue(follow), Is.EqualTo(1f));
            Assert.That(followType.GetField("lookHeightOffset").GetValue(follow), Is.EqualTo(0f));
        }

        [Test]
        public void MissingTargetWarnsOnceAndCanRecover()
        {
            Set("player", null);
            LogAssert.Expect(LogType.Warning, Missing);
            Assert.That(Step(), Is.False);
            Assert.That(Step(), Is.False);
            Assert.That(camera.enabled, Is.False);
            Call("SetTarget", player.transform);
            Assert.That(Step(), Is.True);
            Assert.That(camera.enabled, Is.True);
        }

        [Test]
        public void DestroyedTargetCanBeReplacedWithoutOldSmoothingState()
        {
            Assert.That(Step(), Is.True);
            Object.DestroyImmediate(player);
            LogAssert.Expect(LogType.Warning, Missing);
            Assert.That(Step(), Is.False);
            var replacement = Make("Respawned player");
            replacement.transform.position = new Vector3(20f, 0f, 0f);
            Call("SetTarget", replacement.transform);
            Assert.That(Step(), Is.True);
            Assert.That(camera.transform.position.x, Is.EqualTo(20f).Within(0.001f));
        }

        [Test]
        public void AcquiresOneTaggedPlayerButDoesNotChooseBetweenTwo()
        {
            player.tag = "Player";
            var second = Make("Second player");
            second.tag = "Player";
            Set("findTaggedPlayer", true);
            Call("SetTarget", (Transform)null);
            LogAssert.Expect(LogType.Warning, Missing);
            Assert.That(Step(), Is.False);
            second.tag = "Untagged";
            Call("SetTarget", (Transform)null);
            Assert.That(Step(), Is.True);
            Assert.That(followType.GetField("player").GetValue(follow), Is.EqualTo(player.transform));
        }

        [Test]
        public void RejectsSelfAsTargetWithoutInvalidLookRotation()
        {
            Call("SetTarget", follow.transform);
            LogAssert.Expect(LogType.Warning, Missing);
            Assert.That(Step(), Is.False);
            Assert.That(Step(), Is.False);
        }

        [Test]
        public void WallShortensBoomImmediately()
        {
            Step();
            var wall = Box(new Vector3(0f, 2f, -2f), new Vector3(10f, 6f, 0.2f)).GetComponent<Collider>();
            Assert.That(Step(), Is.True);
            Assert.That(camera.transform.position.z, Is.GreaterThan(-1.9f));
            AssertClear(wall);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void WallRemovalReturnsSmoothlyAtRepresentativeFrameRates(int fps)
        {
            var wall = Box(new Vector3(0f, 2f, -2f), new Vector3(10f, 6f, 0.2f));
            Assert.That(Step(), Is.True);
            Vector3 initial = camera.transform.position;
            Object.DestroyImmediate(wall);
            float last = initial.z;
            for (int i = 0; i < fps; i++)
            {
                Assert.That(Step(1f / fps), Is.True);
                Assert.That(camera.transform.position.z, Is.InRange(-5.001f, last + 0.00001f));
                last = camera.transform.position.z;
            }
            float expected = -5f + (initial.z + 5f) * Mathf.Exp(-2f);
            Assert.That(last, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void LowCeilingAndCornerLeaveCollisionVolumeClear()
        {
            var ceiling = Box(new Vector3(0f, 2.1f, -3f), new Vector3(10f, 0.2f, 6f)).GetComponent<Collider>();
            var side = Box(new Vector3(0.5f, 1f, -3f), new Vector3(0.2f, 3f, 6f)).GetComponent<Collider>();
            Assert.That(Step(), Is.True);
            AssertClear(ceiling);
            AssertClear(side);
        }

        [Test]
        public void ChildTargetIgnoresItsCharacterAndCompoundColliders()
        {
            var child = Make("Head target");
            child.transform.SetParent(player.transform, false);
            var decoration = Make("Player compound collider");
            decoration.transform.SetParent(player.transform, false);
            decoration.AddComponent<BoxCollider>().size = new Vector3(2f, 3f, 2f);
            Call("SetTarget", child.transform);
            Assert.That(Step(), Is.True);
            Assert.That(camera.transform.position.z, Is.EqualTo(-5f).Within(0.001f));
        }

        [Test]
        public void TriggersAndExcludedLayersDoNotBlockCamera()
        {
            var trigger = Box(new Vector3(0f, 1f, -1f), new Vector3(10f, 6f, 0.2f));
            trigger.GetComponent<Collider>().isTrigger = true;
            var excluded = Box(new Vector3(0f, 1f, -2f), new Vector3(10f, 6f, 0.2f));
            excluded.layer = 8;
            Set("obstructionMask", (LayerMask)~(1 << 8));
            Assert.That(Step(), Is.True);
            Assert.That(camera.transform.position.z, Is.EqualTo(-5f).Within(0.001f));
        }

        [Test]
        public void WideNearPlaneIsIncludedInCollisionRadius()
        {
            camera.nearClipPlane = 0.5f;
            camera.aspect = 2.4f;
            camera.fieldOfView = 90f;
            var wall = Box(new Vector3(0f, 2f, -4f), new Vector3(10f, 6f, 0.2f)).GetComponent<Collider>();
            Assert.That(Step(), Is.True);
            var corners = new Vector3[4];
            camera.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), camera.nearClipPlane, Camera.MonoOrStereoscopicEye.Mono, corners);
            foreach (var corner in corners)
                Assert.That(Get<float>("EffectiveCollisionRadius"), Is.GreaterThanOrEqualTo(corner.magnitude - 0.0001f));
            AssertClear(wall);
        }

        [Test]
        public void SaturatedQueryBufferDoesNotLoseTheNearestWall()
        {
            for (int i = 0; i < 40; i++)
                Box(new Vector3(0f, 1f, -2f - i * 0.02f), new Vector3(10f, 6f, 0.01f));
            Assert.That(Step(), Is.True);
            Assert.That(camera.transform.position.z, Is.GreaterThan(-1.9f));
        }

        [Test]
        public void StartingInsideWallIsResolvedBeforeRendering()
        {
            camera.transform.position = new Vector3(0f, 2f, -3f);
            var wall = Box(camera.transform.position, new Vector3(10f, 6f, 0.5f)).GetComponent<Collider>();
            Assert.That(Step(), Is.True);
            AssertClear(wall);
            Assert.That(camera.transform.position.z, Is.GreaterThan(-2.75f));
        }

        [Test]
        public void PartiallyOverlappingPivotDoesNotPermitCrossingThinWall()
        {
            var wall = Box(new Vector3(0f, 1f, -0.1f), new Vector3(10f, 6f, 0.02f));
            LogAssert.Expect(LogType.Warning, Blocked);
            Assert.That(Step(), Is.False);
            Assert.That(camera.enabled, Is.False);
            Object.DestroyImmediate(wall);
            Assert.That(Step(), Is.True);
            Assert.That(camera.enabled, Is.True);
        }

        [Test]
        public void EmbeddedPivotFailsSafelyAndRestoresRenderingAfterRecovery()
        {
            var wall = Box(Vector3.up, new Vector3(4f, 4f, 4f));
            LogAssert.Expect(LogType.Warning, Blocked);
            Assert.That(Step(), Is.False);
            Assert.That(Step(), Is.False);
            Assert.That(camera.enabled, Is.False);
            Object.DestroyImmediate(wall);
            Assert.That(Step(), Is.True);
            Assert.That(camera.enabled, Is.True);
        }

        [Test]
        public void OriginallyDisabledCameraIsNotEnabledByRecovery()
        {
            camera.enabled = false;
            Set("player", null);
            LogAssert.Expect(LogType.Warning, Missing);
            Assert.That(Step(), Is.False);
            Call("SetTarget", player.transform);
            Assert.That(Step(), Is.True);
            Assert.That(camera.enabled, Is.False);
        }

        [Test]
        public void TeleportAndExplicitSnapDoNotFlyThroughOldSceneSpace()
        {
            Step();
            player.transform.position = new Vector3(20f, 0f, 0f);
            Assert.That(Step(), Is.True);
            Assert.That(camera.transform.position.x, Is.EqualTo(20f).Within(0.001f));
            player.transform.position = new Vector3(21f, 0f, 0f);
            Assert.That((bool)Call("SnapToTarget"), Is.True);
            Assert.That(camera.transform.position.x, Is.EqualTo(21f).Within(0.001f));
        }

        [Test]
        public void RapidTurnDoesNotPlaceCameraInsideCorner()
        {
            var wall = Box(new Vector3(-2f, 2f, 0f), new Vector3(0.2f, 6f, 10f)).GetComponent<Collider>();
            Step();
            player.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            for (int i = 0; i < 120; i++)
            {
                Assert.That(Step(), Is.True);
                AssertClear(wall);
            }
        }

        [Test]
        public void ReenabledComponentStartsWithFreshTracking()
        {
            Step();
            ((Behaviour)follow).enabled = false;
            Assert.That(Step(), Is.False);
            player.transform.position = Vector3.right;
            ((Behaviour)follow).enabled = true;
            Assert.That(Step(), Is.True);
            Assert.That(camera.transform.position.x, Is.EqualTo(1f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator UnloadedTargetSceneCanBeReplaced()
        {
            Scene oldScene = SceneManager.CreateScene("Camera target " + Guid.NewGuid());
            SceneManager.MoveGameObjectToScene(player, oldScene);
            Step();
            ((Behaviour)follow).enabled = false;
            yield return SceneManager.UnloadSceneAsync(oldScene);
            var replacement = Make("Reloaded target");
            replacement.tag = "Player";
            replacement.transform.position = new Vector3(10f, 0f, 0f);
            Set("findTaggedPlayer", true);
            ((Behaviour)follow).enabled = true;
            Assert.That(Step(), Is.True);
            Assert.That(camera.transform.position.x, Is.EqualTo(10f).Within(0.001f));
        }

        [Test]
        public void ShoulderFramingRevealsForwardEnemyPastThePlayerCollider()
        {
            camera.nearClipPlane = .3f;
            // Match the authored cylinder silhouette explicitly: visual occlusion
            // must not depend on CharacterController query registration before movement.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            objects.Add(body);
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = Vector3.up;
            body.transform.localScale = new Vector3(.6f, 1, .6f);
            var enemy = Box(new Vector3(0, 1, 1.6f), new Vector3(.8f, 2, .8f)).GetComponent<Collider>();
            Assert.That(Step(), Is.True);
            var physics = player.scene.GetPhysicsScene();
            Vector3 direction = enemy.bounds.center - camera.transform.position;
            Assert.That(physics.Raycast(camera.transform.position, direction.normalized, out var centeredHit,
                direction.magnitude + 1, ~0, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(centeredHit.collider, Is.EqualTo(body.GetComponent<Collider>()).Or.EqualTo(player.GetComponent<CharacterController>()),
                "The baseline must reproduce body occlusion.");
            Set("shoulderOffset", 1.4f);
            Assert.That((bool)Call("SnapToTarget"), Is.True);
            direction = enemy.bounds.center - camera.transform.position;
            Assert.That(physics.Raycast(camera.transform.position, direction.normalized, out var shoulderHit,
                direction.magnitude + 1, ~0, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(shoulderHit.collider, Is.EqualTo(enemy), "A forward enemy must remain visible beside the player.");
            Assert.That(camera.transform.position.x, Is.EqualTo(1.4f).Within(.001f));
            Assert.That(camera.WorldToViewportPoint(enemy.bounds.center).x,
                Is.GreaterThan(camera.WorldToViewportPoint(player.transform.position + Vector3.up).x + .025f));
        }

        [TestCase(5f, 2f, 0f, 1.4f, 1f, 60f)]
        [TestCase(3f, 4f, 1.5f, -1.8f, .5f, 75f)]
        public void ShoulderFramingRetainsNearPlaneClearanceBesideWallAndDuringTurn(float distance, float height, float lookHeightOffset, float shoulderOffset, float shoulderAimFraction, float fieldOfView)
        {
            camera.nearClipPlane = .3f;
            camera.fieldOfView = fieldOfView;
            Set("distance", distance);
            Set("height", height);
            Set("lookHeightOffset", lookHeightOffset);
            Set("shoulderAimFraction", shoulderAimFraction);
            var rightWall = Box(new Vector3(2.1f, 2, -2), new Vector3(.2f, 6, 20)).GetComponent<Collider>();
            var leftWall = Box(new Vector3(-2.1f, 2, -2), new Vector3(.2f, 6, 20)).GetComponent<Collider>();
            Set("shoulderOffset", shoulderOffset);
            Assert.That(Step(), Is.True); AssertClear(rightWall); AssertClear(leftWall);
            if (shoulderOffset == 1.4f)
                Assert.That(camera.transform.position.x, Is.EqualTo(1.4f).Within(.001f),
                    "The original shoulder fits fully inside the four-metre corridor.");
            else
                Assert.That(camera.transform.position.x,
                    Is.InRange(leftWall.bounds.max.x + Get<float>("EffectiveCollisionRadius") - .001f, -.001f),
                    "The wider authored shoulder must retract before its near plane reaches the wall.");
            Assert.That(Get<float>("EffectiveCollisionRadius"), Is.GreaterThan(fieldOfView == 75f ? .55f : .46f));
            player.transform.rotation = Quaternion.Euler(0, -90, 0);
            for (int i = 0; i < 90; i++)
            {
                Assert.That(Step(), Is.True); AssertClear(rightWall); AssertClear(leftWall);
            }
            Assert.That(player.GetComponent<CharacterController>().enabled, Is.True);
            Assert.That(Get<bool>("HasSafePose"), Is.True);
        }

        [TestCase(5f, 2f, 0f, 1.4f, 1f, 60f)]
        [TestCase(3f, 4f, 1.5f, -1.8f, .5f, 75f)]
        public void LockedGateTurnDuringJumpRetainsAValidatedPreviousCameraPose(float distance, float height, float lookHeightOffset, float shoulderOffset, float shoulderAimFraction, float fieldOfView)
        {
            camera.nearClipPlane = .3f;
            camera.fieldOfView = fieldOfView;
            Set("distance", distance);
            Set("height", height);
            Set("lookHeightOffset", lookHeightOffset);
            Set("shoulderAimFraction", shoulderAimFraction);
            Set("shoulderOffset", shoulderOffset);
            var gate = Box(new Vector3(0, 2.2f, .5f), new Vector3(4, 4.4f, .35f)).GetComponent<Collider>();
            var rightWall = Box(new Vector3(2.2f, 2.25f, -2), new Vector3(.4f, 4.5f, 20)).GetComponent<Collider>();
            var leftWall = Box(new Vector3(-2.2f, 2.25f, -2), new Vector3(.4f, 4.5f, 20)).GetComponent<Collider>();
            var ceiling = Box(new Vector3(0, 4.75f, -2), new Vector3(4.8f, .5f, 20)).GetComponent<Collider>();
            Assert.That(Step(), Is.True);
            // The real jump peaks at about 1.27 m. Turning at the authored
            // 120 degrees/second then asks for a rear view beyond the gate.
            for (int frame = 0; frame <= 90; frame++)
            {
                float time = frame / 60f;
                player.transform.position = new Vector3(0, Mathf.Max(0, 5 * time - 4.905f * time * time), 0);
                player.transform.rotation = Quaternion.Euler(0, frame * 2, 0);
                Assert.That(Step(), Is.True, "The ordinary jump/turn must keep a checked view at frame " + frame);
                Assert.That(camera.enabled, Is.True);
                foreach (var obstacle in new[] { gate, rightWall, leftWall, ceiling }) AssertClear(obstacle);
                Assert.That(camera.transform.position.z, Is.LessThan(gate.bounds.min.z),
                    "A retained view must stay on the player's side of the locked panel.");
            }
            player.transform.position = new Vector3(0, 0, -7);
            Assert.That((bool)Call("SnapToTarget"), Is.True);
            if (shoulderOffset == 1.4f)
                Assert.That(Vector3.Distance(camera.transform.position, new Vector3(-1.4f, height, -7 + distance)), Is.LessThan(.001f),
                    "The original shoulder must recover fully inside the corridor.");
            else
                Assert.That(camera.transform.position.x,
                    Is.InRange(.001f, rightWall.bounds.min.x - Get<float>("EffectiveCollisionRadius") + .001f),
                    "The wider shoulder must remain clear of the opposite corridor wall after turning.");
            foreach (var obstacle in new[] { gate, rightWall, leftWall, ceiling }) AssertClear(obstacle);
            // The authored shoulder needs more lateral room. Require its full
            // signed pose after leaving the finite corridor walls and ceiling.
            player.transform.position = new Vector3(0, 0, -20);
            Assert.That((bool)Call("SnapToTarget"), Is.True);
            Assert.That(Vector3.Distance(camera.transform.position, new Vector3(-shoulderOffset, height, -20 + distance)), Is.LessThan(.001f),
                "Full shoulder framing must recover after leaving the confined corridor.");
            foreach (var obstacle in new[] { gate, rightWall, leftWall, ceiling }) AssertClear(obstacle);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovingTargetAtHeldCameraAimUsesSafeBoomWithoutInvalidRotation()
        {
            Set("distance", 1f);
            Set("height", 2f);
            Set("pivotHeight", 1f);
            Set("lookHeightOffset", 1f);
            Set("smoothSpeed", 0f);
            Assert.That((bool)Call("SnapToTarget"), Is.True);
            Vector3 heldPosition = camera.transform.position;
            player.transform.position = Vector3.back;
            Assert.That(Step(), Is.True);
            Assert.That(Vector3.Distance(camera.transform.position, heldPosition), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(camera.transform.forward, Vector3.down), Is.LessThan(.001f));
            Assert.That(Get<bool>("HasSafePose"), Is.True);
            Assert.That(camera.enabled, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0f, 0f)]
        [TestCase(1.3f, 1.3f)]
        [TestCase(-1f, 0f)]
        [TestCase(3f, 2f)]
        [TestCase(float.NaN, 0f)]
        [TestCase(float.PositiveInfinity, 0f)]
        [TestCase(float.NegativeInfinity, 0f)]
        public void LookHeightOffsetChangesOnlyAimAndRetainsTheCollisionPivot(float requested, float expected)
        {
            camera.nearClipPlane = .3f;
            Set("distance", 3.5f);
            Set("height", 3.3f);
            Set("shoulderOffset", -1.4f);
            Set("shoulderAimFraction", .5f);
            Set("lookHeightOffset", requested);
            // Moving the physics pivot to the authored aim height would place
            // it inside this collider. The existing pivot must remain at 1 m.
            var obstacle = Box(new Vector3(0, 2.3f, 0), Vector3.one * .2f).GetComponent<Collider>();
            Assert.That(Step(), Is.True);
            Assert.That(followType.GetField("lookHeightOffset").GetValue(follow), Is.EqualTo(expected));
            Assert.That(Vector3.Distance(camera.transform.position, new Vector3(-1.4f, 3.3f, -3.5f)), Is.LessThan(.001f));
            Vector3 aim = new Vector3(-.7f, 1 + expected, 0);
            Assert.That(Vector3.Distance(camera.transform.forward, (aim - camera.transform.position).normalized),
                Is.LessThan(.001f));
            AssertClear(obstacle);
            Assert.That(player.GetComponent<CharacterController>().enabled, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0f, 0f)]
        [TestCase(.5f, .5f)]
        [TestCase(1f, 1f)]
        [TestCase(-1f, 0f)]
        [TestCase(2f, 1f)]
        [TestCase(float.NaN, 1f)]
        [TestCase(float.PositiveInfinity, 1f)]
        [TestCase(float.NegativeInfinity, 1f)]
        public void ShoulderAimFractionChangesOnlyAimAndRetainsTheFullCameraBoom(float requested, float expected)
        {
            camera.nearClipPlane = .3f;
            Set("distance", 3.5f);
            Set("height", 3.3f);
            Set("shoulderOffset", -1.4f);
            Set("shoulderAimFraction", requested);
            Set("lookHeightOffset", 1.3f);
            var obstacle = Box(new Vector3(0, 2.3f, 0), Vector3.one * .2f).GetComponent<Collider>();
            Assert.That(Step(), Is.True);
            Assert.That(followType.GetField("shoulderAimFraction").GetValue(follow), Is.EqualTo(expected));
            Assert.That(Vector3.Distance(camera.transform.position, new Vector3(-1.4f, 3.3f, -3.5f)), Is.LessThan(.001f),
                "An aiming fraction must not scale the physical shoulder offset.");
            Vector3 aim = new Vector3(-1.4f * expected, 2.3f, 0);
            Assert.That(Vector3.Distance(camera.transform.forward, (aim - camera.transform.position).normalized),
                Is.LessThan(.001f));
            AssertClear(obstacle);
            Assert.That(player.GetComponent<CharacterController>().enabled, Is.True);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
