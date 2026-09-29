using System;
using UnityEngine;

namespace ShadowsOfTheForsaken.Progression
{
    [DisallowMultipleComponent]
    public sealed class LevelProgressionController : MonoBehaviour
    {
        private LevelProgression progress;
        private bool destroyed;
        public event Action<ProgressionChange> Changed;

        public LevelProgressSnapshot Snapshot
        {
            get
            {
                EnsureInitialized();
                return progress.Snapshot;
            }
        }

        private void Awake() { EnsureInitialized(); }

        private void OnEnable()
        {
            EnsureInitialized();
            progress.Changed -= ForwardChange;
            progress.Changed += ForwardChange;
        }

        private void OnDisable()
        {
            if (progress != null) progress.Changed -= ForwardChange;
        }

        private void OnDestroy()
        {
            destroyed = true;
            if (progress != null) progress.Changed -= ForwardChange;
            Changed = null;
            progress = null;
        }

        public bool IsPassageOpen(LevelRoom from, LevelRoom to)
        {
            EnsureInitialized();
            return progress.IsPassageOpen(from, to);
        }

        public bool CanEnter(LevelRoom destination)
        {
            if (destroyed || !isActiveAndEnabled) return false;
            EnsureInitialized();
            return progress.CanEnter(destination);
        }

        public bool CanComplete(LevelObjective objective)
        {
            if (destroyed || !isActiveAndEnabled) return false;
            EnsureInitialized();
            return progress.CanComplete(objective);
        }

        public bool TryEnter(LevelRoom destination, Guid sessionId)
        {
            if (destroyed || !isActiveAndEnabled) return false;
            EnsureInitialized();
            return progress.TryEnter(destination, sessionId);
        }

        public bool TryComplete(LevelObjective objective, Guid sessionId)
        {
            if (destroyed || !isActiveAndEnabled) return false;
            EnsureInitialized();
            return progress.TryComplete(objective, sessionId);
        }

        public bool TryResetSession()
        {
            if (destroyed || !isActiveAndEnabled) return false;
            EnsureInitialized();
            progress.Reset();
            return true;
        }

        private void EnsureInitialized()
        {
            if (destroyed) throw new ObjectDisposedException(nameof(LevelProgressionController));
            if (progress == null) progress = new LevelProgression();
        }

        private void ForwardChange(ProgressionChange change)
        {
            var listeners = Changed;
            if (listeners == null) return;
            foreach (Action<ProgressionChange> listener in listeners.GetInvocationList())
            {
                try { listener(change); }
                catch (Exception error) { Debug.LogException(error, this); }
            }
        }
    }
}
