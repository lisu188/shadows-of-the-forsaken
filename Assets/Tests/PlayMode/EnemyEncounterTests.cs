using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Encounters;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // Component fixtures use a real, tiny NavMesh and isolated PhysicsScene.
    // They test encounter contracts; the saved-scene route suite separately
    // proves mandatory progression using gameplay rather than flag injection.
    public sealed class EnemyEncounterTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly Vector3 origin = new Vector3(6400, 0, 6400);
        private Scene scene;
        private LevelProgressionController progression;
        private Component enemy, enemyHealth, melee, playerHealth;
        private NavMeshData data;
        private NavMeshDataInstance instance;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1;
            scene = SceneManager.CreateScene("Enemy encounter " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            progression = Make("Progression", Vector3.zero).AddComponent<LevelProgressionController>();
            Box("Floor", new Vector3(0, -.1f, 0), new Vector3(20, .2f, 20));
            Bake();
            var player = Make("Player", new Vector3(0, 0, 1.5f));
            player.SetActive(false);
            playerHealth = player.AddComponent(RuntimeType("ShadowsOfTheForsaken.Combat.CombatHealth"));
            Set(playerHealth, "progression", progression);
            var playerBody = player.AddComponent<CapsuleCollider>();
            playerBody.center = Vector3.up; playerBody.height = 2; playerBody.radius = .3f;
            player.SetActive(true);
            var actor = Make("Enemy", Vector3.zero);
            actor.SetActive(false);
            enemyHealth = actor.AddComponent(RuntimeType("ShadowsOfTheForsaken.Combat.CombatHealth"));
            Set(enemyHealth, "progression", progression);
            melee = actor.AddComponent(RuntimeType("ShadowsOfTheForsaken.Combat.MeleeCombat"));
            Set(melee, "windup", .55f); Set(melee, "range", 1.8f);
            var body = actor.AddComponent<CapsuleCollider>();
            body.center = Vector3.up; body.height = 2; body.radius = .4f;
            var navigation = actor.AddComponent<NavMeshAgent>();
            navigation.radius = .4f; navigation.height = 2;
            enemy = actor.AddComponent(RuntimeType("ShadowsOfTheForsaken.Encounters.EnemyEncounter"));
            Set(enemy, "progression", progression);
            Set(enemy, "playerHealth", playerHealth);
            Set(enemy, "arenaCenter", origin);
            Set(enemy, "arenaHalfExtents", new Vector3(9, 3, 9));
            actor.SetActive(true);
            Call(enemy, "OnApplicationFocus", true);
            Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            if (instance.valid) instance.Remove();
            if (data != null) Object.DestroyImmediate(data);
            Time.timeScale = previousTimeScale;
            var unload = SceneManager.UnloadSceneAsync(scene);
            float deadline = Time.realtimeSinceStartup + 10;
            while (unload != null && !unload.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(unload == null || unload.isDone, Is.True, "Encounter fixture unload timed out.");
        }

        [Test]
        public void EncounterDeferredAgentWaitsForNavigationDataWithoutMovingTheActor()
        {
            var agent = enemy.GetComponent<NavMeshAgent>();
            agent.enabled = false;
            instance.Remove();
            Set(enemy, "initializeNavigationOnStart", true);
            Vector3 position = enemy.transform.position;
            Guid life = Get<Guid>(enemyHealth, "LifeId");
            Call(enemy, "Start");
            Tick();
            Assert.That(agent.enabled, Is.False, "Missing scene navigation must not trigger native agent registration.");
            instance = NavMesh.AddNavMeshData(data);
            Tick();
            Assert.That(agent.enabled && agent.isOnNavMesh, Is.True);
            Assert.That(enemy.transform.position, Is.EqualTo(position));
            Assert.That(Get<Guid>(enemyHealth, "LifeId"), Is.EqualTo(life));
            Assert.That(Get<bool>(enemy, "IsActivated"), Is.False, "Navigation initialization must not activate an encounter.");
        }

        [Test]
        public void EncounterDormantActorRejectsDamageAndRoomReentryPreservesLife()
        {
            Tick();
            Assert.That(Get<bool>(enemy, "IsActivated"), Is.False);
            Assert.That(Damage(enemyHealth, 25), Is.False, "The approach cannot kill an unactivated encounter.");
            Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, progression.Snapshot.SessionId), Is.True);
            Tick();
            Assert.That(Get<bool>(enemy, "IsActivated"), Is.True);
            Guid life = Get<Guid>(enemyHealth, "LifeId");
            Assert.That(Damage(enemyHealth, 25), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.Courtyard, progression.Snapshot.SessionId), Is.True);
            Tick();
            Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, progression.Snapshot.SessionId), Is.True);
            Tick();
            Assert.That(Get<Guid>(enemyHealth, "LifeId"), Is.EqualTo(life));
            Assert.That(Get<int>(enemyHealth, "Current"), Is.EqualTo(75));
        }

        [TestCase(LevelRoom.FirstEncounter, LevelRoom.Courtyard, LevelObjective.FirstEnemyDefeated)]
        [TestCase(LevelRoom.ThroneRoom, LevelRoom.FirstEncounter, LevelObjective.MinibossDefeated)]
        [TestCase(LevelRoom.FinalArena, LevelRoom.Catacombs, LevelObjective.FinalEnemyDefeated)]
        public void EncounterRetreatingLastSwingCreditsEachFightExactlyOnceOnReturn(LevelRoom room, LevelRoom retreat, LevelObjective objective)
        {
            Set(enemy, "encounterRoom", room); Set(enemy, "objective", objective);
            PrepareRoom(room);
            Tick();
            Assert.That(Get<bool>(enemy, "IsActivated"), Is.True);
            Assert.That(progression.TryEnter(retreat, progression.Snapshot.SessionId), Is.True);
            Assert.That(Damage(enemyHealth, 1000), Is.True);
            Tick();
            Assert.That(Get<bool>(enemy, "HasPendingDeath"), Is.True);
            Assert.That((progression.Snapshot.CompletedObjectives & objective), Is.EqualTo(LevelObjective.None));
            Assert.That(enemy.GetComponent<Collider>().enabled, Is.False, "Defeated bodies cannot block a passage.");
            Assert.That(progression.TryEnter(room, progression.Snapshot.SessionId), Is.True);
            Tick(); Tick();
            Assert.That(Get<bool>(enemy, "DeathCredited"), Is.True);
            Assert.That(Get<bool>(enemy, "HasPendingDeath"), Is.False);
            Assert.That((progression.Snapshot.CompletedObjectives & objective), Is.EqualTo(objective));
            Assert.That(Damage(enemyHealth, 1), Is.False);
        }

        [Test]
        public void EncounterResetRestoresBodyAndRejectsOldDeathPayload()
        {
            PrepareRoom(LevelRoom.FirstEncounter); Tick();
            Guid session = Get<Guid>(enemyHealth, "SessionId"), life = Get<Guid>(enemyHealth, "LifeId");
            Damage(enemyHealth, 1000);
            progression.TryResetSession();
            Call(enemy, "ResetEncounter");
            Call(enemy, "OnDied", new HealthChange(100, 0, 100, life, session, false));
            Tick();
            Assert.That(Get<bool>(enemy, "IsActivated"), Is.False);
            Assert.That(Get<bool>(enemy, "HasPendingDeath"), Is.False);
            Assert.That(Get<int>(enemyHealth, "Current"), Is.EqualTo(100));
            Assert.That(enemy.GetComponent<Collider>().enabled, Is.True);
            Assert.That(Vector3.Distance(enemy.transform.position, origin), Is.LessThan(.05f));
        }

        [Test]
        public void EncounterDisabledAcrossResetRestoresNewSessionWithoutOldCredit()
        {
            PrepareRoom(LevelRoom.FirstEncounter); Tick(); Damage(enemyHealth, 1000);
            enemy.gameObject.SetActive(false);
            progression.TryResetSession();
            enemy.gameObject.SetActive(true);
            Tick();
            Assert.That(Get<Guid>(enemyHealth, "SessionId"), Is.EqualTo(progression.Snapshot.SessionId));
            Assert.That(Get<int>(enemyHealth, "Current"), Is.EqualTo(100));
            Assert.That(Get<bool>(enemy, "HasPendingDeath"), Is.False);
            Assert.That(Get<bool>(enemy, "IsActivated"), Is.False);
        }

        [Test]
        public void EncounterWallPreventsDetectionAndMeleeThroughGate()
        {
            Box("Closed physical gate", new Vector3(0, 1, .7f), new Vector3(4, 3, .2f));
            PrepareRoom(LevelRoom.FirstEncounter); Tick();
            Assert.That(Get<AttackPhase>(melee, "Phase"), Is.EqualTo(AttackPhase.Ready));
            Call(melee, "Simulate", 1f);
            Assert.That(Get<int>(playerHealth, "Current"), Is.EqualTo(100));
        }

        [TestCase("pause")]
        [TestCase("focus")]
        [TestCase("terminal")]
        [TestCase("dead-target")]
        [TestCase("time-scale")]
        public void EncounterLostOrSuspendedTargetInterruptsTelegraph(string reason)
        {
            PrepareRoom(LevelRoom.FirstEncounter); Tick();
            Assert.That(Get<AttackPhase>(melee, "Phase"), Is.EqualTo(AttackPhase.Windup));
            if (reason == "pause") Call(enemy, "OnApplicationPause", true);
            if (reason == "focus") Call(enemy, "OnApplicationFocus", false);
            if (reason == "terminal") Call(enemy, "SetSessionRunning", false);
            if (reason == "dead-target") Damage(playerHealth, 1000);
            if (reason == "time-scale") Time.timeScale = 0;
            Tick();
            Call(melee, "Simulate", 1f);
            Assert.That(Get<int>(playerHealth, "Current"), Is.EqualTo(reason == "dead-target" ? 0 : 100));
            Assert.That((bool)Call(melee, "TryAttack"), Is.False);
        }

        [UnityTest]
        public IEnumerator EncounterRealNavMeshChasesButCannotCrossAnUncarvedPhysicalGate()
        {
            PrepareRoom(LevelRoom.FirstEncounter);
            playerHealth.transform.position = origin + new Vector3(0, 0, 6);
            Tick();
            Assert.That(enemy.GetComponent<NavMeshAgent>().isOnNavMesh, Is.True);
            float deadline = Time.realtimeSinceStartup + 4;
            while (enemy.transform.position.z < origin.z + .5f && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(enemy.transform.position.z, Is.GreaterThanOrEqualTo(origin.z + .5f), "A real agent must move before checking its blocker.");
            float wallZ = enemy.transform.position.z + 1;
            Box("Gate without navigation carving", new Vector3(0, 1, wallZ - origin.z), new Vector3(12, 3, .25f));
            float until = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.That(enemy.transform.position.z, Is.LessThan(wallZ - .4f), "Physics must block movement even before NavMesh carving catches up.");
            Assert.That(Get<int>(playerHealth, "Current"), Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator EncounterWithdrawnPlayerDoesNotPullEnemyOutOfArena()
        {
            PrepareRoom(LevelRoom.FirstEncounter);
            playerHealth.transform.position = origin + new Vector3(0, 0, 6);
            Tick();
            float deadline = Time.realtimeSinceStartup + 4;
            while (enemy.transform.position.z < origin.z + 1 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(enemy.transform.position.z, Is.GreaterThan(origin.z + .5f));
            playerHealth.transform.position = origin + new Vector3(0, 0, 12);
            float untilHome = Time.realtimeSinceStartup + 5;
            while (Vector3.Distance(enemy.transform.position, origin) > .2f && Time.realtimeSinceStartup < untilHome)
            {
                Assert.That(Mathf.Abs(enemy.transform.position.z - origin.z), Is.LessThan(9));
                yield return null;
            }
            Assert.That(Vector3.Distance(enemy.transform.position, origin), Is.LessThan(.2f));
            Assert.That(Get<int>(playerHealth, "Current"), Is.EqualTo(100));
        }

        [Test]
        public void EncounterUnreachableTargetCannotBecomeStraightLineMovement()
        {
            PrepareRoom(LevelRoom.FirstEncounter);
            // It is inside the arena but on no reachable navigation floor.
            playerHealth.transform.position = origin + new Vector3(0, 2, 4);
            Tick(); Tick();
            Assert.That(Get<EncounterPhase>(enemy, "Phase"), Is.EqualTo(EncounterPhase.Blocked));
            Assert.That(Vector3.Distance(enemy.transform.position, origin), Is.LessThan(.05f));
            Assert.That(Get<AttackPhase>(melee, "Phase"), Is.EqualTo(AttackPhase.Ready));
        }

        [UnityTest]
        public IEnumerator EncounterNavigatesAroundBakedObstacleWithoutEnteringIt()
        {
            var wall = Box("Pillar", new Vector3(0, 1.5f, 3), new Vector3(2, 3, .8f));
            if (instance.valid) instance.Remove();
            Object.DestroyImmediate(data);
            Bake(wall.GetComponent<BoxCollider>());
            var agent = enemy.GetComponent<NavMeshAgent>();
            Assert.That(agent.Warp(origin), Is.True);
            PrepareRoom(LevelRoom.FirstEncounter);
            // Acquire on the near side first; then the last-known target can be
            // pursued around the column even though the centre ray is occluded.
            playerHealth.transform.position = origin + new Vector3(0, 0, 2);
            Tick();
            playerHealth.transform.position = origin + new Vector3(0, 0, 6);
            bool wentAround = false;
            float deadline = Time.realtimeSinceStartup + 8;
            while (Vector3.Distance(enemy.transform.position, playerHealth.transform.position) > 1.7f && Time.realtimeSinceStartup < deadline)
            {
                if (Mathf.Abs(enemy.transform.position.x - origin.x) > 1.2f) wentAround = true;
                Assert.That(wall.GetComponent<Collider>().bounds.Contains(enemy.transform.position + Vector3.up), Is.False);
                yield return null;
            }
            Assert.That(wentAround, Is.True, "The path must go around the baked pillar.");
            Assert.That(enemy.transform.position.z, Is.GreaterThan(origin.z + 4), "The actor must clear the far face of the pillar.");
            Assert.That(Vector3.Distance(enemy.transform.position, playerHealth.transform.position), Is.LessThanOrEqualTo(1.7f),
                "Arrival must respect the authored 1.5m stopping distance rather than walking into the target.");
        }

        [Test]
        public void EncounterGateCarvingFollowsPhysicalClosureAndRecognizesKinematicEnemyOccupants()
        {
            var gateObject = Make("Gate", new Vector3(4, 1, 0));
            gateObject.SetActive(false);
            var box = gateObject.AddComponent<BoxCollider>(); box.size = new Vector3(2, 3, .5f);
            var blocker = gateObject.AddComponent<NavMeshObstacle>();
            var gate = gateObject.AddComponent(RuntimeType("ProgressionGate"));
            Set(gate, "progression", progression);
            gateObject.SetActive(true);
            Call(gate, "RefreshGate");
            Assert.That(box.enabled && blocker.enabled, Is.True);
            PrepareRoom(LevelRoom.FirstEncounter);
            Assert.That(progression.TryComplete(LevelObjective.FirstEnemyDefeated, progression.Snapshot.SessionId), Is.True);
            Call(gate, "RefreshGate");
            Assert.That(box.enabled || blocker.enabled, Is.False);
            enemy.transform.position = origin + new Vector3(4, 0, 0);
            var rigidbody = enemy.gameObject.AddComponent<Rigidbody>(); rigidbody.isKinematic = true;
            progression.TryResetSession();
            Call(gate, "RefreshGate");
            Assert.That(Get<bool>(gate, "ClosePending"), Is.True);
            Assert.That(box.enabled || blocker.enabled, Is.False, "Navigation may not close before the occupied physical gate.");
            enemy.transform.position = origin;
            Call(gate, "RefreshGate");
            Assert.That(Get<bool>(gate, "ClosePending"), Is.False);
            Assert.That(box.enabled && blocker.enabled, Is.True);
        }

        private void PrepareRoom(LevelRoom room)
        {
            Guid session = progression.Snapshot.SessionId;
            Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, session), Is.True);
            if (room == LevelRoom.FirstEncounter) return;
            Assert.That(progression.TryComplete(LevelObjective.FirstEnemyDefeated, session), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.Puzzle, session), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.MainPuzzleSolved, session), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, session), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.ThroneRoom, session), Is.True);
            if (room == LevelRoom.ThroneRoom) return;
            Assert.That(progression.TryComplete(LevelObjective.MinibossDefeated, session), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.Library, session), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.LibraryOpened, session), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.Catacombs, session), Is.True);
            Assert.That(progression.TryEnter(LevelRoom.FinalArena, session), Is.True);
        }

        private void Bake(BoxCollider obstacle = null)
        {
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(origin + new Vector3(0, -.1f, 0), Quaternion.identity, Vector3.one),
                    size = new Vector3(20, .2f, 20), area = 0 }
            };
            if (obstacle != null)
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = obstacle.transform.localToWorldMatrix * Matrix4x4.Translate(obstacle.center),
                    size = obstacle.size, area = 0 });
            var settings = NavMesh.GetSettingsByIndex(0);
            data = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(origin + Vector3.up * 2, new Vector3(24, 6, 24)), Vector3.zero, Quaternion.identity);
            Assert.That(data, Is.Not.Null);
            instance = NavMesh.AddNavMeshData(data);
        }
        private GameObject Make(string name, Vector3 offset)
        {
            var result = new GameObject(name); objects.Add(result);
            SceneManager.MoveGameObjectToScene(result, scene);
            result.transform.position = origin + offset;
            return result;
        }
        private GameObject Box(string name, Vector3 position, Vector3 size)
        {
            var result = Make(name, position);
            result.AddComponent<BoxCollider>().size = size;
            return result;
        }
        private void Tick() => Call(enemy, "Tick", .02f);
        private static bool Damage(Component target, int damage) =>
            (bool)Call(target, "TryDamage", damage, Get<Guid>(target, "LifeId"), Get<Guid>(target, "SessionId"));
        private static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static void Set(Component target, string field, object value) => target.GetType().GetField(field).SetValue(target, value);
        private static T Get<T>(Component target, string property) => (T)target.GetType().GetProperty(property).GetValue(target);
        private static object Call(Component target, string method, params object[] arguments) =>
            target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, arguments);
    }
}
