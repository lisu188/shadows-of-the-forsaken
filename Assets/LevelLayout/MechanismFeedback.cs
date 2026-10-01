using System;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;

// Small scene presentation bridge; InteractionTarget owns one-shot use and the
// existing progression owns objectives. This component never awards progress.
[DisallowMultipleComponent, RequireComponent(typeof(InteractionTarget))]
public sealed class MechanismFeedback : MonoBehaviour
{
    public InteractionTarget target;
    public LevelHUD hud;
    public Transform movingPart;
    public Vector3 activatedEulerAngles;
    public Renderer[] rewardVisuals = Array.Empty<Renderer>();
    public string activatedMessage = "The mechanism responds.";
    public bool hideWhenConsumed;
    private LevelProgressionController observed;
    private Quaternion originalRotation;
    private bool[] originalVisibility;
    private bool captured;

    private void OnEnable()
    {
        if (target == null) target = GetComponent<InteractionTarget>();
        if (!captured)
        {
            originalRotation = movingPart != null ? movingPart.localRotation : Quaternion.identity;
            originalVisibility = new bool[rewardVisuals.Length];
            for (int i = 0; i < rewardVisuals.Length; i++) originalVisibility[i] = rewardVisuals[i] != null && rewardVisuals[i].enabled;
            captured = true;
        }
        target.Activated += Activated;
        observed = target.progression;
        if (observed != null) observed.Changed += Changed;
        Present(target.IsConsumed);
    }
    private void OnDisable()
    {
        if (target != null) target.Activated -= Activated;
        if (observed != null) observed.Changed -= Changed;
        observed = null;
        if (captured) Present(false);
    }
    private void Changed(ProgressionChange change) => Present(change.Kind != ProgressionChangeKind.SessionReset && target.IsConsumed);
    private void Activated(Guid session)
    {
        if (observed == null || observed.Snapshot.SessionId != session) return;
        Present(target.IsConsumed);
        if (hud != null) hud.ShowMessage(activatedMessage, hideWhenConsumed ? 6 : 3);
    }
    private void Present(bool used)
    {
        if (movingPart != null) movingPart.localRotation = used ? Quaternion.Euler(activatedEulerAngles) : originalRotation;
        for (int i = 0; i < rewardVisuals.Length && i < originalVisibility.Length; i++)
            if (rewardVisuals[i] != null) rewardVisuals[i].enabled = originalVisibility[i] && !(hideWhenConsumed && used);
    }
}
