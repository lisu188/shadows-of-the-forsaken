using System;
using System.Linq;
using ForsakenStandaloneRenderedRouteSupport;
using NUnit.Framework;

namespace RenderedRoutePureTests
{
    public sealed class RenderedRouteMetricsTests
    {
        private const string Token = "177a4ab6-5417-47c0-936b-0a171e177344";
        private static RenderedRouteVisitMetrics Visit() => new RenderedRouteVisitMetrics(Token, "Library", 1, 0);
        private static void Account(RenderedVisitSummary value)
        {
            Assert.That(value.raw_cold_inclusive.histogram.Sum(), Is.EqualTo(value.raw_cold_inclusive.measured_frames));
            Assert.That(value.warm.histogram.Sum(), Is.EqualTo(value.warm.measured_frames));
            Assert.That(value.raw_cold_inclusive.measured_frames + value.exclusions.Where(x => x.reason != "warmup").Sum(x => x.intervals), Is.EqualTo(value.observed_intervals));
            Assert.That(value.warm.measured_frames + value.exclusions.Sum(x => x.intervals), Is.EqualTo(value.observed_intervals));
        }

        [Test]
        public void WarmLaneMatchesUnchangedProductionHistogramPolicy()
        {
            var measured = Visit(); var production = new PlayerFrameMetrics();
            measured.Observe(0, 0, Token, "Library"); production.Record(0, PlayerFrameExclusion.Transition);
            double now = 0;
            for (int i = 1; i <= 500; i++)
            {
                double delta = i % 89 == 0 ? .8 : i % 11 == 0 ? .035 : .02;
                double prior = now; now += delta;
                measured.Observe(now, i, Token, "Library"); production.Record(now - prior, PlayerFrameExclusion.None);
            }
            var actual = measured.Close(now); var expected = production.Snapshot(true);
            Assert.That(actual.warm.histogram, Is.EqualTo(expected.histogram));
            Assert.That(actual.warm.measured_frames, Is.EqualTo(expected.measuredFrames));
            Assert.That(actual.warm.mean_milliseconds, Is.EqualTo(expected.averageMilliseconds));
            Assert.That(actual.warm.p95_upper_milliseconds, Is.EqualTo(expected.p95UpperBoundMilliseconds));
            Assert.That(actual.warmup_observed_eligible_seconds, Is.EqualTo(expected.warmupObservedEligibleSeconds));
            Account(actual);
        }

        [Test]
        public void RawIncludesColdFramesButShortVisitDoesNotQualifyWarmLane()
        {
            var value = Visit(); value.Observe(0, 0, Token, "Library"); value.Observe(2, 1, Token, "Library");
            var result = value.Close(2.5);
            Assert.That(result.raw_cold_inclusive.measured_frames, Is.EqualTo(1));
            Assert.That(result.warm.has_samples, Is.False); Assert.That(result.warm.within_provisional_budget, Is.False);
            Assert.That(result.unobserved_tail_seconds, Is.EqualTo(.5)); Account(result);
        }

        [Test]
        public void EntireWarmupCrossingFrameIsExcludedWithoutTrimmingLaterOutlier()
        {
            var value = Visit(); value.Observe(0, 0, Token, "Library"); value.Observe(2.6, 1, Token, "Library");
            value.Observe(5.2, 2, Token, "Library"); value.Observe(15.2, 3, Token, "Library");
            var result = value.Close(15.2);
            Assert.That(result.raw_cold_inclusive.measured_frames, Is.EqualTo(3));
            Assert.That(result.warm.measured_frames, Is.EqualTo(1));
            Assert.That(result.warm.histogram_overflow_frames, Is.EqualTo(1));
            Assert.That(result.warm.p95_upper_milliseconds, Is.EqualTo(10000));
            Assert.That(result.warm.within_provisional_budget, Is.False); Account(result);
        }

        [Test]
        public void CaptureInvalidationExcludesWholeCrossingIntervalAndRestartsBaseline()
        {
            var value = Visit(); value.Observe(0, 0, Token, "Library"); value.Observe(.02, 1, Token, "Library");
            value.Invalidate("capture-crossing"); value.Observe(10, 2, Token, "Library"); value.Observe(10.02, 3, Token, "Library");
            var result = value.Close(10.02);
            Assert.That(result.raw_cold_inclusive.measured_frames, Is.EqualTo(2));
            Assert.That(result.raw_cold_inclusive.maximum_milliseconds, Is.EqualTo(20).Within(.00001));
            Assert.That(result.exclusions.Single(x => x.reason == "capture-crossing").seconds, Is.EqualTo(9.98)); Account(result);
        }

        [Test]
        public void MissingRenderFramesExcludeGapRatherThanInventFastOrSlowFrame()
        {
            var value = Visit(); value.Observe(0, 0, Token, "Library"); value.Observe(.02, 1, Token, "Library");
            value.Observe(2, 8, Token, "Library"); value.Observe(2.02, 9, Token, "Library");
            var result = value.Close(2.02); Assert.That(result.raw_cold_inclusive.measured_frames, Is.EqualTo(2));
            Assert.That(result.exclusions.Single(x => x.reason == "missing-render-frame").intervals, Is.EqualTo(1)); Account(result);
        }

        [Test]
        public void ForeignSessionCannotRelabelVisitOrDonateAnInterval()
        {
            var value = Visit(); value.Observe(0, 0, Token, "Library"); value.Observe(.02, 1, Guid.NewGuid().ToString(), "Library");
            value.Observe(.04, 2, Token, "Library"); var result = value.Close(.04);
            Assert.That(result.session_id, Is.EqualTo(Token)); Assert.That(result.raw_cold_inclusive.has_samples, Is.False); Account(result);
        }

        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(-1)]
        public void InvalidClockCannotEnterHistogram(double invalid)
        {
            var value = Visit(); value.Observe(0, 0, Token, "Library"); Assert.That(value.Observe(invalid, 1, Token, "Library"), Is.False);
            var result = value.Close(0); Assert.That(result.raw_cold_inclusive.has_samples, Is.False); Account(result);
        }

        [Test]
        public void DuplicateFrameIsExcludedAndClosureIsSingleUse()
        {
            var value = Visit(); value.Observe(0, 0, Token, "Library"); value.Observe(.01, 0, Token, "Library");
            var result = value.Close(.02); Assert.That(result.exclusions.Single(x => x.reason == "invalid-clock-or-frame").intervals, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => value.Close(.02));
            Assert.Throws<InvalidOperationException>(() => value.Observe(.03, 1, Token, "Library")); Account(result);
        }

        [Test]
        public void HistogramRejectsOverflowAndKeepsConservativeBucketUpperBound()
        {
            var value = new RenderedRouteHistogram(); value.Record(.01661); var summary = value.Snapshot();
            Assert.That(summary.p95_upper_milliseconds, Is.EqualTo(16.75));
            Assert.Throws<OverflowException>(() => value.Record(double.MaxValue));
        }

        private static RenderedCycleObservation Cycle() => new RenderedCycleObservation
        {
            context_open = true, begin_eligible = true, end_owned_member = true, identity_matches = true, visit_open = true,
            camera_active_unbound = true, running = true, unpaused_positive_time = true, begin_frame = 7, end_frame = 7,
            begin_generation = 2, end_generation = 2, camera_count = 1, owned_begins = 1, owned_ends = 1
        };

        [Test]
        public void OnlyBalancedOwnedUnboundSameFrameCycleQualifies()
        { Assert.That(RenderedCycleEligibility.Accept(Cycle()), Is.True); }

        [TestCase("begin")] [TestCase("end")] [TestCase("camera")] [TestCase("identity")]
        [TestCase("bracket")] [TestCase("frame")] [TestCase("target")] [TestCase("paused")]
        public void UnownedUnbalancedChangedOrSuspendedCycleIsRejected(string fault)
        {
            var cycle = Cycle();
            switch (fault)
            {
                case "begin": cycle.owned_begins = 2; break;
                case "end": cycle.owned_ends = 0; break;
                case "camera": cycle.end_owned_member = false; break;
                case "identity": cycle.identity_matches = false; break;
                case "bracket": cycle.end_generation = 4; break;
                case "frame": cycle.end_frame++; break;
                case "target": cycle.camera_active_unbound = false; break;
                case "paused": cycle.unpaused_positive_time = false; break;
            }
            Assert.That(RenderedCycleEligibility.Accept(cycle), Is.False);
        }

        [Test]
        public void CleanupAttemptsLaterResourcesAfterAnEarlierRemovalFails()
        {
            var called = new System.Collections.Generic.List<string>(); var errors = new System.Collections.Generic.List<Exception>();
            RenderedRouteCleanup.Run(new Action[] { () => { called.Add("unsubscribe"); throw new InvalidOperationException("fault"); },
                () => called.Add("dispose-recorder"), () => called.Add("destroy-owned-helper") }, errors.Add);
            Assert.That(called, Is.EqualTo(new[] { "unsubscribe", "dispose-recorder", "destroy-owned-helper" }));
            Assert.That(errors.Count, Is.EqualTo(1));
        }

        [Test]
        public void CleanupRetainsDestroyedResourceFailuresAndStillClosesOtherLeases()
        {
            int released = 0; var errors = new System.Collections.Generic.List<Exception>();
            RenderedRouteCleanup.Run(new Action[] { () => throw new ObjectDisposedException("fake owned camera"),
                () => released++, () => throw new ObjectDisposedException("fake recorder"), () => released++ }, errors.Add);
            Assert.That(released, Is.EqualTo(2)); Assert.That(errors.Count, Is.EqualTo(2));
            Assert.That(errors.All(error => error is ObjectDisposedException), Is.True);
        }
        [Test]
        public void CompletedIdentityAndObjectivesSurviveLaterResetObservations()
        {
            var value = new RenderedRouteCompletion(); var original = Guid.Parse(Token);
            value.Observe(original, 1, false); Assert.That(value.Completed, Is.False);
            value.Observe(original, 79, true);
            value.Observe(Guid.NewGuid(), 0, false);
            Assert.That(value.Completed, Is.True); Assert.That(value.Session, Is.EqualTo(original)); Assert.That(value.Objectives, Is.EqualTo(79));
        }
    }
}
