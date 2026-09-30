using System.Collections.Generic;
using UnityEngine;

// Presentation only: retain the real player/attack colliders while the existing
// collision-safe camera is pushed too close to see past the player's body.
[DefaultExecutionOrder(100), DisallowMultipleComponent, RequireComponent(typeof(CameraFollow))]
public sealed class CameraPlayerOcclusion : MonoBehaviour
{
    public Renderer[] playerVisuals = System.Array.Empty<Renderer>();
    [Min(.1f)] public float hideDistance = 1.25f;
    [Min(.2f)] public float showDistance = 1.6f;
    private readonly Dictionary<Renderer, bool> previousFlags = new Dictionary<Renderer, bool>();
    private CameraFollow follow;
    private Transform observedTarget;
    public bool BodyHidden { get; private set; }

    private void Awake() { follow = GetComponent<CameraFollow>(); }
    private void LateUpdate() => RefreshVisibility();
    private void OnDisable() { Restore(); observedTarget = null; }
    private void OnDestroy() => Restore();

    public void RefreshVisibility()
    {
        if (follow == null) follow = GetComponent<CameraFollow>();
        if (follow == null || playerVisuals == null) { Restore(); return; }
        var target = follow.player;
        if (target != observedTarget) { Restore(); observedTarget = target; }
        if (!isActiveAndEnabled || !follow.isActiveAndEnabled || !follow.HasSafePose || target == null)
        {
            Restore(); return;
        }
        float near = float.IsNaN(hideDistance) || float.IsInfinity(hideDistance) ? 1.25f : Mathf.Max(.1f, hideDistance);
        float far = float.IsNaN(showDistance) || float.IsInfinity(showDistance) ? 1.6f : Mathf.Max(near + .1f, showDistance);
        float distance = Vector3.Distance(transform.position, target.position + Vector3.up * follow.pivotHeight);
        if (BodyHidden ? distance >= far : distance >= near)
        {
            if (BodyHidden && distance >= far) Restore();
            return;
        }
        if (BodyHidden) return;
        var character = target.GetComponentInParent<CharacterController>();
        var actor = character != null ? character.transform : target;
        foreach (var visual in playerVisuals)
        {
            if (visual == null || !visual.transform.IsChildOf(actor) || previousFlags.ContainsKey(visual)) continue;
            previousFlags.Add(visual, visual.forceRenderingOff);
            visual.forceRenderingOff = true;
        }
        BodyHidden = previousFlags.Count > 0;
    }

    private void Restore()
    {
        foreach (var item in previousFlags) if (item.Key != null) item.Key.forceRenderingOff = item.Value;
        previousFlags.Clear(); BodyHidden = false;
    }
}
