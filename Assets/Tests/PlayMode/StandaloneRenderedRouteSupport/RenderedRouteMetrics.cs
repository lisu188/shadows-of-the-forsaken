using System;
using System.Collections.Generic;

namespace ForsakenStandaloneRenderedRouteSupport
{
    // Test-only, Unity-free statistics. Raw and warm lanes share one histogram
    // implementation; the warm lane matches PlayerFrameMetrics' existing policy.
    public sealed class RenderedRouteHistogram
    {
        public const double BucketMilliseconds = .25;
        public const int FiniteBuckets = 2048;
        private readonly long[] bins = new long[FiniteBuckets + 1];
        private long count, over60, overBudget;
        private double total, maximum;

        public void Record(double seconds)
        {
            if (!Finite(seconds) || seconds < 0) throw new ArgumentException("Invalid rendered interval.");
            double ms = seconds * 1000;
            if (!Finite(ms) || !Finite(total + ms)) throw new OverflowException("Rendered interval total is not finite.");
            count = checked(count + 1); total += ms; maximum = Math.Max(maximum, ms);
            if (ms > 1000.0 / 60) over60 = checked(over60 + 1);
            if (ms > 33.3) overBudget = checked(overBudget + 1);
            int index = ms > FiniteBuckets * BucketMilliseconds ? FiniteBuckets :
                Math.Max(0, (int)Math.Ceiling(ms / BucketMilliseconds) - 1);
            bins[index] = checked(bins[index] + 1);
        }

        public RenderedHistogramSummary Snapshot()
        {
            long cumulative = 0, rank = (long)Math.Ceiling(count * .95);
            double p95 = 0;
            for (int i = 0; count > 0 && i < bins.Length; i++)
            {
                cumulative += bins[i];
                if (cumulative < rank) continue;
                p95 = i == FiniteBuckets ? maximum : (i + 1) * BucketMilliseconds; break;
            }
            return new RenderedHistogramSummary
            {
                measured_frames = count, measured_seconds = total / 1000,
                mean_milliseconds = count == 0 ? 0 : total / count, p95_upper_milliseconds = p95,
                maximum_milliseconds = maximum, frames_over_60hz = over60, frames_over_33_3ms = overBudget,
                histogram = (long[])bins.Clone(), histogram_overflow_frames = bins[FiniteBuckets],
                has_samples = count != 0, within_provisional_budget = count != 0 && total / count <= 33.3 && p95 <= 33.3
            };
        }
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    // A value-only predicate makes cycle ownership/bracket validation testable
    // without claiming that synthetic inputs are an actual render observation.
    public struct RenderedCycleObservation
    {
        public bool context_open, begin_eligible, end_owned_member, identity_matches, visit_open;
        public bool camera_active_unbound, running, unpaused_positive_time;
        public int begin_frame, end_frame, begin_generation, end_generation, camera_count, owned_begins, owned_ends;
    }
    public static class RenderedCycleEligibility
    {
        public static bool Accept(RenderedCycleObservation value) => value.context_open && value.begin_eligible &&
            value.begin_frame == value.end_frame && value.begin_generation == value.end_generation &&
            value.end_owned_member && value.camera_count == 1 && value.owned_begins == 1 && value.owned_ends == 1 &&
            value.identity_matches && value.visit_open && value.camera_active_unbound && value.running && value.unpaused_positive_time;
    }
    public static class RenderedRouteCleanup
    {
        // Teardown must attempt all independent removals even when one lease
        // fails. Used only for bounded test-owned resources, never game objects.
        public static void Run(IReadOnlyList<Action> actions, Action<Exception> report)
        {
            if (actions == null || report == null || actions.Count > 32) throw new ArgumentException("Invalid bounded cleanup.");
            foreach (var action in actions)
                try { action(); } catch (Exception error) { report(error); }
        }
    }
    public sealed class RenderedRouteCompletion
    {
        public bool Completed { get; private set; }
        public Guid Session { get; private set; }
        public int Objectives { get; private set; }
        public void Observe(Guid session, int objectives, bool completed)
        {
            if (Completed) return;
            if (session == Guid.Empty) throw new ArgumentException("Completion requires the original session.");
            Session = session; Objectives = objectives; Completed = completed;
        }
    }

    public sealed class RenderedRouteVisitMetrics
    {
        public const double WarmupSeconds = 5;
        private readonly string session, room;
        private readonly long visit;
        private readonly double started;
        private readonly RenderedRouteHistogram raw = new RenderedRouteHistogram(), warm = new RenderedRouteHistogram();
        private readonly Dictionary<string, RenderedExclusion> exclusions = new Dictionary<string, RenderedExclusion>(StringComparer.Ordinal);
        private double previous, warmup;
        private long observed;
        private int previousFrame;
        private bool baseline, closed;
        private string pending = "transition";
        public double LastEligibleSeconds { get; private set; }
        public bool LastWasWarm { get; private set; }

        public RenderedRouteVisitMetrics(string sessionId, string roomName, long visitNumber, double now)
        {
            if (!Guid.TryParse(sessionId, out var token) || token == Guid.Empty || string.IsNullOrWhiteSpace(roomName) ||
                visitNumber < 1 || !RenderedRouteHistogram.Finite(now) || now < 0)
                throw new ArgumentException("A rendered visit requires an immutable identity and finite clock.");
            session = sessionId; room = roomName; visit = visitNumber; started = previous = now;
        }

        public void Invalidate(string reason)
        {
            if (closed) return;
            if (string.IsNullOrEmpty(reason) || reason.Length > 80) throw new ArgumentException("Invalid interval exclusion.");
            baseline = false; pending = reason;
        }

        public bool Observe(double now, int frame, string sessionId, string roomName, string exclusion = null)
        {
            if (closed) throw new InvalidOperationException("Rendered visit is closed.");
            observed = checked(observed + 1);
            LastEligibleSeconds = 0; LastWasWarm = false;
            if (sessionId != session || roomName != room) { AddExclusion("foreign-identity", 0); Invalidate("foreign-identity"); return false; }
            if (!RenderedRouteHistogram.Finite(now) || now < previous || frame < 0 || (baseline && frame <= previousFrame))
            { AddExclusion("invalid-clock-or-frame", 0); Invalidate("invalid-clock-or-frame"); return false; }
            double seconds = now - previous;
            string reason = exclusion ?? (!baseline ? pending : frame != previousFrame + 1 ? "missing-render-frame" : null);
            previous = now; previousFrame = frame; baseline = exclusion == null; pending = "transition";
            if (reason != null) { AddExclusion(reason, seconds); return false; }
            raw.Record(seconds); LastEligibleSeconds = seconds;
            if (warmup < WarmupSeconds)
            { warmup += seconds; AddExclusion("warmup", seconds); return true; }
            warm.Record(seconds); LastWasWarm = true; return true;
        }

        private void AddExclusion(string reason, double seconds)
        {
            if (!exclusions.TryGetValue(reason, out var row))
            {
                if (exclusions.Count >= 32) throw new InvalidOperationException("Rendered exclusion category limit exceeded.");
                row = new RenderedExclusion { reason = reason }; exclusions.Add(reason, row);
            }
            row.intervals = checked(row.intervals + 1); row.seconds += seconds;
        }

        public RenderedVisitSummary Close(double now)
        {
            if (closed) throw new InvalidOperationException("Rendered visit can close only once.");
            if (!RenderedRouteHistogram.Finite(now) || now < previous) throw new ArgumentException("Invalid rendered visit end.");
            closed = true;
            var excluded = new RenderedExclusion[exclusions.Count]; int i = 0;
            foreach (var pair in exclusions) excluded[i++] = new RenderedExclusion
            { reason = pair.Key, intervals = pair.Value.intervals, seconds = pair.Value.seconds };
            Array.Sort(excluded, (a, b) => string.CompareOrdinal(a.reason, b.reason));
            return new RenderedVisitSummary
            {
                session_id = session, room = room, visit = visit, start_realtime = started, end_realtime = now,
                duration_seconds = now - started, unobserved_tail_seconds = now - previous,
                observed_intervals = observed,
                warmup_observed_eligible_seconds = warmup, pending_boundary_exclusion = baseline ? "none" : pending,
                raw_cold_inclusive = raw.Snapshot(), warm = warm.Snapshot(), exclusions = excluded
            };
        }
    }

    [Serializable] public sealed class RenderedHistogramSummary
    {
        public long measured_frames, frames_over_60hz, frames_over_33_3ms, histogram_overflow_frames;
        public double measured_seconds, mean_milliseconds, p95_upper_milliseconds, maximum_milliseconds;
        public double histogram_bucket_milliseconds = RenderedRouteHistogram.BucketMilliseconds;
        public int histogram_finite_buckets = RenderedRouteHistogram.FiniteBuckets;
        public bool has_samples, within_provisional_budget;
        public long[] histogram;
    }
    [Serializable] public sealed class RenderedExclusion { public string reason; public long intervals; public double seconds; }
    [Serializable] public sealed class RenderedVisitSummary
    {
        public string session_id, room, pending_boundary_exclusion;
        public long visit;
        public long observed_intervals;
        public double start_realtime, end_realtime, duration_seconds, unobserved_tail_seconds;
        public double warmup_required_eligible_seconds = RenderedRouteVisitMetrics.WarmupSeconds, warmup_observed_eligible_seconds;
        public RenderedHistogramSummary raw_cold_inclusive, warm;
        public RenderedExclusion[] exclusions;
    }
}
