using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The driver's tick clock (design doc 12.9, requirement 2). The contract
    // under test: Advance moves the baseline unconditionally, Reset swallows
    // whatever interval preceded it, and a backwards clock passes through as
    // the nonpositive dt the session no-ops on.
    public class TickBaselineTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Advance_returns_the_elapsed_seconds_and_moves_the_baseline()
        {
            var baseline = new TickBaseline(T0);

            Assert.AreEqual(1.5, baseline.Advance(T0.AddSeconds(1.5)), 1e-9);
            Assert.AreEqual(0.5, baseline.Advance(T0.AddSeconds(2)), 1e-9);
        }

        // The dialog-pooling regression: frames refused during
        // AwaitingIdleClaim must not accumulate. The baseline knows nothing of
        // phases, so the guarantee is structural - every Advance measures only
        // since the previous one, whether or not that dt was accepted.
        [Test]
        public void Consecutive_advances_never_pool_a_refused_interval()
        {
            var baseline = new TickBaseline(T0);

            var sum = 0.0;
            for (var i = 1; i <= 10; i++)
                sum = Math.Max(sum, baseline.Advance(T0.AddSeconds(i)));

            Assert.AreEqual(1.0, sum, 1e-9, "each advance saw one second, never the accumulated ten");
        }

        // The pause-replay regression: a resume below the idle minimum must not
        // replay the paused interval as live production. The reset makes the
        // next advance measure from the resume, not the pause.
        [Test]
        public void Reset_swallows_the_interval_that_preceded_it()
        {
            var baseline = new TickBaseline(T0);
            baseline.Advance(T0.AddSeconds(1));

            baseline.Reset(T0.AddSeconds(100));

            Assert.AreEqual(2.0, baseline.Advance(T0.AddSeconds(102)), 1e-9);
        }

        [Test]
        public void A_backwards_clock_passes_through_as_nonpositive_dt()
        {
            var baseline = new TickBaseline(T0);

            var dt = baseline.Advance(T0.AddSeconds(-5));

            Assert.AreEqual(-5.0, dt, 1e-9, "the session treats this as a no-op");
            // The baseline moved anyway: live play resumes from wherever the
            // clock now claims to be, rather than refusing ticks until it
            // catches back up to the high-water mark.
            Assert.AreEqual(5.0, baseline.Advance(T0), 1e-9);
        }
    }
}
