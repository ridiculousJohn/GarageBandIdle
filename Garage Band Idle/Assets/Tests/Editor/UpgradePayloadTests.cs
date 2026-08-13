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

        private static readonly ModifierSubject CashYield = TestContent.YieldOf("cash");
        private static readonly ModifierSelector CashYieldSel = TestContent.Sel("cash_yield");

        private static EffectContext Context(ModifierSystem modifiers)
            => new(TestContent.MakeEconomy(), new FlagSystem(), modifiers);

        // A payload naming cash's yield scales what a firing pays, and the
        // multipliers compose. A flat "+1 Cash per press" is not a payload at all:
        // it is a ProductionContribution on the upgrade (rule 11), which sums with
        // the jam line rather than composing over it - see
        // UpgradeContributions_AreLiveExactlyWhileTheLatchHolds.
        [Test]
        public void AYieldMultiplier_ScalesWhatAFiringPays()
        {
            var modifiers = new ModifierSystem();
            var production = TestContent.MakeYieldProduction(1, modifiers);

            new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 2)
                .Apply(Context(modifiers), ContentScope.Run);

            Assert.AreEqual(2.0, production.YieldOf("cash").ToDouble(), 1e-9, "base 1 x 2");

            modifiers.Grant(CashYieldSel, ModifierOperation.Multiply, ContentScope.Run, 3);
            Assert.AreEqual(6.0, production.YieldOf("cash").ToDouble(), 1e-9, "1 x 2 x 3");
        }

        // The replacement for the flat-add payload: an upgrade CONTRIBUTES its
        // bonus, and the latch is the lifetime. Buying it adds a line to cash's
        // yield; a run reset clears the latch and the line goes with it, with
        // nothing having to remember to withdraw the bonus.
        [Test]
        public void UpgradeContributions_AreLiveExactlyWhileTheLatchHolds()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var jam = TestContent.MakeProducer("jam", ("cash", 1, ProductionFeed.Yield, null));
            var stagePresence = TestContent.MakeUpgrade("stage_presence", UpgradeType.Buff,
                ContentScope.Run, null, null, costAmount: 250,
                contributions: new List<ProductionContribution>
                {
                    TestContent.Line("stage_presence", "cash", 1, ProductionFeed.Yield),
                });
            var upgrades = new UpgradeSystem(new[] { stagePresence }, currencies, flags, modifiers);
            var context = TestContent.MakeContext(currencies, flags: flags);
            var production = new ProductionSystem(new[] { jam }, null, upgrades, currencies, modifiers, context);

            Assert.AreEqual(1.0, production.YieldOf("cash").ToDouble(), 1e-9, "the jam line alone");

            currencies.Add("cash", 250);
            Assert.IsTrue(upgrades.TryBuy(upgrades.Get("stage_presence"), context),
                "contributions alone are a complete grant - an upgrade needs no payload beside them");

            Assert.AreEqual(2.0, production.YieldOf("cash").ToDouble(), 1e-9,
                "1 + 1, SUMMED with the jam line rather than composed over it");

            modifiers.Grant(CashYieldSel, ModifierOperation.Multiply, ContentScope.Run, 3);
            Assert.AreEqual(6.0, production.YieldOf("cash").ToDouble(), 1e-9,
                "(1 + 1) x 3 - the one shape every composed number has");

            upgrades.ResetRunScoped();
            Assert.AreEqual(3.0, production.YieldOf("cash").ToDouble(), 1e-9,
                "the latch is gone, so the line is gone - 1 x 3");
        }

        // A bonus is not a button. Fireability is a rule about what a MODULE may
        // name - an authored producer - never something derived from holding a
        // yield line, which made an applied upgrade fireable purely because
        // stage_presence contributes to cash's yield.
        [Test]
        public void AnUpgradeContributingAYield_IsNotFireable()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var jam = TestContent.MakeProducer("jam", ("cash", 1, ProductionFeed.Yield, null));
            var stagePresence = TestContent.MakeUpgrade("stage_presence", UpgradeType.ContentUnlock,
                ContentScope.Run, null, null,
                contributions: new List<ProductionContribution>
                {
                    TestContent.Line("stage_presence", "cash", 1, ProductionFeed.Yield),
                });
            var upgrades = new UpgradeSystem(new[] { stagePresence }, currencies, flags, modifiers);
            var context = TestContent.MakeContext(currencies, flags: flags);
            var production = new ProductionSystem(new[] { jam }, null, upgrades, currencies, modifiers, context);

            // no gate, so it latches on the first pass and its line goes live
            upgrades.EvaluateContentUnlocks(context);
            Assert.AreEqual(2.0, production.YieldOf("cash").ToDouble(), 1e-9, "the bonus is in cash's yield");

            Assert.IsTrue(production.CanFire("jam"));
            Assert.IsFalse(production.CanFire("stage_presence"),
                "an upgrade is a contributor, not a surface");
            CollectionAssert.AreEqual(new[] { "cash" }, production.FiredCurrencies("jam"));
            CollectionAssert.IsEmpty(production.FiredCurrencies("stage_presence"));
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

            new GrantModifierEffect(TestContent.Sel("practice_amp"), ModifierOperation.Multiply, 2).Apply(Context(modifiers), ContentScope.Run);

            Assert.AreEqual(0.8, system.Get("practice_amp").LineValue().ToDouble(), 1e-9, "0.4 x 2");
            Assert.AreEqual(3.0, system.Get("drummer").LineValue().ToDouble(), 1e-9,
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

            new GrantModifierEffect(TestContent.Sel("cash_rate"), ModifierOperation.Multiply, 1.5)
                .Apply(Context(modifiers), ContentScope.Run);
            TestContent.AccrueGenerators(system, currencies, modifiers, 10);

            Assert.AreEqual(45.0, (currencies.Get("cash") - cashBefore).ToDouble(), 1e-9, "3 x 1.5 x 10s");
            Assert.AreEqual(50.0, (currencies.Get("fans") - fansBefore).ToDouble(), 1e-9,
                "an undeclared currency takes no multiplier");
        }

        // ONE grant, whatever it reaches - not one per named id. Granting per id
        // would silently miss whatever is added to the set later, and it is a
        // selector reaching several numbers rather than several modifiers.
        //
        // Terms are NAMES and any one matching is enough (rule 11), so naming two
        // number ids reaches both - which is what makes "double both rates" a
        // sayable thing rather than a selector that quietly reaches neither.
        [Test]
        public void OneEffectGrantsOneModifier_ThatEveryNamedNumberAsksAbout()
        {
            var modifiers = new ModifierSystem();

            new GrantModifierEffect(TestContent.Sel("cash_rate", "fans_rate"), ModifierOperation.Multiply, 2)
                .Apply(Context(modifiers), ContentScope.Run);

            Assert.AreEqual(2.0,
                modifiers.For(TestContent.RateOf("cash")).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(2.0,
                modifiers.For(TestContent.RateOf("fans")).Multiply.ToDouble(), 1e-9,
                "one grant, two numbers asking about it - not one grant per name");
            Assert.AreEqual(1.0,
                modifiers.For(TestContent.YieldOf("cash")).Multiply.ToDouble(), 1e-9,
                "and nothing it did not name");

            // the same set, named once instead of listed - which is what survives a
            // third currency being added
            new GrantModifierEffect(TestContent.Sel("run_currency_rate"), ModifierOperation.Multiply, 3)
                .Apply(Context(modifiers), ContentScope.Run);

            Assert.AreEqual(3.0,
                modifiers.For(new ModifierSubject("merch_rate", new[] { "run_currency_rate" })).Multiply.ToDouble(),
                1e-9, "a tag is how a set gets a name");
        }

        // The upgrade's declaration is the only place a payload's lifetime is
        // stated, and the release reads it off the FACT rather than off the store
        // (design doc section 12, rule 6): a run-scoped latch clears, so
        // re-projecting does not re-grant its add; a permanent-in-chapter latch
        // survives, so re-projecting does.
        [TestCase(ContentScope.Run, 1.0)]
        [TestCase(ContentScope.PermanentInChapter, 2.0)]
        public void PayloadScope_DecidesWhatARunResetKeeps(ContentScope scope, double afterReset)
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var production = TestContent.MakeYieldProduction(1, modifiers, currencies, flags);
            var upgrades = new UpgradeSystem(new[]
            {
                // no gate = met from the start, so it latches on the first pass
                TestContent.MakeUpgrade("tap_boost", UpgradeType.ContentUnlock, scope, null,
                    new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 2)),
            }, currencies, flags, modifiers);

            upgrades.EvaluateContentUnlocks(TestContent.MakeContext(currencies, flags: flags));
            Assert.AreEqual(2.0, production.YieldOf("cash").ToDouble(), 1e-9);

            TestContent.RunReset(modifiers, upgrades);

            Assert.AreEqual(afterReset, production.YieldOf("cash").ToDouble(), 1e-9);
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
            var production = TestContent.MakeYieldProduction(1, modifiers, currencies, flags);
            var upgrades = new UpgradeSystem(new[]
            {
                // no gate = met from the start, so it applies on the first pass
                TestContent.MakeUpgrade("permanent_tap", UpgradeType.ContentUnlock,
                    ContentScope.PermanentInChapter, null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 4)),
            }, currencies, flags, modifiers);

            upgrades.EvaluateContentUnlocks(TestContent.MakeContext(currencies, flags: flags));
            Assert.AreEqual(4.0, production.YieldOf("cash").ToDouble(), 1e-9, "base 1 x 4");

            TestContent.RunReset(modifiers, upgrades);
            Assert.AreEqual(4.0, production.YieldOf("cash").ToDouble(), 1e-9,
                "the definition's permanent-in-chapter scope kept the latch, and the projection rebuilt the grant from it");
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
            var production = TestContent.MakeYieldProduction(1, modifiers, currencies, flags);
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("stage_presence", UpgradeType.Buff, ContentScope.Run,
                    new CurrencyBalanceCondition("cash", 250), new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 2), costAmount: 250),
            }, currencies, flags, modifiers);
            var context = TestContent.MakeContext(currencies, flags: flags);
            var stagePresence = upgrades.Get("stage_presence");

            Assert.IsFalse(upgrades.TryBuy(stagePresence, context), "the gate is unmet at zero cash");
            Assert.IsFalse(stagePresence.Applied);

            currencies.Add("cash", 249);
            Assert.IsFalse(upgrades.TryBuy(stagePresence, context), "still short of the gate");

            var yieldDuringSpend = 0.0;
            currencies.BalanceChanged += (id, _) =>
            {
                if (id == "cash")
                    yieldDuringSpend = production.YieldOf("cash").ToDouble();
            };
            currencies.Add("cash", 1);

            Assert.IsTrue(upgrades.TryBuy(stagePresence, context), "gate met and affordable");
            Assert.AreEqual(0.0, currencies.Get("cash").ToDouble(), 1e-9, "the declared currency is charged");
            Assert.AreEqual(2.0, production.YieldOf("cash").ToDouble(), 1e-9, "base 1 x the granted multiplier");
            Assert.AreEqual(2.0, yieldDuringSpend, 1e-9, "the buff was already granted when the spend fired");

            Assert.IsFalse(upgrades.TryBuy(stagePresence, context), "an applied buff is never bought twice");
        }

        // The one place awards run: the purchase. Re-buying after a run reset
        // re-pays because TryBuy re-charged; the rebuild boundary in between pays
        // nothing, because no projection path holds a GameAction.
        [Test]
        public void TryBuy_ExecutesActionsOncePerPurchase_AndTheRebuildNeverDoes()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("advance", UpgradeType.Buff, ContentScope.Run,
                    null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1),
                    costAmount: 250,
                    actions: new List<GameAction> { new GrantCurrencyAction("fans", 10) }),
            }, currencies, flags, modifiers);
            var context = TestContent.MakeContext(currencies, flags: flags);

            currencies.Add("cash", 250);
            Assert.IsTrue(upgrades.TryBuy(upgrades.Get("advance"), context));
            Assert.AreEqual(10.0, currencies.Get("fans").ToDouble(), 1e-9, "the purchase paid the award");

            upgrades.ProjectModifiers();
            Assert.AreEqual(10.0, currencies.Get("fans").ToDouble(), 1e-9,
                "the rebuild re-applied the payload and paid nothing - it cannot see actions");

            TestContent.RunReset(modifiers, upgrades);
            currencies.Add("cash", 250);
            Assert.IsTrue(upgrades.TryBuy(upgrades.Get("advance"), context), "run-scoped: re-bought each run");
            Assert.AreEqual(20.0, currencies.Get("fans").ToDouble(), 1e-9,
                "re-buying re-pays: TryBuy re-charged, so this is a purchase, not a repeat");
        }

        // An action ENTRY is not a grant: a serialized null slot and an award of
        // nothing both pass a count check while granting nothing, and a report
        // never stops a boot - so TryBuy asks each action whether it would
        // execute, and refuses before any state moves.
        [Test]
        public void TryBuy_RefusesAPurchaseWhoseActionsCannotExecute()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("hollow", UpgradeType.Buff, ContentScope.Run,
                    null, payload: null, costAmount: 250,
                    actions: new List<GameAction> { null }),
                TestContent.MakeUpgrade("zeroed", UpgradeType.Buff, ContentScope.Run,
                    null, payload: null, costAmount: 250,
                    actions: new List<GameAction> { new GrantCurrencyAction("fans", 0) }),
                // a positive award to a currency no reachable pool holds: Add
                // would land nowhere, so the preflight must catch it too
                TestContent.MakeUpgrade("ghost", UpgradeType.Buff, ContentScope.Run,
                    null, payload: null, costAmount: 250,
                    actions: new List<GameAction> { new GrantCurrencyAction("merch", 100) }),
            }, currencies, flags, modifiers);
            var context = TestContent.MakeContext(currencies, flags: flags);
            currencies.Add("cash", 500);

            LogAssert.Expect(LogType.Error,
                "UpgradeSystem: upgrade 'hollow' has no payload and no executable action. Refusing the purchase rather than charging for nothing.");
            Assert.IsFalse(upgrades.TryBuy(upgrades.Get("hollow"), context));
            Assert.IsFalse(upgrades.Get("hollow").Applied, "nothing latched");

            LogAssert.Expect(LogType.Error,
                "UpgradeSystem: upgrade 'zeroed' has no payload and no executable action. Refusing the purchase rather than charging for nothing.");
            Assert.IsFalse(upgrades.TryBuy(upgrades.Get("zeroed"), context));

            LogAssert.Expect(LogType.Error,
                "UpgradeSystem: upgrade 'ghost' has no payload and no executable action. Refusing the purchase rather than charging for nothing.");
            Assert.IsFalse(upgrades.TryBuy(upgrades.Get("ghost"), context));

            Assert.AreEqual(500.0, currencies.Get("cash").ToDouble(), 1e-9,
                "no refusal charged anything");
        }

        [Test]
        public void TryBuy_FiresUpgradeAppliedOncePerPurchase()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var upgrades = new UpgradeSystem(new[]
            {
                TestContent.MakeUpgrade("amp_strings", UpgradeType.Buff, ContentScope.Run,
                    null, new GrantModifierEffect(TestContent.Sel("practice_amp"), ModifierOperation.Multiply, 2), costAmount: 500),
            }, currencies, new FlagSystem(), modifiers);
            var context = TestContent.MakeContext(currencies);
            var applied = 0;
            upgrades.UpgradeApplied += _ => applied++;
            currencies.Add("cash", 500);

            Assert.IsTrue(upgrades.TryBuy(upgrades.Get("amp_strings"), context));
            Assert.IsFalse(upgrades.TryBuy(upgrades.Get("amp_strings"), context));

            Assert.AreEqual(1, applied, "one notification for the one purchase");
            Assert.AreEqual(2.0,
                modifiers.For(TestContent.Num("practice_amp")).Multiply.ToDouble(),
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
                    new GrantModifierEffect(TestContent.Sel("cash_rate"), ModifierOperation.Multiply, 1.5), costAmount: 20000),
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
                    null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1)),
                TestContent.MakeUpgrade("no_currency", UpgradeType.Buff, ContentScope.Run,
                    null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1), costCurrencyId: "", costAmount: 100),
                TestContent.MakeUpgrade("reveal", UpgradeType.ContentUnlock, ContentScope.PermanentInChapter,
                    null, new SetFlagEffect("fans")),
            }, currencies, new FlagSystem(), modifiers);
            var context = TestContent.MakeContext(currencies);
            currencies.Add("cash", 1000);

            LogAssert.Expect(LogType.Error,
                "UpgradeSystem: upgrade 'no_payload' has no payload and no executable action. Refusing the purchase rather than charging for nothing.");
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
                    null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1), costAmount: 250),
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
                    null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1), costAmount: 100),
                TestContent.MakeUpgrade("second", UpgradeType.Buff, ContentScope.Run,
                    null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1), costAmount: 100),
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
                    null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1), costAmount: 250),
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

            // a negative multiplier negates the whole product it lands in, which is
            // the same failure a zero one is. There is no negative-bonus report
            // beside it (rule 11): a flat bonus is a contribution, and a negative
            // one is refused where it is authored
            LogAssert.Expect(LogType.Error,
                "GameEffect: Upgrade 'drain_tap' (payload) has a non-positive multiplier (-1).");
            new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, -1).Validate(context, "Upgrade 'drain_tap' (payload)");

            // no term report here: this fixture has no ContentDatabase, so there is
            // no content set to resolve against and reporting every term as unknown
            // would drown what the test is asserting. The term check has its own
            // test with a database - Validate_ReportsATermNothingAnswersTo.
            LogAssert.Expect(LogType.Error,
                "GameEffect: Upgrade 'zero_amp' (payload) has a non-positive multiplier (0).");
            new GrantModifierEffect(TestContent.Sel("practice_amp"), ModifierOperation.Multiply, 0).Validate(context, "Upgrade 'zero_amp' (payload)");
        }

        // Now that one class carries the target and the operation as serialized
        // enums, an asset can hold an int no member defines - a state the specialized
        // payload classes could not represent, because each hardcoded both. Reported
        // as undefined rather than as the uninitialized zero, which is a different
        // mistake with a different cause.
        //
        // Only the OPERATION is an enum now: what a modifier reaches is a selector
        // over open content, so there is no stat kind left that a serialized int
        // could hold an undefined value of. A term naming nothing is the analogous
        // failure and is reported against the content set instead - see
        // Validate_ReportsATermNothingAnswersTo.
        [TestCase(99, "GameEffect: Upgrade 'x' (payload) has modifier operation 99, which no ModifierOperation defines.")]
        [TestCase(0, "GameEffect: Upgrade 'x' (payload) names no modifier operation (uninitialized).")]
        public void Validate_ReportsAnEnumValueNoMemberDefines(int operation, string expected)
        {
            var context = TestContent.MakeContext(TestContent.MakeEconomy());

            LogAssert.Expect(LogType.Error, expected);
            new GrantModifierEffect(TestContent.Sel("cash_yield"), (ModifierOperation)operation, 1)
                .Validate(context, "Upgrade 'x' (payload)");
        }

        // An open vocabulary has no compiler behind it, so this is the guard: a term
        // that answers to nothing stores a modifier no number ever asks about, which
        // looks authored rather than broken. It is resolved against the whole
        // content set, since a term does not say which family it belongs to.
        [Test]
        public void Validate_ReportsATermNothingAnswersTo()
        {
            var database = TestContent.MakeDatabase(
                generators: new[] { TestContent.MakeGenerator("practice_amp", "cash", 60, 1.15, 0.4) });
            var currencies = TestContent.MakeEconomy();
            var context = new ConditionContext(currencies, null, null, database: database);

            LogAssert.Expect(LogType.Error,
                "GameEffect: Upgrade 'x' (payload) targets 'drummer_csah', which no definition id, tag or produced number answers to.");
            new GrantModifierEffect(TestContent.Sel("drummer_csah"), ModifierOperation.Multiply, 2)
                .Validate(context, "Upgrade 'x' (payload)");

            // every kind of term that DOES resolve, reported by nothing: a
            // definition id, a contribution's own id, and a produced number's
            // derived id (which no asset carries, so it has to be recognised
            // separately)
            new GrantModifierEffect(TestContent.Sel("practice_amp", "practice_amp_cash", "cash_rate"),
                    ModifierOperation.Multiply, 2)
                .Validate(context, "Upgrade 'y' (payload)");
        }
    }
}
