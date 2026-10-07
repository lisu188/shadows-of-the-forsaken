using System;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Interactions;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class ProgressionGate : MonoBehaviour
{
    public LevelProgressionController progression;
    public LevelRoom from = LevelRoom.FirstEncounter;
    public LevelRoom to = LevelRoom.Puzzle;
    public Renderer[] closedVisuals = Array.Empty<Renderer>();
    public LayerMask occupantMask = ~0;
    private BoxCollider barrier;
    private NavMeshObstacle navigationBlocker;
    private LevelProgressionController observed;
    private readonly GateClosure state = new GateClosure();
    private Collider[] occupants = new Collider[16];

    public bool IsOpen => state.IsOpen;
    public bool ClosePending => state.ClosePending;

    private void Awake()
    {
        barrier = GetComponent<BoxCollider>();
        navigationBlocker = GetComponent<NavMeshObstacle>();
    }
    private void OnEnable()
    {
        if (barrier == null) barrier = GetComponent<BoxCollider>();
        if (navigationBlocker == null) navigationBlocker = GetComponent<NavMeshObstacle>();
        if (closedVisuals == null || closedVisuals.Length == 0) closedVisuals = GetComponentsInChildren<Renderer>(true);
        observed = progression;
        if (observed != null) observed.Changed += OnProgressionChanged;
        RefreshGate();
    }
    private void OnDisable() { if (observed != null) observed.Changed -= OnProgressionChanged; observed = null; }
    private void Update() => RefreshGate();
    private void OnProgressionChanged(ProgressionChange change) => RefreshGate();

    public void RefreshGate()
    {
        if (!isActiveAndEnabled || barrier == null) return;
        bool requestedOpen = progression != null && observed == progression && ValidRoom(from) && ValidRoom(to) &&
            progression.IsPassageOpen(from, to);
        state.Refresh(requestedOpen, IsOccupied());
        barrier.isTrigger = false;
        barrier.enabled = !state.IsOpen;
        if (navigationBlocker != null)
        {
            navigationBlocker.shape = NavMeshObstacleShape.Box;
            navigationBlocker.center = barrier.center;
            navigationBlocker.size = barrier.size;
            navigationBlocker.carving = true;
            navigationBlocker.carveOnlyStationary = false;
            // Follow the physical state, including occupancy-delayed closure.
            // Following the requested progression state would trap an actor.
            navigationBlocker.enabled = !state.IsOpen;
        }
        foreach (var visual in closedVisuals) if (visual != null) visual.enabled = !state.IsOpen;
    }

    private bool IsOccupied()
    {
        // Respawn/teleport may run immediately before the reset notification,
        // including when Unity's automatic transform synchronization is disabled.
        Physics.SyncTransforms();
        var scale = transform.lossyScale;
        var half = Vector3.Scale(barrier.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        half += Vector3.one * 0.05f;
        var physics = gameObject.scene.GetPhysicsScene();
        int count;
        while (true)
        {
            count = physics.OverlapBox(transform.TransformPoint(barrier.center), half, occupants,
                transform.rotation, occupantMask, QueryTriggerInteraction.Ignore);
            if (count < occupants.Length) break;
            if (occupants.Length >= 512) return true;
            Array.Resize(ref occupants, occupants.Length * 2);
        }
        for (int i = 0; i < count; i++)
        {
            var hit = occupants[i];
            if (hit == null || hit == barrier || hit.transform.IsChildOf(transform)) continue;
            if (hit.GetComponentInParent<CharacterController>() != null ||
                hit.GetComponentInParent<CombatHealth>() != null ||
                hit.GetComponentInParent<NavMeshAgent>() != null ||
                (hit.attachedRigidbody != null && !hit.attachedRigidbody.isKinematic)) return true;
        }
        return false;
    }

    private static bool ValidRoom(LevelRoom room) => room >= LevelRoom.Courtyard && room <= LevelRoom.Exit;
}
