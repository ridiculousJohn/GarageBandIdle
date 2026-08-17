using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle
{
    // What is in scope (design doc section 12, rule 12): this scope's truth,
    // then its ancestors', outward to the root, in that order, enabled links
    // only. Exactly ONE implementation of that iteration lives here - the
    // resolvers below and the CurrencyRouter's construction all consume it -
    // because otherwise "in scope" has several answers that can drift.
    //
    // Three resolutions, three public functions, because what each does at a
    // link genuinely differs: a currency has a first owner (CurrencyRouter is
    // that resolver, built over this chain's pools), a flag is satisfied by
    // any link, and modifiers accumulate across every link. "Accumulate a
    // currency" is not a concept, so there is no mode parameter for it.
    //
    // One node per scope, linked outward; a parent's node is shared by every
    // child's. The node is also where "notifications go inward" crosses scope
    // boundaries: it aggregates its own stores' change signals with its outer
    // node's, so an inner subscriber holds ONE subscription however deep the
    // tree - the same shape CurrencyRouter gives balances. That aggregation is
    // why this is IDisposable, and at N levels that disposal discipline is
    // load-bearing: a discarded node still cascading would feed a dead scope's
    // subscribers changes for a ladder nobody is playing.
    public class ScopeChain : IModifierResolver, IDisposable
    {
        private readonly ScopeChain _outer;

        public CurrencyManager Pool { get; }
        public FlagSystem Flags { get; }
        public ModifierSystem Modifiers { get; }
        public ScopeChain Outer => _outer;

        // change signals from this link and every link outward, in one
        // subscription: reads go outward, so an inner gate or row must hear an
        // outer fact move. Balance changes are deliberately absent - the
        // CurrencyRouter already aggregates every pool in scope.
        public event Action<string> FlagSet;
        public event Action<string> FlagCleared;
        public event Action<ModifierSelector> ModifiersChanged;

        public ScopeChain(ScopeChain outer, CurrencyManager pool, FlagSystem flags, ModifierSystem modifiers)
        {
            _outer = outer;
            Pool = pool;
            Flags = flags;
            Modifiers = modifiers;

            if (Flags != null)
            {
                Flags.FlagSet += HandleFlagSet;
                Flags.FlagCleared += HandleFlagCleared;
            }
            if (Modifiers != null)
                Modifiers.Changed += HandleModifiersChanged;

            // the cascade: the outer node already aggregates ITS outward links,
            // so one subscription per node covers the whole chain above it
            if (_outer != null)
            {
                _outer.FlagSet += HandleFlagSet;
                _outer.FlagCleared += HandleFlagCleared;
                _outer.ModifiersChanged += HandleModifiersChanged;
            }
        }

        // The link past the tree's root: the pool the tree hangs under (the
        // permanent pool, for a real tree). Currencies only - it has no flag
        // registry and no modifier store to resolve, which is a fact about the
        // startup pool rather than a special case here.
        public ScopeChain(CurrencyManager outerPool)
        {
            Pool = outerPool;
        }

        public void Dispose()
        {
            if (Flags != null)
            {
                Flags.FlagSet -= HandleFlagSet;
                Flags.FlagCleared -= HandleFlagCleared;
            }
            if (Modifiers != null)
                Modifiers.Changed -= HandleModifiersChanged;
            if (_outer != null)
            {
                _outer.FlagSet -= HandleFlagSet;
                _outer.FlagCleared -= HandleFlagCleared;
                _outer.ModifiersChanged -= HandleModifiersChanged;
            }
        }

        // ---- the one iteration -------------------------------------------------
        //
        // Self outward to the root, enabled links only. Every consumer walks
        // through these two, so which links count has exactly one answer.
        // The enabled filter is the seam 7.5 step 4 fills in - today every
        // link is active, because enablement does not exist yet.

        private ScopeChain FirstLink() => FirstActiveFrom(this);

        private static ScopeChain NextLink(ScopeChain link) => FirstActiveFrom(link._outer);

        private static ScopeChain FirstActiveFrom(ScopeChain link)
        {
            // step 4: skip disabled links here, in this one place
            return link;
        }

        // ---- the three resolutions ---------------------------------------------

        // A currency resolves to its first owner outward - one balance. That
        // resolver is CurrencyRouter, built over this walk; this collector is
        // how it takes the walk rather than re-implementing it. The router
        // flattens the result into a map at construction (a cache of the walk,
        // not a per-read walk), which step 4 must rebuild when enabling or
        // disabling a scope changes what the walk yields.
        internal void CollectPools(List<CurrencyManager> into)
        {
            for (var link = FirstLink(); link != null; link = NextLink(link))
            {
                if (link.Pool != null)
                    into.Add(link.Pool);
            }
        }

        // a flag is satisfied by ANY link that has it set
        public bool ResolveFlag(string flagId)
        {
            for (var link = FirstLink(); link != null; link = NextLink(link))
            {
                if (link.Flags != null && link.Flags.IsSet(flagId))
                    return true;
            }
            return false;
        }

        // whether any link's registry declares the flag - validation's
        // question, chain-wide for the same reason gating on it is
        public bool IsFlagKnown(string flagId)
        {
            for (var link = FirstLink(); link != null; link = NextLink(link))
            {
                if (link.Flags != null && link.Flags.IsKnown(flagId))
                    return true;
            }
            return false;
        }

        // Every link contributes: the composition over the whole chain. Each
        // link's store answers with its own composition - the ONE matching
        // rule, asked store by store - and the fold multiplies them, which is
        // exactly composing the concatenated modifier list because a
        // composition is a product and nothing else (ModifierComposition: no
        // Add, so no application order exists to get wrong).
        public ModifierComposition For(in ModifierSubject subject)
        {
            var multiply = BigNumber.One;
            for (var link = FirstLink(); link != null; link = NextLink(link))
            {
                if (link.Modifiers != null)
                    multiply *= link.Modifiers.For(subject).Multiply;
            }
            return new ModifierComposition(multiply);
        }

        private void HandleFlagSet(string flagId) => FlagSet?.Invoke(flagId);

        private void HandleFlagCleared(string flagId) => FlagCleared?.Invoke(flagId);

        private void HandleModifiersChanged(ModifierSelector selector) => ModifiersChanged?.Invoke(selector);
    }
}
