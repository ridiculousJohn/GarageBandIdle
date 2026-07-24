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

        [Test]
        public void TotalPerSecond_SumsOnlyTheRequestedCurrency()
        {
            var currencies = TestContent.MakeEconomy();
            var cashGen = new Generator(TestContent.MakeGenerator("cash_gen", "cash", 10, 1.15, 3));
            var recordsGen = new Generator(TestContent.MakeGenerator("records_gen", "records", 10, 1.15, 50));
            TestContent.BuyTimes(cashGen, currencies, 4);
            TestContent.BuyTimes(recordsGen, currencies, 2);
            var generators = new[] { cashGen, recordsGen };

            var cashPerSecond = ProductionCalculator.TotalPerSecond(generators, "cash", BigNumber.One);
            var recordsPerSecond = ProductionCalculator.TotalPerSecond(generators, "records", BigNumber.One);

            Assert.AreEqual(12.0, cashPerSecond.ToDouble(), 1e-9);   // 3 × 4 owned
            Assert.AreEqual(100.0, recordsPerSecond.ToDouble(), 1e-9); // 50 × 2 owned
        }

        [Test]
        public void TotalPerSecond_AppliesIncomeMultiplier()
        {
            var currencies = TestContent.MakeEconomy();
            var generator = new Generator(TestContent.MakeGenerator("gen", "cash", 10, 1.15, 5));
            TestContent.BuyTimes(generator, currencies, 2);

            var perSecond = ProductionCalculator.TotalPerSecond(new[] { generator }, "cash", 1.5);

            Assert.AreEqual(15.0, perSecond.ToDouble(), 1e-9); // 5 × 2 × 1.5
        }

        [Test]
        public void Tick_AddsProductionTimesSeconds()
        {
            var currencies = TestContent.MakeEconomy();
            var definition = TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4);
            var system = new GeneratorSystem(new[] { definition }, currencies);
            TestContent.BuyTimes(system.Get("amp"), currencies, 1);
            var before = currencies.Get("cash");

            system.Tick(10.0, BigNumber.One, new[] { "cash" });

            Assert.AreEqual(4.0, (currencies.Get("cash") - before).ToDouble(), 1e-9); // 0.4/sec × 10s
        }

        // a multiplier is an output effect that declares its targets: production
        // of a currency it doesn't name is untouched, no matter what generators
        // exist — fans/records producers must never inherit the cash buff
        [Test]
        public void Tick_AppliesTheMultiplierOnlyToTheCurrenciesItDeclares()
        {
            var currencies = TestContent.MakeEconomy();
            var cashGen = TestContent.MakeGenerator("cash_gen", "cash", 10, 1.15, 3);
            var fansGen = TestContent.MakeGenerator("fans_gen", "fans", 10, 1.15, 5);
            var system = new GeneratorSystem(new[] { cashGen, fansGen }, currencies);
            TestContent.BuyTimes(system.Get("cash_gen"), currencies, 1);
            TestContent.BuyTimes(system.Get("fans_gen"), currencies, 1);
            var cashBefore = currencies.Get("cash");
            var fansBefore = currencies.Get("fans");

            system.Tick(10.0, 2.0, new[] { "cash" });

            Assert.AreEqual(60.0, (currencies.Get("cash") - cashBefore).ToDouble(), 1e-9); // 3 × 2 × 10s
            Assert.AreEqual(50.0, (currencies.Get("fans") - fansBefore).ToDouble(), 1e-9,
                "undeclared currency takes no multiplier"); // 5 × 1 × 10s
        }

        [Test]
        public void TryBuy_DeductsCostAndFailsWhenUnaffordable()
        {
            var currencies = TestContent.MakeEconomy();
            var generator = new Generator(TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4));

            currencies.Add("cash", 100);
            Assert.IsTrue(generator.TryBuy(currencies));
            Assert.AreEqual(1, generator.Owned);
            Assert.AreEqual(40.0, currencies.Get("cash").ToDouble(), 1e-9);

            // next cost is 69; 40 on hand is not enough
            Assert.IsFalse(generator.TryBuy(currencies));
            Assert.AreEqual(1, generator.Owned);
            Assert.AreEqual(40.0, currencies.Get("cash").ToDouble(), 1e-9);
        }

        // fail closed on broken content: a negative base output (invalid data —
        // boot validation reports it) must never drain a currency
        [Test]
        public void ProductionPerSecond_FailsClosedOnANegativeBaseOutput()
        {
            var currencies = TestContent.MakeEconomy();
            var generator = new Generator(TestContent.MakeGenerator("leak", "cash", 10, 1.15, -5));
            TestContent.BuyTimes(generator, currencies, 1);

            Assert.AreEqual(0.0, generator.ProductionPerSecond.ToDouble(), 1e-9, "never negative production");
        }

        // fail closed on broken content: a non-positive cost (invalid data —
        // boot validation reports it) must never be an endless free purchase
        [Test]
        public void TryBuy_FailsClosedOnANonPositiveCost()
        {
            var currencies = TestContent.MakeEconomy();
            var generator = new Generator(TestContent.MakeGenerator("broken", "cash", 0, 0, 1));
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
            var generator = new Generator(TestContent.MakeGenerator("merch_stand", "fans", 60, 1.15, 1));
            currencies.Add("cash", 100);

            Assert.IsTrue(generator.TryBuy(currencies));
            Assert.AreEqual(40.0, currencies.Get("cash").ToDouble(), 1e-9, "the declared cost currency is charged");
            Assert.AreEqual(0.0, currencies.Get("fans").ToDouble(), 1e-9, "the produced currency is untouched by a purchase");
        }

        // run reset (album release, event baseline; design doc section 7):
        // gear and bandmates are re-bought each run, so every owned count
        // zeroes — and no subscriber may ever observe a half-reset fleet
        // (state, then notify)
        [Test]
        public void ResetOwned_ZeroesEveryGenerator_AndNotifiesAfterAllSettle()
        {
            var currencies = TestContent.MakeEconomy();
            var system = new GeneratorSystem(new[]
            {
                TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4),
                TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3),
            }, currencies);
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

        // save/load: the fleet restores as one atomic operation — every count
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
            }, currencies);

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
            }, currencies);

            LogAssert.Expect(LogType.Error, "GeneratorSystem: RestoreOwned with unknown generator id 'ghost'. Skipping it.");
            system.RestoreOwned(new Dictionary<string, int> { { "ghost", 3 } });

            LogAssert.Expect(LogType.Error, "Generator: RestoreOwned with negative count '-1' for 'amp'. Restoring zero.");
            system.RestoreOwned(new Dictionary<string, int> { { "amp", -1 } });
            Assert.AreEqual(0, system.Get("amp").Owned);
        }

        // state-then-notify: the spend's BalanceChanged is a synchronous signal
        // that condition evaluators react to, so the purchase must already be
        // counted when it fires — an ownedCount gate may never observe the cost
        // deducted with Owned still stale
        [Test]
        public void TryBuy_OwnedIsCountedBeforeTheSpendNotifies()
        {
            var currencies = TestContent.MakeEconomy();
            var generator = new Generator(TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4));
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
