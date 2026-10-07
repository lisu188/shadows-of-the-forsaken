using System;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;

namespace ShadowsOfTheForsaken.Level
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class RoomTransition : MonoBehaviour
    {
        [SerializeField] private LevelProgressionController progression;
        [SerializeField] private PlayerMovement movement;
        [SerializeField] private LevelRoom from;
        [SerializeField] private LevelRoom to;
        [SerializeField] private Vector3 fromSafe;
        [SerializeField] private Vector3 toSafe;
        private Vector3 normal;
        private Guid crossingSession;
        private bool tracking;
        private int acceptedSide;

        public void Configure(LevelProgressionController controller, PlayerMovement player, LevelRoom source,
            LevelRoom destination, Vector3 sourceSafePosition, Vector3 destinationSafePosition)
        {
            progression = controller;
            movement = player;
            from = source;
            to = destination;
            fromSafe = sourceSafePosition;
            toSafe = destinationSafePosition;
            SetupPlane();
            tracking = false;
        }

        private void Awake() => SetupPlane();
        private void OnDisable() => tracking = false;

        private void SetupPlane()
        {
            var trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            normal = toSafe - fromSafe;
            normal.y = 0;
            normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : transform.forward;
        }

        private bool IsPlayer(Collider other) => movement != null &&
            (other.transform == movement.transform || other.transform.IsChildOf(movement.transform));

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other) || progression == null || !progression.isActiveAndEnabled) return;
            crossingSession = progression.Snapshot.SessionId;
            tracking = true;
            acceptedSide = progression.Snapshot.Room == from ? -1 : progression.Snapshot.Room == to ? 1 : 0;
            EvaluateCrossing();
        }

        private void OnTriggerStay(Collider other)
        {
            if (IsPlayer(other) && tracking) EvaluateCrossing();
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsPlayer(other)) tracking = false;
        }

        private void EvaluateCrossing()
        {
            if (movement == null || progression == null || !progression.isActiveAndEnabled) return;
            if (crossingSession != progression.Snapshot.SessionId)
            {
                // Do not replace an old crossing's token after reset. The scene respawn starts the next crossing.
                tracking = false;
                return;
            }
            float signedDistance = Vector3.Dot(movement.transform.position - transform.position, normal);
            int side = signedDistance > 0.08f ? 1 : signedDistance < -0.08f ? -1 : 0;
            if (side == 0 || side == acceptedSide) return;
            var current = progression.Snapshot.Room;
            var destination = side > 0 ? to : from;
            if ((current == from || current == to) && progression.TryEnter(destination, crossingSession))
            {
                acceptedSide = side;
                return;
            }
            // The tall solid gate is the primary barrier; this handles trigger crossings, fast motion and stale routes.
            Vector3 safe = current == to ? toSafe : fromSafe;
            var character = movement.GetComponent<CharacterController>();
            bool wasEnabled = character != null && character.enabled;
            if (wasEnabled) character.enabled = false;
            movement.transform.position = safe;
            movement.ResetMotion();
            if (wasEnabled) character.enabled = true;
            tracking = false;
        }
    }
}
