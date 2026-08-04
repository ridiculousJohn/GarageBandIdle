using System;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle
{
    // Everything a Condition may read when evaluated or validated. One context
    // serves gates, unlocks, section visibility, and event availability; new
    // systems (the bar system, records) plug in here rather than growing new
    // evaluator entry points.
    //
    // It also carries the aggregate "some condition's answer may have moved"
    // signal, because the set of things a Condition can read is exactly the set
    // of fields here: the four input events below are the complete list, and a
    // new condition type that reads a new system adds its event here rather than
    // teaching every caller to re-evaluate. Subscribing here is what makes the
    // context IDisposable - a discarded context must stop listening to systems
    // that outlive it.
    //
    // A handler's whole job is to set one bool. Nothing evaluates from inside a
    // notification, which is what keeps the state-then-notify guarantee
    // structural: a signal that fires mid-mutation cannot reach an evaluator.
    public class ConditionContext : IDisposable
    {
        public CurrencyManager Currencies { get; }
        public GeneratorSystem Generators { get; }
        public FlagSystem Flags { get; }

        // currency id backing recordsCumulative; Records are never spent, so
        // cumulative Records equals the currency's lifetime-earned total
        public string RecordsCurrencyId { get; }

        // definition registries, used by Validate to resolve content ids;
        // null in unit tests, which validate against the live systems instead
        public ContentDatabase Database { get; }

        // completed-bar counts for barsCompleted (BarSystem in the running game);
        // null in fixtures that stand up no bars, which makes every barsCompleted
        // condition evaluate as unmet
        public IBarCompletionSource Bars { get; }

        // Fired by Drain once evaluation has settled: "conditions have moved,
        // re-ask." One subscription replaces the per-input set a view used to
        // hold, and it arrives after the drain rather than during it, so a
        // subscriber never reads half-applied unlocks.
        public event Action Settled;

        // a fresh context has never been evaluated, so the first drain performs
        // the initial pass
        private bool _dirty = true;

        public ConditionContext(CurrencyManager currencies, GeneratorSystem generators, FlagSystem flags,
            string recordsCurrencyId = "records", ContentDatabase database = null, IBarCompletionSource bars = null)
        {
            Currencies = currencies;
            Generators = generators;
            Flags = flags;
            RecordsCurrencyId = recordsCurrencyId;
            Database = database;
            Bars = bars;

            // the condition inputs, one per readable system: balances and earned
            // totals (currency, currencyEarnedTotal, recordsCumulative) >
            // BalanceChanged, flagSet > FlagSet, ownedCount >
            // GeneratorOwnedChanged, barsCompleted > BarCompleted. Each is
            // null-tolerant because fixtures stand up only the systems their
            // conditions read.
            if (Currencies != null)
                Currencies.BalanceChanged += HandleBalanceChanged;
            if (Flags != null)
                Flags.FlagSet += HandleFlagSet;
            if (Generators != null)
                Generators.GeneratorOwnedChanged += HandleGeneratorOwnedChanged;
            if (Bars != null)
                Bars.BarCompleted += HandleBarCompleted;
        }

        public void Dispose()
        {
            if (Currencies != null)
                Currencies.BalanceChanged -= HandleBalanceChanged;
            if (Flags != null)
                Flags.FlagSet -= HandleFlagSet;
            if (Generators != null)
                Generators.GeneratorOwnedChanged -= HandleGeneratorOwnedChanged;
            if (Bars != null)
                Bars.BarCompleted -= HandleBarCompleted;
        }

        // The drain: runs the given evaluation exactly once if any input moved
        // since the last drain, then publishes Settled. Both halves live here so
        // that consuming the signal without publishing it is not expressible.
        //
        // The flag clears BEFORE evaluating, so anything the evaluation itself
        // dirties - a content unlock's setFlag is the live case - is still
        // pending at the next drain. That is deliberately not a loop to
        // fixpoint: the caller's seam runs every tick, so a second-order chain
        // (a flag that opens a generator's unlock) resolves on the next tick,
        // which is exactly when the per-tick poll this replaced resolved it.
        public void Drain(Action evaluate)
        {
            if (!_dirty)
                return;

            _dirty = false;
            evaluate?.Invoke();
            Settled?.Invoke();
        }

        private void HandleBalanceChanged(string currencyId, BigNumber balance) => _dirty = true;

        private void HandleFlagSet(string flagId) => _dirty = true;

        private void HandleGeneratorOwnedChanged(Economy.Generator generator) => _dirty = true;

        private void HandleBarCompleted(Content.BarState bar) => _dirty = true;
    }
}
