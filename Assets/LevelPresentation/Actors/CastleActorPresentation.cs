using System;
using System.Collections.Generic;
using ShadowsOfTheForsaken.Combat;
using UnityEngine;

// DOCX 1-3/6: visible people and creatures. Only authored child transforms move;
// colliders, actor roots, combat, navigation and progression remain authoritative.
[DefaultExecutionOrder(50), DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth), typeof(MeleeCombat))]
public sealed class CastleActorPresentation : MonoBehaviour
{
    public Transform motionRoot, torso, head, leftArm, rightArm, leftForearm, rightForearm;
    public Transform leftLeg, rightLeg, leftShin, rightShin, cloak;
    public bool demonic;
    [Min(.1f)] public float strideRate = 8;

    private struct BindPose
    {
        public Transform part;
        public Vector3 position, scale;
        public Quaternion rotation;
    }
    private readonly List<BindPose> bindPoses = new List<BindPose>();
    private CombatHealth health;
    private MeleeCombat melee;
    private Vector3 previousPosition;
    private float gait, phaseAge, deathAge, movementBlend;
    private AttackPhase phase;
    private bool captured;

    private void Awake()
    {
        health = GetComponent<CombatHealth>();
        melee = GetComponent<MeleeCombat>();
        CaptureBindPoses();
    }

    private void OnEnable()
    {
        CaptureBindPoses();
        health.Changed += HealthChanged;
        melee.PhaseChanged += PhaseChanged;
        ResetPresentation();
    }

    private void OnDisable()
    {
        if (health != null) health.Changed -= HealthChanged;
        if (melee != null) melee.PhaseChanged -= PhaseChanged;
        RestoreBindPoses();
    }

    private void CaptureBindPoses()
    {
        if (captured) return;
        var seen = new HashSet<Transform>();
        foreach (var part in new[] { motionRoot, torso, head, leftArm, rightArm, leftForearm,
            rightForearm, leftLeg, rightLeg, leftShin, rightShin, cloak })
        {
            // A malformed reference must never rotate the collider/root or an
            // unrelated actor. Null optional parts are legitimate (no cape).
            if (part == null || part == transform || !part.IsChildOf(transform) || !seen.Add(part)) continue;
            bindPoses.Add(new BindPose { part = part, position = part.localPosition,
                rotation = part.localRotation, scale = part.localScale });
        }
        captured = true;
    }

    private void RestoreBindPoses()
    {
        foreach (var pose in bindPoses)
            if (pose.part != null)
            {
                pose.part.localPosition = pose.position;
                pose.part.localRotation = pose.rotation;
                pose.part.localScale = pose.scale;
            }
    }

    private void ResetPresentation()
    {
        RestoreBindPoses();
        previousPosition = transform.position;
        phase = melee.Phase;
        gait = phaseAge = deathAge = movementBlend = 0;
    }

    private void HealthChanged(HealthChange change)
    {
        // Read the actual current life: an earlier listener can reset health
        // before an old death payload reaches this presentation observer.
        if (change.IsReset || (change.IsDeath && health.IsAlive)) ResetPresentation();
    }

    private void PhaseChanged(AttackPhase value)
    {
        phase = melee.Phase;
        phaseAge = 0;
    }

    private void LateUpdate() => Present(Time.deltaTime);

    public void Present(float seconds)
    {
        if (!isActiveAndEnabled || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        float step = Mathf.Min(seconds, .1f);
        RestoreBindPoses();
        Vector3 displacement = transform.position - previousPosition;
        previousPosition = transform.position;
        if (!health.IsAlive)
        {
            deathAge += step;
            float fall = Mathf.SmoothStep(0, 1, Mathf.Clamp01(deathAge / .55f));
            Rotate(motionRoot, new Vector3(-82 * fall, 0, 13 * fall));
            Offset(motionRoot, new Vector3(0, .12f * fall, 0));
            Rotate(rightArm, new Vector3(0, 0, -30 * fall));
            Rotate(leftArm, new Vector3(0, 0, 35 * fall));
            return;
        }
        deathAge = 0;
        float speed = displacement.sqrMagnitude > 4 ? 0 : new Vector2(displacement.x, displacement.z).magnitude / seconds;
        movementBlend = Mathf.MoveTowards(movementBlend, Mathf.Clamp01(speed / 3), step * 8);
        gait += step * Mathf.Lerp(1.8f, strideRate, movementBlend);
        float stride = Mathf.Sin(gait) * movementBlend;
        Offset(motionRoot, new Vector3(0, Mathf.Sin(gait * 2) * .025f * movementBlend, 0));
        Rotate(torso, new Vector3(demonic ? 7 : 0, stride * 4, Mathf.Sin(gait * .6f) * 1.1f));
        Rotate(head, new Vector3(Mathf.Sin(gait * .6f) * 1.5f, -stride * 3, 0));
        Rotate(leftLeg, new Vector3(stride * 28, 0, 0));
        Rotate(rightLeg, new Vector3(-stride * 28, 0, 0));
        Rotate(leftShin, new Vector3(Mathf.Max(0, -stride) * 28, 0, 0));
        Rotate(rightShin, new Vector3(Mathf.Max(0, stride) * 28, 0, 0));
        Rotate(leftArm, new Vector3(-stride * 18, 0, demonic ? 12 : 0));
        Rotate(rightArm, new Vector3(stride * 18, 0, demonic ? -12 : 0));
        Rotate(cloak, new Vector3(4 + movementBlend * 12 + Mathf.Sin(gait) * 3, 0, stride * 3));
        if (phase != melee.Phase) { phase = melee.Phase; phaseAge = 0; }
        phaseAge += step;
        if (phase == AttackPhase.Windup)
            AttackPose(Mathf.Clamp01(phaseAge / Mathf.Max(.01f, melee.windup)), false);
        else if (phase == AttackPhase.Active)
            AttackPose(Mathf.Clamp01(phaseAge / Mathf.Max(.01f, melee.activeWindow)), true);
        else if (phase == AttackPhase.Recovery)
        {
            float remaining = 1 - Mathf.Clamp01(phaseAge / Mathf.Max(.01f, melee.cooldown));
            Rotate(rightArm, new Vector3(-65, 25, 12) * remaining);
            Rotate(leftArm, new Vector3(-25, -10, 15) * remaining);
            Rotate(torso, new Vector3(6, 20, 0) * remaining);
        }
    }

    private void AttackPose(float progress, bool swinging)
    {
        float lift = swinging ? 1 : Mathf.SmoothStep(0, 1, progress);
        Vector3 raised = demonic ? new Vector3(-125, -18, -28) : new Vector3(-142, -18, -18);
        Vector3 strike = new Vector3(-65, 25, 12);
        Rotate(rightArm, swinging ? Vector3.Lerp(raised, strike, progress) : raised * lift);
        Rotate(rightForearm, new Vector3(-20 * (1 - (swinging ? progress : 0)) * lift, 0, 0));
        Rotate(leftArm, (demonic ? new Vector3(-115, 18, 28) : new Vector3(-32, 0, 14)) * lift);
        Rotate(leftForearm, new Vector3(demonic ? -15 : -28, 0, 0) * lift);
        Rotate(torso, new Vector3(swinging ? 7 : -4, swinging ? Mathf.Lerp(-22, 20, progress) : -22 * lift, 0));
    }

    private bool Owns(Transform part)
    {
        foreach (var pose in bindPoses) if (pose.part == part && part != null) return true;
        return false;
    }
    private void Rotate(Transform part, Vector3 angles)
    {
        if (Owns(part)) part.localRotation *= Quaternion.Euler(angles);
    }
    private void Offset(Transform part, Vector3 offset)
    {
        if (Owns(part)) part.localPosition += offset;
    }
}
