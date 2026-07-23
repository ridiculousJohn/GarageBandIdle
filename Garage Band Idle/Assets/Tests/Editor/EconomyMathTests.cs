using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

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

            system.Tick(10.0, BigNumber.One);

            Assert.AreEqual(4.0, (currencies.Get("cash") - before).ToDouble(), 1e-9); // 0.4/sec × 10s
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
