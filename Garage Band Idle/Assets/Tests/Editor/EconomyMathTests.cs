using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Pure math: the cost curve and production formulas from design doc sections 3 and 6.
    public class EconomyMathTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        [TestCase(0, 60.0)]
        [TestCase(1, 69.0)]
        [TestCase(2, 79.35)]
        [TestCase(10, 242.733464)]
        public void Cost_FollowsExponentialCurve(int owned, double expected)
        {
            var amp = TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4);

            var cost = CostCalculator.Cost(amp, owned);

            Assert.AreEqual(expected, cost.ToDouble(), expected * 1e-6);
        }

        [TestCase(0, 1.0)]
        [TestCase(1, 1.02)]
        [TestCase(10, 1.2)]
        [TestCase(30, 1.6)]
        public void IncomeMultiplier_IsOnePlusBuffPerRecord(int records, double expected)
        {
            var multiplier = ProductionCalculator.IncomeMultiplier(records, 0.02);

            Assert.AreEqual(expected, multiplier.ToDouble(), 1e-9);
        }

        // the album payout, floor((fans / 5) ^ 0.5) - the four worked examples
        // are the JSON's recordsFormulaExamples, plus the edges: below 5 fans
        // the floor gives nothing, and zero fans is a legal (empty) release.
        // The curve is authored content now (rule 14), so the math is asked of
        // the formula the chapter files, evaluated over a real balance.
        [TestCase(0, 0)]
        [TestCase(4, 0)]
        [TestCase(5, 1)]
        [TestCase(50, 3)]
        [TestCase(125, 5)]
        [TestCase(500, 10)]
        [TestCase(2000, 20)]
        public void RootOfBalance_FloorsTheRootOfTheFansBalance(double fansThisRun, int expected)
        {
            var currencies = TestContent.MakeEconomy();
            currencies.Add("fans", fansThisRun);

            var earned = new RootOfBalanceFormula("fans", 5)
                .Evaluate(new EffectContext(currencies, null, null));

            Assert.AreEqual(expected, earned.ToDouble(), 1e-9);
        }

        [Test]
        public void TotalPerSecond_SumsOnlyTheRequestedCurrency()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var cashGen = new Generator(TestContent.MakeGenerator("cash_gen", "cash", 10, 1.15, 3), modifiers);
            var recordsGen = new Generator(TestContent.MakeGenerator("records_gen", "records", 10, 1.15, 50), modifiers);
            TestContent.BuyTimes(cashGen, currencies, 4);
            TestContent.BuyTimes(recordsGen, currencies, 2);
            // Each generator's LINE for a currency, not a helper that sums the fleet:
            // summing one currency's generator output was half of that currency's
            // rate, and the other half lived in ProductionSystem. A currency's rate
            // is its producer's now, summed and composed in one place.
            var cashPerSecond = cashGen.LineValue("cash");
            var recordsPerSecond = recordsGen.LineValue("records");

            Assert.AreEqual(12.0, cashPerSecond.ToDouble(), 1e-9);   // 3 x 4 owned
            Assert.AreEqual(100.0, recordsPerSecond.ToDouble(), 1e-9); // 50 x 2 owned
        }

        // a generator's own output modifier composes into its rate, so the row
        // readout and the tick both pick it up with no second code path
        [Test]
        public void ProductionPerSecond_ComposesTheGeneratorsOwnOutputModifier()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var generator = new Generator(TestContent.MakeGenerator("gen", "cash", 10, 1.15, 5), modifiers);
            TestContent.BuyTimes(generator, currencies, 2);
            Assert.AreEqual(10.0, TestContent.LineValue(generator).ToDouble(), 1e-9, "5 output x 2 owned");

            modifiers.Grant(TestContent.Sel("gen"),
                ModifierOperation.Multiply, ContentScope.Run, 1.5);

            Assert.AreEqual(15.0, TestContent.LineValue(generator).ToDouble(), 1e-9, "5 x 2 x 1.5");

            // a modifier naming another generator never reaches this one
            modifiers.Grant(TestContent.Sel("someone_else"),
                ModifierOperation.Multiply, ContentScope.Run, 10);
            Assert.AreEqual(15.0, TestContent.LineValue(generator).ToDouble(), 1e-9, "another generator's buff is not ours");
        }

        // the per-unit figure a row shows beside the total is derived from the
        // same composition, so the two can never contradict each other - an
        // unbuffed "each" next to a buffed total reads as a bug
        [Test]
        public void PerUnitProduction_TracksTheBuff_AndAlwaysDividesTheTotal()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var target = TestContent.Num("gen");
            var targetSel = TestContent.Sel("gen");
            var generator = new Generator(TestContent.MakeGenerator("gen", "cash", 10, 1.15, 0.4), modifiers);

            Assert.AreEqual(0.4, TestContent.PerUnitLineValue(generator).ToDouble(), 1e-9,
                "unowned, the row previews what the first unit would produce");

            TestContent.BuyTimes(generator, currencies, 5);
            Assert.AreEqual(0.4, TestContent.PerUnitLineValue(generator).ToDouble(), 1e-9);

            modifiers.Grant(targetSel, ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(0.8, TestContent.PerUnitLineValue(generator).ToDouble(), 1e-9, "0.4 x 2, the buff reaches the per-unit");
            Assert.AreEqual(4.0, TestContent.LineValue(generator).ToDouble(), 1e-9, "0.4 x 5 x 2");
            Assert.AreEqual(TestContent.LineValue(generator).ToDouble(),
                (TestContent.PerUnitLineValue(generator) * generator.Owned).ToDouble(), 1e-9,
                "owned x each == the total the row shows beside it");
        }

        // The fleet-level-lump case is gone with Add itself (rule 11): a flat bonus
        // is a contribution to the number it raises, so "+20 to this generator's
        // output" is not expressible as a modifier and the question it raised -
        // +20 to the fleet or +20 to each unit - is unsayable rather than answered
        // by a division. What remains is the identity, which a multiplier keeps
        // trivially and which the row still depends on.
        [Test]
        public void PerUnitProduction_TimesOwned_IsAlwaysTheTotal()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var generator = new Generator(TestContent.MakeGenerator("gen", "cash", 10, 1.15, 5), modifiers);
            TestContent.BuyTimes(generator, currencies, 4);

            modifiers.Grant(TestContent.Sel("gen"),
                ModifierOperation.Multiply, ContentScope.Run, 3);

            Assert.AreEqual(60.0, TestContent.LineValue(generator).ToDouble(), 1e-9, "5 x 4 x 3");
            Assert.AreEqual(15.0, TestContent.PerUnitLineValue(generator).ToDouble(), 1e-9, "5 x 3");
            Assert.AreEqual(TestContent.LineValue(generator).ToDouble(),
                (TestContent.PerUnitLineValue(generator) * generator.Owned).ToDouble(), 1e-9);
        }

        [Test]
        public void PerUnitProduction_FailsClosedOnANegativeBaseOutput()
        {
            var generator = new Generator(TestContent.MakeGenerator("gen", "cash", 10, 1.15, -5), new ModifierSystem());

            Assert.AreEqual(0.0, TestContent.PerUnitLineValue(generator).ToDouble(), 1e-9, "never advertises negative output");
        }

        // an unowned generator produces nothing whatever reaches it: a multiplier on
        // gear the player never bought scales zero, which is the only answer that
        // cannot pay out
        [Test]
        public void ProductionPerSecond_IsZeroWhileUnowned_WhateverReachesIt()
        {
            var modifiers = new ModifierSystem();
            var generator = new Generator(TestContent.MakeGenerator("gen", "cash", 10, 1.15, 5), modifiers);

            modifiers.Grant(TestContent.Sel("gen"),
                ModifierOperation.Multiply, ContentScope.Run, 100);

            Assert.AreEqual(0.0, TestContent.LineValue(generator).ToDouble(), 1e-9, "nothing owned, nothing produced");
        }

        [Test]
        public void Tick_AddsProductionTimesSeconds()
        {
            var currencies = TestContent.MakeEconomy();
            var definition = TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4);
            var system = new GeneratorSystem(new[] { definition }, currencies, new ModifierSystem());
            TestContent.BuyTimes(system.Get("amp"), currencies, 1);
            var before = currencies.Get("cash");

            TestContent.AccrueGenerators(system, currencies, new ModifierSystem(), 10.0);

            Assert.AreEqual(4.0, (currencies.Get("cash") - before).ToDouble(), 1e-9); // 0.4/sec x 10s
        }

        // a multiplier is granted against the currency production it names:
        // production of a currency nothing targets is untouched, no matter what
        // generators exist - fans/records producers never inherit the cash buff
        [Test]
        public void Tick_AppliesAMultiplierOnlyToTheCurrencyItTargets()
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

            modifiers.Grant(TestContent.Sel("cash_rate"),
                ModifierOperation.Multiply, ContentScope.Run, 2.0);
            TestContent.AccrueGenerators(system, currencies, modifiers, 10.0);

            Assert.AreEqual(60.0, (currencies.Get("cash") - cashBefore).ToDouble(), 1e-9); // 3 x 2 x 10s
            Assert.AreEqual(50.0, (currencies.Get("fans") - fansBefore).ToDouble(), 1e-9,
                "an untargeted currency takes no multiplier"); // 5 x 1 x 10s
        }

        // the Records buff is a derived modifier: always on, tracking the
        // cumulative total, and confined to the currencies the chapter declares
        [Test]
        public void RecordsIncomeModifier_TracksTheCumulativeTotal_AndOnlyItsOwnCurrency()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var cashGen = TestContent.MakeGenerator("cash_gen", "cash", 10, 1.15, 3);
            var fansGen = TestContent.MakeGenerator("fans_gen", "fans", 10, 1.15, 5);
            var system = new GeneratorSystem(new[] { cashGen, fansGen }, currencies, modifiers);
            TestContent.BuyTimes(system.Get("cash_gen"), currencies, 1);
            TestContent.BuyTimes(system.Get("fans_gen"), currencies, 1);
            modifiers.AddDerived(new RecordsIncomeModifier(currencies, "records", 0.02, "cash"));

            var cashTarget = TestContent.RateOf("cash");
            var cashTargetSel = TestContent.Sel("cash_rate");
            var fansTarget = TestContent.RateOf("fans");
            Assert.AreEqual(1.0, modifiers.For(cashTarget).Multiply.ToDouble(), 1e-9, "no records, no bonus");

            currencies.Add("records", 10);

            Assert.AreEqual(1.2, modifiers.For(cashTarget).Multiply.ToDouble(), 1e-9,
                "the value follows the total with nothing re-applying it");
            Assert.AreEqual(1.0, modifiers.For(fansTarget).Multiply.ToDouble(), 1e-9,
                "an undeclared currency never inherits the Records buff");

            // rebuilding the grant store leaves derived modifiers standing: the
            // Records total is what governs this buff's lifetime, and a total in
            // a pool no release touches needs nothing re-applied
            modifiers.Grant(cashTargetSel, ModifierOperation.Multiply, ContentScope.Run, 3.0);
            Assert.AreEqual(3.6, modifiers.For(cashTarget).Multiply.ToDouble(), 1e-9, "granted x derived");
            modifiers.ResetGranted();
            Assert.AreEqual(1.2, modifiers.For(cashTarget).Multiply.ToDouble(), 1e-9, "the derived buff survives");
        }

        // "Cumulative Records" has one reading, shared by the permanent income
        // buff and the capstone gate, so the two can never drift apart. Records
        // are accumulated and never spent, which makes this invisible today -
        // spending is the only way to tell the readings apart, and it proves
        // which one is in force rather than relying on the rule holding forever.
        [Test]
        public void RecordsIncomeModifier_AndTheCapstoneGate_ReadTheSameTotal()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            modifiers.AddDerived(new RecordsIncomeModifier(currencies, "records", 0.02, "cash"));
            var cashTarget = TestContent.RateOf("cash");
            var gate = new RecordsCumulativeCondition(10);
            var context = TestContent.MakeContext(currencies);

            currencies.Add("records", 10);
            Assert.AreEqual(1.2, modifiers.For(cashTarget).Multiply.ToDouble(), 1e-9);
            Assert.IsTrue(gate.Evaluate(context));

            currencies.Add("records", -10);

            Assert.AreEqual(1.2, modifiers.For(cashTarget).Multiply.ToDouble(), 1e-9,
                "a permanent buff cannot shrink");
            Assert.IsTrue(gate.Evaluate(context), "and a chapter gate cannot un-gate");
        }

        [Test]
        public void TryBuy_DeductsCostAndFailsWhenUnaffordable()
        {
            var currencies = TestContent.MakeEconomy();
            var generator = new Generator(TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4), new ModifierSystem());

            currencies.Add("cash", 100);
            Assert.IsTrue(generator.TryBuy(currencies));
            Assert.AreEqual(1, generator.Owned);
            Assert.AreEqual(40.0, currencies.Get("cash").ToDouble(), 1e-9);

            // next cost is 69; 40 on hand is not enough
            Assert.IsFalse(generator.TryBuy(currencies));
            Assert.AreEqual(1, generator.Owned);
            Assert.AreEqual(40.0, currencies.Get("cash").ToDouble(), 1e-9);
        }

        // fail closed on broken content: a negative base output (invalid data -
        // boot validation reports it) must never drain a currency
        [Test]
        public void ProductionPerSecond_FailsClosedOnANegativeBaseOutput()
        {
            var currencies = TestContent.MakeEconomy();
            var generator = new Generator(TestContent.MakeGenerator("leak", "cash", 10, 1.15, -5), new ModifierSystem());
            TestContent.BuyTimes(generator, currencies, 1);

            Assert.AreEqual(0.0, TestContent.LineValue(generator).ToDouble(), 1e-9, "never negative production");
        }

        // fail closed on broken content: a non-positive cost (invalid data -
        // boot validation reports it) must never be an endless free purchase
        [Test]
        public void TryBuy_FailsClosedOnANonPositiveCost()
        {
            var currencies = TestContent.MakeEconomy();
            var generator = new Generator(TestContent.MakeGenerator("broken", "cash", 0, 0, 1), new ModifierSystem());
            currencies.Add("cash", 100);

            Assert.IsFalse(generator.TryBuy(currencies));
            Assert.AreEqual(0, generator.Owned);
            Assert.AreEqual(100.0, currencies.Get("cash").ToDouble(), 1e-9, "nothing charged, nothing granted");
        }

        // cost and produces are independent declarations: buying charges the
        // declared cost currency and never touches the produced currency, so a
        // "buy with Cash, produce Merch" generator is expressible
        [Test]
        public void TryBuy_ChargesTheCostCurrency_NeverTheProducedCurrency()
        {
            var currencies = TestContent.MakeEconomy();
            var generator = new Generator(TestContent.MakeGenerator("merch_stand", "fans", 60, 1.15, 1), new ModifierSystem());
            currencies.Add("cash", 100);

            Assert.IsTrue(generator.TryBuy(currencies));
            Assert.AreEqual(40.0, currencies.Get("cash").ToDouble(), 1e-9, "the declared cost currency is charged");
            Assert.AreEqual(0.0, currencies.Get("fans").ToDouble(), 1e-9, "the produced currency is untouched by a purchase");
        }

        // run reset (album release, event baseline; design doc section 7):
        // gear and bandmates are re-bought each run, so every owned count
        // zeroes - and no subscriber may ever observe a half-reset fleet
        // (state, then notify)
        [Test]
        public void ResetOwned_ZeroesEveryGenerator_AndNotifiesAfterAllSettle()
        {
            var currencies = TestContent.MakeEconomy();
            var system = new GeneratorSystem(new[]
            {
                TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4),
                TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3),
            }, currencies, new ModifierSystem());
            TestContent.BuyTimes(system.Get("amp"), currencies, 2);
            TestContent.BuyTimes(system.Get("drummer"), currencies, 3);

            var notifications = 0;
            var observedHalfReset = false;
            system.GeneratorOwnedChanged += _ =>
            {
                notifications++;
                if (system.Get("amp").Owned != 0 || system.Get("drummer").Owned != 0)
                    observedHalfReset = true;
            };

            system.ResetOwned();

            Assert.AreEqual(0, system.Get("amp").Owned);
            Assert.AreEqual(0, system.Get("drummer").Owned);
            Assert.AreEqual(2, notifications, "one notification per generator that changed");
            Assert.IsFalse(observedHalfReset, "every subscriber sees the whole fleet settled");
            Assert.AreEqual(60.0, system.Get("amp").NextCost.ToDouble(), 1e-9, "the cost curve restarts");

            system.ResetOwned();
            Assert.AreEqual(2, notifications, "an already-zero fleet notifies nothing");
        }

        // save/load: the fleet restores as one atomic operation - every count
        // settles before any notification, so an ownedCount gate never
        // observes a half-restored fleet; the cost curve resumes at the
        // restored counts
        [Test]
        public void RestoreOwned_EstablishesTheFleetBeforeNotifying()
        {
            var currencies = TestContent.MakeEconomy();
            var system = new GeneratorSystem(new[]
            {
                TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4),
                TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3),
            }, currencies, new ModifierSystem());

            var notifications = 0;
            var observedPartialRestore = false;
            system.GeneratorOwnedChanged += _ =>
            {
                notifications++;
                if (system.Get("amp").Owned != 2 || system.Get("drummer").Owned != 5)
                    observedPartialRestore = true;
            };

            system.RestoreOwned(new Dictionary<string, int> { { "amp", 2 }, { "drummer", 5 } });

            Assert.AreEqual(2, system.Get("amp").Owned);
            Assert.AreEqual(5, system.Get("drummer").Owned);
            Assert.AreEqual(2, notifications, "one notification per generator that changed");
            Assert.IsFalse(observedPartialRestore, "every subscriber sees the whole fleet settled");
            Assert.AreEqual(60 * System.Math.Pow(1.15, 2), system.Get("amp").NextCost.ToDouble(), 1e-6,
                "the cost curve resumes at the restored count");
        }

        // corrupt or stale save data fails closed: an unknown id is reported
        // and skipped, a negative count restores as zero
        [Test]
        public void RestoreOwned_FailsClosedOnStaleAndCorruptSaveData()
        {
            var currencies = TestContent.MakeEconomy();
            var system = new GeneratorSystem(new[]
            {
                TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4),
            }, currencies, new ModifierSystem());

            LogAssert.Expect(LogType.Error, "GeneratorSystem: RestoreOwned with unknown generator id 'ghost'. Skipping it.");
            system.RestoreOwned(new Dictionary<string, int> { { "ghost", 3 } });

            LogAssert.Expect(LogType.Error, "Generator: RestoreOwned with negative count '-1' for 'amp'. Restoring zero.");
            system.RestoreOwned(new Dictionary<string, int> { { "amp", -1 } });
            Assert.AreEqual(0, system.Get("amp").Owned);
        }

        // state-then-notify: the spend's BalanceChanged is a synchronous signal
        // that condition evaluators react to, so the purchase must already be
        // counted when it fires - an ownedCount gate may never observe the cost
        // deducted with Owned still stale
        [Test]
        public void TryBuy_OwnedIsCountedBeforeTheSpendNotifies()
        {
            var currencies = TestContent.MakeEconomy();
            var generator = new Generator(TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4), new ModifierSystem());
            currencies.Add("cash", 60);

            var ownedDuringSpend = -1;
            currencies.BalanceChanged += (id, _) =>
            {
                if (id == "cash")
                    ownedDuringSpend = generator.Owned;
            };

            Assert.IsTrue(generator.TryBuy(currencies));
            Assert.AreEqual(1, ownedDuringSpend);
        }
    }
}
