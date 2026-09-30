using System;
using System.Collections.Generic;

namespace ShadowsOfTheForsaken.Interactions
{
    public readonly struct InteractionCandidate
    {
        public int Id { get; }
        public float DistanceSquared { get; }
        public float Facing { get; }
        public bool Visible { get; }
        public int Order { get; }
        public InteractionCandidate(int id, float distanceSquared, float facing, bool visible, int order = 0)
        {
            Id = id;
            DistanceSquared = distanceSquared;
            Facing = facing;
            Visible = visible;
            Order = order;
        }
    }

    public static class InteractionSelection
    {
        // Return an index, not a sentinel key: callers may supply negative keys.
        public static int Select(IReadOnlyList<InteractionCandidate> candidates, float range, float minimumFacing)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (!Finite(range) || range <= 0 || !Finite(minimumFacing) || minimumFacing < -1 || minimumFacing > 1)
                throw new ArgumentOutOfRangeException(nameof(range));
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                var item = candidates[i];
                if (!item.Visible || !Finite(item.DistanceSquared) || item.DistanceSquared < 0 ||
                    item.DistanceSquared > range * range || !Finite(item.Facing) || item.Facing < minimumFacing || item.Facing > 1)
                    continue;
                if (best < 0 || ComesBefore(item, candidates[best])) best = i;
            }
            return best;
        }

        private static bool ComesBefore(InteractionCandidate candidate, InteractionCandidate previous)
        {
            if (candidate.DistanceSquared != previous.DistanceSquared) return candidate.DistanceSquared < previous.DistanceSquared;
            if (candidate.Facing != previous.Facing) return candidate.Facing > previous.Facing;
            if (candidate.Order != previous.Order) return candidate.Order < previous.Order;
            return candidate.Id < previous.Id;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class OneShotInteraction
    {
        public Guid SessionId { get; private set; }
        public bool Consumed { get; private set; }

        public void Reset(Guid sessionId)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("A live session is required.", nameof(sessionId));
            SessionId = sessionId;
            Consumed = false;
        }

        public bool CanConsume(Guid sessionId) => sessionId != Guid.Empty && sessionId == SessionId && !Consumed;

        public bool TryConsume(Guid sessionId)
        {
            if (!CanConsume(sessionId)) return false;
            Consumed = true;
            return true;
        }
    }

    public sealed class GateClosure
    {
        // An uninitialized gate starts open so first activation cannot trap an occupant.
        public bool IsOpen { get; private set; } = true;
        public bool ClosePending { get; private set; }

        public void Refresh(bool passageOpen, bool occupied)
        {
            ClosePending = !passageOpen && IsOpen && occupied;
            IsOpen = passageOpen || ClosePending;
        }
    }
}
