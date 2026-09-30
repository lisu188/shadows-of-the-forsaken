using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    public sealed class CombatPhysicsTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private Scene scene;
        private Scene originalScene;
        private Component attacker, attackerHealth, targetHealth;
        private InputActionAsset input;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputSettings.UpdateMode previousMode;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale; Time.timeScale = 1;
            previousMode = InputSystem.settings.updateMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            originalScene = SceneManager.GetActiveScene();
            scene = SceneManager.CreateScene("Combat physics " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var player = Actor("Attacker", Vector3.zero);
            attackerHealth = player.GetComponent(HealthType);
            attacker = player.GetComponent(MeleeType);
            var target = Actor("Target", new Vector3(0, 0, 1.5f));
            targetHealth = target.GetComponent(HealthType);
            Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            if (input != null) Object.DestroyImmediate(input);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.updateMode = previousMode;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            Time.timeScale = previousTimeScale;
            if (scene.IsValid() && scene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(scene);
                float deadline = Time.realtimeSinceStartup + 10;
                while (unload != null && !unload.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(unload == null || unload.isDone, Is.True, "Combat fixture unload timed out.");
            }
        }

        [Test]
        public void CombatPhysicsWindupActiveWindowAndCooldownUseRealTarget()
        {
            Assert.That(Start(), Is.True);
            Step(.1f); Assert.That(Health(targetHealth), Is.EqualTo(100));
            Assert.That(Start(), Is.False);
            Step(.11f); Assert.That(Health(targetHealth), Is.EqualTo(75));
            Step(.2f); Assert.That(Health(targetHealth), Is.EqualTo(75));
            Assert.That(Start(), Is.False);
            Step(.4f); Assert.That(Start(), Is.True);
            Step(.21f); Assert.That(Health(targetHealth), Is.EqualTo(50));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0, 0, 3)]
        [TestCase(0, 0, -1.5f)]
        [TestCase(1.5f, 0, 0)]
        public void CombatPhysicsRejectsTargetsOutsideRangeOrForwardArc(float x, float y, float z)
        {
            targetHealth.transform.position = new Vector3(x, y, z);
            Start(); Step(.25f);
            Assert.That(Health(targetHealth), Is.EqualTo(100));
        }

        [Test]
        public void CombatPhysicsWallBlocksDamageEvenWhenNotInTargetMask()
        {
            targetHealth.gameObject.layer = 8;
            Set(attacker, "targetMask", (LayerMask)(1 << 8));
            Box("Wall", new Vector3(0, 1, .7f), new Vector3(4, 3, .2f));
            Start(); Step(.25f);
            Assert.That(Health(targetHealth), Is.EqualTo(100));
        }

        [Test]
        public void CombatPhysicsEmbeddedWallUsesTheActorsLocalPhysicsScene()
        {
            Box("Wall containing attack origin", new Vector3(0, 1, 0), new Vector3(.4f, 2, .4f));
            Start(); Step(.25f);
            Assert.That(Health(targetHealth), Is.EqualTo(100), "A ray starting inside a wall must not bypass LOS.");
        }

        [Test]
        public void CombatPhysicsUnrelatedDefaultSceneWallDoesNotBlockLocalScene()
        {
            var outside = new GameObject("Other scene wall"); objects.Add(outside);
            SceneManager.MoveGameObjectToScene(outside, originalScene);
            outside.transform.position = new Vector3(0, 1, 0);
            outside.AddComponent<BoxCollider>().size = new Vector3(10, 10, 10);
            Start(); Step(.25f);
            Assert.That(Health(targetHealth), Is.EqualTo(75));
        }

        [Test]
        public void CombatPhysicsCompoundColliderAndRepeatedFramesHitOnceWithoutSelfDamage()
        {
            var extra = Box("Target second collider", new Vector3(.1f, 1, 1.5f), new Vector3(.5f, 1, .5f));
            extra.transform.SetParent(targetHealth.transform, true);
            var own = Box("Own weapon collider", new Vector3(.1f, 1, .4f), new Vector3(.2f, .2f, .3f));
            own.transform.SetParent(attacker.transform, true);
            var ownChildHealth = own.AddComponent(HealthType);
            Start(); Step(.21f); Step(.02f); Step(.02f);
            Assert.That(Health(targetHealth), Is.EqualTo(75));
            Assert.That(Health(attackerHealth), Is.EqualTo(100));
            Assert.That(Health(ownChildHealth), Is.EqualTo(100), "An attached child must neither block nor receive its owner's attack.");
        }

        [Test]
        public void CombatPhysicsLateTargetCannotBeHitAfterActiveWindow()
        {
            targetHealth.transform.position = new Vector3(0, 0, 5);
            Start(); Step(.4f);
            targetHealth.transform.position = new Vector3(0, 0, 1.5f);
            Step(.1f);
            Assert.That(Health(targetHealth), Is.EqualTo(100));
        }

        [Test]
        public void CombatPhysicsLargeFrameCrossingWindowStillHitsOnce()
        {
            Start(); Step(2); Step(2);
            Assert.That(Health(targetHealth), Is.EqualTo(75));
        }

        [Test]
        public void CombatPhysicsOverlappingLargeTargetUsesSurfaceDistance()
        {
            targetHealth.transform.position = new Vector3(0, 0, 5);
            targetHealth.GetComponent<BoxCollider>().size = new Vector3(1, 2, 12);
            Start(); Step(.25f);
            Assert.That(Health(targetHealth), Is.EqualTo(75), "Range is measured to the collider surface, not its distant centre.");
        }

        [Test]
        public void CombatPhysicsDeathOnceAndDeadAttackerCannotAttack()
        {
            int deaths = 0;
            Action<HealthChange> observer = change => { Assert.That(change.IsDeath, Is.True); deaths++; };
            targetHealth.GetType().GetEvent("Died").AddEventHandler(targetHealth, observer);
            Set(attacker, "damage", 100);
            Start(); Step(.25f); Step(1); Start(); Step(.25f);
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(Health(targetHealth), Is.Zero);
            Assert.That((bool)Call(targetHealth.GetComponent(MeleeType), "TryAttack"), Is.False);
            Assert.That(Damage(targetHealth, 1), Is.False);
            targetHealth.GetType().GetEvent("Died").RemoveEventHandler(targetHealth, observer);
        }

        [Test]
        public void CombatPhysicsResetInvalidatesPendingSwingAndOldDamageTokens()
        {
            Guid oldLife = Get<Guid>(targetHealth, "LifeId"), oldSession = Get<Guid>(targetHealth, "SessionId");
            Start(); Step(.1f);
            Call(attackerHealth, "ResetHealth"); Call(targetHealth, "ResetHealth");
            Step(.4f);
            Assert.That(Health(targetHealth), Is.EqualTo(100));
            Assert.That((bool)Call(targetHealth, "TryDamage", 100, oldLife, oldSession), Is.False);
            Start(); Step(.25f); Assert.That(Health(targetHealth), Is.EqualTo(75));
        }

        [Test]
        public void CombatPhysicsSessionResetCancelsSwingAndPreservesOriginalDeathToken()
        {
            var sessionObject = new GameObject("Session"); objects.Add(sessionObject);
            SceneManager.MoveGameObjectToScene(sessionObject, scene);
            var progression = sessionObject.AddComponent<LevelProgressionController>();
            foreach (var body in new[] { attackerHealth, targetHealth })
            {
                ((Behaviour)body).enabled = false; Set(body, "progression", progression); ((Behaviour)body).enabled = true;
            }
            Guid oldSession = progression.Snapshot.SessionId;
            Start(); Step(.1f); progression.TryResetSession(); Step(.4f);
            Assert.That(Health(targetHealth), Is.EqualTo(100));
            HealthChange death = default;
            Action<HealthChange> capture = value => death = value;
            targetHealth.GetType().GetEvent("Died").AddEventHandler(targetHealth, capture);
            Guid capturedLife = Get<Guid>(targetHealth, "LifeId");
            Guid capturedSession = Get<Guid>(targetHealth, "SessionId");
            Damage(targetHealth, 100);
            progression.TryResetSession();
            Assert.That(death.LifeId, Is.EqualTo(capturedLife));
            Assert.That(death.SessionId, Is.EqualTo(capturedSession).And.Not.EqualTo(oldSession));
            Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, progression.Snapshot.SessionId), Is.True);
            Assert.That(progression.TryComplete(LevelObjective.FirstEnemyDefeated, death.SessionId), Is.False);
            Assert.That(Health(targetHealth), Is.EqualTo(100));
            targetHealth.GetType().GetEvent("Died").RemoveEventHandler(targetHealth, capture);
        }

        [Test]
        public void CombatPhysicsLethalChangedObserverResetPreservesOriginalDeathNotification()
        {
            var sessionObject = new GameObject("Reentrant death session"); objects.Add(sessionObject);
            SceneManager.MoveGameObjectToScene(sessionObject, scene);
            var progression = sessionObject.AddComponent<LevelProgressionController>();
            foreach (var body in new[] { attackerHealth, targetHealth })
            {
                ((Behaviour)body).enabled = false;
                Set(body, "progression", progression);
                ((Behaviour)body).enabled = true;
            }
            Guid oldLife = Get<Guid>(targetHealth, "LifeId");
            Guid oldSession = progression.Snapshot.SessionId;
            var deaths = new List<HealthChange>();
            int synchronousResets = 0;
            Action<HealthChange> resetInsideChanged = change =>
            {
                if (!change.IsDeath) return;
                synchronousResets++;
                Assert.That(change.LifeId, Is.EqualTo(oldLife));
                Assert.That(change.SessionId, Is.EqualTo(oldSession));
                Assert.That(progression.TryResetSession(), Is.True);
            };
            Action<HealthChange> captureDeath = change => deaths.Add(change);
            var changedEvent = targetHealth.GetType().GetEvent("Changed");
            var diedEvent = targetHealth.GetType().GetEvent("Died");
            changedEvent.AddEventHandler(targetHealth, resetInsideChanged);
            diedEvent.AddEventHandler(targetHealth, captureDeath);
            try
            {
                Assert.That(Damage(targetHealth, 100), Is.True);
                Assert.That(synchronousResets, Is.EqualTo(1));
                Assert.That(deaths, Has.Count.EqualTo(1));
                Assert.That(deaths[0].IsDeath, Is.True);
                Assert.That(deaths[0].LifeId, Is.EqualTo(oldLife));
                Assert.That(deaths[0].SessionId, Is.EqualTo(oldSession));
                Guid newLife = Get<Guid>(targetHealth, "LifeId");
                Guid newSession = progression.Snapshot.SessionId;
                Assert.That(newLife, Is.Not.EqualTo(oldLife));
                Assert.That(newSession, Is.Not.EqualTo(oldSession));
                Assert.That(Get<Guid>(targetHealth, "SessionId"), Is.EqualTo(newSession));
                Assert.That(Health(targetHealth), Is.EqualTo(100));
                Assert.That((bool)Call(targetHealth, "TryDamage", 100, oldLife, oldSession), Is.False);
                Assert.That(progression.TryEnter(LevelRoom.FirstEncounter, newSession), Is.True);
                Assert.That(progression.TryComplete(LevelObjective.FirstEnemyDefeated, deaths[0].SessionId), Is.False);
                Assert.That(deaths, Has.Count.EqualTo(1), "Stale damage must not publish a second old death.");
                Assert.That(Get<Guid>(targetHealth, "LifeId"), Is.EqualTo(newLife));
                Assert.That(Health(targetHealth), Is.EqualTo(100), "The new life must survive the delayed old death notification.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                changedEvent.RemoveEventHandler(targetHealth, resetInsideChanged);
                diedEvent.RemoveEventHandler(targetHealth, captureDeath);
            }
        }

        [TestCase("interrupt")]
        [TestCase("disable")]
        [TestCase("pause")]
        [TestCase("timescale")]
        public void CombatPhysicsInterruptionsCannotReplayPendingSwing(string reason)
        {
            Start(); Step(.1f);
            switch (reason)
            {
                case "interrupt": Call(attacker, "Interrupt"); break;
                case "disable": ((Behaviour)attacker).enabled = false; ((Behaviour)attacker).enabled = true; break;
                case "pause": Call(attacker, "OnApplicationPause", true); Call(attacker, "OnApplicationPause", false); break;
                case "timescale": Time.timeScale = 0; Step(0); Time.timeScale = 1; break;
            }
            Step(1); Assert.That(Health(targetHealth), Is.EqualTo(100));
            Assert.That(Start(), Is.True); Step(.25f); Assert.That(Health(targetHealth), Is.EqualTo(75));
        }

        [Test]
        public void CombatPhysicsPlayerInputHoldAndReenableDoNotDuplicateAttacks()
        {
            var bridge = AddPlayerBridge(out Component movement);
            Press(false, movement); Press(true, movement); Step(.25f);
            Assert.That(Health(targetHealth), Is.EqualTo(75));
            Step(1); Press(true, movement); Step(.25f);
            Assert.That(Health(targetHealth), Is.EqualTo(75), "Holding LPM must not issue another attack.");
            ((Behaviour)bridge).enabled = false; ((Behaviour)bridge).enabled = true;
            Press(false, movement); Press(true, movement); Step(.25f);
            Assert.That(Health(targetHealth), Is.EqualTo(50));
        }

        [TestCase("focus")]
        [TestCase("movement")]
        [TestCase("controls")]
        public void CombatPhysicsPlayerControlLossCancelsPendingSwing(string reason)
        {
            var bridge = AddPlayerBridge(out Component movement);
            Press(false, movement); Press(true, movement); Step(.1f);
            if (reason == "focus") Call(bridge, "OnApplicationFocus", false);
            if (reason == "movement") ((Behaviour)movement).enabled = false;
            if (reason == "controls") Call(movement, "SetControlsEnabled", false);
            Step(.4f); Assert.That(Health(targetHealth), Is.EqualTo(100));
        }

        [Test]
        public void CombatPhysicsPlayerBridgeReconcilesDeathAndResetWhileDisabled()
        {
            var bridge = AddPlayerBridge(out Component movement);
            Damage(attackerHealth, 100);
            Assert.That(Get<bool>(movement, "ControlsEnabled"), Is.False);
            ((Behaviour)bridge).enabled = false;
            Call(attackerHealth, "ResetHealth");
            ((Behaviour)bridge).enabled = true;
            Assert.That(Get<bool>(movement, "ControlsEnabled"), Is.True);
            ((Behaviour)bridge).enabled = false;
            Damage(attackerHealth, 100);
            ((Behaviour)bridge).enabled = true;
            Assert.That(Get<bool>(movement, "ControlsEnabled"), Is.False, "Re-enabling a dead bridge must stop movement.");
            Assert.That(Start(), Is.False);
        }

        private Component AddPlayerBridge(out Component movement)
        {
            var actor = attacker.gameObject;
            actor.SetActive(false);
            Object.DestroyImmediate(actor.GetComponent<BoxCollider>());
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            input = ScriptableObject.CreateInstance<InputActionAsset>();
            input.devices = new InputDevice[] { keyboard, mouse };
            var map = input.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2").AddBinding("<Gamepad>/leftStick");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            map.AddAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            map.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            movement = actor.AddComponent(Type.GetType("PlayerMovement, Assembly-CSharp", true));
            Set(movement, "inputActions", input);
            var character = actor.GetComponent<CharacterController>(); character.height = 2; character.radius = .3f; character.center = Vector3.up;
            // Keep the fixture level while still exercising real movement/input.
            Set(movement, "gravity", 0f);
            var bridge = actor.AddComponent(Type.GetType("PlayerCombat, Assembly-CSharp", true));
            actor.SetActive(true);
            Call(movement, "OnApplicationFocus", true); Call(bridge, "OnApplicationFocus", true);
            return bridge;
        }
        private void Press(bool held, Component movement)
        {
            InputSystem.QueueStateEvent(mouse, held ? new MouseState().WithButton(MouseButton.Left) : new MouseState());
            InputSystem.Update();
            Assert.That(InputState.currentUpdateType, Is.EqualTo(InputUpdateType.Manual));
            Call(movement, "Simulate", 1f / 60);
        }
        private GameObject Actor(string name, Vector3 position)
        {
            var actor = new GameObject(name); objects.Add(actor); actor.SetActive(false);
            SceneManager.MoveGameObjectToScene(actor, scene); actor.transform.position = position;
            actor.AddComponent<BoxCollider>().center = Vector3.up;
            actor.GetComponent<BoxCollider>().size = new Vector3(.6f, 2, .6f);
            actor.AddComponent(HealthType); actor.AddComponent(MeleeType); actor.SetActive(true);
            return actor;
        }
        private GameObject Box(string name, Vector3 position, Vector3 size)
        {
            var box = new GameObject(name); objects.Add(box); SceneManager.MoveGameObjectToScene(box, scene);
            box.transform.position = position; box.AddComponent<BoxCollider>().size = size; return box;
        }
        private static Type HealthType => Type.GetType("ShadowsOfTheForsaken.Combat.CombatHealth, Assembly-CSharp", true);
        private static Type MeleeType => Type.GetType("ShadowsOfTheForsaken.Combat.MeleeCombat, Assembly-CSharp", true);
        private bool Start() => (bool)Call(attacker, "TryAttack");
        private void Step(float seconds) => Call(attacker, "Simulate", seconds);
        private static int Health(Component body) => Get<int>(body, "Current");
        private static bool Damage(Component body, int amount) =>
            (bool)Call(body, "TryDamage", amount, Get<Guid>(body, "LifeId"), Get<Guid>(body, "SessionId"));
        private static T Get<T>(Component component, string property) => (T)component.GetType().GetProperty(property).GetValue(component);
        private static void Set(Component component, string field, object value) => component.GetType().GetField(field).SetValue(component, value);
        private static object Call(Component component, string method, params object[] arguments)
        {
            try { return component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(component, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
    }
}
