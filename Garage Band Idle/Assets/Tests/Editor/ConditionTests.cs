using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

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
            var generators = new GeneratorSystem(new[] { definition }, currencies, new ModifierSystem());
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

        // A threshold of zero or less is satisfied by an empty balance, an unowned
        // generator and an untouched bar group, so an always-open gate is exactly
        // what a mistyped or defaulted threshold produces. Every threshold type
        // fails closed on it instead, the same rule a null compound child follows.
        [Test]
        public void NonPositiveThreshold_FailsClosed_ForEveryThresholdType()
        {
            var currencies = TestContent.MakeEconomy();
            var definition = TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4);
            var generators = new GeneratorSystem(new[] { definition }, currencies, new ModifierSystem());
            var bars = new StubBarCompletion(3);
            var context = new ConditionContext(currencies, generators, new FlagSystem(), bars: bars);

            // every actual value here is comfortably above zero, so only the
            // threshold rule can be what refuses these
            currencies.Add("cash", 500);
            currencies.Add("records", 40);
            TestContent.BuyTimes(generators.Get("amp"), currencies, 2);

            Assert.IsFalse(new CurrencyBalanceCondition("cash", 0).Evaluate(context), "currency");
            Assert.IsFalse(new CurrencyEarnedTotalCondition("cash", 0).Evaluate(context), "currencyEarnedTotal");
            Assert.IsFalse(new OwnedCountCondition("amp", 0).Evaluate(context), "ownedCount");
            Assert.IsFalse(new BarsCompletedCondition("learn_covers", 0).Evaluate(context), "barsCompleted");
            Assert.IsFalse(new RecordsCumulativeCondition(-1).Evaluate(context), "recordsCumulative");
        }

        // the report names the JSON key to fix, since the mistake is in the chapter
        // data rather than in the field it deserialized into - and every condition
        // spells its threshold `value`, including the one whose C# field is still
        // called _amount
        [Test]
        public void NonPositiveThreshold_IsReported_NamingTheJsonKey()
        {
            var currencies = TestContent.MakeEconomy();
            var context = TestContent.MakeContext(currencies, flags: new FlagSystem());

            LogAssert.Expect(LogType.Error,
                "Condition: Upgrade 'x' (gate) has a non-positive value (0) - the gate would be met before play starts.");
            new CurrencyBalanceCondition("cash", 0).Validate(context, "Upgrade 'x' (gate)");

            LogAssert.Expect(LogType.Error,
                "Condition: Section 'y' (visibleWhen) has a non-positive value (0) - the gate would be met before play starts.");
            new CurrencyEarnedTotalCondition("cash", 0).Validate(context, "Section 'y' (visibleWhen)");
        }

        // a positive threshold is silent - the check must not report every gate
        [Test]
        public void PositiveThreshold_IsNotReported()
        {
            var currencies = TestContent.MakeEconomy();
            var context = TestContent.MakeContext(currencies, flags: new FlagSystem());

            new CurrencyBalanceCondition("cash", 250).Validate(context, "Upgrade 'x' (gate)");
            new RecordsCumulativeCondition(30).Validate(context, "Chapter 'ch1' (capstone)");
        }

        // completed-bar counts without a BarSystem, so a threshold test does not
        // have to stand up bar groups to have something above zero to compare
        private class StubBarCompletion : IBarCompletionSource
        {
            private readonly int _completed;

            public StubBarCompletion(int completed) => _completed = completed;

            public int CompletedCount(string groupId) => _completed;

            // a stub count never moves, so there is nothing to subscribe to
            public event System.Action<Content.BarState> BarCompleted { add { } remove { } }
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
