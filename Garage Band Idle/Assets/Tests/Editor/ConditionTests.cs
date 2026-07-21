using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The Condition family: one behavioral test per type, the balance-vs-earned
    // distinction, compound all/any semantics, and the fail-closed rules (null
    // compound children, barsCompleted before a bar system exists).
    public class ConditionTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        [Test]
        public void NullCondition_MeansNoGate_AndIsAlwaysMet()
        {
            Assert.IsTrue(ConditionEvaluator.IsMet(null, TestContent.MakeContext(TestContent.MakeEconomy())));
        }

        [Test]
        public void CurrencyBalance_TracksSpending_ButEarnedTotalDoesNot()
        {
            var currencies = TestContent.MakeEconomy();
            var context = TestContent.MakeContext(currencies);
            currencies.Add("cash", 100);
            currencies.Add("cash", -80); // spend down to 20

            Assert.IsFalse(new CurrencyBalanceCondition("cash", 100).Evaluate(context),
                "balance condition re-checks the current balance");
            Assert.IsTrue(new CurrencyEarnedTotalCondition("cash", 100).Evaluate(context),
                "earned-total condition survives spending");
        }

        [Test]
        public void OwnedCount_ChecksGeneratorOwnership()
        {
            var currencies = TestContent.MakeEconomy();
            var definition = TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4);
            var generators = new GeneratorSystem(new[] { definition }, currencies);
            var context = TestContent.MakeContext(currencies, generators);
            var condition = new OwnedCountCondition("amp", 2);

            Assert.IsFalse(condition.Evaluate(context));

            TestContent.BuyTimes(generators.Get("amp"), currencies, 2);

            Assert.IsTrue(condition.Evaluate(context));
        }

        [Test]
        public void FlagSet_ChecksFlagSystem()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var context = TestContent.MakeContext(currencies, flags: flags);
            var condition = new FlagSetCondition("fans");

            Assert.IsFalse(condition.Evaluate(context));

            flags.Set("fans");

            Assert.IsTrue(condition.Evaluate(context));
        }

        [Test]
        public void RecordsCumulative_ReadsLifetimeEarned()
        {
            var currencies = TestContent.MakeEconomy();
            var context = TestContent.MakeContext(currencies);
            var condition = new RecordsCumulativeCondition(30);

            currencies.Add("records", 29);
            Assert.IsFalse(condition.Evaluate(context));

            currencies.Add("records", 1);
            Assert.IsTrue(condition.Evaluate(context), "cumulative records reach the gate");
        }

        [Test]
        public void BarsCompleted_FailsClosed_UntilABarSystemExists()
        {
            // no IBarCompletionSource is wired until the bars slice; the
            // condition must evaluate as unmet rather than accidentally passing
            var context = TestContent.MakeContext(TestContent.MakeEconomy());

            Assert.IsFalse(new BarsCompletedCondition("learn_covers", 1).Evaluate(context));
        }

        [Test]
        public void Compound_All_RequiresEveryChild()
        {
            var currencies = TestContent.MakeEconomy();
            var context = TestContent.MakeContext(currencies);
            currencies.Add("cash", 100);
            var condition = new CompoundCondition(new List<Condition>
            {
                new CurrencyEarnedTotalCondition("cash", 50),
                new CurrencyEarnedTotalCondition("cash", 500),
            }, null);

            Assert.IsFalse(condition.Evaluate(context));

            currencies.Add("cash", 400);

            Assert.IsTrue(condition.Evaluate(context));
        }

        [Test]
        public void Compound_Any_RequiresAtLeastOneChild()
        {
            var currencies = TestContent.MakeEconomy();
            var context = TestContent.MakeContext(currencies);
            var condition = new CompoundCondition(null, new List<Condition>
            {
                new CurrencyEarnedTotalCondition("cash", 500),
                new CurrencyEarnedTotalCondition("fans", 10),
            });

            Assert.IsFalse(condition.Evaluate(context));

            currencies.Add("fans", 10); // only the second leg

            Assert.IsTrue(condition.Evaluate(context));
        }

        [Test]
        public void Compound_MixedAllAndAny_RequiresBoth()
        {
            var currencies = TestContent.MakeEconomy();
            var context = TestContent.MakeContext(currencies);
            var condition = new CompoundCondition(
                new List<Condition> { new CurrencyEarnedTotalCondition("cash", 100) },
                new List<Condition> { new CurrencyEarnedTotalCondition("fans", 10) });

            currencies.Add("cash", 100);
            Assert.IsFalse(condition.Evaluate(context), "all met, any not met");

            currencies.Add("fans", 10);
            Assert.IsTrue(condition.Evaluate(context));
        }

        [Test]
        public void Compound_NullChild_FailsClosed()
        {
            var currencies = TestContent.MakeEconomy();
            var context = TestContent.MakeContext(currencies);
            currencies.Add("cash", 100);
            var condition = new CompoundCondition(new List<Condition>
            {
                new CurrencyEarnedTotalCondition("cash", 50),
                null,
            }, null);

            Assert.IsFalse(condition.Evaluate(context), "a null child must never pass");
        }

        [Test]
        public void FlagSystem_KnownList_MarksUndeclaredFlags()
        {
            var flags = new FlagSystem(new[] { "fans", "covers", "album" });

            Assert.IsTrue(flags.IsKnown("fans"));
            Assert.IsFalse(flags.IsKnown("backroom"), "undeclared flag is unknown");

            var unrestricted = new FlagSystem();
            Assert.IsTrue(unrestricted.IsKnown("anything"), "no declared list means unrestricted");
        }
    }
}
