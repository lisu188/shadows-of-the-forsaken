using System.Collections.Generic;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShadowsOfTheForsaken.Level
{
    /// <summary>Scene-owned composition of the single DOCX level. No persistent/global session.</summary>
    [DisallowMultipleComponent]
    public sealed class ForsakenLevel : MonoBehaviour
    {
        public InputActionAsset inputActions;
        public Material surfaceMaterial;
        public Material nightSky;
        public LevelProgressionController Progression { get; private set; }
        public PlayerMovement Movement { get; private set; }
        public PlayerCombat Combat { get; private set; }
        public PlayerInteraction Interaction { get; private set; }
        public CameraFollow Follow { get; private set; }
        public readonly List<EnemyCombat> Enemies = new List<EnemyCombat>();
        public readonly List<PassageGate> Gates = new List<PassageGate>();
        public readonly List<RoomTransition> Transitions = new List<RoomTransition>();
        public RunePuzzle Puzzle { get; private set; }
        public bool Failed { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public bool IsTerminal => Failed || (Progression != null && Progression.Snapshot.IsCompleted);
        private InputAction restart;
        private bool restartPending;
        private System.Guid restartSession;
        private bool focused;
        private bool paused;
        private Vector3 spawn = new Vector3(0, 0.08f, -51);
        private readonly List<Material> materials = new List<Material>();

        private void Awake()
        {
            Progression = gameObject.AddComponent<LevelProgressionController>();
            var architecture = new CastleArchitecture(transform, surfaceMaterial, materials);
            architecture.BuildShell(nightSky);
            BuildPlayer(architecture);
            BuildConnections(architecture);
            BuildEncounters(architecture);
            BuildMechanisms(architecture);
            architecture.Furnish();
            gameObject.AddComponent<LevelAtmosphere>().Configure(architecture.TorchLights);
            gameObject.AddComponent<LevelHud>().Configure(this);
            Physics.SyncTransforms();
            Follow.SnapToTarget();
            var arguments = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
                if (arguments[i] == "-forsakenSmokeOutput")
                    gameObject.AddComponent<LevelDeliveryProbe>().Configure(this, System.IO.Path.GetFullPath(arguments[i + 1]));
        }

        private void OnEnable()
        {
            focused = Application.isFocused;
            Progression.Changed += ProgressChanged;
            Combat.Died += PlayerDied;
            var source = inputActions != null ? inputActions : InputSystem.actions;
            var action = source != null ? source.FindAction("Player/Restart") : null;
            if (action != null)
            {
                restart = action.Clone();
                restart.performed += RestartRequested;
                restart.Enable();
            }
        }

        private void OnDisable()
        {
            if (Progression != null) Progression.Changed -= ProgressChanged;
            if (Combat != null) Combat.Died -= PlayerDied;
            if (restart != null)
            {
                restart.performed -= RestartRequested;
                restart.Disable();
                restart.Dispose();
                restart = null;
            }
            restartPending = false;
        }

        private void OnApplicationFocus(bool value)
        {
            focused = value;
            if (!value) restartPending = false;
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            if (value) restartPending = false;
        }

        private void OnDestroy()
        {
            foreach (var material in materials) if (material != null) Destroy(material);
        }

        private void Update()
        {
            if (restartPending)
            {
                restartPending = false;
                if (focused && !paused && Time.timeScale > 0 && restartSession == Progression.Snapshot.SessionId) TryRestart();
            }
            if (!IsTerminal) ElapsedSeconds += Time.deltaTime;
            // Falling out of world is a failure, never an alternate progression route.
            if (!IsTerminal && Movement.transform.position.y < -15) PlayerDied();
        }

        private void RestartRequested(InputAction.CallbackContext context)
        {
            if (IsTerminal && focused && !paused && Time.timeScale > 0)
            {
                restartSession = Progression.Snapshot.SessionId;
                restartPending = true;
            }
        }

        private void PlayerDied()
        {
            Failed = true;
            Movement.SetControlsEnabled(false);
        }

        private void ProgressChanged(ProgressionChange change)
        {
            if (change.After.IsCompleted) Movement.SetControlsEnabled(false);
            // Observers only reset presentation here; no synchronous progression commands.
            if (change.Kind == ProgressionChangeKind.SessionReset)
            {
                Failed = false;
                ElapsedSeconds = 0;
                var controller = Movement.GetComponent<CharacterController>();
                controller.enabled = false;
                Movement.transform.SetPositionAndRotation(spawn, Quaternion.identity);
                controller.enabled = true;
                Movement.ResetMotion();
                Movement.SetControlsEnabled(true);
                Physics.SyncTransforms();
                Follow.SnapToTarget();
            }
        }

        public bool TryRestart()
        {
            return IsTerminal && Progression.TryResetSession();
        }

        private void BuildPlayer(CastleArchitecture art)
        {
            var player = new GameObject("Forsaken wanderer");
            player.SetActive(false);
            player.transform.SetParent(transform, false);
            player.transform.position = spawn;
            player.tag = "Player";
            var body = player.AddComponent<CharacterController>();
            body.height = 1.8f; body.radius = 0.32f; body.center = Vector3.up * 0.9f;
            body.stepOffset = 0.25f; body.slopeLimit = 45; body.skinWidth = 0.03f;
            body.minMoveDistance = 0; // Preserve real small per-frame movement at uncapped frame rates.
            Movement = player.AddComponent<PlayerMovement>();
            Movement.speed = 2f; Movement.rotationSpeed = 110; Movement.inputActions = inputActions;
            var sword = art.Actor(player.transform, false, false);
            Combat = player.AddComponent<PlayerCombat>();
            Combat.Configure(Progression, 100);
            Interaction = player.AddComponent<PlayerInteraction>();
            Interaction.Configure(Movement, Progression, 2.6f);
            Combat.SetWeaponVisual(sword);
            player.SetActive(true);
            var camera = new GameObject("Main Camera");
            camera.transform.SetParent(transform, false);
            camera.tag = "MainCamera";
            var view = camera.AddComponent<Camera>();
            view.nearClipPlane = 0.08f; view.farClipPlane = 360; view.fieldOfView = 58;
            view.backgroundColor = new Color(0.035f, 0.045f, 0.06f);
            camera.AddComponent<AudioListener>();
            Follow = camera.AddComponent<CameraFollow>();
            Follow.distance = 3.8f; Follow.height = 2.7f; Follow.pivotHeight = 1.25f;
            Follow.smoothSpeed = 8; Follow.SetTarget(player.transform);
        }

        private void BuildConnections(CastleArchitecture art)
        {
            Passage(art, LevelRoom.Courtyard, LevelRoom.FirstEncounter, new Vector3(0, 0, 18), Vector3.forward);
            Passage(art, LevelRoom.FirstEncounter, LevelRoom.Puzzle, new Vector3(-12, 0, 30), Vector3.forward);
            Passage(art, LevelRoom.FirstEncounter, LevelRoom.ThroneRoom, new Vector3(12, 0, 42), Vector3.forward);
            Passage(art, LevelRoom.ThroneRoom, LevelRoom.Library, new Vector3(24, 0, 54), Vector3.forward);
            Passage(art, LevelRoom.Library, LevelRoom.Catacombs, new Vector3(24, 0, 66), Vector3.forward);
            Passage(art, LevelRoom.Catacombs, LevelRoom.FinalArena, new Vector3(12, 0, 78), Vector3.forward);
            Passage(art, LevelRoom.Catacombs, LevelRoom.BonusRoom, new Vector3(-12, 0, 78), Vector3.forward);
            Passage(art, LevelRoom.FinalArena, LevelRoom.Exit, new Vector3(12, 0, 102), Vector3.forward);
            // Concealed lower tunnel, distinct from the map's upper bonus-room branch.
            Passage(art, LevelRoom.ThroneRoom, LevelRoom.BonusRoom, new Vector3(-6, 0, 96), Vector3.left);
            art.ShortcutEntrance(Progression);
        }

        private void Passage(CastleArchitecture art, LevelRoom from, LevelRoom to, Vector3 position, Vector3 direction)
        {
            var barrier = art.Door(position, direction, to == LevelRoom.Catacombs || to == LevelRoom.BonusRoom);
            var gate = barrier.AddComponent<PassageGate>();
            gate.Configure(Progression, from, to, barrier.GetComponent<Collider>(), barrier.GetComponentsInChildren<Renderer>());
            Gates.Add(gate);
            var crossing = new GameObject(from + " to " + to);
            crossing.transform.SetParent(transform, false);
            crossing.transform.SetPositionAndRotation(position + Vector3.up * 4, Quaternion.LookRotation(direction));
            var trigger = crossing.AddComponent<BoxCollider>();
            trigger.isTrigger = true; trigger.size = new Vector3(5, 10, 0.8f);
            // CharacterController contacts need a trigger Rigidbody on the stationary volume.
            var rigidbody = crossing.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true; rigidbody.useGravity = false;
            var transition = crossing.AddComponent<RoomTransition>();
            transition.Configure(Progression, Movement, from, to, position - direction * 2 + Vector3.up * .08f,
                position + direction * 2 + Vector3.up * .08f);
            Transitions.Add(transition);
        }

        private void BuildEncounters(CastleArchitecture art)
        {
            Enemy(art, "Gate demon", new Vector3(0, .08f, 25), LevelRoom.FirstEncounter,
                LevelObjective.FirstEnemyDefeated, new Bounds(new Vector3(0, 2, 24), new Vector3(10, 5, 10)), 45, 10, 1.8f, false);
            Enemy(art, "The corrupted castellan", new Vector3(12, .08f, 50), LevelRoom.ThroneRoom,
                LevelObjective.MinibossDefeated, new Bounds(new Vector3(12, 2, 48), new Vector3(10, 5, 10)), 100, 16, 2.1f, true);
            Enemy(art, "The last curse", new Vector3(12, .08f, 94), LevelRoom.FinalArena,
                LevelObjective.FinalEnemyDefeated, new Bounds(new Vector3(12, 2, 90), new Vector3(10, 5, 22)), 120, 20, 2.3f, false);
        }

        private void Enemy(CastleArchitecture art, string title, Vector3 position, LevelRoom room,
            LevelObjective objective, Bounds bounds, int health, int damage, float speed, bool human)
        {
            var actor = new GameObject(title);
            actor.SetActive(false); actor.transform.SetParent(transform, false); actor.transform.position = position;
            var controller = actor.AddComponent<CharacterController>();
            controller.height = 1.9f; controller.radius = .4f; controller.center = Vector3.up * .95f;
            art.Actor(actor.transform, true, human);
            var enemy = actor.AddComponent<EnemyCombat>();
            enemy.Configure(Progression, Combat, room, objective, bounds, health, damage, speed);
            Enemies.Add(enemy); actor.SetActive(true);
        }

        private void BuildMechanisms(CastleArchitecture art)
        {
            var puzzle = new GameObject("Runes of the moon");
            puzzle.transform.SetParent(transform, false);
            Puzzle = puzzle.AddComponent<RunePuzzle>();
            Puzzle.Configure(Progression, new[] { 0, 1, 2 }, "The inscription reads: crescent, crown, flame.");
            art.Inscription(new Vector3(-12, 1.8f, 40.8f), "CRESCENT  >  CROWN  >  FLAME", 180);
            string[] names = { "Crescent", "Crown", "Flame" };
            for (int i = 0; i < 3; i++)
            {
                var rune = art.Mechanism(new Vector3(-15 + i * 3, 0, 38), names[i], false);
                rune.AddComponent<LevelInteractable>().ConfigureRune(Puzzle, i, names[i]);
            }
            var book = art.Mechanism(new Vector3(25.5f, 0, 61.7f), "Forbidden book", false);
            book.AddComponent<LevelInteractable>().ConfigureObjective(Progression, LevelObjective.LibraryOpened, "Turn the forbidden book / reveal the hidden door");
            var lever = art.Mechanism(new Vector3(27.5f, 0, 72), "Concealed cult lever", true);
            lever.AddComponent<LevelInteractable>().ConfigureObjective(Progression, LevelObjective.SecretLeverPulled, "Pull the concealed lever (optional)");
            var relic = art.Mechanism(new Vector3(-12, 0, 96), "Relic of the fallen kingdom", false);
            relic.AddComponent<LevelInteractable>().ConfigureObjective(Progression, LevelObjective.BonusDiscovered, "Discover the relic (optional)");
        }
    }
}
