using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // GateEvaluator: one behavioral test per condition type, plus the
    // balance-vs-earned distinction and the unknown-type fail-safe.
    public class GateEvaluatorTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private static GateCondition Balance(string currencyId, double amount)
            => new(GateCondition.TypeCurrencyBalance, currencyId, "", "", amount);

        private static GateCondition EarnedTotal(string currencyId, double amount)
            => new(GateCondition.TypeCurrencyEarnedTotal, currencyId, "", "", amount);

        [Test]
        public void EmptyConditionList_IsAlwaysMet()
        {
            Assert.IsTrue(GateEvaluator.AllMet(new List<GateCondition>(), TestContent.MakeEconomy(), null, null));
        }

        [Test]
        public void CurrencyBalance_TracksSpending_ButEarnedTotalDoesNot()
        {
            var currencies = TestContent.MakeEconomy();
            currencies.Add("cash", 100);
            currencies.Add("cash", -80); // spend down to 20

            Assert.IsFalse(GateEvaluator.AllMet(new List<GateCondition> { Balance("cash", 100) }, currencies, null, null),
                "balance gate re-checks the current balance");
            Assert.IsTrue(GateEvaluator.AllMet(new List<GateCondition> { EarnedTotal("cash", 100) }, currencies, null, null),
                "earned-total gate survives spending");
        }

        [Test]
        public void OwnedCount_ChecksGeneratorOwnership()
        {
            var currencies = TestContent.MakeEconomy();
            var definition = TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4);
            var system = new GeneratorSystem(new[] { definition }, currencies, new FlagSystem());
            var gate = new List<GateCondition> { new(GateCondition.TypeOwnedCount, "", "amp", "", 2) };

            Assert.IsFalse(GateEvaluator.AllMet(gate, currencies, system, null));

            TestContent.BuyTimes(system.Get("amp"), currencies, 2);

            Assert.IsTrue(GateEvaluator.AllMet(gate, currencies, system, null));
        }

        [Test]
        public void FlagSet_ChecksFlagSystem()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var gate = new List<GateCondition> { new(GateCondition.TypeFlagSet, "", "", "fans", 0) };

            Assert.IsFalse(GateEvaluator.AllMet(gate, currencies, null, flags));

            flags.Set("fans");

            Assert.IsTrue(GateEvaluator.AllMet(gate, currencies, null, flags));
        }

        [Test]
        public void AllConditionsMustHold()
        {
            var currencies = TestContent.MakeEconomy();
            currencies.Add("cash", 100);
            var gate = new List<GateCondition> { EarnedTotal("cash", 50), EarnedTotal("cash", 500) };

            Assert.IsFalse(GateEvaluator.AllMet(gate, currencies, null, null));
        }

        [Test]
        public void UnknownConditionType_NeverPasses()
        {
            // coversCompleted has no handler until the covers slice; it must
            // evaluate as unmet rather than accidentally passing
            var gate = new List<GateCondition> { new(GateCondition.TypeCoversCompleted, "", "", "", 0) };

            Assert.IsFalse(GateEvaluator.AllMet(gate, TestContent.MakeEconomy(), null, null));
        }
    }
}
