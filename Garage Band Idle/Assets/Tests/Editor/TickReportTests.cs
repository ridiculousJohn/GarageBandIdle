using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // What ONE tick actually moved (design doc 12.11), recorded at the mutation
    // sites rather than measured as a balance delta. The arithmetic everywhere
    // is TestTree's: one practice_amp pays cash at 0.5/s, the Jam's reveal-gated
    // entry pays rehearsal at 0.5/s, and cover_1 drinks rehearsal at 2/s toward
    // a fillAmount of 100. That pool-limited pair is the shape a gross gather
    // gets wrong in both directions at once - the supply slope would show the
    // balance climbing while the demand slope would fill the bar 4x too fast.
    public class TickReportTests
    {
        // The cadence never fires here: every row ticks by hand through
        // Session.Tick, so the interval stays the asset's own default.
        private static GameConfig Config() => ScriptableObject.CreateInstance<GameConfig>();

        // Computed amounts are asserted within tolerance, never bit-exact:
        // BigDouble's base-10 mantissa is binary-inexact for most values, so an
        // exact compare would pass or fail on the luck of the inputs.
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(),
                Math.Max(1e-9, Math.Abs(expected) * 1e-12), what ?? string.Empty);

        // A live session over the standing tree, entered with the stamp at Now
        // so the switch skips the offer. The amp count is a parameter because
        // the completion row wants no rate production at all.
        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly GameSession Session;

            public Fixture(int amps = 1)
            {
                Tree.Tier1.generatorCounts["practice_amp"] = amps;
                Tree.Ch1.lastActiveUtc = Tree.Now;
                Session = new GameSession(Tree.Root, Config());
                Session.SwitchChapter(Tree.Ch1, Tree.Now);
            }

            public DateTime At(double seconds) => Tree.Now.AddSeconds(seconds);
            public GameContext Ctx(double seconds) => new GameContext(Tree.Tier1, At(seconds));

            // One tick of the whole window, ending where the window ends; the
            // session holds the report that tick produced.
            public TickReport Tick(double seconds)
            {
                Session.Tick(seconds, At(seconds));
                return Session.LastTick;
            }

            // The Jam's rehearsal entries sit behind the reveal (12.2), so the
            // flag is what turns the 0.5/s supply on.
            public void RevealRehearsal() => Tree.Tier1.flags.Add("rehearsal_revealed");

            // Selection written as a FACT, bypassing the entry point: a report
            // test is not a SetActiveBars test, and the command would clear the
            // very report the row wants to read.
            public void SelectCover1() =>
                Tree.Tier1.activeBars[Tree.LearnCovers.Id] = new HashSet<string> { Tree.Cover1.Id };

            public BigNumber Balance(string currencyId) => Tree.Tier1.balances[currencyId];

            public BigNumber Progress(BarDefinition bar) =>
                Tree.Tier1.barProgress.TryGetValue(bar.Id, out var value) ? value : BigNumber.Zero;
        }

        // ---- the pool-limited pair ----

        [Test]
        public void A_pool_limited_bar_reports_a_flat_pool_and_the_fill_it_actually_took()
        {
            var f = new Fixture();
            f.RevealRehearsal();
            f.SelectCover1();

            var report = f.Tick(1);

            // 0.5 deposited and 0.5 drawn in the same second: the pool is what
            // limits the draw, so the net is zero and the bar fills at the
            // supply rate rather than at its own 2/s demand.
            AssertClose(0, report.CurrencyNet(f.Tree.Tier1, "rehearsal"), "0.5 in, 0.5 out");
            AssertClose(0, report.CurrencySlope(f.Tree.Tier1, "rehearsal"));
            AssertClose(0.5, report.BarFill(f.Tree.Tier1, f.Tree.Cover1.Id));
            AssertClose(0.5, report.BarSlope(f.Tree.Tier1, f.Tree.Cover1.Id), "realized, not demanded");

            // And the facts agree with the report, which is the whole point of
            // recording at the sites: the balance never climbs and the bar
            // never fills faster than the pool it drinks.
            AssertClose(0, f.Balance("rehearsal"), "drained as fast as it is fed");
            AssertClose(0.5, f.Progress(f.Tree.Cover1), "progress");
        }

        [Test]
        public void A_draining_pool_reports_the_negative_net_the_balance_lost()
        {
            var f = new Fixture();
            f.RevealRehearsal();
            f.SelectCover1();
            f.Tree.Tier1.balances["rehearsal"] = 10;

            var report = f.Tick(1);

            // 0.5 in against the bar's full 2/s draw, the stock covering the
            // shortfall: 10 + 0.5 - 2.
            AssertClose(-1.5, report.CurrencyNet(f.Tree.Tier1, "rehearsal"));
            AssertClose(-1.5, report.CurrencySlope(f.Tree.Tier1, "rehearsal"));
            AssertClose(2, report.BarFill(f.Tree.Tier1, f.Tree.Cover1.Id), "the bar drew its whole rate");
            AssertClose(8.5, f.Balance("rehearsal"));
        }

        // ---- plain production ----

        [Test]
        public void Rate_production_reports_its_deposit_over_the_ticks_own_seconds()
        {
            var f = new Fixture();

            var report = f.Tick(10);

            // One amp at 0.5/s for ten seconds, and the slope is that net per
            // real second - the same 0.5 whatever window measured it.
            AssertClose(5, report.CurrencyNet(f.Tree.Tier1, "cash"));
            AssertClose(0.5, report.CurrencySlope(f.Tree.Tier1, "cash"));
            Assert.AreEqual(10, report.Seconds, 1e-12);
            AssertClose(5, f.Balance("cash"));

            // Unrecorded is zero, never a miss: no cover was selected, so the
            // bar keys were never written and answer zero all the same.
            AssertClose(0, report.BarFill(f.Tree.Tier1, f.Tree.Cover1.Id), "nothing filled");
            Assert.AreSame(report, f.Session.LastTick, "the session holds the tick's own report");
        }

        // ---- what clears it ----

        [Test]
        public void A_non_tick_transaction_clears_the_report()
        {
            var f = new Fixture();
            Assert.IsNotNull(f.Tick(1));

            // A command can invalidate every measured slope, so the session
            // sits at truth until the next tick measures the new state.
            Assert.IsTrue(f.Session.FireProducer(f.Ctx(1), f.Tree.TapProducer));
            Assert.IsNull(f.Session.LastTick);
        }

        [Test]
        public void A_refused_command_leaves_the_report_in_place()
        {
            var f = new Fixture();
            var report = f.Tick(1);

            // One second of cash at 0.5/s against a second amp's 69 and its
            // 100-earned gate: refused before any mutation, so there is nothing
            // for the refusal to invalidate.
            Assert.IsFalse(f.Session.TryBuy(f.Ctx(1), f.Tree.PracticeAmp));
            Assert.AreSame(report, f.Session.LastTick);
        }

        // ---- one-shot mutations ----

        [Test]
        public void A_completion_payout_moves_truth_without_moving_a_slope()
        {
            var f = new Fixture(amps: 0);
            f.Tree.Cover1.onComplete.Add(new AddCurrency { currencies = { f.Tree.Cash }, amount = 1000 });
            f.RevealRehearsal();
            f.SelectCover1();
            f.Tree.Tier1.balances["rehearsal"] = 100;
            f.Tree.Tier1.barProgress[f.Tree.Cover1.Id] = 99.5;

            var report = f.Tick(1);

            // The 2/s draw carries 99.5 across the 100 threshold and the
            // completion banks its 1000 cash. Site recording is what keeps that
            // one-shot out of the slope: a state delta would extrapolate the
            // payout as if it repeated every second.
            AssertClose(1000, f.Balance("cash"), "truth moved");
            AssertClose(0, report.CurrencyNet(f.Tree.Tier1, "cash"), "and the report did not");
            AssertClose(0, report.CurrencySlope(f.Tree.Tier1, "cash"));
            AssertClose(2, report.BarFill(f.Tree.Tier1, f.Tree.Cover1.Id), "the draw itself is recorded");
        }

        // ---- the guards ----

        [Test]
        public void A_no_op_tick_reports_an_empty_report_rather_than_null()
        {
            var f = new Fixture();
            var config = Config();

            // The two no-op guards: no foreground chapter, and a dt that
            // measures nothing. Both answer a report with no seconds, so a
            // consumer reads zero slopes instead of dereferencing a null.
            AssertEmpty(TickSystem.Tick(f.Tree.Root, null, config, 1, f.At(1)));
            AssertEmpty(TickSystem.Tick(f.Tree.Root, f.Tree.Ch1, config, 0, f.At(0)));

            void AssertEmpty(TickReport report)
            {
                Assert.IsNotNull(report);
                Assert.AreEqual(0, report.Seconds, 1e-12);
                AssertClose(0, report.CurrencyNet(f.Tree.Tier1, "cash"));
                AssertClose(0, report.CurrencySlope(f.Tree.Tier1, "cash"), "no seconds, no slope");
                AssertClose(0, report.BarFill(f.Tree.Tier1, f.Tree.Cover1.Id));
                AssertClose(0, report.BarSlope(f.Tree.Tier1, f.Tree.Cover1.Id));
            }
        }
    }
}
