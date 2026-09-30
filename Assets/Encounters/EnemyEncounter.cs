using System;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.AI;

namespace ShadowsOfTheForsaken.Encounters
{
    // DOCX sections 3, 6 and 8. One authored actor is reused across sessions;
    // leaving and re-entering an arena never spawns another enemy or restores HP.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatHealth), typeof(MeleeCombat), typeof(NavMeshAgent))]
    public sealed class EnemyEncounter : MonoBehaviour
    {
        public LevelProgressionController progression;
        public CombatHealth playerHealth;
        public LevelRoom encounterRoom = LevelRoom.FirstEncounter;
        public LevelObjective objective = LevelObjective.FirstEnemyDefeated;
        public Vector3 arenaCenter;
        public Vector3 arenaHalfExtents = new Vector3(4, 3, 4);
        [Min(.1f)] public float detectionRadius = 9;
        [Min(.1f)] public float movementSpeed = 2.8f;
        [Min(1)] public float turnSpeed = 360;
        [Min(.1f)] public float stoppingDistance = 1.5f;
        // Authored scenes save the agent disabled so NavMeshSurface.OnEnable
        // can register its data before the native agent first registers.
        public bool initializeNavigationOnStart;

        private CombatHealth health;
        private MeleeCombat melee;
        private NavMeshAgent agent;
        private LevelProgressionController observed;
        private readonly EncounterState state = new EncounterState();
        private NavMeshPath path;
        private Collider[] bodyColliders;
        private bool[] originalColliderStates;
        private RaycastHit[] hits = new RaycastHit[16];
        private Collider[] overlaps = new Collider[16];
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private bool initialized, sessionRunning = true, applicationPaused, applicationFocused = true, detected;
        private bool resetPending;
        private bool navigationInitializationPending;
        private float repathRemaining;
        private bool pathUsable;
        private Vector3 pathDestination;

        public bool IsActivated => state.Activated;
        public bool HasPendingDeath => state.HasPendingDeath;
        public bool DeathCredited => state.DeathCredited;
        public EncounterPhase Phase { get; private set; } = EncounterPhase.Dormant;
        public Guid CapturedSessionId => state.SessionId;
        public Guid CapturedLifeId => state.LifeId;

        private void Awake() => Initialize();
        private void Start()
        {
            navigationInitializationPending = initializeNavigationOnStart && !agent.enabled;
            InitializeNavigation();
        }

        private void InitializeNavigation()
        {
            if (!navigationInitializationPending) return;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(transform.position, out var point, .25f, filter) ||
                Mathf.Abs(point.position.y - transform.position.y) > .25f) return;
            agent.enabled = true;
            navigationInitializationPending = false;
        }

        private void Initialize()
        {
            if (initialized) return;
            health = GetComponent<CombatHealth>();
            melee = GetComponent<MeleeCombat>();
            agent = GetComponent<NavMeshAgent>();
            path = new NavMeshPath();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            applicationFocused = Application.isFocused;
            bodyColliders = GetComponentsInChildren<Collider>(true);
            originalColliderStates = new bool[bodyColliders.Length];
            for (int i = 0; i < bodyColliders.Length; i++) originalColliderStates[i] = bodyColliders[i].enabled;
            agent.updatePosition = agent.updateRotation = false;
            agent.autoTraverseOffMeshLink = false;
            agent.speed = movementSpeed;
            agent.stoppingDistance = stoppingDistance;
            agent.acceleration = 18;
            state.Reset(health.SessionId, health.LifeId);
            initialized = true;
            health.SetEncounterDamageEnabled(false);
            melee.SetAttacksEnabled(false);
            StopNavigation();
        }

        private void OnEnable()
        {
            Initialize();
            health.Died += OnDied;
            observed = progression;
            if (observed != null) observed.Changed += OnProgressionChanged;
            // Disabled actors may have missed the reset event. Re-arm only in a
            // new session, never because an old death callback has arrived.
            if (progression != null && state.SessionId != progression.Snapshot.SessionId) ResetEncounter();
        }

        private void OnDisable()
        {
            if (health != null) health.Died -= OnDied;
            if (observed != null) observed.Changed -= OnProgressionChanged;
            observed = null;
            if (melee != null) melee.SetAttacksEnabled(false);
            StopNavigation();
        }

        private void OnProgressionChanged(ProgressionChange change)
        {
            if (change.Kind != ProgressionChangeKind.SessionReset) return;
            resetPending = true;
            melee.SetAttacksEnabled(false);
            StopNavigation();
        }

        private void OnDied(HealthChange change)
        {
            // Preserve the original payload even if an earlier health observer
            // reset the session or actor. Never substitute the current tokens.
            if (!change.IsDeath || !state.RecordDeath(change.SessionId, change.LifeId)) return;
            melee.SetAttacksEnabled(false);
            StopNavigation();
            SetBodyColliders(false);
            Phase = EncounterPhase.Defeated;
        }

        private void Update() => Tick(Time.deltaTime);
        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused) Suspend();
        }
        private void OnApplicationFocus(bool focused)
        {
            applicationFocused = focused;
            if (!focused) Suspend();
        }

        public void SetSessionRunning(bool value)
        {
            sessionRunning = value;
            if (!value) Suspend();
        }

        public void ResetEncounter()
        {
            Initialize();
            Suspend();
            if (progression != null && health.SessionId != progression.Snapshot.SessionId) health.ResetHealth();
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.Warp(spawnPosition);
                agent.nextPosition = spawnPosition;
            }
            SetBodyColliders(true);
            state.Reset(health.SessionId, health.LifeId);
            health.SetEncounterDamageEnabled(false);
            detected = resetPending = pathUsable = false;
            repathRemaining = 0;
            Phase = EncounterPhase.Dormant;
        }

        public void Tick(float seconds)
        {
            if (!isActiveAndEnabled || !Finite(seconds) || seconds <= 0) return;
            InitializeNavigation();
            if (resetPending) ResetEncounter();
            bool running = sessionRunning && !applicationPaused && applicationFocused && Time.timeScale > 0 &&
                progression != null && progression.isActiveAndEnabled && !progression.Snapshot.IsCompleted &&
                progression.Snapshot.SessionId == state.SessionId && health.SessionId == state.SessionId &&
                health.LifeId == state.LifeId;
            bool roomMatches = progression != null && progression.Snapshot.Room == encounterRoom;
            if (running && health.IsAlive && ValidObjective() &&
                state.TryActivate(health.SessionId, health.LifeId, roomMatches))
                health.SetEncounterDamageEnabled(true);

            // A last swing may kill just as the player crosses back out. Credit
            // remains attached to the dead life until its room is entered again.
            if (running && ValidObjective() && state.CanCredit(progression.Snapshot.SessionId, health.LifeId,
                    roomMatches, progression.CanComplete(objective)))
            {
                Guid session = state.SessionId, life = state.LifeId;
                if (progression.TryComplete(objective, session)) state.ConfirmCredit(session, life);
            }

            bool targetAvailable = playerHealth != null && playerHealth.isActiveAndEnabled && playerHealth.IsAlive &&
                playerHealth.SessionId == state.SessionId;
            bool targetInside = targetAvailable && Contains(playerHealth.transform.position);
            bool sight = targetInside && ClearSight(playerHealth.transform.position + Vector3.up);
            float distance = targetAvailable ? FlatDistance(transform.position, playerHealth.transform.position) : float.PositiveInfinity;
            if (state.Activated && targetInside && distance <= detectionRadius && sight) detected = true;
            if (!targetAvailable || !targetInside) detected = false;
            bool atHome = FlatDistance(transform.position, spawnPosition) < .15f;
            bool canStrike = targetAvailable && targetInside && sight && distance <= stoppingDistance + .12f;
            bool pursuing = detected && targetAvailable && targetInside;
            Vector3 destination = pursuing ? playerHealth.transform.position : spawnPosition;
            bool navigating = running && health.IsAlive && state.Activated && !canStrike && melee.Phase == AttackPhase.Ready &&
                (pursuing || !atHome);
            repathRemaining -= seconds;
            bool canNavigate = navigating && RefreshPath(destination);
            Phase = EncounterState.Decide(state.Activated, health.IsAlive, running, targetAvailable, targetInside,
                detected, melee.Phase != AttackPhase.Ready, canStrike, canNavigate, atHome);

            switch (Phase)
            {
                case EncounterPhase.Chasing:
                case EncounterPhase.Returning:
                    melee.SetAttacksEnabled(false);
                    MoveNavigation(seconds);
                    break;
                case EncounterPhase.Attacking:
                    StopNavigation();
                    melee.SetAttacksEnabled(true);
                    if (melee.Phase == AttackPhase.Ready && canStrike)
                    {
                        Face(playerHealth.transform.position - transform.position, seconds);
                        Vector3 direction = playerHealth.transform.position - transform.position;
                        direction.y = 0;
                        if (direction.sqrMagnitude < .001f || Vector3.Angle(transform.forward, direction) < melee.arcDegrees * .4f)
                            melee.TryAttack();
                    }
                    break;
                default:
                    Suspend();
                    break;
            }
        }

        private bool RefreshPath(Vector3 destination)
        {
            if (!agent.enabled || !agent.isOnNavMesh || !Contains(transform.position)) return false;
            if (repathRemaining > 0 && pathUsable && Vector3.Distance(destination, pathDestination) < .5f &&
                !agent.isPathStale && agent.pathStatus == NavMeshPathStatus.PathComplete) return true;
            repathRemaining = .2f;
            pathUsable = false;
            if (!NavMesh.SamplePosition(destination, out var sample, .8f, agent.areaMask) || !Contains(sample.position)) return false;
            if (!agent.CalculatePath(sample.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            foreach (var corner in path.corners) if (!Contains(corner)) return false;
            agent.speed = movementSpeed;
            agent.stoppingDistance = detected ? stoppingDistance : .05f;
            agent.nextPosition = transform.position;
            if (!agent.SetPath(path)) return false;
            agent.isStopped = false;
            pathDestination = destination;
            return pathUsable = true;
        }

        private void MoveNavigation(float seconds)
        {
            if (!agent.enabled || !agent.isOnNavMesh || !pathUsable) { StopNavigation(); return; }
            agent.isStopped = false;
            Vector3 displacement = Vector3.ClampMagnitude(agent.nextPosition - transform.position, movementSpeed * seconds);
            if (displacement.sqrMagnitude < .0000001f) return;
            Vector3 next = transform.position + displacement;
            if (!Contains(next) || !ClearMovement(displacement))
            {
                // Navigation carving is asynchronous; the physics barrier is the
                // authority even before its NavMeshObstacle has updated the mesh.
                agent.nextPosition = transform.position;
                StopNavigation();
                Phase = EncounterPhase.Blocked;
                return;
            }
            transform.position = next;
            agent.nextPosition = next;
            Face(displacement, seconds);
        }

        private bool ClearMovement(Vector3 displacement)
        {
            float radius = Mathf.Max(.05f, agent.radius - .025f);
            float height = Mathf.Max(radius * 2, agent.height);
            Vector3 bottom = transform.position + Vector3.up * (radius + .04f);
            Vector3 top = transform.position + Vector3.up * (height - radius);
            var physics = gameObject.scene.GetPhysicsScene();
            int count;
            while (true)
            {
                count = physics.CapsuleCast(bottom, top, radius, displacement.normalized, hits,
                    displacement.magnitude + .025f, ~0, QueryTriggerInteraction.Ignore);
                if (count < hits.Length) break;
                if (hits.Length >= 512) return false;
                Array.Resize(ref hits, hits.Length * 2);
            }
            for (int i = 0; i < count; i++)
                if (hits[i].collider != null && !hits[i].collider.transform.IsChildOf(transform)) return false;
            return true;
        }

        private bool ClearSight(Vector3 point)
        {
            Physics.SyncTransforms();
            Vector3 origin = transform.position + Vector3.up;
            Vector3 direction = point - origin;
            var physics = gameObject.scene.GetPhysicsScene();
            int count;
            while (true)
            {
                count = physics.OverlapSphere(origin, .01f, overlaps, ~0, QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length) break;
                if (overlaps.Length >= 512) return false;
                Array.Resize(ref overlaps, overlaps.Length * 2);
            }
            for (int i = 0; i < count; i++) if (BlocksSight(overlaps[i])) return false;
            if (direction.sqrMagnitude < .0001f) return true;
            while (true)
            {
                count = physics.Raycast(origin, direction.normalized, hits, direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
                if (count < hits.Length) break;
                if (hits.Length >= 512) return false;
                Array.Resize(ref hits, hits.Length * 2);
            }
            for (int i = 0; i < count; i++) if (BlocksSight(hits[i].collider)) return false;
            return true;
        }

        private bool BlocksSight(Collider collider) => collider != null && !collider.transform.IsChildOf(transform) &&
            collider.GetComponentInParent<CombatHealth>() != playerHealth;
        private bool Contains(Vector3 point)
        {
            Vector3 offset = point - arenaCenter;
            return Mathf.Abs(offset.x) <= arenaHalfExtents.x && Mathf.Abs(offset.y) <= arenaHalfExtents.y &&
                Mathf.Abs(offset.z) <= arenaHalfExtents.z;
        }
        private void Face(Vector3 direction, float seconds)
        {
            direction.y = 0;
            if (direction.sqrMagnitude > .0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * seconds);
        }
        private void StopNavigation()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = true;
            agent.ResetPath();
            agent.nextPosition = transform.position;
            pathUsable = false;
        }
        private void Suspend()
        {
            if (melee != null) melee.SetAttacksEnabled(false);
            StopNavigation();
        }
        private void SetBodyColliders(bool restore)
        {
            for (int i = 0; i < bodyColliders.Length; i++)
                if (bodyColliders[i] != null) bodyColliders[i].enabled = restore && originalColliderStates[i];
        }
        private bool ValidObjective() =>
            (encounterRoom == LevelRoom.FirstEncounter && objective == LevelObjective.FirstEnemyDefeated) ||
            (encounterRoom == LevelRoom.ThroneRoom && objective == LevelObjective.MinibossDefeated) ||
            (encounterRoom == LevelRoom.FinalArena && objective == LevelObjective.FinalEnemyDefeated);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0;
            return Vector3.Distance(a, b);
        }
        private void OnValidate()
        {
            detectionRadius = Finite(detectionRadius) ? Mathf.Clamp(detectionRadius, .1f, 100) : 9;
            movementSpeed = Finite(movementSpeed) ? Mathf.Clamp(movementSpeed, .1f, 20) : 2.8f;
            stoppingDistance = Finite(stoppingDistance) ? Mathf.Clamp(stoppingDistance, .1f, 5) : 1.5f;
            turnSpeed = Finite(turnSpeed) ? Mathf.Clamp(turnSpeed, 1, 1080) : 360;
            arenaHalfExtents = new Vector3(SafeExtent(arenaHalfExtents.x), SafeExtent(arenaHalfExtents.y), SafeExtent(arenaHalfExtents.z));
        }
        private static float SafeExtent(float value) => Finite(value) ? Mathf.Clamp(value, .1f, 100) : 4;
    }
}
