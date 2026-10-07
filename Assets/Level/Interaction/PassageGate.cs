using ShadowsOfTheForsaken.Progression;
using UnityEngine;

namespace ShadowsOfTheForsaken.Level
{
    [DisallowMultipleComponent]
    public sealed class PassageGate : MonoBehaviour
    {
        [SerializeField] private LevelProgressionController progression;
        [SerializeField] private LevelRoom from;
        [SerializeField] private LevelRoom to;
        [SerializeField] private Collider barrier;
        [SerializeField] private Renderer[] visuals;
        private LevelProgressionController subscribed;
        public bool IsOpen { get; private set; }

        public void Configure(LevelProgressionController controller, LevelRoom source, LevelRoom destination,
            Collider blockingCollider, Renderer[] closedVisuals = null)
        {
            Unsubscribe();
            progression = controller;
            from = source;
            to = destination;
            barrier = blockingCollider;
            visuals = closedVisuals;
            Subscribe();
            Refresh();
        }

        private void OnEnable() { Subscribe(); Refresh(); }
        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (!isActiveAndEnabled || progression == null || subscribed == progression) return;
            subscribed = progression;
            subscribed.Changed += Observe;
        }

        private void Unsubscribe()
        {
            if (subscribed != null) subscribed.Changed -= Observe;
            subscribed = null;
        }

        private void Observe(ProgressionChange change) => Refresh();

        private void Refresh()
        {
            IsOpen = progression != null && progression.IsPassageOpen(from, to);
            if (barrier != null) barrier.enabled = !IsOpen;
            if (visuals != null) foreach (var visual in visuals) if (visual != null) visual.enabled = !IsOpen;
        }
    }
}
