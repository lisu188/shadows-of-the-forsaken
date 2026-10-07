using System;

// A single immutable room/session attribution. The observer supplies real clock
// and eligibility observations; this class never reads or changes gameplay.
public sealed class PlayerRoomFrameMetrics
{
    public Guid SessionId { get; }
    public string Room { get; }
    public long Visit { get; }
    private readonly double started;
    private double previous;
    private bool baseline, previousEligible, closed;
    private PlayerFrameExclusion pending = PlayerFrameExclusion.Transition;
    private readonly PlayerFrameMetrics frames = new PlayerFrameMetrics();

    public PlayerRoomFrameMetrics(Guid sessionId, string room, long visit, double now)
    {
        if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(room) || visit < 1 || !Finite(now) || now < 0)
            throw new ArgumentException("A room visit requires identity and a finite start observation.");
        SessionId = sessionId; Room = room; Visit = visit; started = previous = now;
    }

    // Remember a suspension even when no LateUpdate ran before focus returned.
    public void Invalidate(PlayerFrameExclusion reason)
    {
        if (closed) return;
        baseline = false;
        if (reason == PlayerFrameExclusion.Paused ||
            (reason == PlayerFrameExclusion.Unfocused && pending != PlayerFrameExclusion.Paused)) pending = reason;
        else if (pending == PlayerFrameExclusion.None) pending = PlayerFrameExclusion.Transition;
    }

    public bool Record(double now, Guid token, string room, PlayerFrameExclusion reason)
    {
        if (closed) return false;
        if (token != SessionId || room != Room)
        { Invalidate(PlayerFrameExclusion.Transition); return false; }
        if (!Finite(now) || now < previous)
        { frames.Record(double.NaN, PlayerFrameExclusion.Invalid); Invalidate(PlayerFrameExclusion.Transition); return false; }
        bool eligible = reason == PlayerFrameExclusion.None;
        if (pending == PlayerFrameExclusion.Paused || reason == PlayerFrameExclusion.Paused) reason = PlayerFrameExclusion.Paused;
        else if (pending == PlayerFrameExclusion.Unfocused || reason == PlayerFrameExclusion.Unfocused) reason = PlayerFrameExclusion.Unfocused;
        else if (reason == PlayerFrameExclusion.None && (!baseline || !previousEligible)) reason = PlayerFrameExclusion.Transition;
        bool measured = frames.Record(now - previous, reason);
        previous = now; baseline = true; previousEligible = eligible; pending = PlayerFrameExclusion.None;
        return measured;
    }

    public PlayerRoomFrameSummary Close(double now)
    {
        if (closed) throw new InvalidOperationException("A room visit can only be closed once.");
        if (!Finite(now) || now < previous) throw new ArgumentException("Invalid room end observation.");
        closed = true;
        return new PlayerRoomFrameSummary
        {
            sessionId = SessionId.ToString(), room = Room, visit = Visit,
            startRealtimeSeconds = started, endRealtimeSeconds = now, durationSeconds = now - started,
            // A callback occurs between observed frames. Keep the trailing partial
            // interval separate rather than fabricating a measured/excluded frame.
            unobservedTailSeconds = now - previous, pendingBoundaryExclusion = pending.ToString(),
            frameIntervals = frames.Snapshot(true)
        };
    }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

[Serializable]
public sealed class PlayerRoomFrameSummary
{
    public string sessionId, room, pendingBoundaryExclusion;
    public long visit;
    public double startRealtimeSeconds, endRealtimeSeconds, durationSeconds, unobservedTailSeconds;
    public PlayerFrameSummary frameIntervals;
}
