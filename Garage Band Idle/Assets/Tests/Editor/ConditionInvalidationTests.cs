using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The condition invalidation signal: ConditionContext listens to the four
    // inputs a Condition can read and answers "has any of them moved since the
    // last drain?", which is what replaced evaluating every gate on every tick.
    // These cover the drain's contract rather than any single condition type -
    // when it evaluates, when it stays silent, and what it does with work the
    // evaluation itself creates.
    public class ConditionInvalidationTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        // A fresh context has never been evaluated, so it starts dirty: the
        // alternative is a context that reveals nothing until something unrelated
        // happens to move, which would silently drop every unlock whose condition
        // was already met at construction.
        [Test]
        public void FreshContext_Drains_ThenGoesQuietUntilAnInputMoves()
        {
            var currencies = TestContent.MakeEconomy();
            using var context = new ConditionContext(currencies, null, new FlagSystem());
            var evaluations = 0;
            var settled = 0;
            context.Settled += () => settled++;

            context.Drain(() => evaluations++);

            Assert.AreEqual(1, evaluations, "a context that has never been evaluated is dirty");
            Assert.AreEqual(1, settled, "and publishes once it has");

            context.Drain(() => evaluations++);
            context.Drain(() => evaluations++);

            Assert.AreEqual(1, evaluations, "nothing moved, so there is nothing to re-ask");
            Assert.AreEqual(1, settled, "and no reason to wake the views");
        }

        // The four inputs are the whole vocabulary rule 8 can read: balances and
        // earned totals (currency, currencyEarnedTotal, recordsCumulative), flags
        // (flagSet), owned counts (ownedCount) and completed bars (barsCompleted).
        // Each is exercised in isolation - a purchase would move a balance and an
        // owned count together and prove neither.
        [Test]
        public void EveryConditionInput_MarksTheContextDirty()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var generators = new GeneratorSystem(
                new[] { TestContent.MakeGenerator("amp", "cash", 60, 1.15, 0.4) },
                currencies, new ModifierSystem());
            var bars = new RaisableBarCompletion();
            using var context = new ConditionContext(currencies, generators, flags, bars: bars);

            Assert.AreEqual(1, DrainCount(context), "the opening drain");

            currencies.Add("cash", 5);
            Assert.AreEqual(1, DrainCount(context), "a balance moved");

            flags.Set("fans");
            Assert.AreEqual(1, DrainCount(context), "a flag latched");

            generators.RestoreOwned(new Dictionary<string, int> { { "amp", 2 } });
            Assert.AreEqual(1, DrainCount(context), "an owned count moved");

            bars.Complete();
            Assert.AreEqual(1, DrainCount(context), "a bar completed");
        }

        // The flag clears before the evaluation runs, so a setFlag payload applied
        // during the drain leaves the context dirty rather than having its signal
        // swallowed by the drain that caused it. Clearing afterwards would lose
        // the second-order chain entirely - a flag that opens a generator's unlock
        // would never be seen.
        [Test]
        public void WhatTheEvaluationItselfDirties_IsPendingAtTheNextDrain()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            using var context = new ConditionContext(currencies, null, flags);
            context.Drain(null); // clear the opening dirty state

            var evaluations = 0;
            currencies.Add("cash", 5);
            context.Drain(() =>
            {
                evaluations++;

                // the shape a content unlock's payload has: applied from inside
                // the evaluation, and its flag can open another gate
                if (evaluations == 1)
                    flags.Set("fans");
            });

            Assert.AreEqual(1, evaluations, "one pass per drain - the drain does not loop to a fixpoint");

            context.Drain(() => evaluations++);
            Assert.AreEqual(2, evaluations, "the flag set during the pass survived it");

            context.Drain(() => evaluations++);
            Assert.AreEqual(2, evaluations, "and nothing lingers once it has been seen");
        }

        // Settled means "everything has settled, re-ask" - a view that re-asked
        // before the unlocks applied would read the state the drain exists to
        // finish computing.
        [Test]
        public void Settled_FiresAfterTheEvaluation()
        {
            var currencies = TestContent.MakeEconomy();
            using var context = new ConditionContext(currencies, null, new FlagSystem());
            var order = new List<string>();
            context.Settled += () => order.Add("settled");

            context.Drain(() => order.Add("evaluated"));

            Assert.AreEqual(new[] { "evaluated", "settled" }, order);
        }

        // The context subscribes to systems that can outlive it (slice 5.5 makes
        // one per economy, and an unfocused economy's context is discarded), so a
        // disposed one must stop hearing about inputs it no longer reads.
        [Test]
        public void Dispose_StopsListeningToEveryInput()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var generators = new GeneratorSystem(
                new[] { TestContent.MakeGenerator("mic", "cash", 60, 1.15, 0.4) },
                currencies, new ModifierSystem());
            var bars = new RaisableBarCompletion();
            var context = new ConditionContext(currencies, generators, flags, bars: bars);
            context.Drain(null); // clear the opening dirty state

            context.Dispose();

            currencies.Add("cash", 5);
            flags.Set("fans");
            generators.RestoreOwned(new Dictionary<string, int> { { "mic", 1 } });
            bars.Complete();

            Assert.AreEqual(0, DrainCount(context), "a disposed context hears nothing");
        }

        // 1 when the context was dirty, 0 when it was clean
        private static int DrainCount(ConditionContext context)
        {
            var evaluations = 0;
            context.Drain(() => evaluations++);
            return evaluations;
        }

        // a completion source whose count never matters here - the tests need the
        // signal, not the number
        private class RaisableBarCompletion : IBarCompletionSource
        {
            public event Action<Content.BarState> BarCompleted;

            public int CompletedCount(string groupId) => 0;

            public void Complete() => BarCompleted?.Invoke(null);
        }
    }
}
