using System;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;

namespace ShadowsOfTheForsaken.Level
{
    [DisallowMultipleComponent]
    public sealed class PlayerInteraction : MonoBehaviour
    {
        [SerializeField] private PlayerMovement movement;
        [SerializeField] private LevelProgressionController progression;
        [SerializeField, Min(0.1f)] private float range = 2.6f;
        private PlayerMovement subscribed;
        private CharacterController character;
        private Guid observedSession;

        public LevelInteractable Focused { get; private set; }
        public string CurrentPrompt => Focused != null ? Focused.Prompt : "";
        public string LastFeedback { get; private set; } = "";

        public void Configure(PlayerMovement player, LevelProgressionController controller, float interactionRange = 2.6f)
        {
            Unsubscribe();
            movement = player;
            progression = controller;
            range = Mathf.Max(0.1f, interactionRange);
            character = player != null ? player.GetComponent<CharacterController>() : null;
            observedSession = controller != null ? controller.Snapshot.SessionId : Guid.Empty;
            Subscribe();
        }

        private void Awake()
        {
            if (movement == null) movement = GetComponent<PlayerMovement>();
            if (movement != null) character = movement.GetComponent<CharacterController>();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable()
        {
            Unsubscribe();
            Focused = null;
        }

        private void Subscribe()
        {
            if (!isActiveAndEnabled || movement == null || subscribed == movement) return;
            subscribed = movement;
            subscribed.InteractRequested += OnInteractRequested;
        }

        private void Unsubscribe()
        {
            if (subscribed != null) subscribed.InteractRequested -= OnInteractRequested;
            subscribed = null;
        }

        private void Update()
        {
            if (progression != null && observedSession != progression.Snapshot.SessionId)
            {
                observedSession = progression.Snapshot.SessionId;
                LastFeedback = "";
            }
            Focused = SelectTarget();
        }

        private void OnInteractRequested() => TryInteract();

        public bool TryInteract()
        {
            if (!isActiveAndEnabled || movement == null || !movement.isActiveAndEnabled || !movement.ControlsEnabled ||
                progression == null || !progression.isActiveAndEnabled || progression.Snapshot.IsCompleted || Time.timeScale <= 0) return false;
            // A press starts a new immediate action. Its token is passed unchanged to the mechanism.
            var capturedSession = progression.Snapshot.SessionId;
            Focused = SelectTarget();
            if (Focused == null) return false;
            bool applied = Focused.TryInteract(capturedSession);
            LastFeedback = Focused.Feedback;
            return applied;
        }

        private LevelInteractable SelectTarget()
        {
            if (movement == null || !movement.isActiveAndEnabled || !movement.ControlsEnabled ||
                progression == null || !progression.isActiveAndEnabled || progression.Snapshot.IsCompleted) return null;
            Vector3 origin = character != null
                ? movement.transform.TransformPoint(character.center) + Vector3.up * character.height * 0.2f
                : movement.transform.position + Vector3.up;
            var colliders = Physics.OverlapSphere(origin, range, ~0, QueryTriggerInteraction.Collide);
            LevelInteractable nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (var candidate in colliders)
            {
                var target = candidate.GetComponentInParent<LevelInteractable>();
                if (target == null || !target.CanInteract) continue;
                Vector3 point = candidate.bounds.center;
                float distance = Vector3.Distance(origin, point);
                if (distance > range || distance > nearestDistance ||
                    (Mathf.Approximately(distance, nearestDistance) && nearest != null && target.GetInstanceID() > nearest.GetInstanceID())) continue;
                if (!Visible(origin, point, target)) continue;
                nearest = target;
                nearestDistance = distance;
            }
            return nearest;
        }

        private bool Visible(Vector3 origin, Vector3 targetPoint, LevelInteractable target)
        {
            Vector3 offset = targetPoint - origin;
            if (offset.sqrMagnitude < 0.0001f) return true;
            foreach (var hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == movement.transform || hit.transform.IsChildOf(movement.transform)) continue;
                if (hit.transform == target.transform || hit.transform.IsChildOf(target.transform)) continue;
                return false;
            }
            return true;
        }
    }
}
