using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public sealed class ProgressionControllerTests
    {
        private GameObject owner;
        private LevelProgressionController controller;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("Progression test owner");
            controller = owner.AddComponent<LevelProgressionController>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (owner != null) Object.Destroy(owner);
            yield return null;
        }

        [Test]
        public void StartsInNewSceneScopedSession()
        {
            Assert.That(controller.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
            Assert.That(controller.Snapshot.SessionId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(controller.CanEnter(LevelRoom.FirstEncounter), Is.True);
            Assert.That(controller.CanEnter(LevelRoom.Exit), Is.False);
        }

        [Test]
        public void ForwardsCommittedChangesAndSuppressesDuplicateObjectives()
        {
            var changes = new List<ProgressionChange>();
            controller.Changed += changes.Add;
            var session = controller.Snapshot.SessionId;
            Assert.That(controller.TryEnter(LevelRoom.FirstEncounter, session), Is.True);
            Assert.That(controller.CanComplete(LevelObjective.FirstEnemyDefeated), Is.True);
            Assert.That(controller.TryComplete(LevelObjective.FirstEnemyDefeated, session), Is.True);
            Assert.That(controller.TryComplete(LevelObjective.FirstEnemyDefeated, session), Is.False);
            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(changes[1].After.CompletedObjectives, Is.EqualTo(LevelObjective.FirstEnemyDefeated));
            Assert.That(controller.IsPassageOpen(LevelRoom.FirstEncounter, LevelRoom.Puzzle), Is.True);
        }

        [Test]
        public void DisableAndEnableDoNotDuplicateSubscriptionsOrResetProgress()
        {
            var count = 0;
            controller.Changed += _ => count++;
            var session = controller.Snapshot.SessionId;
            Assert.That(controller.TryEnter(LevelRoom.FirstEncounter, session), Is.True);
            for (var i = 0; i < 3; i++)
            {
                controller.enabled = false;
                Assert.That(controller.TryComplete(LevelObjective.FirstEnemyDefeated, session), Is.False);
                Assert.That(controller.TryResetSession(), Is.False);
                controller.enabled = true;
            }
            Assert.That(controller.Snapshot.SessionId, Is.EqualTo(session));
            Assert.That(controller.Snapshot.Room, Is.EqualTo(LevelRoom.FirstEncounter));
            Assert.That(controller.TryComplete(LevelObjective.FirstEnemyDefeated, session), Is.True);
            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void InactiveGameObjectRejectsCommands()
        {
            var session = controller.Snapshot.SessionId;
            owner.SetActive(false);
            Assert.That(controller.CanEnter(LevelRoom.FirstEncounter), Is.False);
            Assert.That(controller.TryEnter(LevelRoom.FirstEncounter, session), Is.False);
            Assert.That(controller.TryResetSession(), Is.False);
            owner.SetActive(true);
            Assert.That(controller.TryEnter(LevelRoom.FirstEncounter, session), Is.True);
        }

        [Test]
        public void ResetNotifiesOnceAndRejectsPreviousRunCallbacks()
        {
            var old = controller.Snapshot.SessionId;
            Assert.That(controller.TryEnter(LevelRoom.FirstEncounter, old), Is.True);
            Assert.That(controller.TryComplete(LevelObjective.FirstEnemyDefeated, old), Is.True);
            var resets = 0;
            controller.Changed += change => { if (change.Kind == ProgressionChangeKind.SessionReset) resets++; };
            Assert.That(controller.TryResetSession(), Is.True);
            Assert.That(resets, Is.EqualTo(1));
            Assert.That(controller.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(controller.TryEnter(LevelRoom.FirstEncounter, old), Is.False);
            Assert.That(controller.TryEnter(LevelRoom.FirstEncounter, controller.Snapshot.SessionId), Is.True);
            Assert.That(controller.TryComplete(LevelObjective.FirstEnemyDefeated, old), Is.False);
        }

        [UnityTest]
        public IEnumerator DestroyedControllerRejectsCommandsAndNewInstanceStartsClean()
        {
            var original = controller;
            var oldSession = original.Snapshot.SessionId;
            Object.Destroy(owner);
            yield return null;
            Assert.That(original.TryEnter(LevelRoom.FirstEncounter, oldSession), Is.False);
            Assert.That(original.TryResetSession(), Is.False);
            Assert.Throws<ObjectDisposedException>(() => { var ignored = original.Snapshot; });
            owner = new GameObject("New progression owner");
            controller = owner.AddComponent<LevelProgressionController>();
            Assert.That(controller.Snapshot.Room, Is.EqualTo(LevelRoom.Courtyard));
            Assert.That(controller.Snapshot.SessionId, Is.Not.EqualTo(oldSession));
            Assert.That(controller.TryEnter(LevelRoom.FirstEncounter, oldSession), Is.False);
        }

        [UnityTest]
        public IEnumerator SceneUnloadDoesNotLeakProgressOrNotificationsIntoNextScene()
        {
            var scene = SceneManager.CreateScene("ProgressionLifecycle_" + Guid.NewGuid().ToString("N"));
            SceneManager.MoveGameObjectToScene(owner, scene);
            var old = controller;
            var session = old.Snapshot.SessionId;
            var notifications = 0;
            old.Changed += _ => notifications++;
            Assert.That(old.TryEnter(LevelRoom.FirstEncounter, session), Is.True);
            var unload = SceneManager.UnloadSceneAsync(scene);
            Assert.That(unload, Is.Not.Null);
            for (var frame = 0; frame < 300 && !unload.isDone; frame++) yield return null;
            Assert.That(unload.isDone, Is.True, "Scene unload timed out.");
            Assert.That(old.TryComplete(LevelObjective.FirstEnemyDefeated, session), Is.False);
            owner = new GameObject("Next scene progression");
            controller = owner.AddComponent<LevelProgressionController>();
            Assert.That(controller.TryEnter(LevelRoom.FirstEncounter, controller.Snapshot.SessionId), Is.True);
            Assert.That(controller.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Assert.That(controller.Snapshot.SessionId, Is.Not.EqualTo(session));
            Assert.That(notifications, Is.EqualTo(1));
        }
    }
}
