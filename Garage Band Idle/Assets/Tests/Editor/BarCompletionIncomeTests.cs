using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // What a bar's completion is worth (design doc 12.7/12.9): the payout one
    // completion would deposit, the rate it earns while it runs, and the
    // completion income the tick report records beside its deposits. The tree
    // is BarFixture's: a gig at tier1 that fills from time and whose completion
    // fires a draw generator's yield and banks a fixed AddCurrency at root.
    public class BarCompletionIncomeTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(),
                Math.Max(1e-9, Math.Abs(expected) * 1e-9), what ?? string.Empty);

        // The authored shape: the gig repeats, its completion fires the draw
        // (3 rehearsal and 0.5 fans per unit) and banks 7 shared at root.
        // `idle` is a second gig nobody selects, paying buzz, so its absence
        // from the report is readable on a currency nothing else pays.
        private class Gigs
        {
            public readonly BarFixture F = new();
            public readonly GroupDefinition Calendar;
            public readonly BarDefinition Gig, Idle;
            public readonly GeneratorDefinition Draw;
            public readonly CurrencyDefinition Buzz;

            public Gigs(double fillAmount = 5, double fillRate = 1, double yieldFactor = 1,
                        double rateFactor = 1, double gameSpeed = 1)
            {
                Buzz = TestTree.DeclareCurrency(F.Tier1Def, "buzz");
                Calendar = F.Group(F.Tier1Def, "calendar", null, maxActive: 6);
                Gig = F.Bar(Calendar, "gig", fillAmount, fillRate, new Always());
                Idle = F.Bar(Calendar, "idle_gig", 10, 1, new Always());

                Draw = TestTree.MakeDefinition<GeneratorDefinition>("draw");
                Draw.availableWhen = new Always();
                Draw.costCurrency = F.Rehearsal;
                Draw.baseCost = 5;
                Draw.growth = 1.3;
                Draw.produces.Add(TestTree.Entry(F.Rehearsal, Stat.Yield, 3));
                Draw.produces.Add(TestTree.Entry(F.Fans, Stat.Yield, 0.5));
                F.Tier1Def.generators.Add(Draw);

                Gig.onComplete.Add(new FireGeneratorYield { generator = Draw });
                Gig.onComplete.Add(new AddCurrency { currencies = { F.Shared }, amount = 7 });
                Idle.onComplete.Add(new AddCurrency { currencies = { Buzz }, amount = 5 });

                if (yieldFactor != 1)
                    Permanent(F.Tier1Def, "draw_yield", new Effect { target = "draw", stat = Stat.Yield, multiplier = yieldFactor });
                if (rateFactor != 1)
                    Permanent(F.Tier1Def, "gig_rate", new Effect { target = "gig", stat = Stat.Rate, multiplier = rateFactor });
                if (gameSpeed != 1)
                    Permanent(F.Ch1Def, "haste", new Effect { stat = Stat.GameSpeed, multiplier = gameSpeed });

                F.Build();
            }

            private static void Permanent(ScopeDefinition scope, string id, Effect effect)
            {
                var modifier = TestTree.MakeDefinition<ModifierDefinition>(id);
                modifier.effects.Add(effect);
                scope.modifiers.Add(modifier);
                scope.permanentModifiers.Add(modifier);
            }

            public GameContext Ctx => new GameContext(F.Tier1, F.Now);

            public void Own(int count) => F.Tier1.generatorCounts[Draw.Id] = count;

            public BigNumber Of(List<(CurrencyDefinition currency, ScopeState home, BigNumber amount)> payout,
                                CurrencyDefinition currency, ScopeState home)
            {
                foreach (var entry in payout)
                    if (entry.currency == currency && entry.home == home)
                        return entry.amount;
                Assert.Fail($"no entry for {currency.Id}");
                return BigNumber.Zero;
            }
        }

        private static GameConfig Config() => ScriptableObject.CreateInstance<GameConfig>();

        // ---- what one completion pays ----

        // The preview and the deposit are one resolution: the draw's yield at
        // its owned count under its multiplier, and AddCurrency's AmountFor. A
        // real completion in a segment moves each balance by exactly what the
        // preview named.
        [Test]
        public void The_payout_preview_equals_what_a_real_completion_deposits()
        {
            var g = new Gigs(yieldFactor: 2);
            g.Own(3);
            g.F.Select(g.F.Tier1, g.Calendar, g.Gig);
            g.F.Tier1.barProgress[g.Gig.Id] = 4.5;

            var payout = BarSystem.CompletionPayout(g.Ctx, g.Gig);

            Assert.AreEqual(3, payout.Count, "rehearsal, fans and shared");
            AssertClose(3 * 3 * 2, g.Of(payout, g.F.Rehearsal, g.F.Tier1), "rehearsal");
            AssertClose(0.5 * 3 * 2, g.Of(payout, g.F.Fans, g.F.Tier1), "fans");
            AssertClose(7, g.Of(payout, g.F.Shared, g.F.Root), "shared, homed at root");

            var rehearsal = g.F.Balance(g.F.Tier1, g.F.Rehearsal);
            var fans = g.F.Balance(g.F.Tier1, g.F.Fans);
            var shared = g.F.Balance(g.F.Root, g.F.Shared);
            g.F.Segment(1);

            Assert.AreEqual((BigNumber)1, g.F.Fills(g.F.Tier1, g.Gig), "one crossing");
            AssertClose(g.Of(payout, g.F.Rehearsal, g.F.Tier1).ToDouble(),
                g.F.Balance(g.F.Tier1, g.F.Rehearsal) - rehearsal, "rehearsal deposited");
            AssertClose(g.Of(payout, g.F.Fans, g.F.Tier1).ToDouble(),
                g.F.Balance(g.F.Tier1, g.F.Fans) - fans, "fans deposited");
            AssertClose(g.Of(payout, g.F.Shared, g.F.Root).ToDouble(),
                g.F.Balance(g.F.Root, g.F.Shared) - shared, "shared deposited");
        }

        // Two entries paying one (currency, home) are one line, summed.
        [Test]
        public void Amounts_to_one_currency_and_home_are_summed()
        {
            var g = new Gigs();
            g.Gig.onComplete.Add(new AddCurrency { currencies = { g.F.Rehearsal }, amount = 4 });
            g.Own(2);

            var payout = BarSystem.CompletionPayout(g.Ctx, g.Gig);

            Assert.AreEqual(3, payout.Count);
            AssertClose(3 * 2 + 4, g.Of(payout, g.F.Rehearsal, g.F.Tier1));
        }

        // A level nobody bought still names what it pays, at zero.
        [Test]
        public void An_unowned_level_pays_zero_and_keeps_its_lines()
        {
            var g = new Gigs();

            var payout = BarSystem.CompletionPayout(g.Ctx, g.Gig);

            AssertClose(0, g.Of(payout, g.F.Rehearsal, g.F.Tier1));
            AssertClose(0, g.Of(payout, g.F.Fans, g.F.Tier1));
            AssertClose(7, g.Of(payout, g.F.Shared, g.F.Root), "AddCurrency does not read the count");
        }

        // ---- what it earns per real second ----

        // payout x (fill speed x game speed) / fillAmount: a 6-unit gig at
        // 2 units/s under a x1.5 rate effect fills at 3/s, so at game speed 2.5
        // it completes 1.25 times per real second.
        [Test]
        public void The_completion_rate_is_the_payout_times_completions_per_real_second()
        {
            var g = new Gigs(fillAmount: 6, fillRate: 2, yieldFactor: 2, rateFactor: 1.5);
            g.Own(3);

            AssertClose(3, BarSystem.FillRate(g.Ctx, g.Gig), "the fill speed the draw resolves");
            var rates = BarSystem.CompletionRate(g.Ctx, g.Gig, 2.5);

            AssertClose(3 * 3 * 2 * 1.25, g.Of(rates, g.F.Rehearsal, g.F.Tier1), "rehearsal/s");
            AssertClose(0.5 * 3 * 2 * 1.25, g.Of(rates, g.F.Fans, g.F.Tier1), "fans/s");
            AssertClose(7 * 1.25, g.Of(rates, g.F.Shared, g.F.Root), "shared/s");
        }

        // The single-bar fill speed is the planned entry's: the demand the
        // tick resolves for a drawing bar carries the same number.
        [Test]
        public void The_single_bar_fill_rate_is_the_rate_the_demand_carries()
        {
            var g = new Gigs(fillRate: 2, rateFactor: 1.5);
            g.F.Select(g.F.Tier1, g.Calendar, g.Gig);

            var demand = g.F.Resolve();

            Assert.AreEqual(1, demand.bars.Count);
            AssertClose(demand.bars[0].rate.ToDouble(), BarSystem.FillRate(g.Ctx, g.Gig));
        }

        // ---- the tick report ----

        // For a running bar that does not stall, the slope over one tick is
        // the bar's completion rate at the same game speed.
        [Test]
        public void The_reports_completion_slope_equals_the_completion_rate()
        {
            var g = new Gigs(fillAmount: 6, fillRate: 2, yieldFactor: 2, rateFactor: 1.5, gameSpeed: 2);
            g.Own(3);
            g.F.Select(g.F.Tier1, g.Calendar, g.Gig);
            var config = Config();

            var speed = TickSystem.GameSpeed(new GameContext(g.F.Ch1, g.F.Now), g.F.Ch1, config);
            Assert.AreEqual(2, speed, 1e-12, "the haste the fixture authored");
            var rates = BarSystem.CompletionRate(g.Ctx, g.Gig, speed);

            var report = TickSystem.Tick(g.F.Root, g.F.Ch1, config, 0.75, g.F.Now.AddSeconds(0.75));

            AssertClose(g.Of(rates, g.F.Rehearsal, g.F.Tier1).ToDouble(),
                report.CompletionSlope(g.F.Tier1, g.F.Rehearsal.Id), "rehearsal");
            AssertClose(g.Of(rates, g.F.Fans, g.F.Tier1).ToDouble(),
                report.CompletionSlope(g.F.Tier1, g.F.Fans.Id), "fans");
            AssertClose(g.Of(rates, g.F.Shared, g.F.Root).ToDouble(),
                report.CompletionSlope(g.F.Root, g.F.Shared.Id), "shared");
            AssertClose(0.75 * g.Of(rates, g.F.Shared, g.F.Root).ToDouble(),
                report.CompletionIncome(g.F.Root, g.F.Shared.Id), "the income is the slope times the seconds");
        }

        // A bar the calendar does not hold draws nothing, so it records
        // nothing - while its own rate still answers what it would earn.
        [Test]
        public void A_bar_not_selected_records_no_completion_income()
        {
            var g = new Gigs();
            g.Own(1);
            g.F.Select(g.F.Tier1, g.Calendar, g.Gig);

            var report = TickSystem.Tick(g.F.Root, g.F.Ch1, Config(), 1, g.F.Now.AddSeconds(1));

            AssertClose(0, report.CompletionSlope(g.F.Tier1, g.Buzz.Id), "idle_gig is not running");
            AssertClose(0.5, g.Of(BarSystem.CompletionRate(g.Ctx, g.Idle, 1), g.Buzz, g.F.Tier1),
                "5 buzz per 10 s, answered whether or not it draws");
            Assert.Greater(report.CompletionSlope(g.F.Tier1, g.F.Rehearsal.Id).ToDouble(), 0,
                "the selected gig records");
        }

        // A completion's lump lands in the balance and in the completion
        // ledger, never in the deposit slope the readout interpolates by.
        [Test]
        public void A_lump_payout_leaves_the_deposit_slope_untouched()
        {
            var g = new Gigs();
            g.Own(2);
            g.F.Select(g.F.Tier1, g.Calendar, g.Gig);
            g.F.Tier1.barProgress[g.Gig.Id] = 4.5;

            var report = TickSystem.Tick(g.F.Root, g.F.Ch1, Config(), 1, g.F.Now.AddSeconds(1));

            AssertClose(6, g.F.Balance(g.F.Tier1, g.F.Rehearsal), "the completion paid 3 x 2");
            AssertClose(0, report.DepositNet(g.F.Tier1, g.F.Rehearsal.Id), "no rate deposit");
            AssertClose(0, report.DepositSlope(g.F.Tier1, g.F.Rehearsal.Id), "and no deposit slope");
            AssertClose(6.0 / 5, report.CompletionSlope(g.F.Tier1, g.F.Rehearsal.Id),
                "one unit of a five-unit cycle, worth a fifth of the lump");
        }
    }
}
