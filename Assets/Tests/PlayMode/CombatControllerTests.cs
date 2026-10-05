using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public class CombatControllerTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<GameObject> suspendedSceneRoots = new List<GameObject>();
        private readonly InputTestFixture inputFixture = new InputTestFixture();
        private LevelProgressionController progression;
        private Component player;
        private Component enemy;
        private InputActionAsset inputs;
        private Mouse mouse;
        private InputSettings.UpdateMode previousUpdateMode;
        private static readonly MethodInfo ManualInputUpdate = typeof(InputSystem).GetMethod("Update",
            BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(InputUpdateType) }, null);

        [SetUp]
        public void SetUp()
        {
            SuspendAuthoredSceneRoots();
            inputFixture.Setup();
            previousUpdateMode = InputSystem.settings.updateMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            mouse = InputSystem.AddDevice<Mouse>();
            inputs = ScriptableObject.CreateInstance<InputActionAsset>();
            inputs.devices = new InputDevice[] { mouse };
            var map = inputs.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            map.AddAction("Jump", InputActionType.Button);
            map.AddAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            map.AddAction("Interact", InputActionType.Button);
            progression = Make("Combat session").AddComponent<LevelProgressionController>();
            var playerObject = Make("Combat player");
            playerObject.SetActive(false);
            playerObject.transform.position = new Vector3(0, 1, 0);
            var movement = playerObject.AddComponent(Type.GetType("PlayerMovement, Assembly-CSharp", true));
            movement.GetType().GetField("inputActions").SetValue(movement, inputs);
            player = playerObject.AddComponent(Type.GetType("PlayerCombat, Assembly-CSharp", true));
            Call(player, "Configure", progression, 100);
            playerObject.SetActive(true);
            Call(movement, "OnApplicationFocus", true);
            Call(player, "OnApplicationFocus", true);
            PumpInput();
            Assert.That((bool)movement.GetType().GetField("inputUpdated", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(movement),
                Is.True, "Neutral focus input must reach the movement consumer before testing attack delivery");
            var enemyObject = Make("Combat demon");
            enemyObject.SetActive(false);
            enemyObject.transform.position = new Vector3(0, 1, 1.6f);
            enemy = enemyObject.AddComponent(Type.GetType("EnemyCombat, Assembly-CSharp", true));
            Call(enemy, "Configure", progression, player, LevelRoom.FirstEncounter,
                LevelObjective.FirstEnemyDefeated, new Bounds(Vector3.zero, new Vector3(12, 8, 12)), 60, 15, 2.2f);
            enemyObject.SetActive(true);
            Call(enemy, "OnApplicationFocus", true);
            progression.TryEnter(LevelRoom.FirstEncounter, progression.Snapshot.SessionId);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                for (int i = objects.Count - 1; i >= 0; i--)
                    if (objects[i] != null) Object.DestroyImmediate(objects[i]);
                objects.Clear();
                if (inputs != null) Object.DestroyImmediate(inputs);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                InputSystem.settings.updateMode = previousUpdateMode;
            }
            finally
            {
                try { inputFixture.TearDown(); }
                finally
                {
                    foreach (var root in suspendedSceneRoots) if (root != null) root.SetActive(true);
                    suspendedSceneRoots.Clear();
                }
            }
        }

        private void SuspendAuthoredSceneRoots()
        {
            var levelType = Type.GetType("ShadowsOfTheForsaken.Level.ForsakenLevel, Assembly-CSharp", true);
            suspendedSceneRoots.Clear();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.isLoaded || (scene.path != "Assets/Scenes/ForsakenCastle.unity" && scene.path != "Assets/Scenes/SampleScene.unity")) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name.IndexOf("TestRunner", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    bool ownsGeometry = root.GetComponent<Collider>() != null || root.GetComponent<Camera>() != null || root.GetComponent<Light>() != null;
                    bool ownsLevel = root.GetComponent(levelType) != null || root.name == "Shadows of the Forsaken";
                    if (root.activeSelf && (ownsGeometry || ownsLevel))
                    {
                        suspendedSceneRoots.Add(root);
                        root.SetActive(false);
                    }
                }
            }
        }

        private GameObject Make(string name)
        {
            var result = new GameObject(name);
            objects.Add(result);
            return result;
        }

        private static object Call(Component component, string method, params object[] arguments)
        {
            try { return component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(component, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        private static T Read<T>(Component component, string property)
        {
            return (T)component.GetType().GetProperty(property).GetValue(component);
        }

        private static void PumpInput()
        {
            Assert.That(ManualInputUpdate, Is.Not.Null, "Pinned Input System must expose its internal typed update pump");
            try { ManualInputUpdate.Invoke(null, new object[] { InputUpdateType.Manual }); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        private static void Advance(Component component, float seconds)
        {
            int frames = Mathf.CeilToInt(seconds * 60);
            for (int i = 0; i < frames; i++) Call(component, "Simulate", 1f / 60);
        }

        [Test]
        public void PlayerSwingHitsCompoundEnemyOnceAndGenuineDeathReportsObjective()
        {
            var extraCollider = Make("Compound hit collider");
            extraCollider.transform.SetParent(enemy.transform, false);
            extraCollider.AddComponent<BoxCollider>().size = Vector3.one;
            Physics.SyncTransforms();
            Call(player, "RequestAttack");
            Advance(player, 0.7f);
            Assert.That(Read<int>(enemy, "Health"), Is.EqualTo(35));
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
            Call(player, "RequestAttack"); Advance(player, 0.7f);
            Call(player, "RequestAttack"); Advance(player, 0.7f);
            Assert.That(Read<bool>(enemy, "IsDead"), Is.True);
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.FirstEnemyDefeated));
            Assert.That(enemy.GetComponent<CharacterController>().enabled, Is.False);
        }

        [Test]
        public void MovementAttackRequestReachesSubscribedCombat()
        {
            var movement = player.GetComponent(Type.GetType("PlayerMovement, Assembly-CSharp", true));
            InputSystem.QueueStateEvent(mouse, new MouseState());
            PumpInput();
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            PumpInput();
            Call(movement, "Simulate", 1f / 60);
            Advance(player, 0.7f);
            Assert.That(Read<int>(enemy, "Health"), Is.EqualTo(35));
        }

        [Test]
        public void InvalidArenaCannotReplaceConfiguredEncounter()
        {
            var invalid = new Bounds(new Vector3(float.NaN, 0, 0), new Vector3(12, 8, 12));
            Assert.Throws<ArgumentException>(() => Call(enemy, "Configure", progression, player,
                LevelRoom.FirstEncounter, LevelObjective.FirstEnemyDefeated, invalid, 60, 15, 2.2f));
            Assert.That(Read<int>(enemy, "Health"), Is.EqualTo(60));
            Assert.That(Read<LevelRoom>(enemy, "Room"), Is.EqualTo(LevelRoom.FirstEncounter));
        }

        [Test]
        public void WallAndRearTargetBlockMeleeDamage()
        {
            var wall = Make("Melee obstruction");
            wall.transform.position = new Vector3(0, 1.5f, 0.8f);
            wall.AddComponent<BoxCollider>().size = new Vector3(2, 3, 0.2f);
            Physics.SyncTransforms();
            Call(player, "RequestAttack"); Advance(player, 0.7f);
            Assert.That(Read<int>(enemy, "Health"), Is.EqualTo(60));
            Object.DestroyImmediate(wall);
            enemy.transform.position = new Vector3(0, 1, -1.6f);
            Physics.SyncTransforms();
            Call(player, "RequestAttack"); Advance(player, 0.7f);
            Assert.That(Read<int>(enemy, "Health"), Is.EqualTo(60));
        }

        [Test]
        public void EnemyTelegraphDealsNoWindupDamageThenHitsOnce()
        {
            Advance(enemy, 0.5f);
            Assert.That(Read<int>(player, "Health"), Is.EqualTo(100));
            Assert.That(Read<object>(enemy, "Phase").ToString(), Is.EqualTo("Windup"));
            Advance(enemy, 0.4f);
            Assert.That(Read<int>(player, "Health"), Is.EqualTo(85));
        }

        [Test]
        public void LeavingEncounterBoundsStopsPursuitAndDamage()
        {
            player.transform.position = new Vector3(0, 1, 20);
            Physics.SyncTransforms();
            var position = enemy.transform.position;
            Advance(enemy, 3);
            Assert.That(enemy.transform.position, Is.EqualTo(position));
            Assert.That(Read<int>(player, "Health"), Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator NativeUpdatePursuesAndDamagesAtUncappedFrameRate()
        {
            return ObserveNativePursuit(-1);
        }

        [UnityTest]
        public IEnumerator NativeUpdatePursuesAndDamagesAtSixtyFramesPerSecond()
        {
            return ObserveNativePursuit(60);
        }

        private IEnumerator ObserveNativePursuit(int requestedFrameRate)
        {
            int previousFrameRate = Application.targetFrameRate;
            int previousVSync = QualitySettings.vSyncCount;
            float previousTimeScale = Time.timeScale;
            var floor = Make("Native pursuit floor");
            floor.transform.position = Vector3.down * .1f;
            floor.AddComponent<BoxCollider>().size = new Vector3(20, .2f, 20);
            var playerMotor = player.GetComponent<CharacterController>();
            var enemyMotor = enemy.GetComponent<CharacterController>();
            playerMotor.enabled = false;
            playerMotor.height = 1.8f; playerMotor.center = Vector3.up * .9f;
            playerMotor.radius = .32f; playerMotor.skinWidth = .03f; playerMotor.stepOffset = .25f;
            player.transform.position = new Vector3(0, .03f, 0);
            playerMotor.enabled = true;
            enemyMotor.enabled = false;
            enemyMotor.height = 1.9f; enemyMotor.center = Vector3.up * .95f; enemyMotor.radius = .4f;
            enemy.transform.position = new Vector3(0, .03f, 4);
            enemyMotor.enabled = true;
            Physics.SyncTransforms();
            int firstFrame = Time.frameCount;
            int observations = 0;
            float minimumDelta = float.PositiveInfinity, maximumDelta = 0, simulatedSeconds = 0;
            float initialZ = enemy.transform.position.z;
            float deadline = Time.realtimeSinceStartup + 8;
            try
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = requestedFrameRate;
                Time.timeScale = 1;
                Assert.That(Read<bool>(player, "CanFight"), Is.True);
                Debug.Log("Native pursuit starts: requested FPS " + requestedFrameRate +
                    ", player minMoveDistance " + playerMotor.minMoveDistance +
                    ", enemy minMoveDistance " + enemyMotor.minMoveDistance);
                // Observe ordinary MonoBehaviour.Update only. No direct Simulate or CharacterController.Move calls.
                while (Read<int>(player, "Health") == 100 && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    minimumDelta = Mathf.Min(minimumDelta, Time.deltaTime);
                    maximumDelta = Mathf.Max(maximumDelta, Time.deltaTime);
                    simulatedSeconds += Time.deltaTime;
                    observations++;
                }
                Assert.That(enemy.transform.position.z, Is.LessThan(initialZ - 1), "Ordinary Update must pursue an idle player.");
                Assert.That(Read<int>(player, "Health"), Is.LessThan(100), "Ordinary Update must deliver a telegraphed enemy hit.");
            }
            finally
            {
                Debug.Log("Native pursuit finishes: requested FPS " + requestedFrameRate +
                    ", frames " + (Time.frameCount - firstFrame) + ", observations " + observations +
                    ", observed seconds " + simulatedSeconds + ", delta min/max " + minimumDelta + "/" + maximumDelta +
                    ", enemy position " + enemy.transform.position + ", enemy phase " + Read<object>(enemy, "Phase") +
                    ", player health " + Read<int>(player, "Health") + ", player CanFight " + Read<bool>(player, "CanFight") +
                    ", enemy grounded " + enemyMotor.isGrounded + ", enemy minMoveDistance " + enemyMotor.minMoveDistance);
                Application.targetFrameRate = previousFrameRate;
                QualitySettings.vSyncCount = previousVSync;
                Time.timeScale = previousTimeScale;
            }
        }

        [Test]
        public void SessionResetRevivesEnemyAndPlayerAndRejectsOldHit()
        {
            var oldSession = progression.Snapshot.SessionId;
            Call(enemy, "ReceiveDamage", 100, oldSession);
            Call(player, "ReceiveDamage", 100, oldSession);
            Assert.That(Read<bool>(enemy, "IsDead"), Is.True);
            Assert.That(Read<bool>(player, "IsDead"), Is.True);
            progression.TryResetSession();
            Assert.That(Read<int>(player, "Health"), Is.EqualTo(100));
            Assert.That(Read<int>(enemy, "Health"), Is.EqualTo(60));
            Assert.That(enemy.GetComponent<CharacterController>().enabled, Is.True);
            progression.TryEnter(LevelRoom.FirstEncounter, progression.Snapshot.SessionId);
            Assert.That(Call(enemy, "ReceiveDamage", 100, oldSession), Is.False);
            Assert.That(Call(player, "ReceiveDamage", 100, oldSession), Is.False);
            Assert.That(progression.Snapshot.CompletedObjectives, Is.EqualTo(LevelObjective.None));
        }

        [Test]
        public void DisabledConsumerCatchesUpToResetOnEnable()
        {
            Call(enemy, "ReceiveDamage", 25, progression.Snapshot.SessionId);
            ((Behaviour)enemy).enabled = false;
            progression.TryResetSession();
            Assert.That(Read<int>(enemy, "Health"), Is.EqualTo(35));
            ((Behaviour)enemy).enabled = true;
            Assert.That(Read<int>(enemy, "Health"), Is.EqualTo(60));
            Assert.That(enemy.transform.position, Is.EqualTo(new Vector3(0, 1, 1.6f)));
        }
    }
}
