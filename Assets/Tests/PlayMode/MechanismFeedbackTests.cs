using System;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public sealed class MechanismFeedbackTests
    {
        private GameObject sessionObject, mechanism;
        private LevelProgressionController progression;
        private Component target, feedback;
        private Renderer visual;

        [SetUp]
        public void SetUp()
        {
            sessionObject = new GameObject("Mechanism session");
            progression = sessionObject.AddComponent<LevelProgressionController>();
        }
        [TearDown]
        public void TearDown()
        {
            if (mechanism != null) Object.DestroyImmediate(mechanism);
            if (sessionObject != null) Object.DestroyImmediate(sessionObject);
        }

        [Test]
        public void RuneFeedbackMovesOnceAndRestoresOnSessionReset()
        {
            EnterPuzzle(); Create(LevelObjective.MainPuzzleSolved, false);
            Assert.That(Activate(), Is.True);
            Assert.That(Quaternion.Angle(visual.transform.localRotation, Quaternion.Euler(0, 0, 35)), Is.LessThan(.01f));
            Assert.That(Activate(), Is.False);
            Assert.That(progression.TryResetSession(), Is.True);
            Assert.That(Quaternion.Angle(visual.transform.localRotation, Quaternion.identity), Is.LessThan(.01f));
            EnterPuzzle(); Assert.That(Activate(), Is.True);
        }

        [Test]
        public void RelicFeedbackHidesOnceAndReconcilesReenableAndNewSession()
        {
            EnterBonus(); Create(LevelObjective.BonusDiscovered, true);
            Assert.That(Activate(), Is.True); Assert.That(visual.enabled, Is.False);
            Assert.That(Activate(), Is.False);
            ((Behaviour)feedback).enabled = false; ((Behaviour)feedback).enabled = true;
            Assert.That(visual.enabled, Is.False);
            Assert.That(progression.TryResetSession(), Is.True); Assert.That(visual.enabled, Is.True);
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            EnterBonus(); Assert.That(Activate(), Is.True); Assert.That(visual.enabled, Is.False);
        }

        [Test]
        public void DamagedMechanismNeverCompletesPuzzleAndCanBeExaminedAfterReset()
        {
            EnterPuzzle(); Create(LevelObjective.None, false);
            Assert.That(Activate(), Is.True);
            Assert.That(progression.Snapshot.CompletedObjectives & LevelObjective.MainPuzzleSolved, Is.EqualTo(LevelObjective.None));
            Assert.That(Activate(), Is.False);
            Assert.That(progression.TryResetSession(), Is.True);
            EnterPuzzle(); Assert.That(Activate(), Is.True);
            Assert.That(progression.Snapshot.CompletedObjectives & LevelObjective.MainPuzzleSolved, Is.EqualTo(LevelObjective.None));
        }

        private void Create(LevelObjective objective, bool hide)
        {
            mechanism = new GameObject("Mechanism"); mechanism.SetActive(false);
            var focus = mechanism.AddComponent<BoxCollider>(); focus.isTrigger = true;
            var shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(shape.GetComponent<Collider>()); shape.transform.SetParent(mechanism.transform, false);
            visual = shape.GetComponent<Renderer>();
            target = mechanism.AddComponent(Type.GetType("InteractionTarget, Assembly-CSharp", true));
            Set(target, "progression", progression); Set(target, "objective", objective); Set(target, "focusCollider", focus);
            feedback = mechanism.AddComponent(Type.GetType("MechanismFeedback, Assembly-CSharp", true));
            Set(feedback, "target", target); Set(feedback, "movingPart", visual.transform);
            Set(feedback, "activatedEulerAngles", new Vector3(0, 0, 35));
            Set(feedback, "rewardVisuals", new[] { visual }); Set(feedback, "hideWhenConsumed", hide);
            mechanism.SetActive(true);
        }
        private bool Activate() => (bool)target.GetType().GetMethod("TryActivate").Invoke(target, new object[] { progression.Snapshot.SessionId });
        private static void Set(Component item, string name, object value) => item.GetType().GetField(name).SetValue(item, value);
        private void Enter(LevelRoom room) => Assert.That(progression.TryEnter(room, progression.Snapshot.SessionId), Is.True);
        private void Complete(LevelObjective objective) => Assert.That(progression.TryComplete(objective, progression.Snapshot.SessionId), Is.True);
        private void EnterPuzzle()
        {
            Enter(LevelRoom.FirstEncounter); Complete(LevelObjective.FirstEnemyDefeated); Enter(LevelRoom.Puzzle);
        }
        private void EnterBonus()
        {
            EnterPuzzle(); Complete(LevelObjective.MainPuzzleSolved); Enter(LevelRoom.FirstEncounter);
            Enter(LevelRoom.ThroneRoom); Complete(LevelObjective.MinibossDefeated); Enter(LevelRoom.Library);
            Complete(LevelObjective.LibraryOpened); Enter(LevelRoom.Catacombs); Complete(LevelObjective.SecretLeverPulled); Enter(LevelRoom.BonusRoom);
        }
    }
}
