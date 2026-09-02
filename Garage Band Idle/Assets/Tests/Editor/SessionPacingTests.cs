using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The session's cadence (design doc 12.9): the frame samples the clock and
    // banks live time, a crossing ticks ONCE with the whole accumulation, and
    // every mutating command settles the bank before it mutates without
    // reading a clock of its own. The arithmetic everywhere: one practice_amp
    // pays cash at 0.5/s live, so a window's production is half its seconds.
    public class SessionPacingTests
    {
        // A one-second interval reads well against the sub-second windows below.
        private static GameConfig Config(double tickInterval = 1)
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.tickIntervalSeconds = tickInterval;
            return config;
        }

        // Computed amounts are asserted within tolerance, never bit-exact:
        // BigDouble's base-10 mantissa is binary-inexact for most values, so an
        // exact compare would pass or fail on the luck of the inputs.
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(),
                Math.Max(1e-9, Math.Abs(expected) * 1e-12), what ?? string.Empty);

        // A live session over the standing tree with one amp owned, plus a
        // refresh counter. The stamp at Now makes the entry skip the offer.
        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly GameSession Session;
            public int Refreshes;

            public Fixture()
            {
                Tree.Tier1.generatorCounts["practice_amp"] = 1;
                Tree.Ch1.lastActiveUtc = Tree.Now;
                Session = new GameSession(Tree.Root, Config());
                Session.Refreshed += () => Refreshes++;
                Session.SwitchChapter(Tree.Ch1, Tree.Now);
                // The switch set the sample at Now; this frame states it before
                // the timed steps.
                Session.Accumulate(Tree.Now);
            }

            public DateTime At(double seconds) => Tree.Now.AddSeconds(seconds);
            public GameContext Ctx(double seconds) => new GameContext(Tree.Tier1, At(seconds));

            // A player action as the game issues it: in the frame that sampled
            // the clock, stamped with that frame's time.
            public void Frame(double seconds) => Session.Accumulate(At(seconds));
            public BigNumber Cash => Tree.Tier1.balances["cash"];
        }

        // A second chapter beside ch1 with a tier and a currency of its own, so
        // a switch has somewhere to land. TestTree builds its states from the
        // chapters present at construction, so they are rebuilt here.
        private class TwoChapters
        {
            public readonly TestTree Tree = new();
            public readonly RootScopeState Root;
            public readonly ChapterScopeState Ch1;
            public readonly ScopeState Tier1;
            public readonly ChapterScopeState Ch2;
            public readonly ScopeState Tier2;
            public readonly GameSession Session;

            public TwoChapters()
            {
                var ch2Def = TestTree.MakeChapter("ch2");
                var tier2Def = TestTree.MakeTier("tier2");
                var merch = TestTree.DeclareCurrency(tier2Def, "merch");
                var merchPress = TestTree.MakeDefinition<ProducerDefinition>("merch_press");
                merchPress.produces.Add(TestTree.Entry(merch, Stat.Rate, 2));
                tier2Def.producers.Add(merchPress);
                ch2Def.children.Add(tier2Def);
                Tree.Chapters.Add(ch2Def);

                Root = ScopeState.Build(Tree.Content);
                Ch1 = (ChapterScopeState)Root.FindInSubtree(Tree.Ch1Def);
                Tier1 = Root.FindInSubtree(Tree.Tier1Def);
                Ch2 = (ChapterScopeState)Root.FindInSubtree(ch2Def);
                Tier2 = Root.FindInSubtree(tier2Def);
                Tier1.generatorCounts["practice_amp"] = 1;
                Ch1.lastActiveUtc = Tree.Now;
                Session = new GameSession(Root, Config());
                Session.SwitchChapter(Ch1, Tree.Now);
                // As in Fixture: the switch set the sample, stated here.
                Session.Accumulate(Tree.Now);
            }

            public DateTime At(double seconds) => Tree.Now.AddSeconds(seconds);
        }

        // ---- the interval ----

        [Test]
        public void A_sub_interval_accumulation_ticks_nothing()
        {
            var f = new Fixture();
            var refreshes = f.Refreshes;

            f.Session.Accumulate(f.At(0.4));

            AssertClose(0, f.Cash, "no tick ran, so the amp paid nothing");
            Assert.AreEqual(refreshes, f.Refreshes);
        }

        [Test]
        public void Crossing_the_interval_ticks_once_with_the_whole_accumulation()
        {
            var f = new Fixture();
            var refreshes = f.Refreshes;

            f.Session.Accumulate(f.At(0.4));
            f.Session.Accumulate(f.At(1.1));

            // One tick of 1.1s at 0.5/s - not a 1.0s window with 0.1 stranded,
            // and not two transactions.
            AssertClose(0.55, f.Cash);
            Assert.AreEqual(refreshes + 1, f.Refreshes);

            // The next crossing carries only its own accumulation, so the two
            // simulated windows are contiguous: 2.2s of live time, all of it paid.
            f.Session.Accumulate(f.At(1.5));
            AssertClose(0.55, f.Cash);
            f.Session.Accumulate(f.At(2.2));
            AssertClose(1.1, f.Cash);
            Assert.AreEqual(refreshes + 2, f.Refreshes);
        }

        // ---- the clears ----

        [Test]
        public void Time_under_the_claim_dialog_never_rides_into_the_first_live_tick()
        {
            // Entered into AwaitingIdleClaim: the amp's idle rate over the away
            // window makes a real offer.
            var tree = new TestTree();
            tree.Tier1.generatorCounts["practice_amp"] = 1;
            tree.Ch1.lastActiveUtc = tree.Now.AddSeconds(-1000);
            var session = new GameSession(tree.Root, Config());
            session.SwitchChapter(tree.Ch1, tree.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, session.Phase);

            // The frame under the dialog is a non-Live sample: it advances the
            // sample and banks nothing, so the dialog time is never pooled.
            session.Accumulate(tree.Now.AddSeconds(0.4));
            Assert.IsTrue(session.ClaimIdle(tree.Now.AddSeconds(0.4)));
            var settled = tree.Tier1.balances["cash"].ToDouble();

            // Had the dialog's 0.4 carried, this 0.6 would have crossed.
            session.Accumulate(tree.Now.AddSeconds(1.0));
            AssertClose(settled, tree.Tier1.balances["cash"], "the first live window is still under the interval");

            // The crossing pays exactly the live time since the claim: 1.1s,
            // never the 1.5s that would include the dialog.
            session.Accumulate(tree.Now.AddSeconds(1.5));
            AssertClose(settled + 0.55, tree.Tier1.balances["cash"]);
        }

        [Test]
        public void A_backwards_sample_clears_pending_and_stalls_nothing()
        {
            var f = new Fixture();

            f.Session.Accumulate(f.At(0.8));
            AssertClose(0, f.Cash);

            // The clock rolls back 5s: the pending 0.8 would span a
            // discontinuity no single tick could stamp, so it goes.
            f.Session.Accumulate(f.At(-4.2));

            // 0.9 on the rolled-back clock ticks nothing - with the 0.8 still
            // pending it would have crossed.
            f.Session.Accumulate(f.At(-3.3));
            AssertClose(0, f.Cash);

            // And 0.2 more crosses with 1.1, never the 2.0 the stale 0.8 would
            // have made: later samples tick normally on the rolled-back clock.
            f.Session.Accumulate(f.At(-3.1));
            AssertClose(0.55, f.Cash);
        }

        // ---- the flush ----

        [Test]
        public void A_buy_settles_the_pre_purchase_window_before_the_generator_exists()
        {
            var f = new Fixture();
            f.Tree.Tier1.balances["cash"] = 1000;
            f.Tree.Tier1.earnedTotals["cash"] = 1000;   // the amp's gate wants 100 earned

            // The frame banked 0.9s, and the buy settles it before it mutates -
            // at ONE amp, since the second is not paid for until the mutation
            // runs against the settled state.
            f.Frame(0.9);
            Assert.IsTrue(f.Session.TryBuy(f.Ctx(0.9), f.Tree.PracticeAmp));

            Assert.AreEqual(2, f.Tree.Tier1.generatorCounts["practice_amp"]);
            AssertClose(1000 + 0.45 - 69, f.Cash, "0.9s at 0.5/s, less the second amp's 60 x 1.15");
        }

        [Test]
        public void A_command_in_the_frame_that_banked_a_sub_interval_settles_it_first()
        {
            var f = new Fixture();
            f.Tree.Tier1.balances["cash"] = 1000;
            f.Tree.Tier1.earnedTotals["cash"] = 1000;

            // The frame banks 0.5 under the interval; the buy in that same
            // frame carries the frame's time, so it measures nothing - and
            // settles the 0.5 at one amp before the second exists. A zero
            // elapsed is not a discontinuity.
            f.Frame(0.5);
            Assert.IsTrue(f.Session.TryBuy(f.Ctx(0.5), f.Tree.PracticeAmp));
            AssertClose(1000 + 0.25 - 69, f.Cash);

            // The bank was settled by the buy, so 0.5 more is still under the
            // interval; had the buy discarded it instead, nothing would differ
            // here, which is why the row above asserts the balance.
            f.Frame(1.0);
            AssertClose(1000 + 0.25 - 69, f.Cash);

            // What crosses is exactly the 1.1s since the buy, at two amps.
            f.Frame(1.6);
            AssertClose(1000 + 0.25 - 69 + 1.1, f.Cash);
        }

        [Test]
        public void A_switch_flushes_the_outgoing_chapter_and_the_incoming_one_inherits_nothing()
        {
            var w = new TwoChapters();

            w.Session.Accumulate(w.At(0.4));
            // The switch, in the frame that banked 0.4s, settles it into the
            // OUTGOING subtree, then the incoming chapter's window starts at the
            // switch moment.
            w.Session.SwitchChapter(w.Ch2, w.At(0.4));
            Assert.AreEqual(SessionPhase.Live, w.Session.Phase);
            AssertClose(0.2, w.Tier1.balances["cash"], "ch1 banked its own 0.4s at 0.5/s");

            // 0.7s of ch2's own time crosses nothing - ch1's 0.4 is not riding along.
            w.Session.Accumulate(w.At(1.1));
            AssertClose(0, w.Tier2.balances["merch"]);

            // 1.1s of ITS time is what ticks.
            w.Session.Accumulate(w.At(1.5));
            AssertClose(2.2, w.Tier2.balances["merch"], "the merch press pays 2/s for the chapter's own window");
            AssertClose(0.2, w.Tier1.balances["cash"], "the outgoing chapter earned nothing after the switch");
        }

        [Test]
        public void A_jam_tap_flushes_the_pending_window_rather_than_clearing_it()
        {
            var f = new Fixture();

            f.Frame(0.5);
            Assert.IsTrue(f.Session.FireProducer(f.Ctx(0.5), f.Tree.TapProducer));
            f.Frame(1.0);
            Assert.IsTrue(f.Session.FireProducer(f.Ctx(1.0), f.Tree.TapProducer));

            // Two taps at 1 cash each plus the amp's full second of rate: a
            // clear per tap would starve the rate production the amp exists for.
            AssertClose(2.5, f.Cash);
        }

        [Test]
        public void Tapping_faster_than_the_cadence_loses_no_rate_production()
        {
            var f = new Fixture();

            // Ten frames a tenth of a second apart, a tap in each: the bank
            // never reaches the interval on its own, so every tick here is a
            // tap settling the frame's tenth. The amp still earns its half second.
            for (var i = 1; i <= 10; i++)
            {
                f.Frame(i * 0.1);
                Assert.IsTrue(f.Session.FireProducer(f.Ctx(i * 0.1), f.Tree.TapProducer));
            }

            AssertClose(10 + 0.5, f.Cash, "ten taps plus 1.0s at 0.5/s");
        }
    }
}
