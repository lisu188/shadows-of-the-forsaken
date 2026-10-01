using System;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Combat;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public sealed class CastleActorPresentationTests
    {
        private GameObject actor;
        private Component health, melee, presentation;
        private Transform motion, arm;
        private CapsuleCollider body;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1;
            actor = new GameObject("Actor presentation fixture");
            actor.SetActive(false);
            actor.transform.position = new Vector3(9100, 0, 9100);
            body = actor.AddComponent<CapsuleCollider>();
            body.radius = .4f; body.height = 2; body.center = Vector3.up;
            health = actor.AddComponent(Runtime("ShadowsOfTheForsaken.Combat.CombatHealth"));
            melee = actor.AddComponent(Runtime("ShadowsOfTheForsaken.Combat.MeleeCombat"));
            Set(melee, "windup", .5f); Set(melee, "activeWindow", .12f); Set(melee, "cooldown", .6f);
            motion = Child("Visual motion", actor.transform, Vector3.zero);
            var torso = Child("Torso", motion, Vector3.up);
            arm = Child("Sword arm", torso, new Vector3(.3f, .5f, 0));
            presentation = actor.AddComponent(Runtime("CastleActorPresentation"));
            Set(presentation, "motionRoot", motion); Set(presentation, "torso", torso); Set(presentation, "rightArm", arm);
            actor.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (actor != null) Object.DestroyImmediate(actor);
            Time.timeScale = previousTimeScale;
        }

        [Test]
        public void ActorPresentationReadsRealAttackDeathAndResetWithoutChangingColliders()
        {
            Vector3 rootPosition = actor.transform.position;
            Quaternion rootRotation = actor.transform.rotation;
            Guid life = Get<Guid>(health, "LifeId"), session = Get<Guid>(health, "SessionId");
            Assert.That((bool)Call(melee, "TryAttack"), Is.True);
            Call(presentation, "Present", .1f);
            Quaternion anticipation = arm.localRotation;
            Assert.That(Quaternion.Angle(anticipation, Quaternion.identity), Is.GreaterThan(5));
            Assert.That(Get<AttackPhase>(melee, "Phase"), Is.EqualTo(AttackPhase.Windup));
            Assert.That(Get<int>(health, "Current"), Is.EqualTo(100));
            Call(melee, "Simulate", .51f);
            Assert.That(Get<AttackPhase>(melee, "Phase"), Is.EqualTo(AttackPhase.Active));
            Call(presentation, "Present", .06f);
            Assert.That(Quaternion.Angle(arm.localRotation, anticipation), Is.GreaterThan(5));
            Assert.That((bool)Call(health, "TryDamage", 100, life, session), Is.True);
            for (int i = 0; i < 6; i++) Call(presentation, "Present", .1f);
            Assert.That(Quaternion.Angle(motion.localRotation, Quaternion.identity), Is.GreaterThan(60));
            Assert.That(actor.transform.position, Is.EqualTo(rootPosition));
            Assert.That(actor.transform.rotation, Is.EqualTo(rootRotation));
            Assert.That(actor.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(1));
            Assert.That(body.enabled, Is.True);
            Assert.That(body.radius, Is.EqualTo(.4f));
            Assert.That(body.height, Is.EqualTo(2));
            Assert.That(body.center, Is.EqualTo(Vector3.up));
            Call(health, "ResetHealth");
            Assert.That(motion.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(motion.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(arm.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(Get<int>(health, "Current"), Is.EqualTo(100));
            Assert.That(Get<Guid>(health, "LifeId"), Is.Not.EqualTo(life));
        }

        [Test]
        public void ActorPresentationDisableRestoresBindPoseAndRejectsActorRootReference()
        {
            Assert.That((bool)Call(melee, "TryAttack"), Is.True);
            Call(presentation, "Present", .1f);
            ((Behaviour)presentation).enabled = false;
            Assert.That(arm.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(motion.localPosition, Is.EqualTo(Vector3.zero));
            // Serialized fields can be changed incorrectly by a consumer. Only
            // captured owned child transforms may ever receive animation.
            Set(presentation, "motionRoot", actor.transform);
            ((Behaviour)presentation).enabled = true;
            Quaternion rotation = actor.transform.rotation;
            Assert.That((bool)Call(health, "TryDamage", 100, Get<Guid>(health, "LifeId"), Get<Guid>(health, "SessionId")), Is.True);
            for (int i = 0; i < 6; i++) Call(presentation, "Present", .1f);
            Assert.That(actor.transform.rotation, Is.EqualTo(rotation));
            Assert.That(body.enabled, Is.True);
        }

        private static Transform Child(string name, Transform parent, Vector3 position)
        {
            var result = new GameObject(name).transform;
            result.SetParent(parent, false); result.localPosition = position;
            return result;
        }
        private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static void Set(Component target, string name, object value) => target.GetType().GetField(name).SetValue(target, value);
        private static T Get<T>(Component target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);
        private static object Call(Component target, string name, params object[] args)
        {
            try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
    }
}
