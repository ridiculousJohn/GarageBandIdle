using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The buff payload handlers (design doc section 4). The load-bearing claims:
    // each payload grants a modifier and holds no state of its own, the effect
    // reaches exactly the target the payload names, and the lifetime comes from
    // the owning upgrade's scope rather than a second declaration.
    public class UpgradePayloadTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private static readonly ModifierTargetKey TapValue = ModifierTargetKey.Global(ModifierTarget.TapValue);

        private static EffectContext Context(ModifierSystem modifiers)
            => new(TestContent.MakeEconomy(), new FlagSystem(), modifiers);

        // a flat tap add lands before the multipliers, which is what makes
        // stage_presence worth more later in a run than at the start
        [Test]
        public void TapValueAdd_GrantsAnAddOnTapValue()
        {
            var modifiers = new ModifierSystem();
            var tap = TestContent.MakeTapProduction(1, modifiers);

            new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1).Apply(Context(modifiers), ContentScope.Run);

            Assert.AreEqual(2.0, tap.TapValue.ToDouble(), 1e-9, "base 1 + 1");

            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 3);
            Assert.AreEqual(6.0, tap.TapValue.ToDouble(), 1e-9, "(1 + 1) x 3");
        }

        [Test]
        public void GeneratorOutputMultiplier_ReachesOnlyTheNamedGenerator()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var system = new GeneratorSystem(new[]
            {
                TestContent.MakeGenerator("practice_amp", "cash", 60, 1.15, 0.4),
                TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3),
            }, currencies, modifiers);
            TestContent.BuyTimes(system.Get("practice_amp"), currencies, 1);
            TestContent.BuyTimes(system.Get("drummer"), currencies, 1);

            new GrantModifierEffect(ModifierTarget.GeneratorOutput, ModifierOperation.Multiply, 2, new List<string> { "practice_amp" }).Apply(Context(modifiers), ContentScope.Run);

            Assert.AreEqual(0.8, system.Get("practice_amp").ProductionPerSecond.ToDouble(), 1e-9, "0.4 x 2");
            Assert.AreEqual(3.0, system.Get("drummer").ProductionPerSecond.ToDouble(), 1e-9,
                "the drummer produces the same currency and is untouched");
        }

        // the payload names the currencies it multiplies, so a generator
        // producing anything else never inherits the income buff
        [Test]
        public void CurrencyPerSecMultiplier_GrantsOnEveryDeclaredCurrency_AndNoOther()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var cashGen = TestContent.MakeGenerator("cash_gen", "cash", 10, 1.15, 3);
            var fansGen = TestContent.MakeGenerator("fans_gen", "fans", 10, 1.15, 5);
            var system = new GeneratorSystem(new[] { cashGen, fansGen }, currencies, modifiers);
            TestContent.BuyTimes(system.Get("cash_gen"), currencies, 1);
            TestContent.BuyTimes(system.Get("fans_gen"), currencies, 1);
            var cashBefore = currencies.Get("cash");
            var fansBefore = currencies.Get("fans");

            new GrantModifierEffect(ModifierTarget.CurrencyProduction, ModifierOperation.Multiply, 1.5, new List<string> { "cash" })
                .Apply(Context(modifiers), ContentScope.Run);
            system.Tick(10);

            Assert.AreEqual(45.0, (currencies.Get("cash") - cashBefore).ToDouble(), 1e-9, "3 x 1.5 x 10s");
            Assert.AreEqual(50.0, (currencies.Get("fans") - fansBefore).ToDouble(), 1e-9,
                "an undeclared currency takes no multiplier");
        }

        [Test]
        public void CurrencyPerSecMultiplier_GrantsOncePerCurrencyItNames()
        {
            var modifiers = new ModifierSystem();

            new GrantModifierEffect(ModifierTarget.CurrencyProduction, ModifierOperation.Multiply, 2, new List<string> { "cash", "fans" })
                .Apply(Context(modifiers), ContentScope.Run);

            Assert.AreEqual(2.0,
                modifiers.For(ModifierTargetKey.Of(ModifierTarget.CurrencyProduction, "cash")).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(2.0,
                modifiers.For(ModifierTargetKey.Of(ModifierTarget.CurrencyProduction, "fans")).Multiply.ToDouble(), 1e-9);
        }

        // the scope travels with the grant, so a run-scoped buff clears on the
        // album release and a permanent-in-chapter one survives it - the
        // upgrade's declaration is the only place that lifetime is stated
        [TestCase(ContentScope.Run, 1.0)]
        [TestCase(ContentScope.PermanentInChapter, 2.0)]
        public void PayloadScope_DecidesWhatARunResetKeeps(ContentScope scope, double afterReset)
        {
            var modifiers = new ModifierSystem();
            var tap = TestContent.MakeTapProduction(1, modifiers);

            new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1).Apply(Context(modifiers), scope);
            Assert.AreEqual(2.0, tap.TapValue.ToDouble(), 1e-9);

            modifiers.ResetRunScoped();

            Assert.AreEqual(afterReset, tap.TapValue.ToDouble(), 1e-9);
        }

        // UpgradeSystem hands the owning definition's scope to the payload. The
        // buff purchase flow will be the real caller; a content unlock is the
        // only path that applies a payload today, so it is what proves the wiring.
        [Test]
        public void UpgradeSystem_PassesTheDefinitionsScopeToThePayload()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var tap = TestContent.MakeTapProduction(1, modifiers, currencies, flags);
            var upgrades = new UpgradeSystem(new[]
            {
                // no gate = met from the start, so it applies on the first pass
                TestContent.MakeUpgrade("permanent_tap", UpgradeType.ContentUnlock,
                    ContentScope.PermanentInChapter, null, new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 4)),
            }, currencies, flags, modifiers);

            upgrades.EvaluateContentUnlocks(TestContent.MakeContext(currencies, flags: flags));
            Assert.AreEqual(5.0, tap.TapValue.ToDouble(), 1e-9, "base 1 + 4");

            modifiers.ResetRunScoped();
            Assert.AreEqual(5.0, tap.TapValue.ToDouble(), 1e-9,
                "the definition's permanent-in-chapter scope reached the grant");
        }

        // buying charges the declared cost currency and grants the payload, and
        // the effect settles before the spend notifies - no subscriber may
        // observe the money gone with the buff not yet applied
        [Test]
        public void TryBuy_ChargesTheCostCurrency_AndGrantsBeforeTheSpendNotifies()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var tap = TestContent.MakeTapProduction(1, modifiers, currencies, flags);
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("stage_presence", UpgradeType.Buff, ContentScope.Run,
                    new CurrencyBalanceCondition("cash", 250), new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1), costAmount: 250),
            }, currencies, flags, modifiers);
            var context = TestContent.MakeContext(currencies, flags: flags);
            var stagePresence = upgrades.Get("stage_presence");

            Assert.IsFalse(upgrades.TryBuy(stagePresence, context), "the gate is unmet at zero cash");
            Assert.IsFalse(stagePresence.Applied);

            currencies.Add("cash", 249);
            Assert.IsFalse(upgrades.TryBuy(stagePresence, context), "still short of the gate");

            var tapDuringSpend = 0.0;
            currencies.BalanceChanged += (id, _) =>
            {
                if (id == "cash")
                    tapDuringSpend = tap.TapValue.ToDouble();
            };
            currencies.Add("cash", 1);

            Assert.IsTrue(upgrades.TryBuy(stagePresence, context), "gate met and affordable");
            Assert.AreEqual(0.0, currencies.Get("cash").ToDouble(), 1e-9, "the declared currency is charged");
            Assert.AreEqual(2.0, tap.TapValue.ToDouble(), 1e-9, "base 1 + the granted add");
            Assert.AreEqual(2.0, tapDuringSpend, 1e-9, "the buff was already granted when the spend fired");

            Assert.IsFalse(upgrades.TryBuy(stagePresence, context), "an applied buff is never bought twice");
        }

        [Test]
        public void TryBuy_FiresUpgradeAppliedOncePerPurchase()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("amp_strings", UpgradeType.Buff, ContentScope.Run,
                    null, new GrantModifierEffect(ModifierTarget.GeneratorOutput, ModifierOperation.Multiply, 2, new List<string> { "practice_amp" }), costAmount: 500),
            }, currencies, new FlagSystem(), modifiers);
            var context = TestContent.MakeContext(currencies);
            var applied = 0;
            upgrades.UpgradeApplied += _ => applied++;
            currencies.Add("cash", 500);

            Assert.IsTrue(upgrades.TryBuy(upgrades.Get("amp_strings"), context));
            Assert.IsFalse(upgrades.TryBuy(upgrades.Get("amp_strings"), context));

            Assert.AreEqual(1, applied, "one notification for the one purchase");
            Assert.AreEqual(2.0,
                modifiers.For(ModifierTargetKey.Of(ModifierTarget.GeneratorOutput, "practice_amp")).Multiply.ToDouble(),
                1e-9);
        }

        // the shared evaluator means a non-Cash gate is the same shape with a
        // different currency id - tight_set is the Ch1 proof
        [Test]
        public void TryBuy_GatesOnAnyCurrency_NotJustTheOneItCharges()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("tight_set", UpgradeType.Buff, ContentScope.Run,
                    new CurrencyBalanceCondition("fans", 30),
                    new GrantModifierEffect(ModifierTarget.CurrencyProduction, ModifierOperation.Multiply, 1.5, new List<string> { "cash" }), costAmount: 20000),
            }, currencies, new FlagSystem(), modifiers);
            var context = TestContent.MakeContext(currencies);
            var tightSet = upgrades.Get("tight_set");
            currencies.Add("cash", 20000);

            Assert.IsFalse(upgrades.IsAvailable(tightSet, context), "the Fans gate is unmet");
            Assert.IsTrue(upgrades.CanAfford(tightSet), "affordability is a separate question");
            Assert.IsFalse(upgrades.TryBuy(tightSet, context));

            currencies.Add("fans", 30);

            Assert.IsTrue(upgrades.IsAvailable(tightSet, context), "the same evaluator, a different currency id");
            Assert.IsTrue(upgrades.TryBuy(tightSet, context));
            Assert.AreEqual(0.0, currencies.Get("cash").ToDouble(), 1e-9, "cash paid");
            Assert.AreEqual(30.0, currencies.Get("fans").ToDouble(), 1e-9, "the gated currency is never charged");
        }

        // fail closed: never charge for a purchase that could grant nothing, and
        // never let missing tuning become an endless free purchase
        [Test]
        public void TryBuy_RefusesBrokenContent_WithoutCharging()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("no_payload", UpgradeType.Buff, ContentScope.Run, null, null, costAmount: 100),
                TestContent.MakeUpgrade("free_buff", UpgradeType.Buff, ContentScope.Run,
                    null, new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1)),
                TestContent.MakeUpgrade("no_currency", UpgradeType.Buff, ContentScope.Run,
                    null, new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1), costCurrencyId: "", costAmount: 100),
                TestContent.MakeUpgrade("reveal", UpgradeType.ContentUnlock, ContentScope.PermanentInChapter,
                    null, new SetFlagEffect("fans")),
            }, currencies, new FlagSystem(), modifiers);
            var context = TestContent.MakeContext(currencies);
            currencies.Add("cash", 1000);

            LogAssert.Expect(LogType.Error,
                "UpgradeSystem: upgrade 'no_payload' has no payload. Refusing the purchase rather than charging for nothing.");
            Assert.IsFalse(upgrades.TryBuy(upgrades.Get("no_payload"), context));

            Assert.IsFalse(upgrades.TryBuy(upgrades.Get("free_buff"), context), "a zero cost is not a free buff");
            Assert.IsFalse(upgrades.TryBuy(upgrades.Get("no_currency"), context), "nothing to charge");

            LogAssert.Expect(LogType.Error,
                "UpgradeSystem: TryBuy on 'reveal', which is a ContentUnlock - only buffs are bought.");
            Assert.IsFalse(upgrades.TryBuy(upgrades.Get("reveal"), context));

            Assert.AreEqual(1000.0, currencies.Get("cash").ToDouble(), 1e-9, "not a coin spent");
        }

        // The run reset acts on declared scope: a run-scoped buff is re-bought each
        // run so its latch clears, while a content unlock is permanent within the
        // chapter and keeps its latch - which is what leaves flags set and content
        // revealed across demos. The latch is all this clears; the effects the
        // purchases granted are ModifierSystem's to reset.
        [Test]
        public void ResetRunScoped_ClearsRunBuffLatches_AndKeepsContentUnlocks()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("stage_presence", UpgradeType.Buff, ContentScope.Run,
                    null, new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1), costAmount: 250),
                TestContent.MakeUpgrade("play_for_crowd", UpgradeType.ContentUnlock,
                    ContentScope.PermanentInChapter, null, new SetFlagEffect("fans")),
            }, currencies, flags, modifiers);
            var context = TestContent.MakeContext(currencies, flags: flags);
            var buff = upgrades.Get("stage_presence");
            var reveal = upgrades.Get("play_for_crowd");

            currencies.Add("cash", 250);
            Assert.IsTrue(upgrades.TryBuy(buff, context));
            upgrades.EvaluateContentUnlocks(context);
            Assert.IsTrue(reveal.Applied, "the unlock applied on its gate");

            Assert.IsTrue(upgrades.ResetRunScoped(), "something was cleared");

            Assert.IsFalse(buff.Applied, "the run-scoped buff is re-bought each run");
            Assert.IsTrue(reveal.Applied, "the content unlock is permanent within the chapter");
            Assert.IsTrue(flags.IsSet("fans"), "and the content it revealed stays revealed");

            currencies.Add("cash", 250);
            Assert.IsTrue(upgrades.TryBuy(buff, context), "the buff is on offer again");
        }

        // state, then notify: no subscriber may see one buff on offer again while
        // another still reads as bought
        [Test]
        public void ResetRunScoped_SettlesEveryLatchBeforeNotifying()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("first", UpgradeType.Buff, ContentScope.Run,
                    null, new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1), costAmount: 100),
                TestContent.MakeUpgrade("second", UpgradeType.Buff, ContentScope.Run,
                    null, new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1), costAmount: 100),
            }, currencies, new FlagSystem(), modifiers);
            var context = TestContent.MakeContext(currencies);
            currencies.Add("cash", 200);
            Assert.IsTrue(upgrades.TryBuy(upgrades.Get("first"), context));
            Assert.IsTrue(upgrades.TryBuy(upgrades.Get("second"), context));

            var anyStillApplied = false;
            var notifications = 0;
            upgrades.UpgradeCleared += _ =>
            {
                notifications++;
                foreach (var upgrade in upgrades.All)
                    anyStillApplied |= upgrade.Applied;
            };

            upgrades.ResetRunScoped();

            Assert.IsFalse(anyStillApplied, "every latch had settled before the first notification");
            Assert.AreEqual(2, notifications, "one per cleared upgrade");
        }

        // The same rule on the apply side, where it bites harder: a setFlag payload
        // fires FlagSet from inside Apply, so an evaluator re-entering on that
        // signal has to find the latch already set. Otherwise it re-applies the
        // unlock and grants its payload a second time - invisible for a flag, which
        // latches idempotently, and a real double grant for anything else.
        [Test]
        public void EvaluateContentUnlocks_LatchesBeforeThePayloadNotifies()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("play_for_crowd", UpgradeType.ContentUnlock,
                    ContentScope.PermanentInChapter, null, new SetFlagEffect("fans")),
            }, currencies, flags, new ModifierSystem());
            var context = TestContent.MakeContext(currencies, flags: flags);
            var reveal = upgrades.Get("play_for_crowd");

            var applications = 0;
            upgrades.UpgradeApplied += _ => applications++;

            var appliedWhenFlagFired = false;
            flags.FlagSet += _ =>
            {
                appliedWhenFlagFired = reveal.Applied;

                // re-entrant evaluation, the shape a condition-invalidation signal
                // gives this call: it must find nothing left to apply
                upgrades.EvaluateContentUnlocks(context);
            };

            upgrades.EvaluateContentUnlocks(context);

            Assert.IsTrue(appliedWhenFlagFired, "the latch settled before the payload's flag fired");
            Assert.AreEqual(1, applications, "re-entrant evaluation granted nothing a second time");
            Assert.IsTrue(flags.IsSet("fans"));
        }

        // a reset with nothing bought is a no-op, so it stays silent rather than
        // waking every row for nothing
        [Test]
        public void ResetRunScoped_IsSilentAndFalseWhenNothingWasBought()
        {
            var currencies = TestContent.MakeEconomy();
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("stage_presence", UpgradeType.Buff, ContentScope.Run,
                    null, new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1), costAmount: 250),
            }, currencies, new FlagSystem(), new ModifierSystem());
            var notified = false;
            upgrades.UpgradeCleared += _ => notified = true;

            Assert.IsFalse(upgrades.ResetRunScoped(), "nothing to clear");
            Assert.IsFalse(notified);
        }

        // tuning that the registry refuses at runtime is reported against the
        // asset too, so a content mistake surfaces at boot instead of as an
        // effect that silently never applied
        [Test]
        public void Validate_ReportsTuningTheRegistryWouldRefuse()
        {
            var context = TestContent.MakeContext(TestContent.MakeEconomy());

            LogAssert.Expect(LogType.Error,
                "GameEffect: Upgrade 'drain_tap' (payload) adds a negative amount (-1) to TapValue.");
            new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, -1).Validate(context, "Upgrade 'drain_tap' (payload)");

            LogAssert.Expect(LogType.Error,
                "GameEffect: Upgrade 'zero_amp' (payload) has a non-positive multiplier (0).");
            LogAssert.Expect(LogType.Error,
                "GameEffect: Upgrade 'zero_amp' (payload) targets unknown generator id 'practice_amp'.");
            new GrantModifierEffect(ModifierTarget.GeneratorOutput, ModifierOperation.Multiply, 0, new List<string> { "practice_amp" }).Validate(context, "Upgrade 'zero_amp' (payload)");
        }

        // Now that one class carries the target and the operation as serialized
        // enums, an asset can hold an int no member defines - a state the specialized
        // payload classes could not represent, because each hardcoded both. Reported
        // as undefined rather than as the uninitialized zero, which is a different
        // mistake with a different cause.
        [TestCase(99, 2, "GameEffect: Upgrade 'x' (payload) has modifier target 99, which no ModifierTarget defines.")]
        [TestCase(1, 99, "GameEffect: Upgrade 'x' (payload) has modifier operation 99, which no ModifierOperation defines.")]
        public void Validate_ReportsAnEnumValueNoMemberDefines(int target, int operation, string expected)
        {
            var context = TestContent.MakeContext(TestContent.MakeEconomy());

            LogAssert.Expect(LogType.Error, expected);
            new GrantModifierEffect((ModifierTarget)target, (ModifierOperation)operation, 1)
                .Validate(context, "Upgrade 'x' (payload)");
        }
    }
}
