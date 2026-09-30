using System;
using ShadowsOfTheForsaken.Interactions;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class InteractionTarget : MonoBehaviour
{
    public LevelProgressionController progression;
    public LevelObjective objective = LevelObjective.None;
    public Collider focusCollider;
    public Transform focusPoint;
    public string prompt = "Use mechanism";
    public bool available = true;
    public int selectionOrder;

    public event Action<Guid> Activated;
    private readonly OneShotInteraction use = new OneShotInteraction();
    private LevelProgressionController observed;
    private bool activating;

    public Vector3 AimPosition => focusPoint != null ? focusPoint.position : focusCollider.bounds.center;
    public bool HasValidFocus => focusCollider != null && focusCollider.enabled && focusCollider.gameObject.activeInHierarchy &&
        (focusCollider.transform == transform || focusCollider.transform.IsChildOf(transform));
    public bool IsConsumed => use.Consumed || (ValidController && objective != LevelObjective.None &&
        (progression.Snapshot.CompletedObjectives & objective) == objective);
    public bool CanActivate => isActiveAndEnabled && HasValidFocus && ValidController && available && !activating &&
        use.CanConsume(progression.Snapshot.SessionId) && !progression.Snapshot.IsCompleted && ValidObjective &&
        (objective == LevelObjective.None || progression.CanComplete(objective));
    public Guid SessionId => use.SessionId;
    private bool ValidController => progression != null && observed == progression && progression.isActiveAndEnabled;
    private bool ValidObjective
    {
        get
        {
            int value = (int)objective;
            return value == 0 || (value > 0 && value <= (int)LevelObjective.FinalEnemyDefeated && (value & (value - 1)) == 0);
        }
    }

    private void OnEnable()
    {
        if (focusCollider == null) focusCollider = GetComponentInChildren<Collider>();
        observed = progression;
        if (observed == null) return;
        observed.Changed += OnProgressionChanged;
        var snapshot = observed.Snapshot;
        if (use.SessionId != snapshot.SessionId) use.Reset(snapshot.SessionId);
        if (ValidObjective && objective != LevelObjective.None && (snapshot.CompletedObjectives & objective) == objective)
            use.TryConsume(snapshot.SessionId);
    }

    private void OnDisable()
    {
        if (observed != null) observed.Changed -= OnProgressionChanged;
        observed = null;
    }

    private void OnDestroy() { Activated = null; }

    private void OnProgressionChanged(ProgressionChange change)
    {
        if (change.Kind == ProgressionChangeKind.SessionReset) use.Reset(change.After.SessionId);
    }

    // Trusted gameplay command. PlayerInteractor performs range and occlusion checks.
    // Delayed callers must preserve the token captured when their action started.
    public bool TryActivate(Guid sessionId)
    {
        if (!CanActivate || !use.CanConsume(sessionId)) return false;
        activating = true;
        try
        {
            if (objective != LevelObjective.None && !progression.TryComplete(objective, sessionId)) return false;
            if (!use.TryConsume(sessionId)) return false;
            var listeners = Activated;
            if (listeners == null) return true;
            foreach (Action<Guid> listener in listeners.GetInvocationList())
            {
                if (this == null || !isActiveAndEnabled || progression == null || progression.Snapshot.SessionId != sessionId) break;
                try { listener(sessionId); }
                catch (Exception error) { Debug.LogException(error, this); }
            }
            return true;
        }
        finally { activating = false; }
    }
}
