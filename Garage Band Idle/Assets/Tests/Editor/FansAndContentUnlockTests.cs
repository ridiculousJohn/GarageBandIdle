using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The content-unlock mechanism and fan accrual. The load-bearing claims: a
    // contentUnlock applies exactly when its gate is met (latching its flag in
    // the single reveal registry), and the fan rate is a function of band size
    // and time only - provably never Cash.
    public class FansAndContentUnlockTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private static UpgradePayload SetFlag(string flagId) => new SetFlagPayload(flagId);

        private static readonly ModifierTargetKey FanRate = ModifierTargetKey.Global(ModifierTarget.FanRate);
        private static readonly ModifierTargetKey TapValue = ModifierTargetKey.Global(ModifierTarget.TapValue);

        [Test]
        public void ContentUnlock_AppliesWhenGateMet_AndSetsTheFlag()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(
                new[] { TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3, isBandmate: true) },
                currencies, modifiers);
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("play_for_crowd", UpgradeType.ContentUnlock,
                    ContentScope.PermanentInChapter,
                    new OwnedCountCondition("drummer", 1), SetFlag("fans")),
            }, currencies, flags, modifiers);
            var context = TestContent.MakeContext(currencies, generators, flags);

            upgrades.EvaluateContentUnlocks(context);
            Assert.IsFalse(flags.IsSet("fans"), "no flag before the gate is met");
            Assert.IsFalse(upgrades.Get("play_for_crowd").Applied);

            TestContent.BuyTimes(generators.Get("drummer"), currencies, 1);
            upgrades.EvaluateContentUnlocks(context);

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
                // no gate = met from the start
                TestContent.MakeUpgrade("auto", UpgradeType.ContentUnlock,
                    ContentScope.PermanentInChapter, null, SetFlag("fans")),
            }, currencies, flags, new ModifierSystem());
            var context = TestContent.MakeContext(currencies, flags: flags);
            var appliedCount = 0;
            upgrades.UpgradeApplied += _ => appliedCount++;

            upgrades.EvaluateContentUnlocks(context);
            upgrades.EvaluateContentUnlocks(context);

            Assert.AreEqual(1, appliedCount, "an applied unlock never re-applies");
        }

        [Test]
        public void BuffUpgrades_AreNeverAutoApplied()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                // met gate (none), but buffs wait for the purchase flow (buff slice)
                TestContent.MakeUpgrade("stage_presence", UpgradeType.Buff,
                    ContentScope.Run, null,
                    new TapValueAddPayload(1)),
            }, currencies, flags, new ModifierSystem());
            var context = TestContent.MakeContext(currencies, flags: flags);

            upgrades.EvaluateContentUnlocks(context);

            Assert.IsFalse(upgrades.Get("stage_presence").Applied);
        }

        [Test]
        public void FanAccrual_IsDormantUntilTheFlag()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(
                new[] { TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3, isBandmate: true) },
                currencies, modifiers);
            var fans = new FanSystem(new FansConfig("fans", "fans", 0.2, 0.02), currencies, generators, flags, modifiers);

            fans.Tick(10);
            Assert.AreEqual(0.0, currencies.Get("fans").ToDouble(), 1e-9, "no accrual before the flag");
            Assert.AreEqual(0.0, fans.RatePerSecond.ToDouble(), 1e-9);

            flags.Set("fans");

            Assert.AreEqual(0.2, fans.RatePerSecond.ToDouble(), 1e-9, "base rate once active");
            fans.Tick(10);
            Assert.AreEqual(2.0, currencies.Get("fans").ToDouble(), 1e-9, "rate x seconds");
        }

        [Test]
        public void FanRateRewards_StackMultiplicatively()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            flags.Set("fans");
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies, modifiers);
            var fans = new FanSystem(new FansConfig("fans", "fans", 0.2, 0.02), currencies, generators, flags, modifiers);
            var context = new Content.RewardContext(currencies, flags, modifiers);

            TestContent.MakeFanRateReward("boost_a", 1.15).Apply(context);
            TestContent.MakeFanRateReward("boost_b", 1.15).Apply(context);

            Assert.AreEqual(0.2 * 1.15 * 1.15, fans.RatePerSecond.ToDouble(), 1e-9);
        }

        // modifiers carry their scope: the run reset (album release, event
        // baseline) drops only run-scoped rewards - a permanent-in-chapter
        // reward must survive it
        [Test]
        public void FanRateMultipliers_RunResetKeepsPermanentInChapter()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            flags.Set("fans");
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies, modifiers);
            var fans = new FanSystem(new FansConfig("fans", "fans", 0.2, 0.02), currencies, generators, flags, modifiers);
            var context = new Content.RewardContext(currencies, flags, modifiers);

            TestContent.MakeFanRateReward("run_boost", 1.5, ContentScope.Run).Apply(context);
            TestContent.MakeFanRateReward("permanent_boost", 2.0, ContentScope.PermanentInChapter).Apply(context);
            Assert.AreEqual(0.2 * 1.5 * 2.0, fans.RatePerSecond.ToDouble(), 1e-9, "both scopes stack");

            modifiers.ResetRunScoped();

            Assert.AreEqual(0.2 * 2.0, fans.RatePerSecond.ToDouble(), 1e-9, "run reset keeps the permanent stack");
        }

        // fail closed on broken content: a non-positive factor (invalid data -
        // boot validation reports it) would zero or negate the whole product
        // for the rest of the run and must never apply
        [Test]
        public void FanRateMultiplier_FailsClosedOnANonPositiveFactor()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            flags.Set("fans");
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies, modifiers);
            var fans = new FanSystem(new FansConfig("fans", "fans", 0.2, 0.02), currencies, generators, flags, modifiers);

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'FanRate' with a non-positive Multiply value '0'. Ignoring - it would zero or negate the whole product.");
            modifiers.Grant(FanRate, ModifierOperation.Multiply, ContentScope.Run, 0);

            Assert.AreEqual(0.2, fans.RatePerSecond.ToDouble(), 1e-9, "the rate is untouched");
        }

        // tap-value rewards target TapValue and stack per scope, mirroring
        // fan-rate rewards: the run reset keeps the permanent-in-chapter stack
        [Test]
        public void TapValueRewards_StackPerScope_AndRunResetKeepsPermanent()
        {
            var modifiers = new ModifierSystem();
            var tap = new TapSystem(2, modifiers);
            var context = new Content.RewardContext(TestContent.MakeEconomy(), new FlagSystem(), modifiers);

            TestContent.MakeTapValueReward("run_x2", 2.0, ContentScope.Run).Apply(context);
            TestContent.MakeTapValueReward("perm_x3", 3.0, ContentScope.PermanentInChapter).Apply(context);
            Assert.AreEqual(12.0, tap.Value.ToDouble(), 1e-9, "base 2 x run 2 x permanent 3");

            modifiers.ResetRunScoped();
            Assert.AreEqual(6.0, tap.Value.ToDouble(), 1e-9, "the run reset keeps the permanent stack");
        }

        // fail closed on broken content: a negative base (invalid data - boot
        // validation reports it) must never drain cash on a tap, and no
        // multiplier can resurrect it
        [Test]
        public void TapValue_FailsClosedOnANegativeBase()
        {
            var modifiers = new ModifierSystem();
            var tap = new TapSystem(-5, modifiers);
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(0.0, tap.Value.ToDouble(), 1e-9, "never a draining tap");
        }

        // fail closed on broken content: a non-positive factor (invalid data -
        // boot validation reports it) must never apply
        [Test]
        public void TapValueMultiplier_FailsClosedOnANonPositiveFactor()
        {
            var modifiers = new ModifierSystem();
            var tap = new TapSystem(2, modifiers);

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'TapValue' with a non-positive Multiply value '0'. Ignoring - it would zero or negate the whole product.");
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 0);

            Assert.AreEqual(2.0, tap.Value.ToDouble(), 1e-9, "the value is untouched");
        }

        // the UI advertises Tap.Value, so it needs a signal for every change to
        // the value - applied modifiers and a run reset that cleared something -
        // and no signal when nothing moved (rejected value, no-op reset, or a
        // modifier on somebody else's target)
        [Test]
        public void TapValueChanged_FiresOnlyWhenTheValueMoves()
        {
            var modifiers = new ModifierSystem();
            var tap = new TapSystem(2, modifiers);
            var changes = 0;
            tap.ValueChanged += () => changes++;

            modifiers.ResetRunScoped();
            Assert.AreEqual(0, changes, "a no-op reset is silent");

            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 2);
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.PermanentInChapter, 3);
            Assert.AreEqual(2, changes, "each applied modifier notifies");

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'TapValue' with a non-positive Multiply value '0'. Ignoring - it would zero or negate the whole product.");
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 0);
            Assert.AreEqual(2, changes, "a rejected value is silent");

            modifiers.Grant(FanRate, ModifierOperation.Multiply, ContentScope.Run, 5);
            Assert.AreEqual(2, changes, "another target's modifier is not ours");

            modifiers.ResetRunScoped();
            Assert.AreEqual(3, changes, "clearing the run stack notifies");
            Assert.AreEqual(6.0, tap.Value.ToDouble(), 1e-9, "base 2 x permanent 3 after the reset");
        }

        // flat adds land before the multipliers, so a tap add is worth more once
        // a multiplier is in play - one composition rule, stated once
        [Test]
        public void TapValue_AddsComposeBeforeMultipliers()
        {
            var modifiers = new ModifierSystem();
            var tap = new TapSystem(2, modifiers);

            modifiers.Grant(TapValue, ModifierOperation.Add, ContentScope.Run, 1);
            Assert.AreEqual(3.0, tap.Value.ToDouble(), 1e-9, "base 2 + 1");

            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 2);
            Assert.AreEqual(6.0, tap.Value.ToDouble(), 1e-9, "(2 + 1) x 2, never 2 + (1 x 2)");
        }

        [Test]
        public void SetFlagReward_LatchesTheFlag()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var context = new Content.RewardContext(currencies, flags, new ModifierSystem());

            TestContent.MakeSetFlagReward("open_backroom", "backroom").Apply(context);

            Assert.IsTrue(flags.IsSet("backroom"));
        }

        [Test]
        public void RewardManager_AppliesByIdFromThePool()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            flags.Set("fans");
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies, modifiers);
            var fans = new FanSystem(new FansConfig("fans", "fans", 0.2, 0.02), currencies, generators, flags, modifiers);
            var rewards = new Content.RewardManager(new Content.RewardDefinition[]
            {
                TestContent.MakeFanRateReward("fan_rate_x1_15", 1.15),
                TestContent.MakeSetFlagReward("open_backroom", "backroom"),
            });
            var context = new Content.RewardContext(currencies, flags, modifiers);

            Assert.IsTrue(rewards.Contains("fan_rate_x1_15"));
            Assert.IsFalse(rewards.Contains("nope"));

            rewards.Apply("fan_rate_x1_15", context);
            Assert.AreEqual(0.2 * 1.15, fans.RatePerSecond.ToDouble(), 1e-9, "pool reward applied by id");

            rewards.Apply("open_backroom", context);
            Assert.IsTrue(flags.IsSet("backroom"), "setFlag rewards run through the same registry");
        }

        [Test]
        public void FanRate_ScalesWithBandmates_NeverWithGearOrCash()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            flags.Set("fans");
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(new[]
            {
                TestContent.MakeGenerator("practice_amp", "cash", 60, 1.15, 0.4), // gear
                TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3, isBandmate: true),
                TestContent.MakeGenerator("bassist", "cash", 4000, 1.15, 20, isBandmate: true),
            }, currencies, modifiers);
            var fans = new FanSystem(new FansConfig("fans", "fans", 0.2, 0.02), currencies, generators, flags, modifiers);

            TestContent.BuyTimes(generators.Get("drummer"), currencies, 2);
            TestContent.BuyTimes(generators.Get("bassist"), currencies, 1);
            Assert.AreEqual(3, fans.BandmateCount);
            Assert.AreEqual(0.26, fans.RatePerSecond.ToDouble(), 1e-9, "0.2 + 0.02 x 3 bandmates");

            // gear must not move the rate
            TestContent.BuyTimes(generators.Get("practice_amp"), currencies, 5);
            Assert.AreEqual(0.26, fans.RatePerSecond.ToDouble(), 1e-9, "amps never change fan rate");

            // neither must Cash itself - fan rate is band size and time only
            currencies.Add("cash", 1e9);
            Assert.AreEqual(0.26, fans.RatePerSecond.ToDouble(), 1e-9, "cash never changes fan rate");
        }
    }
}
