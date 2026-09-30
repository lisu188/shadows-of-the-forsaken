using System;
using System.Collections.Generic;
using ShadowsOfTheForsaken.Interactions;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public sealed class PlayerInteractor : MonoBehaviour
{
    public LevelProgressionController progression;
    [Min(0.1f)] public float range = 2.5f;
    [Range(0, 180)] public float halfAngle = 60;
    public Vector3 localOrigin = Vector3.up;
    public LayerMask targetMask = ~0;
    public LayerMask obstructionMask = ~0;

    public InteractionTarget SelectedTarget { get; private set; }
    public bool CanInteract { get; private set; }
    public string Prompt { get; private set; } = "";
    public event Action SelectionChanged;

    private PlayerMovement movement;
    private Collider[] overlaps = new Collider[32];
    private RaycastHit[] hits = new RaycastHit[32];
    private Collider[] originHits = new Collider[16];
    private readonly HashSet<EntityId> seen = new HashSet<EntityId>();
    private readonly List<InteractionTarget> targets = new List<InteractionTarget>();
    private readonly List<InteractionCandidate> candidates = new List<InteractionCandidate>();
    private const int QueryLimit = 512;

    private void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        movement.InteractRequested += OnInteractRequested;
    }

    private void OnDisable()
    {
        if (movement != null) movement.InteractRequested -= OnInteractRequested;
        SetSelection(null);
    }

    private void OnDestroy() { SelectionChanged = null; }
    private void Update() => RefreshTarget();
    private void OnInteractRequested() => TryInteract();

    public void RefreshTarget()
    {
        if (!isActiveAndEnabled || movement == null || !movement.isActiveAndEnabled || !movement.ControlsEnabled ||
            progression == null || !progression.isActiveAndEnabled || progression.Snapshot.IsCompleted ||
            !Finite(range) || range <= 0 || range > 100 || !Finite(halfAngle) || halfAngle < 0 || halfAngle > 180 ||
            !Finite(localOrigin.x) || !Finite(localOrigin.y) || !Finite(localOrigin.z))
        {
            SetSelection(null);
            return;
        }
        Physics.SyncTransforms();
        var physics = gameObject.scene.GetPhysicsScene();
        var origin = transform.TransformPoint(localOrigin);
        int count;
        while (true)
        {
            count = physics.OverlapSphere(origin, range, overlaps, targetMask, QueryTriggerInteraction.Collide);
            if (count < overlaps.Length) break;
            if (overlaps.Length >= QueryLimit) { SetSelection(null); return; }
            Array.Resize(ref overlaps, overlaps.Length * 2);
        }
        seen.Clear(); targets.Clear(); candidates.Clear();
        for (int i = 0; i < count; i++)
        {
            var target = overlaps[i].GetComponentInParent<InteractionTarget>();
            if (target == null || !target.isActiveAndEnabled || target.progression != progression || !target.HasValidFocus ||
                !seen.Add(target.GetEntityId())) continue;
            targets.Add(target);
        }
        // Keep the full engine identity for deduplication and ordering. The pure
        // core receives only a unique rank in this ordered list, never truncated IDs.
        targets.Sort((first, second) => first.GetEntityId().CompareTo(second.GetEntityId()));
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var offset = target.AimPosition - origin;
            float squared = offset.sqrMagnitude;
            float facing = squared > 0.000001f ? Mathf.Clamp(Vector3.Dot(transform.forward, offset.normalized), -1, 1) : 1;
            candidates.Add(new InteractionCandidate(i, squared, facing,
                squared <= range * range && Visible(physics, origin, target.AimPosition, target), target.selectionOrder));
        }
        int selected = InteractionSelection.Select(candidates, range, Mathf.Cos(halfAngle * Mathf.Deg2Rad));
        SetSelection(selected >= 0 ? targets[selected] : null);
    }

    // A press is a new action: capture first, then revalidate geometry immediately.
    public bool TryInteract()
    {
        if (progression == null || !progression.isActiveAndEnabled) return false;
        Guid session = progression.Snapshot.SessionId;
        RefreshTarget();
        var target = SelectedTarget;
        bool activated = target != null && CanInteract && target.TryActivate(session);
        RefreshTarget();
        return activated;
    }

    private bool Visible(PhysicsScene physics, Vector3 origin, Vector3 destination, InteractionTarget target)
    {
        var offset = destination - origin;
        int count;
        // Raycasts alone do not report a solid collider containing their origin.
        while (true)
        {
            count = physics.OverlapSphere(origin, 0.02f, originHits, obstructionMask, QueryTriggerInteraction.Ignore);
            if (count < originHits.Length) break;
            if (originHits.Length >= QueryLimit) return false;
            Array.Resize(ref originHits, originHits.Length * 2);
        }
        for (int i = 0; i < count; i++) if (Blocks(originHits[i], target)) return false;
        if (offset.sqrMagnitude < 0.000001f) return true;
        while (true)
        {
            count = physics.Raycast(origin, offset.normalized, hits, offset.magnitude, obstructionMask, QueryTriggerInteraction.Ignore);
            if (count < hits.Length) break;
            if (hits.Length >= QueryLimit) return false;
            Array.Resize(ref hits, hits.Length * 2);
        }
        for (int i = 0; i < count; i++)
        {
            if (Blocks(hits[i].collider, target)) return false;
        }
        return true;
    }

    private bool Blocks(Collider hit, InteractionTarget target) => hit != null &&
        hit.transform != transform && !hit.transform.IsChildOf(transform) &&
        hit.transform != target.transform && !hit.transform.IsChildOf(target.transform);

    private void SetSelection(InteractionTarget target)
    {
        bool canUse = target != null && target.CanActivate;
        string text = target == null ? "" : target.prompt;
        bool changed = SelectedTarget != target || CanInteract != canUse || Prompt != text;
        SelectedTarget = target; CanInteract = canUse; Prompt = text;
        if (!changed || SelectionChanged == null) return;
        foreach (Action listener in SelectionChanged.GetInvocationList())
        {
            try { listener(); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
