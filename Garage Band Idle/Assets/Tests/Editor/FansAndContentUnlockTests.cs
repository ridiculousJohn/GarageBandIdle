using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Slice 3: the content-unlock mechanism and fan accrual. The load-bearing
    // claims: a contentUnlock applies exactly when its gate is met, and the fan
    // rate is a function of band size and time only — provably never Cash.
    public class FansAndContentUnlockTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private static GateCondition OwnedCount(string generatorId, double amount)
            => new(GateCondition.TypeOwnedCount, "", generatorId, "", amount);

        private static UpgradePayload UnlockSystem(string systemId)
            => new(UpgradePayload.EffectUnlockSystem, 0, "", systemId);

        [Test]
        public void ContentUnlock_AppliesWhenGateMet_AndSetsTheFlag()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var generators = new GeneratorSystem(
                new[] { TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3, isBandmate: true) },
                currencies, flags);
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("play_for_crowd", UpgradeDefinition.TypeContentUnlock,
                    UpgradeDefinition.ScopePermanentInChapter,
                    new List<GateCondition> { OwnedCount("drummer", 1) }, UnlockSystem("fans")),
            }, currencies, generators, flags);

            upgrades.EvaluateContentUnlocks();
            Assert.IsFalse(flags.IsSet("fans"), "no flag before the gate is met");
            Assert.IsFalse(upgrades.Get("play_for_crowd").Applied);

            TestContent.BuyTimes(generators.Get("drummer"), currencies, 1);
            upgrades.EvaluateContentUnlocks();

            Assert.IsTrue(flags.IsSet("fans"), "owning 1 drummer sets the fans flag");
            Assert.IsTrue(upgrades.Get("play_for_crowd").Applied);
        }

        [Test]
        public void ContentUnlock_FiresUpgradeAppliedOnce()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                // empty gate = met from the start
                TestContent.MakeUpgrade("auto", UpgradeDefinition.TypeContentUnlock,
                    UpgradeDefinition.ScopePermanentInChapter, null, UnlockSystem("fans")),
            }, currencies, null, flags);
            var appliedCount = 0;
            upgrades.UpgradeApplied += _ => appliedCount++;

            upgrades.EvaluateContentUnlocks();
            upgrades.EvaluateContentUnlocks();

            Assert.AreEqual(1, appliedCount, "an applied unlock never re-applies");
        }

        [Test]
        public void BuffUpgrades_AreNeverAutoApplied()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                // met gate (empty), but buffs wait for the purchase flow (slice 5)
                TestContent.MakeUpgrade("stage_presence", UpgradeDefinition.TypeBuff,
                    UpgradeDefinition.ScopeRun, null,
                    new UpgradePayload(UpgradePayload.EffectTapValueAdd, 1, "", "")),
            }, currencies, null, flags);

            upgrades.EvaluateContentUnlocks();

            Assert.IsFalse(upgrades.Get("stage_presence").Applied);
        }

        [Test]
        public void FanAccrual_IsDormantUntilTheFlag()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var generators = new GeneratorSystem(
                new[] { TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3, isBandmate: true) },
                currencies, flags);
            var fans = new FanSystem(new FansConfig(0.2, 0.02), "fans", "fans", currencies, generators, flags);

            fans.Tick(10);
            Assert.AreEqual(0.0, currencies.Get("fans").ToDouble(), 1e-9, "no accrual before the flag");
            Assert.AreEqual(0.0, fans.RatePerSecond.ToDouble(), 1e-9);

            flags.Set("fans");

            Assert.AreEqual(0.2, fans.RatePerSecond.ToDouble(), 1e-9, "base rate once active");
            fans.Tick(10);
            Assert.AreEqual(2.0, currencies.Get("fans").ToDouble(), 1e-9, "rate × seconds");
        }

        [Test]
        public void FanRateRewards_StackMultiplicatively()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            flags.Set("fans");
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies, flags);
            var fans = new FanSystem(new FansConfig(0.2, 0.02), "fans", "fans", currencies, generators, flags);
            var context = new Content.RewardContext(currencies, flags, fans);

            TestContent.MakeFanRateReward("boost_a", 1.15).Apply(context);
            TestContent.MakeFanRateReward("boost_b", 1.15).Apply(context);

            Assert.AreEqual(0.2 * 1.15 * 1.15, fans.RatePerSecond.ToDouble(), 1e-9);
        }

        [Test]
        public void SetFlagReward_LatchesTheFlag()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var context = new Content.RewardContext(currencies, flags, null);

            TestContent.MakeSetFlagReward("open_backroom", "backroom").Apply(context);

            Assert.IsTrue(flags.IsSet("backroom"));
        }

        [Test]
        public void FanRate_ScalesWithBandmates_NeverWithGearOrCash()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            flags.Set("fans");
            var generators = new GeneratorSystem(new[]
            {
                TestContent.MakeGenerator("practice_amp", "cash", 60, 1.15, 0.4), // gear
                TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3, isBandmate: true),
                TestContent.MakeGenerator("bassist", "cash", 4000, 1.15, 20, isBandmate: true),
            }, currencies, flags);
            var fans = new FanSystem(new FansConfig(0.2, 0.02), "fans", "fans", currencies, generators, flags);

            TestContent.BuyTimes(generators.Get("drummer"), currencies, 2);
            TestContent.BuyTimes(generators.Get("bassist"), currencies, 1);
            Assert.AreEqual(3, fans.BandmateCount);
            Assert.AreEqual(0.26, fans.RatePerSecond.ToDouble(), 1e-9, "0.2 + 0.02 × 3 bandmates");

            // gear must not move the rate
            TestContent.BuyTimes(generators.Get("practice_amp"), currencies, 5);
            Assert.AreEqual(0.26, fans.RatePerSecond.ToDouble(), 1e-9, "amps never change fan rate");

            // neither must Cash itself — fan rate is band size and time only
            currencies.Add("cash", 1e9);
            Assert.AreEqual(0.26, fans.RatePerSecond.ToDouble(), 1e-9, "cash never changes fan rate");
        }
    }
}
