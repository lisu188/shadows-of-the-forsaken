using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    public sealed class CastleLayoutSceneTests
    {
        private const string ScenePath = "Assets/Scenes/ForsakenCastle.unity";
        private Scene scene;
        private bool openedForTest;

        [Serializable] private sealed class LayoutModel
        {
            public RoomDefinition[] rooms;
            public PassageDefinition[] passages;
        }

        [Serializable] private sealed class RoomDefinition
        {
            public string id;
            public string objectName;
            public Vector3 position;
        }

        [Serializable] private sealed class PassageDefinition
        {
            public string from;
            public string to;
            public string objectName;
            public Vector3[] waypoints;
            public Vector3 gatePosition;
        }

        [SetUp]
        public void OpenCommittedScene()
        {
            scene = SceneManager.GetSceneByPath(ScenePath);
            openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
        }

        [TearDown]
        public void CloseCommittedScene()
        {
            if (openedForTest && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }

        [Test]
        public void CastleIsFirstEnabledSceneAndHasNoMissingComponents()
        {
            Assert.That(EditorBuildSettings.scenes.First(item => item.enabled).path, Is.EqualTo(ScenePath));
            Assert.That(EditorBuildSettings.scenes.Any(item => item.enabled && item.path == "Assets/Scenes/SampleScene.unity"), Is.True,
                "Keep the existing baseline scene available for its tests.");
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    Assert.That(component != null, Is.True, "Missing component under " + root.name);
            foreach (string name in new[] { "Geometry", "Rooms", "Passages", "Anchors", "Lighting", "Player", "Main Camera", "Gameplay" })
                Assert.That(scene.GetRootGameObjects().Count(root => root.name == name), Is.EqualTo(1), name);
        }

        [Test]
        public void PlayerAndCameraUseExistingScriptsAndExplicitReferences()
        {
            var player = Root("Player");
            Assert.That(player.CompareTag("Player"), Is.True);
            var character = player.GetComponent<CharacterController>();
            Assert.That(character, Is.Not.Null);
            Assert.That(character.height, Is.EqualTo(2f));
            Assert.That(character.radius, Is.EqualTo(0.3f));
            Assert.That(character.center, Is.EqualTo(Vector3.up));
            Assert.That(character.skinWidth, Is.EqualTo(0.02f).Within(0.001f));
            Assert.That(character.stepOffset, Is.EqualTo(0.3f));
            var movement = player.GetComponent(Type.GetType("PlayerMovement, Assembly-CSharp", true));
            Assert.That(movement, Is.Not.Null);
            var serializedMovement = new SerializedObject(movement);
            Assert.That(AssetDatabase.GetAssetPath(serializedMovement.FindProperty("inputActions").objectReferenceValue),
                Is.EqualTo("Assets/InputSystem_Actions.inputactions"));
            Assert.That(serializedMovement.FindProperty("rotationSpeed").floatValue, Is.EqualTo(120f));
            Assert.That(AssetDatabase.AssetPathToGUID("Assets/PlayerMovement.cs"), Is.EqualTo("963b3f782c6d71942ad1e73d117b2820"));

            var cameraObject = Root("Main Camera");
            Assert.That(cameraObject.CompareTag("MainCamera"), Is.True);
            Assert.That(cameraObject.GetComponent<Camera>(), Is.Not.Null);
            Assert.That(cameraObject.GetComponent<AudioListener>(), Is.Not.Null);
            var follow = cameraObject.GetComponent(Type.GetType("CameraFollow, Assembly-CSharp", true));
            Assert.That(follow, Is.Not.Null);
            var serializedFollow = new SerializedObject(follow);
            Assert.That(serializedFollow.FindProperty("player").objectReferenceValue, Is.EqualTo(player.transform));
            Assert.That(serializedFollow.FindProperty("distance").floatValue, Is.EqualTo(3f));
            Assert.That(serializedFollow.FindProperty("height").floatValue, Is.EqualTo(4f));
            Assert.That(serializedFollow.FindProperty("shoulderOffset").floatValue, Is.EqualTo(-1.8f).Within(.001f));
            Assert.That(serializedFollow.FindProperty("shoulderAimFraction").floatValue, Is.EqualTo(.5f));
            Assert.That(serializedFollow.FindProperty("lookHeightOffset").floatValue, Is.EqualTo(1.5f));
            Assert.That(serializedFollow.FindProperty("pivotHeight").floatValue, Is.EqualTo(1f));
            Assert.That(cameraObject.GetComponent<Camera>().nearClipPlane, Is.EqualTo(.3f).Within(.001f));
            Assert.That(cameraObject.GetComponent<Camera>().fieldOfView, Is.EqualTo(75f).Within(.001f));
            Vector3 expectedPosition = player.transform.position - player.transform.forward * 3f
                + Vector3.up * 4f - player.transform.right * 1.8f;
            Vector3 expectedLook = player.transform.position + Vector3.up * 2.5f - player.transform.right * .9f;
            Assert.That(Vector3.Distance(cameraObject.transform.position, expectedPosition), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(cameraObject.transform.forward, (expectedLook - expectedPosition).normalized),
                Is.LessThan(.001f), "The saved camera pose must match its authored follow settings.");
            Assert.That(AssetDatabase.AssetPathToGUID("Assets/CameraFollow.cs"), Is.EqualTo("9191262690f98974abc0076595479fd6"));
            var geometry = Root("Geometry").GetComponentsInChildren<Collider>(true);
            Assert.That(geometry, Is.Not.Empty);
            int mask = serializedFollow.FindProperty("obstructionMask").intValue;
            foreach (var collider in geometry)
            {
                Assert.That(collider.enabled && !collider.isTrigger, Is.True, collider.name);
                Assert.That(mask & (1 << collider.gameObject.layer), Is.Not.Zero, "Camera excludes " + collider.name);
            }
        }

        [Test]
        public void RoomAndPassageMarkersMatchTheProgressionTopologyWithoutRunningIt()
        {
            var rooms = Root("Rooms").transform.Cast<Transform>().Select(room => room.name).ToArray();
            var expectedRooms = Enum.GetValues(typeof(LevelRoom)).Cast<LevelRoom>().Select(room => "Room_" + room).ToArray();
            CollectionAssert.AreEquivalent(expectedRooms, rooms);
            var passageNames = Root("Passages").transform.Cast<Transform>().Select(passage => passage.name).ToArray();
            Assert.That(passageNames.Distinct().Count(), Is.EqualTo(passageNames.Length));

            // Unlock a separate pure model only to obtain its authoritative graph.
            // Do not mutate the saved scene's real progression to inspect topology.
            var progress = UnlockedModel();
            var expectedEdges = new HashSet<string>();
            var enumRooms = Enum.GetValues(typeof(LevelRoom)).Cast<LevelRoom>().ToArray();
            foreach (var from in enumRooms)
                foreach (var to in enumRooms)
                    if ((int)from < (int)to && progress.IsPassageOpen(from, to)) expectedEdges.Add(EdgeKey(from, to));
            var actualEdges = new HashSet<string>();
            foreach (var passage in Root("Passages").transform.Cast<Transform>())
            {
                string[] parts = passage.name.Split('_');
                Assert.That(parts.Length, Is.EqualTo(3), passage.name);
                Assert.That(parts[0], Is.EqualTo("Passage"));
                Assert.That(Enum.TryParse(parts[1], out LevelRoom from), Is.True, passage.name);
                Assert.That(Enum.TryParse(parts[2], out LevelRoom to), Is.True, passage.name);
                Assert.That(actualEdges.Add(EdgeKey(from, to)), Is.True, "Duplicate passage " + passage.name);
                Assert.That(passage.Find("GateAnchor"), Is.Not.Null, passage.name);
                Assert.That(passage.Cast<Transform>().Count(child => child.name.StartsWith("Waypoint_", StringComparison.Ordinal)),
                    Is.GreaterThanOrEqualTo(2), passage.name);
            }
            CollectionAssert.AreEquivalent(expectedEdges, actualEdges);
            Assert.That(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .Count(component => component != null && component.GetType().Name == "LevelProgressionController"), Is.EqualTo(1));
        }

        [Test]
        public void ImportedSceneMarkersMatchTheCommittedLayoutModel()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/LevelLayout/Editor/CastleLayout.json");
            Assert.That(asset, Is.Not.Null);
            var model = JsonUtility.FromJson<LayoutModel>(asset.text);
            Assert.That(model.rooms, Has.Length.EqualTo(9));
            Assert.That(model.passages, Has.Length.EqualTo(9));
            foreach (var room in model.rooms)
            {
                Assert.That(room.objectName, Is.EqualTo("Room_" + room.id));
                var marker = Root("Rooms").transform.Find(room.objectName);
                Assert.That(marker, Is.Not.Null, room.objectName);
                Assert.That(Vector3.Distance(marker.position, room.position), Is.LessThan(0.001f), room.objectName);
            }
            foreach (var definition in model.passages)
            {
                Assert.That(definition.objectName, Is.EqualTo($"Passage_{definition.from}_{definition.to}"));
                var passage = Root("Passages").transform.Find(definition.objectName);
                Assert.That(passage, Is.Not.Null, definition.objectName);
                for (int i = 0; i < definition.waypoints.Length; i++)
                {
                    var point = passage.Find("Waypoint_" + i.ToString("D2"));
                    Assert.That(point, Is.Not.Null, definition.objectName + " waypoint " + i);
                    Assert.That(Vector3.Distance(point.position, definition.waypoints[i]), Is.LessThan(0.001f), point.name);
                }
                Assert.That(Vector3.Distance(passage.Find("GateAnchor").position, definition.gatePosition), Is.LessThan(0.001f),
                    definition.objectName + " gate");
            }
        }

        [Test]
        public void GameplayAnchorsAndSpawnMatchThePlayableScene()
        {
            var anchors = Root("Anchors").transform;
            CollectionAssert.AreEquivalent(new[] { "Spawn", "FirstEnemy", "MainPuzzle", "ThroneMiniboss", "LibraryMechanism",
                "SecretLever", "BonusDiscovery", "FinalEnemy", "Exit" }, anchors.Cast<Transform>().Select(anchor => anchor.name));
            Assert.That(Vector3.Distance(Root("Player").transform.position, anchors.Find("Spawn").position), Is.LessThan(0.01f));
            Assert.That(anchors.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty,
                "Encounter/interaction anchors must remain markers, not active gameplay.");
        }

        [Test]
        public void PlayableSceneWiresOneSessionThreeEncountersAndFiveMechanisms()
        {
            var progressions = Runtime("ShadowsOfTheForsaken.Progression.LevelProgressionController");
            var sessions = Runtime("LevelSessionController");
            var enemies = Runtime("ShadowsOfTheForsaken.Encounters.EnemyEncounter");
            var mechanisms = Runtime("InteractionTarget");
            var gates = Runtime("ProgressionGate");
            Assert.That(progressions, Has.Length.EqualTo(1)); Assert.That(sessions, Has.Length.EqualTo(1));
            Assert.That(enemies, Has.Length.EqualTo(3)); Assert.That(mechanisms, Has.Length.EqualTo(5));
            Assert.That(gates, Has.Length.EqualTo(9));
            var evidence = Runtime("PlayerValidationEvidence");
            Assert.That(evidence, Has.Length.EqualTo(1));
            Assert.That(evidence[0].gameObject, Is.EqualTo(sessions[0].gameObject));
            Assert.That(new SerializedObject(evidence[0]).FindProperty("session").objectReferenceValue, Is.EqualTo(sessions[0]));
            var progression = progressions[0];
            var player = Root("Player");
            var playerHealth = player.GetComponent(Type.GetType("ShadowsOfTheForsaken.Combat.CombatHealth, Assembly-CSharp", true));
            var session = new SerializedObject(sessions[0]);
            Assert.That(session.FindProperty("progression").objectReferenceValue, Is.EqualTo(progression));
            Assert.That(session.FindProperty("playerHealth").objectReferenceValue, Is.EqualTo(playerHealth));
            Assert.That(((Component)session.FindProperty("player").objectReferenceValue).gameObject, Is.EqualTo(player));
            Assert.That(((Component)session.FindProperty("follow").objectReferenceValue).gameObject, Is.EqualTo(Root("Main Camera")));
            Assert.That(session.FindProperty("spawn").objectReferenceValue, Is.EqualTo(Root("Anchors").transform.Find("Spawn")));
            var assignedEnemies = session.FindProperty("encounters");
            Assert.That(assignedEnemies.arraySize, Is.EqualTo(3));
            CollectionAssert.AreEquivalent(enemies, Enumerable.Range(0, 3)
                .Select(index => assignedEnemies.GetArrayElementAtIndex(index).objectReferenceValue));
            foreach (var enemy in enemies)
            {
                var data = new SerializedObject(enemy);
                Assert.That(data.FindProperty("progression").objectReferenceValue, Is.EqualTo(progression), enemy.name);
                Assert.That(data.FindProperty("playerHealth").objectReferenceValue, Is.EqualTo(playerHealth), enemy.name);
                Assert.That(enemy.GetComponent<NavMeshAgent>(), Is.Not.Null, enemy.name);
                Assert.That(enemy.GetComponent<NavMeshAgent>().enabled, Is.False,
                    "Register authored agents only after the scene's navigation surface has enabled.");
                Assert.That(data.FindProperty("initializeNavigationOnStart").boolValue, Is.True, enemy.name);
                Assert.That(enemy.GetComponent<CapsuleCollider>(), Is.Not.Null, enemy.name);
            }
            CollectionAssert.AreEquivalent(new[] { LevelObjective.None, LevelObjective.MainPuzzleSolved,
                LevelObjective.LibraryOpened, LevelObjective.SecretLeverPulled, LevelObjective.BonusDiscovered },
                mechanisms.Select(item => (LevelObjective)new SerializedObject(item).FindProperty("objective").intValue));
            foreach (var mechanism in mechanisms)
            {
                var data = new SerializedObject(mechanism);
                Assert.That(data.FindProperty("progression").objectReferenceValue, Is.EqualTo(progression), mechanism.name);
                Assert.That(data.FindProperty("focusCollider").objectReferenceValue, Is.Not.Null, mechanism.name);
                Assert.That(mechanism.GetComponent(Type.GetType("MechanismFeedback, Assembly-CSharp", true)), Is.Not.Null);
            }
            var initial = new LevelProgression();
            foreach (var gate in gates)
            {
                var data = new SerializedObject(gate);
                Assert.That(data.FindProperty("progression").objectReferenceValue, Is.EqualTo(progression), gate.name);
                var from = (LevelRoom)data.FindProperty("from").intValue;
                var to = (LevelRoom)data.FindProperty("to").intValue;
                bool closed = !initial.IsPassageOpen(from, to);
                var barrier = gate.GetComponent<BoxCollider>();
                var obstacle = gate.GetComponent<NavMeshObstacle>();
                Assert.That(obstacle, Is.Not.Null, gate.name);
                Assert.That(barrier.enabled, Is.EqualTo(closed), gate.name);
                Assert.That(obstacle.enabled, Is.EqualTo(closed), gate.name);
                Assert.That(obstacle.carving, Is.True, gate.name);
                Assert.That(obstacle.center, Is.EqualTo(barrier.center), gate.name);
                Assert.That(obstacle.size, Is.EqualTo(barrier.size), gate.name);
            }
            var surfaces = Runtime("Unity.AI.Navigation.NavMeshSurface");
            Assert.That(surfaces, Has.Length.EqualTo(1));
            var navigation = new SerializedObject(surfaces[0]).FindProperty("m_NavMeshData").objectReferenceValue;
            Assert.That(navigation, Is.Not.Null, "Commit the real native navigation bake.");
            Assert.That(AssetDatabase.GetAssetPath(navigation), Is.EqualTo("Assets/LevelLayout/CastleNavigation.asset"));
            var triggers = Runtime("LevelRoomTrigger"); Assert.That(triggers, Has.Length.EqualTo(9));
            foreach (var trigger in triggers)
                Assert.That(new SerializedObject(trigger).FindProperty("session").objectReferenceValue, Is.EqualTo(sessions[0]));
        }

        private Component[] Runtime(string name) => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true))
            .Where(component => component != null && component.GetType().FullName == name).ToArray();

        private static LevelProgression UnlockedModel()
        {
            var progress = new LevelProgression();
            var session = progress.Snapshot.SessionId;
            Assert.That(progress.TryEnter(LevelRoom.FirstEncounter, session), Is.True);
            Assert.That(progress.TryComplete(LevelObjective.FirstEnemyDefeated, session), Is.True);
            Assert.That(progress.TryEnter(LevelRoom.Puzzle, session), Is.True);
            Assert.That(progress.TryComplete(LevelObjective.MainPuzzleSolved, session), Is.True);
            Assert.That(progress.TryEnter(LevelRoom.FirstEncounter, session), Is.True);
            Assert.That(progress.TryEnter(LevelRoom.ThroneRoom, session), Is.True);
            Assert.That(progress.TryComplete(LevelObjective.MinibossDefeated, session), Is.True);
            Assert.That(progress.TryEnter(LevelRoom.Library, session), Is.True);
            Assert.That(progress.TryComplete(LevelObjective.LibraryOpened, session), Is.True);
            Assert.That(progress.TryEnter(LevelRoom.Catacombs, session), Is.True);
            Assert.That(progress.TryComplete(LevelObjective.SecretLeverPulled, session), Is.True);
            Assert.That(progress.TryEnter(LevelRoom.FinalArena, session), Is.True);
            Assert.That(progress.TryComplete(LevelObjective.FinalEnemyDefeated, session), Is.True);
            return progress;
        }

        private static string EdgeKey(LevelRoom from, LevelRoom to) => (int)from < (int)to ? from + ":" + to : to + ":" + from;
        private GameObject Root(string name) => scene.GetRootGameObjects().Single(root => root.name == name);
    }
}
