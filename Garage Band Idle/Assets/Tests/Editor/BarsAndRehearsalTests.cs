using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The fillable-bar system and rehearsal accrual. The load-bearing claims:
    // bars are independent per-bar progress (never cumulative thresholds on one
    // counter), the continuous drain is clamped and player-directed, completion
    // applies the pool reward exactly once, and barsCompleted conditions read
    // live counts. Rehearsal accrues from tick + taps only after its flag.
    public class BarsAndRehearsalTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private static CurrencyManager MakeEconomyWithRehearsal()
        {
            var groups = new[] { TestContent.MakeGroup("run", true) };
            var currencies = new[]
            {
                TestContent.MakeCurrency("cash", "run"),
                TestContent.MakeCurrency("fans", "run"),
                TestContent.MakeCurrency("rehearsal", "run"),
            };
            return new CurrencyManager(groups, currencies);
        }

        private static BarSystem MakeCoversSetup(CurrencyManager currencies, FlagSystem flags,
            out FanSystem fans)
        {
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies);
            fans = new FanSystem(new FansConfig("fans", "fans", 0.2, 0.02), currencies, generators, flags);
            var rewards = new RewardManager(new RewardDefinition[]
            {
                TestContent.MakeFanRateReward("fan_rate_x1_15", 1.15),
                TestContent.MakeFanRateReward("fan_rate_x1_20", 1.2),
            });

            var bars = new[]
            {
                TestContent.MakeBar("cover_1", "rehearsal", 120, "fan_rate_x1_15"),
                TestContent.MakeBar("cover_2", "rehearsal", 300, "fan_rate_x1_15"),
                TestContent.MakeBar("cover_3", "rehearsal", 600, "fan_rate_x1_20"),
            };
            var group = TestContent.MakeBarGroup("learn_covers", "covers",
                new List<string> { "cover_1", "cover_2", "cover_3" });

            return new BarSystem(new[] { group }, bars, currencies, rewards,
                new RewardContext(currencies, flags, fans));
        }

        [Test]
        public void RehearsalAccrual_IsDormantUntilTheFlag()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var rehearsal = new RehearsalSystem(new RehearsalConfig("rehearsal", "covers", 1, 2),
                currencies, flags);

            rehearsal.Tick(10);
            rehearsal.OnJamTap();
            Assert.AreEqual(0.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "no accrual before the flag");

            flags.Set("covers");

            rehearsal.Tick(10);
            Assert.AreEqual(10.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "perSec × seconds");
            rehearsal.OnJamTap();
            Assert.AreEqual(12.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "+perTap on a Jam tap");
        }

        [Test]
        public void UnconfiguredRehearsal_StaysInert()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("covers");
            var rehearsal = new RehearsalSystem(new RehearsalConfig(null, null, 1, 2), currencies, flags);

            Assert.IsFalse(rehearsal.Configured);
            rehearsal.Tick(10);
            rehearsal.OnJamTap();
            Assert.AreEqual(0.0, currencies.Get("rehearsal").ToDouble(), 1e-9);
        }

        [Test]
        public void Pool_AccumulatesUntilABarIsSelected_ThenDrainsClamped()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);

            currencies.Add("rehearsal", 200);
            bars.Tick();
            Assert.AreEqual(200.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "no target = pool holds");
            Assert.AreEqual(0, bars.CompletedCount("learn_covers"));

            // selecting pours the built-up pool in immediately, clamped to the
            // bar's requirement; the excess stays in the pool
            bars.SetActiveBar("learn_covers", "cover_1");

            var cover1 = bars.GetBars("learn_covers")[0];
            Assert.IsTrue(cover1.Completed, "120 requirement filled from a 200 pool");
            Assert.AreEqual(120.0, cover1.Progress.ToDouble(), 1e-9);
            Assert.AreEqual(80.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "overfill never spends");
        }

        [Test]
        public void Bars_TrackTheirOwnProgress_Independently()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);

            // pour 50 into cover_2, then redirect to cover_3 and pour 70
            currencies.Add("rehearsal", 50);
            bars.SetActiveBar("learn_covers", "cover_2");
            currencies.Add("rehearsal", 70);
            bars.SetActiveBar("learn_covers", "cover_3");

            var list = bars.GetBars("learn_covers");
            Assert.AreEqual(0.0, list[0].Progress.ToDouble(), 1e-9, "unselected bar untouched");
            Assert.AreEqual(50.0, list[1].Progress.ToDouble(), 1e-9, "progress stays when deselected");
            Assert.AreEqual(70.0, list[2].Progress.ToDouble(), 1e-9, "independent accumulation");
            Assert.AreEqual(0, bars.CompletedCount("learn_covers"), "independent bars, not cumulative thresholds");
        }

        [Test]
        public void Completion_AppliesThePoolRewardOnce_AndClearsSelection()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("fans");
            var bars = MakeCoversSetup(currencies, flags, out var fans);
            var completions = 0;
            bars.BarCompleted += _ => completions++;

            bars.SetActiveBar("learn_covers", "cover_1");
            currencies.Add("rehearsal", 120);
            bars.Tick();

            Assert.AreEqual(1, completions);
            Assert.AreEqual(0.2 * 1.15, fans.RatePerSecond.ToDouble(), 1e-9, "fan-rate reward applied on completion");
            Assert.IsNull(bars.GetActiveBar("learn_covers"), "completion clears the target");

            // further ticks and reselection attempts must not re-apply
            currencies.Add("rehearsal", 500);
            bars.Tick();
            bars.SetActiveBar("learn_covers", "cover_1");
            bars.Tick();
            Assert.AreEqual(1, completions, "a completed bar never re-completes");
            Assert.IsNull(bars.GetActiveBar("learn_covers"), "a completed bar cannot be re-selected");
            Assert.AreEqual(0.2 * 1.15, fans.RatePerSecond.ToDouble(), 1e-9);
        }

        [Test]
        public void BarRewards_StackMultiplicativelyOnFanRate()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("fans");
            var bars = MakeCoversSetup(currencies, flags, out var fans);

            currencies.Add("rehearsal", 420);
            bars.SetActiveBar("learn_covers", "cover_1");
            bars.SetActiveBar("learn_covers", "cover_2");

            Assert.AreEqual(2, bars.CompletedCount("learn_covers"));
            Assert.AreEqual(0.2 * 1.15 * 1.15, fans.RatePerSecond.ToDouble(), 1e-9);
        }

        [Test]
        public void BarsCompletedCondition_ReadsLiveCounts()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);
            var context = new ConditionContext(currencies, null, flags, "records", null, bars);
            var condition = new BarsCompletedCondition("learn_covers", 1);

            Assert.IsFalse(ConditionEvaluator.IsMet(condition, context));

            currencies.Add("rehearsal", 120);
            bars.SetActiveBar("learn_covers", "cover_1");

            Assert.IsTrue(ConditionEvaluator.IsMet(condition, context), "cover_1 satisfies barsCompleted >= 1");
        }

        [Test]
        public void TogglingTheActiveBar_Deselects()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);

            bars.SetActiveBar("learn_covers", "cover_2");
            Assert.IsNotNull(bars.GetActiveBar("learn_covers"));

            bars.SetActiveBar("learn_covers", null);
            Assert.IsNull(bars.GetActiveBar("learn_covers"));

            currencies.Add("rehearsal", 60);
            bars.Tick();
            Assert.AreEqual(60.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "deselected = pool accumulates");
        }
    }
}
