using System;
using ShadowsOfTheForsaken.CameraRig.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    public Transform player;
    public float distance = 5.0f;
    public float height = 2.0f;
    public float smoothSpeed = 2f;
    [Range(-2f, 2f)] public float shoulderOffset;

    [Min(0.01f)] public float collisionRadius = 0.2f;
    [Min(0f)] public float collisionPadding = 0.05f;
    [Min(0f)] public float pivotHeight = 1f;
    [Min(0.1f)] public float teleportDistance = 8f;
    public LayerMask obstructionMask = ~0;
    public bool findTaggedPlayer = true;

    private const int MaximumQueryHits = 1024;
    private const string MissingTargetMessage = "CameraFollow: assign player or tag exactly one active character Player.";
    private const string BlockedMessage = "CameraFollow: no safe visible camera pose; rendering suspended until recovery.";
    private RaycastHit[] hits = new RaycastHit[32];
    private Collider[] overlaps = new Collider[32];
    private readonly Vector3[] nearCorners = new Vector3[4];
    private Camera view;
    private PhysicsScene physicsScene;
    private Transform observedTarget;
    private Transform actorRoot;
    private Vector3 previousTargetPosition;
    private bool tracking;
    private bool warnedMissing;
    private bool warnedBlocked;
    private bool renderingSuppressed;
    private bool previousCameraEnabled;
    private float nextTargetSearch;

    public bool HasSafePose { get; private set; }
    public float EffectiveCollisionRadius { get; private set; }

    private void OnEnable()
    {
        view = GetComponent<Camera>();
        ResetTracking();
        SceneManager.sceneLoaded += SceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        RestoreRendering();
        ResetTracking();
    }

    private void OnValidate()
    {
        distance = Sanitize(distance, 5f, 0.1f);
        height = Sanitize(height, 2f, 0f);
        smoothSpeed = Sanitize(smoothSpeed, 2f, 0f);
        shoulderOffset = Finite(shoulderOffset) ? Mathf.Clamp(shoulderOffset, -2f, 2f) : 0f;
        collisionRadius = Sanitize(collisionRadius, 0.2f, 0.01f);
        collisionPadding = Sanitize(collisionPadding, 0.05f, 0f);
        pivotHeight = Sanitize(pivotHeight, 1f, 0f);
        teleportDistance = Sanitize(teleportDistance, 8f, 0.1f);
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        nextTargetSearch = 0f;
        if (mode == LoadSceneMode.Single || player == null) ResetTracking();
    }

    private void LateUpdate() => Simulate(Time.deltaTime);

    public void SetTarget(Transform target)
    {
        player = target;
        ResetTracking();
    }

    public bool SnapToTarget()
    {
        tracking = false;
        return Simulate(0f);
    }

    public bool Simulate(float deltaTime)
    {
        if (!isActiveAndEnabled) return false;
        OnValidate();
        float blend = CameraMotion.Blend(smoothSpeed, deltaTime);
        if (player == null && findTaggedPlayer && Time.unscaledTime >= nextTargetSearch)
            FindTarget();
        if (player == null || !player.gameObject.activeInHierarchy || player == transform || player.IsChildOf(transform))
        {
            tracking = false;
            HasSafePose = false;
            SuppressRendering();
            if (!warnedMissing) Debug.LogWarning(MissingTargetMessage, this);
            warnedMissing = true;
            return false;
        }
        warnedMissing = false;
        bool newTarget = player != observedTarget;
        if (newTarget)
        {
            observedTarget = player;
            var character = player.GetComponentInParent<CharacterController>();
            var body = player.GetComponentInParent<Rigidbody>();
            actorRoot = character != null ? character.transform : body != null ? body.transform : player;
        }
        Physics.SyncTransforms();
        physicsScene = player.gameObject.scene.GetPhysicsScene();
        Vector3 pivot = player.position + Vector3.up * pivotHeight;
        Vector3 shoulder = player.right * shoulderOffset;
        Vector3 desired = player.position - player.forward * distance + Vector3.up * height + shoulder;
        bool snap = !tracking || newTarget || (player.position - previousTargetPosition).sqrMagnitude > teleportDistance * teleportDistance;
        Vector3 candidate = snap ? desired : Vector3.Lerp(transform.position, desired, blend);
        EffectiveCollisionRadius = NearPlaneRadius();
        bool safe = Finite(pivot) && Finite(candidate) && Finite(EffectiveCollisionRadius) &&
                    TryResolve(pivot, candidate, out candidate);
        if (!safe && Finite(pivot) && Finite(desired))
            safe = TryResolve(pivot, desired, out candidate);
        // Turning away while pressed against a gate can put both rearward
        // candidates inside it. Keep the previous view only after validating
        // its complete boom again from the current pivot. Never reuse a pose
        // across a target change, teleport or explicit snap.
        if (!safe && !snap && Finite(pivot) && Finite(transform.position))
            safe = TryResolve(pivot, transform.position, out candidate);
        if (!safe)
        {
            tracking = false;
            HasSafePose = false;
            SuppressRendering();
            if (!warnedBlocked) Debug.LogWarning(BlockedMessage, this);
            warnedBlocked = true;
            return false;
        }
        // Keep collision sweeps anchored at the actor. Only framing moves to
        // the shoulder; the conservative near-plane sphere remains unchanged.
        Vector3 look = pivot + shoulder - candidate;
        Vector3 up = Mathf.Abs(Vector3.Dot(look.normalized, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
        transform.SetPositionAndRotation(candidate, Quaternion.LookRotation(look, up));
        previousTargetPosition = player.position;
        tracking = true;
        HasSafePose = true;
        warnedBlocked = false;
        RestoreRendering();
        return true;
    }

    private void FindTarget()
    {
        nextTargetSearch = Time.unscaledTime + 0.5f;
        Transform found = null;
        foreach (var candidate in GameObject.FindGameObjectsWithTag("Player"))
        {
            if (candidate.transform == transform || candidate.transform.IsChildOf(transform) ||
                candidate.scene.GetPhysicsScene() != gameObject.scene.GetPhysicsScene()) continue;
            if (found != null) return;
            found = candidate.transform;
        }
        player = found;
    }

    private void ResetTracking()
    {
        observedTarget = null;
        actorRoot = null;
        tracking = false;
        HasSafePose = false;
        nextTargetSearch = 0f;
    }

    private float NearPlaneRadius()
    {
        view.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), view.nearClipPlane,
            Camera.MonoOrStereoscopicEye.Mono, nearCorners);
        float radius = collisionRadius;
        foreach (var corner in nearCorners) radius = Mathf.Max(radius, corner.magnitude);
        return radius;
    }

    private bool TryResolve(Vector3 pivot, Vector3 requested, out Vector3 resolved)
    {
        resolved = requested;
        float radius = EffectiveCollisionRadius;
        if (!Finite(radius) || !physicsScene.IsValid()) return false;
        int count = Overlap(pivot, radius);
        if (count < 0) return false;
        for (int i = 0; i < count; i++)
            if (IsObstruction(overlaps[i]) && (overlaps[i].ClosestPoint(pivot) - pivot).sqrMagnitude < 0.000001f)
                return false;
        Vector3 offset = requested - pivot;
        float length = offset.magnitude;
        float minimum = Mathf.Max(0.05f, view.nearClipPlane + collisionPadding);
        if (!Finite(length) || length <= minimum) return false;
        Vector3 direction = offset / length;
        if (!FirstHit(pivot, direction, length, radius, out float sphereHit) ||
            !FirstHit(pivot, direction, length, 0f, out float rayHit)) return false;
        float limit = CameraMotion.CollisionLimit(length, sphereHit, collisionPadding);
        if (!float.IsPositiveInfinity(rayHit))
            limit = Mathf.Min(limit, CameraMotion.CollisionLimit(length, Mathf.Max(0f, rayHit - radius), collisionPadding));
        if (limit <= minimum) return false;
        for (int attempt = 0; attempt <= 24; attempt++)
        {
            float travel = Mathf.Lerp(limit, minimum, attempt / 24f);
            Vector3 position = pivot + direction * travel;
            count = Overlap(position, radius + collisionPadding * 0.5f);
            if (count < 0) return false;
            bool clear = true;
            for (int i = 0; i < count; i++)
                if (IsObstruction(overlaps[i])) { clear = false; break; }
            if (!clear) continue;
            resolved = position;
            return true;
        }
        return false;
    }

    private bool FirstHit(Vector3 origin, Vector3 direction, float length, float radius, out float nearest)
    {
        nearest = float.PositiveInfinity;
        int count;
        while (true)
        {
            count = radius > 0f
                ? physicsScene.SphereCast(origin, radius, direction, hits, length, obstructionMask, QueryTriggerInteraction.Ignore)
                : physicsScene.Raycast(origin, direction, hits, length, obstructionMask, QueryTriggerInteraction.Ignore);
            if (count < hits.Length) break;
            if (hits.Length >= MaximumQueryHits) return false;
            Array.Resize(ref hits, hits.Length * 2);
        }
        for (int i = 0; i < count; i++)
        {
            if (!IsObstruction(hits[i].collider)) continue;
            if (radius > 0f && hits[i].distance <= 0.0001f &&
                (hits[i].collider.ClosestPoint(origin) - origin).sqrMagnitude < radius * radius) continue;
            nearest = Mathf.Min(nearest, hits[i].distance);
        }
        return true;
    }

    private int Overlap(Vector3 position, float radius)
    {
        while (true)
        {
            int count = physicsScene.OverlapSphere(position, radius, overlaps, obstructionMask, QueryTriggerInteraction.Ignore);
            if (count < overlaps.Length) return count;
            if (overlaps.Length >= MaximumQueryHits) return -1;
            Array.Resize(ref overlaps, overlaps.Length * 2);
        }
    }

    private bool IsObstruction(Collider collider)
    {
        if (collider == null || collider.isTrigger || !collider.enabled) return false;
        Transform target = collider.transform;
        return target != actorRoot && !target.IsChildOf(actorRoot) && target != transform && !target.IsChildOf(transform);
    }

    private void SuppressRendering()
    {
        if (view == null || renderingSuppressed) return;
        previousCameraEnabled = view.enabled;
        renderingSuppressed = true;
        view.enabled = false;
    }

    private void RestoreRendering()
    {
        if (!renderingSuppressed) return;
        if (view != null) view.enabled = previousCameraEnabled;
        renderingSuppressed = false;
    }

    private static float Sanitize(float value, float fallback, float minimum) =>
        Finite(value) ? Mathf.Max(minimum, value) : fallback;

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
}
