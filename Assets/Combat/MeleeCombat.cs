using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowsOfTheForsaken.Combat
{
    [DisallowMultipleComponent, RequireComponent(typeof(CombatHealth))]
    public sealed class MeleeCombat : MonoBehaviour
    {
        [Min(1)] public int damage = 25;
        [Min(.01f)] public float range = 2;
        [Range(1, 180)] public float arcDegrees = 90;
        [Min(0)] public float windup = .2f;
        [Min(.001f)] public float activeWindow = .12f;
        [Min(0)] public float cooldown = .38f;
        public float originHeight = 1;
        public LayerMask targetMask = ~0;
        private readonly MeleeAttackState<EntityId> attack = new MeleeAttackState<EntityId>();
        private Collider[] overlaps = new Collider[32];
        private Collider[] originOverlaps = new Collider[8];
        private RaycastHit[] rayHits = new RaycastHit[32];
        private CombatHealth health;
        private PlayerMovement movement;
        private bool attacksEnabled = true;
        private bool applicationPaused;
        private int capturedDamage;
        private float capturedRange, capturedArc, capturedHeight;
        public event Action<AttackPhase> PhaseChanged;
        public AttackPhase Phase => attack.Phase;
        public CombatHealth Health => health != null ? health : (health = GetComponent<CombatHealth>());
        private PlayerMovement Movement => movement != null ? movement : (movement = GetComponent<PlayerMovement>());
        private bool CanAct => isActiveAndEnabled && attacksEnabled && Health.isActiveAndEnabled && Health.IsAlive && Health.DamageEnabled &&
            Time.timeScale > 0 && !applicationPaused && (Movement == null || (Movement.isActiveAndEnabled && Movement.ControlsEnabled)) &&
            (Health.progression == null || (Health.progression.isActiveAndEnabled && !Health.progression.Snapshot.IsCompleted));
        public bool CanAttack => CanAct && Phase == AttackPhase.Ready;

        private void Awake() { health = GetComponent<CombatHealth>(); movement = GetComponent<PlayerMovement>(); }
        private void OnEnable() { Health.Changed += HealthChanged; }
        private void OnDisable() { if (health != null) health.Changed -= HealthChanged; Interrupt(); }
        private void OnDestroy() { PhaseChanged = null; }
        private void Update() => Simulate(Time.deltaTime);
        private void OnApplicationPause(bool paused) { applicationPaused = paused; if (paused) Interrupt(); }
        private void OnApplicationFocus(bool focused) { if (!focused) Interrupt(); }

        public bool TryAttack()
        {
            if (!CanAttack) return false;
            OnValidate();
            var before = Phase;
            if (!attack.TryStart(new AttackTiming(windup, activeWindow, cooldown), Health.LifeId, Health.SessionId)) return false;
            capturedDamage = damage; capturedRange = range; capturedArc = arcDegrees; capturedHeight = originHeight;
            PublishPhase(before);
            return true;
        }

        public void SetAttacksEnabled(bool value)
        {
            attacksEnabled = value;
            if (!value) Interrupt();
        }
        public void Interrupt()
        {
            var before = Phase; attack.Interrupt(); PublishPhase(before);
        }
        public void Simulate(float seconds)
        {
            if (!CanAct || attack.LifeId != Health.LifeId || attack.SessionId != Health.SessionId ||
                (Health.progression != null && attack.SessionId != Health.progression.Snapshot.SessionId)) Interrupt();
            if (!Finite(seconds) || seconds <= 0) return;
            var before = Phase;
            bool sample = attack.Advance(seconds);
            if (sample && CanAct && attack.LifeId == Health.LifeId && attack.SessionId == Health.SessionId)
                SampleHitWindow();
            PublishPhase(before);
        }

        private void HealthChanged(HealthChange change)
        {
            var before = Phase;
            if (change.IsReset) attack.Reset();
            else if (change.IsDeath) attack.Interrupt();
            PublishPhase(before);
        }

        private void SampleHitWindow()
        {
            Physics.SyncTransforms();
            var physics = gameObject.scene.GetPhysicsScene();
            Vector3 origin = transform.TransformPoint(Vector3.up * capturedHeight);
            int count;
            while (true)
            {
                count = physics.OverlapSphere(origin, capturedRange, overlaps, targetMask, QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length) break;
                if (overlaps.Length >= 1024) return; // Never trust a truncated query.
                Array.Resize(ref overlaps, overlaps.Length * 2);
            }
            float minimumDot = Mathf.Cos(capturedArc * .5f * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                if (!CanAct || attack.LifeId != Health.LifeId || attack.SessionId != Health.SessionId) break;
                var collider = overlaps[i];
                if (collider == null || !collider.enabled || collider.isTrigger || IsOwnCollider(collider)) continue;
                var target = collider.GetComponentInParent<CombatHealth>();
                if (target == null || target == Health || !target.isActiveAndEnabled || !target.IsAlive || target.SessionId != attack.SessionId) continue;
                Vector3 point = collider.ClosestPoint(origin);
                Vector3 direction = point - origin;
                float surfaceDistance = direction.magnitude;
                if (direction.sqrMagnitude < .0001f) direction = collider.bounds.center - origin;
                if (direction.sqrMagnitude < .0001f || surfaceDistance > capturedRange + .0001f ||
                    Vector3.Dot(transform.forward, direction.normalized) < minimumDot) continue;
                if (!ClearSight(physics, origin, point, target)) continue;
                Guid targetLife = target.LifeId;
                if (attack.TryRegisterHit(target.GetEntityId()))
                    target.TryDamage(capturedDamage, targetLife, attack.SessionId);
            }
        }

        private bool ClearSight(PhysicsScene physics, Vector3 origin, Vector3 point, CombatHealth target)
        {
            Vector3 direction = point - origin;
            float distance = direction.magnitude;
            // Raycasts do not report a collider containing their origin. Detect
            // embedded walls separately so a wall intersection cannot bypass LOS.
            int embeddedCount;
            while (true)
            {
                embeddedCount = physics.OverlapSphere(origin, .01f, originOverlaps, ~0, QueryTriggerInteraction.Ignore);
                if (embeddedCount < originOverlaps.Length) break;
                if (originOverlaps.Length >= 1024) return false;
                Array.Resize(ref originOverlaps, originOverlaps.Length * 2);
            }
            for (int i = 0; i < embeddedCount; i++)
            {
                var item = originOverlaps[i];
                if (!IsOwnCollider(item) && item.GetComponentInParent<CombatHealth>() != Health &&
                    item.GetComponentInParent<CombatHealth>() != target) return false;
            }
            if (distance < .0001f) return true;
            int count;
            while (true)
            {
                count = physics.Raycast(origin, direction / distance, rayHits, distance + .001f, ~0, QueryTriggerInteraction.Ignore);
                if (count < rayHits.Length) break;
                if (rayHits.Length >= 1024) return false;
                Array.Resize(ref rayHits, rayHits.Length * 2);
            }
            for (int i = 0; i < count; i++)
            {
                var body = rayHits[i].collider.GetComponentInParent<CombatHealth>();
                if (!IsOwnCollider(rayHits[i].collider) && body != Health && body != target) return false;
            }
            return true;
        }
        private bool IsOwnCollider(Collider collider) => collider.transform.IsChildOf(transform);
        private void PublishPhase(AttackPhase previous)
        {
            if (previous == Phase || PhaseChanged == null) return;
            var phase = Phase;
            foreach (Action<AttackPhase> listener in PhaseChanged.GetInvocationList())
                try { listener(phase); }
                catch (Exception error) { Debug.LogException(error, this); }
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Safe(float value, float fallback, float minimum, float maximum) =>
            Finite(value) ? Mathf.Clamp(value, minimum, maximum) : fallback;
        private void OnValidate()
        {
            damage = Mathf.Clamp(damage, 1, 1000000); range = Safe(range, 2, .01f, 100);
            arcDegrees = Safe(arcDegrees, 90, 1, 180); windup = Safe(windup, .2f, 0, 60);
            activeWindow = Safe(activeWindow, .12f, .001f, 60); cooldown = Safe(cooldown, .38f, 0, 60);
            originHeight = Safe(originHeight, 1, 0, 10);
        }
    }
}
