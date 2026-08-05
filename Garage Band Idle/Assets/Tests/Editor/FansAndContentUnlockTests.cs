using System.Collections.Generic;
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

        private static GameEffect SetFlag(string flagId) => new SetFlagEffect(flagId);

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
                    new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1)),
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
            var fans = TestContent.MakeFanProduction(modifiers, generators, currencies,
                new ConditionContext(currencies, generators, flags), new FlagSetCondition("fans"));

            fans.Tick(10);
            Assert.AreEqual(0.0, currencies.Get("fans").ToDouble(), 1e-9, "no accrual before the flag");
            Assert.AreEqual(0.0, fans.RatePerSecond("fans").ToDouble(), 1e-9);

            flags.Set("fans");

            Assert.AreEqual(0.2, fans.RatePerSecond("fans").ToDouble(), 1e-9, "base rate once active");
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
            var fans = TestContent.MakeFanProduction(modifiers, generators, currencies,
                new ConditionContext(currencies, generators, flags), new FlagSetCondition("fans"));
            var context = new EffectContext(currencies, flags, modifiers);

            TestContent.MakeFanRateReward("boost_a", 1.15).Apply(context, ContentScope.Run);
            TestContent.MakeFanRateReward("boost_b", 1.15).Apply(context, ContentScope.Run);

            Assert.AreEqual(0.2 * 1.15 * 1.15, fans.RatePerSecond("fans").ToDouble(), 1e-9);
        }

        // Modifiers carry their scope, and the scope comes from whoever APPLIES the
        // reward, not from the reward: both upgrades here carry the same kind of
        // payload and differ only in the lifetime the applying content declared,
        // which is the whole point of keeping scope off the shared asset.
        //
        // The release is not a filter over the store (design doc section 12, rule
        // 6). It resets the FACTS - here the purchase latches - and re-projects,
        // so the run-scoped multiplier is absent because the latch that produced
        // it is absent, and the permanent one is present because its latch is.
        [Test]
        public void FanRateMultipliers_RunResetKeepsPermanentInChapter()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            flags.Set("fans");
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies, modifiers);
            var fans = TestContent.MakeFanProduction(modifiers, generators, currencies,
                new ConditionContext(currencies, generators, flags), new FlagSetCondition("fans"));
            var upgrades = new UpgradeSystem(new[]
            {
                // no gate = met from the start, so both apply on the first pass
                TestContent.MakeUpgrade("run_boost", UpgradeType.ContentUnlock, ContentScope.Run, null,
                    new GrantModifierEffect(ModifierTarget.FanRate, ModifierOperation.Multiply, 1.5)),
                TestContent.MakeUpgrade("permanent_boost", UpgradeType.ContentUnlock,
                    ContentScope.PermanentInChapter, null,
                    new GrantModifierEffect(ModifierTarget.FanRate, ModifierOperation.Multiply, 2.0)),
            }, currencies, flags, modifiers);

            upgrades.EvaluateContentUnlocks(TestContent.MakeContext(currencies, flags: flags));
            Assert.AreEqual(0.2 * 1.5 * 2.0, fans.RatePerSecond("fans").ToDouble(), 1e-9, "both scopes stack");

            TestContent.RunReset(modifiers, upgrades);

            Assert.AreEqual(0.2 * 2.0, fans.RatePerSecond("fans").ToDouble(), 1e-9,
                "the run latch cleared so its multiplier was not re-projected; the permanent latch survived and was");
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
            var fans = TestContent.MakeFanProduction(modifiers, generators, currencies,
                new ConditionContext(currencies, generators, flags), new FlagSetCondition("fans"));

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'FanRate' with a non-positive Multiply value '0'. Ignoring - it would zero or negate the whole product.");
            modifiers.Grant(FanRate, ModifierOperation.Multiply, ContentScope.Run, 0);

            Assert.AreEqual(0.2, fans.RatePerSecond("fans").ToDouble(), 1e-9, "the rate is untouched");
        }

        // tap-value payloads target TapValue and stack per scope, mirroring
        // fan-rate ones: after the release resets the run latch and re-projects,
        // only the permanent stack is rebuilt
        [Test]
        public void TapValueRewards_StackPerScope_AndRunResetKeepsPermanent()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var tap = TestContent.MakeTapProduction(2, modifiers, currencies, flags);
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("run_x2", UpgradeType.ContentUnlock, ContentScope.Run, null,
                    new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Multiply, 2.0)),
                TestContent.MakeUpgrade("perm_x3", UpgradeType.ContentUnlock,
                    ContentScope.PermanentInChapter, null,
                    new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Multiply, 3.0)),
            }, currencies, flags, modifiers);

            upgrades.EvaluateContentUnlocks(TestContent.MakeContext(currencies, flags: flags));
            Assert.AreEqual(12.0, tap.TapValue.ToDouble(), 1e-9, "base 2 x run 2 x permanent 3");

            TestContent.RunReset(modifiers, upgrades);
            Assert.AreEqual(6.0, tap.TapValue.ToDouble(), 1e-9,
                "only the permanent latch was left to re-project");
        }

        // fail closed on broken content: a negative amount (invalid data - boot
        // validation reports it) must never drain cash on a tap, and no
        // multiplier can resurrect it
        [Test]
        public void TapValue_FailsClosedOnANegativeBase()
        {
            var modifiers = new ModifierSystem();
            var tap = TestContent.MakeTapProduction(-5, modifiers);
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(0.0, tap.TapValue.ToDouble(), 1e-9, "never a draining tap");
        }

        // fail closed on broken content: a non-positive factor (invalid data -
        // boot validation reports it) must never apply
        [Test]
        public void TapValueMultiplier_FailsClosedOnANonPositiveFactor()
        {
            var modifiers = new ModifierSystem();
            var tap = TestContent.MakeTapProduction(2, modifiers);

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'TapValue' with a non-positive Multiply value '0'. Ignoring - it would zero or negate the whole product.");
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 0);

            Assert.AreEqual(2.0, tap.TapValue.ToDouble(), 1e-9, "the value is untouched");
        }

        // Publishing is post-mutation: nothing notifies the UI from inside an
        // operation - modifier grants and gate flips stay silent until the
        // orchestrator's RefreshTapValue says the whole mutation has settled -
        // and the refresh publishes only an actual move (one notification for
        // the operation, none for a no-op, a rejected value, or somebody
        // else's target).
        [Test]
        public void TapValueChanged_FiresOnRefresh_OnlyWhenTheValueMoved()
        {
            var modifiers = new ModifierSystem();
            var tap = TestContent.MakeTapProduction(2, modifiers);
            var changes = 0;
            tap.TapValueChanged += () => changes++;

            tap.RefreshTapValue();
            Assert.AreEqual(0, changes, "nothing moved, nothing published");

            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 2);
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.PermanentInChapter, 3);
            Assert.AreEqual(0, changes, "mid-mutation grants never notify the UI directly");

            tap.RefreshTapValue();
            Assert.AreEqual(1, changes, "one settled operation, one notification");
            Assert.AreEqual(12.0, tap.TapValue.ToDouble(), 1e-9, "base 2 x 2 x 3");

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'TapValue' with a non-positive Multiply value '0'. Ignoring - it would zero or negate the whole product.");
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 0);
            modifiers.Grant(FanRate, ModifierOperation.Multiply, ContentScope.Run, 5);
            tap.RefreshTapValue();
            Assert.AreEqual(1, changes, "a rejected value and another target's modifier move nothing");

            modifiers.ResetGranted();
            tap.RefreshTapValue();
            Assert.AreEqual(2, changes, "rebuilding the store moved the value");
            Assert.AreEqual(2.0, tap.TapValue.ToDouble(), 1e-9,
                "base 2: an emptied store composes to identity until the projection re-runs");
        }

        // A composing config may carry any gate the data model supports (rule
        // 13 forbids nothing): the tap pays it only while its gate holds, the
        // evaluated value follows the gate immediately, and the post-mutation
        // refresh is what tells the UI - so payout and display can never
        // diverge across a gate transition.
        [Test]
        public void GatedComposingConfig_PaysAndPublishesOnItsGateTransition()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var producer = TestContent.MakeProducer("jam", new List<ProductionConfig>
            {
                new("cash", 1, ProductionTrigger.Tap, null, ModifierTarget.TapValue),
                new("cash", 4, ProductionTrigger.Tap, new FlagSetCondition("amped"), ModifierTarget.TapValue),
            });
            var production = new ProductionSystem(new[] { producer }, currencies, modifiers,
                TestContent.MakeContext(currencies, flags: flags));
            var changes = 0;
            production.TapValueChanged += () => changes++;

            Assert.AreEqual(1.0, production.TapValue.ToDouble(), 1e-9, "the gated yield is dormant");
            production.FireTap();
            Assert.AreEqual(1.0, currencies.Get("cash").ToDouble(), 1e-9, "a tap pays only the open config");

            flags.Set("amped");
            Assert.AreEqual(5.0, production.TapValue.ToDouble(), 1e-9, "the evaluated value follows the gate");
            Assert.AreEqual(0, changes, "no notification until the mutation settles");

            production.RefreshTapValue();
            Assert.AreEqual(1, changes, "the settled refresh publishes the gate transition");

            production.FireTap();
            Assert.AreEqual(6.0, currencies.Get("cash").ToDouble(), 1e-9,
                "payout matches the advertised value: 1 + (1 + 4)");
        }

        // flat adds land before the multipliers, so a tap add is worth more once
        // a multiplier is in play - one composition rule, stated once
        [Test]
        public void TapValue_AddsComposeBeforeMultipliers()
        {
            var modifiers = new ModifierSystem();
            var tap = TestContent.MakeTapProduction(2, modifiers);

            modifiers.Grant(TapValue, ModifierOperation.Add, ContentScope.Run, 1);
            Assert.AreEqual(3.0, tap.TapValue.ToDouble(), 1e-9, "base 2 + 1");

            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 2);
            Assert.AreEqual(6.0, tap.TapValue.ToDouble(), 1e-9, "(2 + 1) x 2, never 2 + (1 x 2)");
        }

        [Test]
        public void SetFlagReward_LatchesTheFlag()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var context = new EffectContext(currencies, flags, new ModifierSystem());

            // a flag is permanent within its chapter by definition, so the scope the
            // applier passes is not consulted - the latch is not a scoped modifier
            TestContent.MakeSetFlagReward("open_backroom", "backroom").Apply(context, ContentScope.Run);

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
            var fans = TestContent.MakeFanProduction(modifiers, generators, currencies,
                new ConditionContext(currencies, generators, flags), new FlagSetCondition("fans"));
            var rewards = new Content.RewardManager(new Content.RewardDefinition[]
            {
                TestContent.MakeFanRateReward("fan_rate_x1_15", 1.15),
                TestContent.MakeSetFlagReward("open_backroom", "backroom"),
            });
            var context = new EffectContext(currencies, flags, modifiers);

            Assert.IsTrue(rewards.Contains("fan_rate_x1_15"));
            Assert.IsFalse(rewards.Contains("nope"));

            rewards.Apply("fan_rate_x1_15", context, ContentScope.Run);
            Assert.AreEqual(0.2 * 1.15, fans.RatePerSecond("fans").ToDouble(), 1e-9, "pool reward applied by id");

            rewards.Apply("open_backroom", context, ContentScope.Run);
            Assert.IsTrue(flags.IsSet("backroom"), "setFlag rewards run through the same registry");
        }

        // The identity 5.7 rests on: the composed fan rate is
        // (baseFansPerSec + perBandmate x bandmates) x rewards, which is exactly
        // what FanSystem computed by hand before it was deleted. Chapter 1's
        // observable number - 0.22/s with one Drummer - is the anchor, and the
        // multiplier leg proves the reward scales the COMBINED base-plus-derived
        // value rather than the base alone. Separate tests cover each term; this
        // one is the composition, because that is the claim "no gameplay change"
        // actually makes.
        [Test]
        public void FanRate_ComposesBasePlusBandmateAdd_ThenRewardMultipliers()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            flags.Set("fans");
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(new[]
            {
                TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3, isBandmate: true),
            }, currencies, modifiers);
            var fans = TestContent.MakeFanProduction(modifiers, generators, currencies,
                new ConditionContext(currencies, generators, flags), new FlagSetCondition("fans"));

            TestContent.BuyTimes(generators.Get("drummer"), currencies, 1);
            Assert.AreEqual(0.22, fans.RatePerSecond("fans").ToDouble(), 1e-9,
                "0.2 base + 0.02 x 1 bandmate - the rate Chapter 1 shows after the first Drummer");

            var effects = new EffectContext(currencies, flags, modifiers);
            TestContent.MakeFanRateReward("boost", 1.15).Apply(effects, ContentScope.Run);

            Assert.AreEqual(0.22 * 1.15, fans.RatePerSecond("fans").ToDouble(), 1e-9,
                "the multiplier scales base + bandmate add together, never the base alone");

            // and it accrues at exactly that rate, so the composition is what the
            // player is actually paid - not just what a readout advertises
            fans.Tick(10);
            Assert.AreEqual(0.22 * 1.15 * 10, currencies.Get("fans").ToDouble(), 1e-9);
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
            var fans = TestContent.MakeFanProduction(modifiers, generators, currencies,
                new ConditionContext(currencies, generators, flags), new FlagSetCondition("fans"));

            TestContent.BuyTimes(generators.Get("drummer"), currencies, 2);
            TestContent.BuyTimes(generators.Get("bassist"), currencies, 1);
            Assert.AreEqual(0.06, modifiers.For(FanRate).Add.ToDouble(), 1e-9,
                "3 bandmates x 0.02, contributed as a derived Add on the fan rate");
            Assert.AreEqual(0.26, fans.RatePerSecond("fans").ToDouble(), 1e-9, "0.2 + 0.02 x 3 bandmates");

            // gear must not move the rate
            TestContent.BuyTimes(generators.Get("practice_amp"), currencies, 5);
            Assert.AreEqual(0.26, fans.RatePerSecond("fans").ToDouble(), 1e-9, "amps never change fan rate");

            // neither must Cash itself - fan rate is band size and time only
            currencies.Add("cash", 1e9);
            Assert.AreEqual(0.26, fans.RatePerSecond("fans").ToDouble(), 1e-9, "cash never changes fan rate");
        }
    }
}
