using System;

// Unity-free, bounded statistics over observations supplied by the opt-in
// observer. This class never reads or changes the game clock or session state.
public enum PlayerFrameExclusion { None, Warmup, Unfocused, Paused, NotRunning, Transition, Invalid }

public sealed class PlayerFrameMetrics
{
    public const double WarmupSeconds = 5;
    public const double BucketMilliseconds = .25;
    public const int FiniteBuckets = 2048; // <=512 ms, followed by one overflow bucket.
    private readonly long[] histogram = new long[FiniteBuckets + 1];
    private readonly long[] excluded = new long[7];
    private readonly double[] excludedMilliseconds = new double[7];
    private long observed, frames, over60Hz, overBudget;
    private double warmup, total, maximum;

    public bool Record(double seconds, PlayerFrameExclusion reason)
    {
        observed++;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            excluded[(int)PlayerFrameExclusion.Invalid]++;
            return false;
        }
        if (reason < PlayerFrameExclusion.None || reason > PlayerFrameExclusion.Invalid)
            reason = PlayerFrameExclusion.Invalid;
        double milliseconds = seconds * 1000;
        if (reason == PlayerFrameExclusion.None && warmup < WarmupSeconds)
        {
            warmup += seconds;
            reason = PlayerFrameExclusion.Warmup;
        }
        if (reason != PlayerFrameExclusion.None)
        {
            excluded[(int)reason]++;
            excludedMilliseconds[(int)reason] += milliseconds;
            return false;
        }
        // Neither long frames nor the overflow bucket are capped or trimmed.
        frames++;
        total += milliseconds;
        maximum = Math.Max(maximum, milliseconds);
        if (milliseconds > 1000.0 / 60) over60Hz++;
        if (milliseconds > 33.3) overBudget++;
        int bucket = milliseconds > FiniteBuckets * BucketMilliseconds ? FiniteBuckets :
            Math.Max(0, (int)Math.Ceiling(milliseconds / BucketMilliseconds) - 1);
        histogram[bucket]++;
        return true;
    }

    public PlayerFrameSummary Snapshot(bool includeHistogram)
    {
        long cumulative = 0;
        long percentileRank = (long)Math.Ceiling(frames * .95);
        double p95 = 0;
        for (int bucket = 0; frames > 0 && bucket < histogram.Length; bucket++)
        {
            cumulative += histogram[bucket];
            if (cumulative < percentileRank) continue;
            p95 = bucket == FiniteBuckets ? maximum : (bucket + 1) * BucketMilliseconds;
            break;
        }
        var reasons = new PlayerFrameExcludedCount[excluded.Length - 1];
        for (int i = 1; i < excluded.Length; i++)
            reasons[i - 1] = new PlayerFrameExcludedCount
            { reason = ((PlayerFrameExclusion)i).ToString(), frames = excluded[i], milliseconds = excludedMilliseconds[i] };
        return new PlayerFrameSummary
        {
            observedFrames = observed, measuredFrames = frames, measuredSeconds = total / 1000,
            warmupRequiredEligibleSeconds = WarmupSeconds, warmupObservedEligibleSeconds = warmup,
            averageMilliseconds = frames > 0 ? total / frames : 0,
            p95UpperBoundMilliseconds = p95, maximumMilliseconds = maximum,
            framesOver60Hz = over60Hz, framesOver33Point3Milliseconds = overBudget,
            histogramBucketMilliseconds = BucketMilliseconds, histogramFiniteBuckets = FiniteBuckets,
            histogramOverflowFrames = histogram[FiniteBuckets],
            histogram = includeHistogram ? (long[])histogram.Clone() : Array.Empty<long>(), excluded = reasons,
            hasSamples = frames > 0,
            withinProvisionalFrameBudget = frames > 0 && total / frames <= 33.3 && p95 <= 33.3
        };
    }
}

[Serializable]
public sealed class PlayerFrameSummary
{
    public long observedFrames, measuredFrames, framesOver60Hz, framesOver33Point3Milliseconds, histogramOverflowFrames;
    public double measuredSeconds, warmupRequiredEligibleSeconds, warmupObservedEligibleSeconds;
    public double averageMilliseconds, p95UpperBoundMilliseconds, maximumMilliseconds, histogramBucketMilliseconds;
    public int histogramFiniteBuckets;
    public long[] histogram;
    public PlayerFrameExcludedCount[] excluded;
    public bool hasSamples, withinProvisionalFrameBudget;
}

[Serializable]
public sealed class PlayerFrameExcludedCount
{
    public string reason;
    public long frames;
    public double milliseconds;
}
