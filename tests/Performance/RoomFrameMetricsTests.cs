using System;
using System.Linq;
using NUnit.Framework;

public sealed class RoomFrameMetricsTests
{
    private static readonly Guid Token = new Guid("04eed639-4e74-4bb3-91f2-9994d1fe6a32");

    [Test]
    public void VisitRetainsIdentityWarmupHistogramAndUnobservedTail()
    {
        var visit = new PlayerRoomFrameMetrics(Token, "Library", 3, 10);
        Assert.That(visit.Record(10.1, Token, "Library", PlayerFrameExclusion.None), Is.False);
        Assert.That(visit.Record(15.1, Token, "Library", PlayerFrameExclusion.None), Is.False);
        Assert.That(visit.Record(15.11, Token, "Library", PlayerFrameExclusion.None), Is.True);
        var row = visit.Close(15.15);
        Assert.That(row.sessionId, Is.EqualTo(Token.ToString()));
        Assert.That(row.room, Is.EqualTo("Library"));
        Assert.That(row.visit, Is.EqualTo(3));
        Assert.That(row.durationSeconds, Is.EqualTo(5.15).Within(1e-8));
        Assert.That(row.unobservedTailSeconds, Is.EqualTo(.04).Within(1e-8));
        Assert.That(row.frameIntervals.measuredFrames, Is.EqualTo(1));
        Assert.That(row.frameIntervals.histogram.Sum(), Is.EqualTo(1));
        Assert.That(row.frameIntervals.excluded.Single(x => x.reason == "Transition").milliseconds, Is.EqualTo(100).Within(1e-6));
        Assert.That(row.frameIntervals.excluded.Single(x => x.reason == "Warmup").milliseconds, Is.EqualTo(5000).Within(1e-6));
        Assert.That(visit.Record(16, Token, "Library", PlayerFrameExclusion.None), Is.False);
        Assert.Throws<InvalidOperationException>(() => visit.Close(16));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ForeignIdentityCannotAcquireSamplesOrRelabelVisit(bool differentSession)
    {
        var visit = new PlayerRoomFrameMetrics(Token, "Library", 1, 0);
        visit.Record(.1, Token, "Library", PlayerFrameExclusion.None);
        visit.Record(5.1, Token, "Library", PlayerFrameExclusion.None);
        Assert.That(visit.Record(5.2, differentSession ? Guid.NewGuid() : Token,
            differentSession ? "Library" : "Catacombs", PlayerFrameExclusion.None), Is.False);
        Assert.That(visit.Record(5.3, Token, "Library", PlayerFrameExclusion.None), Is.False);
        var row = visit.Close(5.4);
        Assert.That(row.sessionId, Is.EqualTo(Token.ToString()));
        Assert.That(row.room, Is.EqualTo("Library"));
        Assert.That(row.frameIntervals.measuredFrames, Is.Zero);
        Assert.That(row.frameIntervals.excluded.Single(x => x.reason == "Transition").frames, Is.EqualTo(2));
    }

    [TestCase(PlayerFrameExclusion.Unfocused)]
    [TestCase(PlayerFrameExclusion.Paused)]
    public void SuspensionWithoutFramesIsRememberedAfterReturn(PlayerFrameExclusion reason)
    {
        var visit = new PlayerRoomFrameMetrics(Token, "Library", 1, 0);
        visit.Record(.1, Token, "Library", PlayerFrameExclusion.None);
        visit.Record(5.1, Token, "Library", PlayerFrameExclusion.None);
        visit.Invalidate(reason); visit.Invalidate(PlayerFrameExclusion.Transition);
        Assert.That(visit.Record(15.1, Token, "Library", PlayerFrameExclusion.None), Is.False);
        Assert.That(visit.Record(15.11, Token, "Library", PlayerFrameExclusion.None), Is.True);
        var summary = visit.Close(15.11).frameIntervals;
        Assert.That(summary.excluded.Single(x => x.reason == reason.ToString()).milliseconds, Is.EqualTo(10000).Within(1e-6));
        Assert.That(summary.measuredFrames, Is.EqualTo(1));
        Assert.That(summary.measuredSeconds, Is.EqualTo(.01).Within(1e-8));
    }

    [Test]
    public void EmptyVisitsAndFinalPartialIntervalsNeverBecomeFrames()
    {
        var empty = new PlayerRoomFrameMetrics(Token, "Puzzle", 1, 4).Close(4.2);
        Assert.That(empty.frameIntervals.observedFrames, Is.Zero);
        Assert.That(empty.frameIntervals.hasSamples, Is.False);
        Assert.That(empty.frameIntervals.withinProvisionalFrameBudget, Is.False);
        Assert.That(empty.unobservedTailSeconds, Is.EqualTo(.2).Within(1e-8));
    }

    [Test]
    public void InvalidClockCannotPoisonLaterDurationOrProduceSamples()
    {
        var visit = new PlayerRoomFrameMetrics(Token, "Library", 1, 10);
        foreach (double now in new[] { double.NaN, double.PositiveInfinity, 9.0 })
            Assert.That(visit.Record(now, Token, "Library", PlayerFrameExclusion.None), Is.False);
        Assert.Throws<ArgumentException>(() => visit.Close(9));
        var row = visit.Close(11);
        Assert.That(row.durationSeconds, Is.EqualTo(1));
        Assert.That(row.frameIntervals.excluded.Single(x => x.reason == "Invalid").frames, Is.EqualTo(3));
        Assert.That(row.frameIntervals.measuredFrames, Is.Zero);
    }
}
