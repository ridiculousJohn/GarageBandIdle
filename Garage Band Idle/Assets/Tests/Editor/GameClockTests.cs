using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The one time source (design doc 12.9): real time advances at EVERY entry
    // point, while game time counts only the deltas frames actually delivered -
    // which is what keeps a suspended gap visible to the idle path and out of
    // live production.
    public class GameClockTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Construction_starts_at_the_given_moment_with_no_elapsed_time()
        {
            var clock = new GameClock(Now);

            Assert.AreEqual(Now, clock.RealTimeUtc);
            Assert.AreEqual(0d, clock.DeltaSeconds, 1e-9);
            Assert.AreEqual(0d, clock.GameTimeSeconds, 1e-9);
        }

        [Test]
        public void Real_time_advances_at_every_entry_point()
        {
            var clock = new GameClock(Now);

            clock.Frame(Now.AddSeconds(1), 1);
            Assert.AreEqual(Now.AddSeconds(1), clock.RealTimeUtc);

            // The lifecycle path moves it just as far: a resume that left the
            // time stale would compute a zero idle window over the whole gap.
            clock.Resample(Now.AddSeconds(600));
            Assert.AreEqual(Now.AddSeconds(600), clock.RealTimeUtc);
        }

        [Test]
        public void A_frame_contributes_its_delta_to_game_time()
        {
            var clock = new GameClock(Now);

            clock.Frame(Now.AddSeconds(0.25), 0.25);

            Assert.AreEqual(0.25d, clock.DeltaSeconds, 1e-9);
            Assert.AreEqual(0.25d, clock.GameTimeSeconds, 1e-9);
        }

        [Test]
        public void A_resample_moves_real_time_over_the_gap_and_leaves_game_time()
        {
            // The resume case: the away window is visible in real time, which is
            // what the idle path measures, and game time never counts a second
            // of it. The frame delta is zero because no frame elapsed.
            var clock = new GameClock(Now);
            clock.Frame(Now.AddSeconds(0.5), 0.5);

            clock.Resample(Now.AddSeconds(3600));

            Assert.AreEqual(Now.AddSeconds(3600), clock.RealTimeUtc);
            Assert.AreEqual(0d, clock.DeltaSeconds, 1e-9);
            Assert.AreEqual(0.5d, clock.GameTimeSeconds, 1e-9);
        }

        [Test]
        public void Game_time_is_the_sum_of_the_live_deltas()
        {
            var clock = new GameClock(Now);
            var at = Now;
            var total = 0d;
            foreach (var delta in new[] { 0.016, 0.033, 0.25, 1.5 })
            {
                at = at.AddSeconds(delta);
                total += delta;
                clock.Frame(at, delta);
            }
            Assert.AreEqual(total, clock.GameTimeSeconds, 1e-9);

            // A suspension between two frames adds nothing to the sum, so the
            // frame after it resumes counting where the last one stopped.
            clock.Resample(at.AddSeconds(120));
            clock.Frame(at.AddSeconds(120.02), 0.02);

            Assert.AreEqual(total + 0.02, clock.GameTimeSeconds, 1e-9);
        }
    }
}
