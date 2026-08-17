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
        public ICurrencies Currencies { get; }
        public GeneratorSystem Generators { get; }
        public FlagSystem Flags { get; }

        // The chain this scope resolves through (rule 12): flag reads go
        // outward, any link satisfies. Null outside a scope tree - fixtures
        // standing up bare systems - where the single FlagSystem above is the
        // whole answer. Conditions ask IsFlagSet/IsFlagKnown below rather than
        // choosing between the two themselves.
        public ScopeChain Chain { get; }

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

        // Boot validation's ear, null everywhere else: while the validator's
        // flag-setter sweep is listening, SetFlagEffect.Validate reports the flag
        // it sets here. The sweep sees asset-level truth through the family's own
        // validation traversal - CompoundEffect.Validate already forwards to
        // children - rather than through an external walk into payload internals,
        // and the scope pairing stays at the validator's call site (rule 11: the
        // lifetime belongs to the owning fact).
        public Action<string> FlagSetterReport { get; set; }

        // Fired by Drain once evaluation has settled: "conditions have moved,
        // re-ask." One subscription per view rather than one per condition input,
        // and it arrives after the drain rather than during it, so a subscriber
        // never reads half-applied unlocks.
        public event Action Settled;

        // a fresh context has never been evaluated, so the first drain performs
        // the initial pass
        private bool _dirty = true;

        // nesting depth of SuppressInvalidation scopes; while non-zero the
        // condition inputs are observed but do not dirty the flag
        private int _suppressDepth;

        // nesting depth of DeferSettled scopes, and whether a drain happened inside
        // one - so many drains publish once, on the way out
        private int _deferSettledDepth;
        private bool _settledPending;

        public ConditionContext(ICurrencies currencies, GeneratorSystem generators, FlagSystem flags,
            string recordsCurrencyId = "records", ContentDatabase database = null, IBarCompletionSource bars = null,
            ScopeChain chain = null)
        {
            Currencies = currencies;
            Generators = generators;
            Flags = flags;
            Chain = chain;
            RecordsCurrencyId = recordsCurrencyId;
            Database = database;
            Bars = bars;

            // the condition inputs, one per readable system: balances and earned
            // totals (currency, currencyEarnedTotal, recordsCumulative) >
            // BalanceChanged, flagSet > FlagSet, ownedCount >
            // GeneratorOwnedChanged, barsCompleted > BarCompleted. Each is
            // null-tolerant because fixtures stand up only the systems their
            // conditions read.
            //
            // Two of the inputs already span the chain: BalanceChanged, because
            // the router aggregates every pool in scope, and the flag events,
            // because the chain aggregates every registry in scope - an inner
            // gate on an outer flag must re-evaluate when the outer scope
            // latches it (reads go outward, notifications come inward, rule
            // 12). With a chain the subscription is the chain's aggregate ONLY:
            // its innermost link is the flags system above, so subscribing to
            // both would dirty twice per latch.
            if (Currencies != null)
                Currencies.BalanceChanged += HandleBalanceChanged;
            if (Chain != null)
            {
                // both directions: a cleared flag moves flagSet answers exactly
                // as a set one does, and a run reset is when it happens
                Chain.FlagSet += HandleFlagSet;
                Chain.FlagCleared += HandleFlagSet;
            }
            else if (Flags != null)
            {
                Flags.FlagSet += HandleFlagSet;
                Flags.FlagCleared += HandleFlagSet;
            }
            if (Generators != null)
                Generators.GeneratorOwnedChanged += HandleGeneratorOwnedChanged;
            if (Bars != null)
                Bars.BarCompleted += HandleBarCompleted;
        }

        public void Dispose()
        {
            if (Currencies != null)
                Currencies.BalanceChanged -= HandleBalanceChanged;
            if (Chain != null)
            {
                Chain.FlagSet -= HandleFlagSet;
                Chain.FlagCleared -= HandleFlagSet;
            }
            else if (Flags != null)
            {
                Flags.FlagSet -= HandleFlagSet;
                Flags.FlagCleared -= HandleFlagSet;
            }
            if (Generators != null)
                Generators.GeneratorOwnedChanged -= HandleGeneratorOwnedChanged;
            if (Bars != null)
                Bars.BarCompleted -= HandleBarCompleted;
        }

        // The flag resolution conditions ask (rule 12's table): any link in
        // scope satisfies. One home for the chain-or-single choice, so a
        // condition and its validation cannot make it differently.
        public bool IsFlagSet(string flagId)
            => Chain != null ? Chain.ResolveFlag(flagId) : Flags != null && Flags.IsSet(flagId);

        // Validation's version of the same walk: whether any registry in scope
        // declares the id. True when there is nothing to ask, because a fixture
        // with no flag system is not evidence of a typo.
        public bool IsFlagKnown(string flagId)
            => Chain != null ? Chain.IsFlagKnown(flagId) : Flags == null || Flags.IsKnown(flagId);

        // The drain: runs the given evaluation exactly once if any input moved
        // since the last drain, then publishes Settled. Both halves live here so
        // that consuming the signal without publishing it is not expressible.
        //
        // The flag clears BEFORE evaluating, so anything the evaluation itself
        // dirties - a content unlock's setFlag is the live case - is still
        // pending at the next drain. Drain stays one pass to a call: the loop to a
        // fixpoint lives in the caller's settle seam, which drains again while work
        // is pending, so a second-order chain (a flag that opens a generator's
        // unlock) resolves inside that settle rather than a tick later.
        public void Drain(Action evaluate)
        {
            if (!_dirty)
                return;

            _dirty = false;
            evaluate?.Invoke();
            Publish();
        }

        // Coalesces Settled for the duration of the scope: however many drains run
        // inside it, subscribers hear about it ONCE, on the way out. This does not
        // weaken the "consuming the signal without publishing it is not expressible"
        // property Drain rests on - every drain inside still publishes, the
        // publication is just deferred and merged.
        //
        // It exists for the settle's bounded fixpoint, restore's included. That loop
        // drains repeatedly by design: pass one applies an unlock whose flag opens
        // something else, pass two picks it up. Publishing each pass would hand
        // subscribers exactly the half-derived state the fixpoint exists to prevent -
        // a section visible because pass one latched its flag, beside a row still
        // missing the buff pass two grants.
        public SettledDeferral DeferSettled() => new(this);

        public readonly struct SettledDeferral : IDisposable
        {
            private readonly ConditionContext _context;

            internal SettledDeferral(ConditionContext context)
            {
                _context = context;
                if (_context != null)
                    _context._deferSettledDepth++;
            }

            public void Dispose()
            {
                if (_context == null)
                    return;

                _context._deferSettledDepth--;
                if (_context._deferSettledDepth > 0 || !_context._settledPending)
                    return;

                _context._settledPending = false;
                _context.Settled?.Invoke();
            }
        }

        private void Publish()
        {
            if (_deferSettledDepth > 0)
            {
                _settledPending = true;
                return;
            }

            Settled?.Invoke();
        }

        // Forces the next drain to evaluate. Required by any mutation that changes
        // state WITHOUT publishing - a context-wide restore silences its primitives
        // so no observer sees partial state, which also silences the very events
        // this class listens to. Without this, a restore into an already-settled
        // context leaves _dirty false and Drain returns having evaluated nothing:
        // no unlocks, no Settled, no reveal. It cannot be left to the fresh-context
        // default either, since that is true exactly once and a second restore is
        // the case the guarantee is about.
        public void MarkDirty() => _dirty = true;

        // Whether a drain would evaluate anything. Read by the settle's bounded
        // fixpoint: Drain clears the flag BEFORE evaluating, so a flag or latch the
        // evaluation itself applies leaves work pending, and the settle has to be able
        // to ask whether it is finished rather than assume one pass was enough.
        public bool IsDirty => _dirty;

        // Suppresses invalidation for the duration of the scope. Used by the
        // notification REPLAY at the end of a restore: those events describe state
        // that has already been drained and settled, so letting them dirty the flag
        // would demand a second drain - and then "which Settled is the terminal
        // one" has two answers. Suppressing instead keeps the restore one pass and
        // lets it finish provably clean.
        //
        // Returned as its concrete type so `using` needs no boxing, and nested
        // (depth-counted) because a caller may already be inside one.
        public InvalidationSuppression SuppressInvalidation() => new(this);

        public readonly struct InvalidationSuppression : IDisposable
        {
            private readonly ConditionContext _context;

            internal InvalidationSuppression(ConditionContext context)
            {
                _context = context;
                if (_context != null)
                    _context._suppressDepth++;
            }

            public void Dispose()
            {
                if (_context != null)
                    _context._suppressDepth--;
            }
        }

        // Every condition input funnels through here, so suppression is one rule in
        // one place rather than a check each handler could forget.
        private void Invalidate()
        {
            if (_suppressDepth == 0)
                _dirty = true;
        }

        private void HandleBalanceChanged(string currencyId, BigNumber balance) => Invalidate();

        private void HandleFlagSet(string flagId) => Invalidate();

        private void HandleGeneratorOwnedChanged(Economy.Generator generator) => Invalidate();

        private void HandleBarCompleted(Content.BarState bar) => Invalidate();
    }
}
