using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle
{
    // The one record an event leaves in its host scope (design doc 12.8).
    [Serializable]
    public class ActiveEvent
    {
        public string eventId;
        public double remainingSeconds;
        public bool goalReached;
    }

    // A timed buff (Encore) - absolute expiry, burns real time app-closed (design doc 9).
    [Serializable]
    public class TimedBuff
    {
        public string buffId;
        public DateTime expiresAtUtc;
    }

    // A song written during a run (tier = the run's Catalog) or kept forever
    // (root = Discography). Chapter 6 machinery; the field exists because the
    // schema is complete from day one (design doc 12.3).
    [Serializable]
    public class SongEntry
    {
        public string songId;
        public string name;
    }

    // One line of a claim: an amount, and the HOME it lands in. The claim is
    // held at the CHAPTER but addresses what the chapter's production paid into,
    // which includes currencies homed at tiers BELOW it - so this is the one
    // coordinate in the schema that names a scope instead of resolving outward
    // from the scope holding the fact. Scope ids are unique tree-wide, so the
    // name is exact, and the save resolves it in one named step - downward from
    // the chapter, or outward for a currency homed further out - with lookups it
    // keeps private, since this is the only coordinate that needs them.
    [Serializable]
    public class ClaimEntry
    {
        public string scopeId;
        public string currencyId;
        public BigNumber amount;
    }

    // The idle dialog's exactly-once claim transaction (design doc 12.9).
    [Serializable]
    public class PendingClaim
    {
        public string claimId;
        public List<ClaimEntry> amounts = new();
        public bool doubled;
        public bool settled;
    }

    // Every mutable fact a reset destroys, in one replaceable payload. Reset
    // REPLACES this object wholesale, which is what makes clearing complete by
    // construction (design doc 12.3): a field added here next month is cleared
    // because it is here - no clear method to forget to update.
    // Abstract, like every payload above a leaf: a scope class names the
    // concrete type it wants, so nothing can hold a payload by default.
    [Serializable]
    public abstract class ScopeFacts
    {
        public Dictionary<string, BigNumber> balances = new();
        public Dictionary<string, BigNumber> earnedTotals = new();     // per currency, same home as its balance
        public Dictionary<string, int> generatorCounts = new();
        public HashSet<string> flags = new();
        public HashSet<string> purchasedUpgrades = new();
        public HashSet<string> firedTriggers = new();                  // one-shot trigger latches - a reset re-arms
        public Dictionary<string, BigNumber> barProgress = new();      // uncapped - overfill is allowed
        public Dictionary<string, int> fillCounts = new();             // repeating bars
        public Dictionary<string, HashSet<string>> activeBars = new(); // per group
        public Dictionary<string, int> modifierStacks = new();   // granted stacks, keyed like every other count fact
        public List<TimedBuff> timedBuffs = new();
        public List<SongEntry> songs = new();
    }

    // Facts a scope that can HOST an event holds - tiers and chapters. Root
    // cannot: its handicaps would gather into every chapter's walk and its
    // occupancy would be global, so a root event declaration is refused at load
    // (12.12) and the record it would leave is not even representable here.
    // At most one per host (design doc 12.8): a field rather than a list, so a
    // second record cannot exist instead of being something the save filter has
    // to police - picking a survivor between two is a choice nothing justifies.
    // Never a payload itself - only the base the interior leaves derive, so
    // "can host an event" is a fact of the type rather than a question asked
    // of a scope that might answer no. The parallel of InteriorDefinition.
    [Serializable]
    public abstract class InteriorFacts : ScopeFacts
    {
        public ActiveEvent activeEvent;
    }

    // A tier's payload. Nothing beyond what hosting brings; it exists because
    // a tier names its own concrete type like every other scope class does.
    [Serializable]
    public class TierFacts : InteriorFacts
    {
    }

    // Facts only the root holds. A separate payload rather than fields every
    // scope carries: a tier that cannot use them should not be able to hold
    // them, and the type says so instead of a load-time filter.
    [Serializable]
    public class RootFacts : ScopeFacts
    {
        public Dictionary<string, int> roadieAllocation = new();       // chapterId to stationed count
        public HashSet<string> entitlements = new();                   // store-written
    }

    // Facts only a chapter holds. A chapter can host an event, so it builds on
    // the host payload rather than the common base.
    [Serializable]
    public class ChapterFacts : InteriorFacts
    {
        public PendingClaim pendingClaim;
    }

    // A scope is a plain state container; the save IS the tree of these (design
    // doc 12.3/12.10). The COMPLETE mutable state is the facts payload; a
    // chapter adds lastActiveUtc OUTSIDE its payload on purpose - it is the one
    // field a reset re-stamps rather than clears (a fresh chapter owes no idle).
    public abstract class ScopeState
    {
        public readonly ScopeDefinition Definition;
        public readonly ScopeState Parent;
        public readonly List<ScopeState> Children = new();

        // Readable anywhere, replaceable only through Clear and the load path:
        // the payload's TYPE is the placement invariant, so an assignment that
        // swapped it would put root facts on a tier by the back door.
        public ScopeFacts facts { get; private set; }

        // Delegating accessors: callers read and mutate the current payload
        // without knowing reset is a payload swap.
        public Dictionary<string, BigNumber> balances => facts.balances;
        public Dictionary<string, BigNumber> earnedTotals => facts.earnedTotals;
        public Dictionary<string, int> generatorCounts => facts.generatorCounts;
        public HashSet<string> flags => facts.flags;
        public HashSet<string> purchasedUpgrades => facts.purchasedUpgrades;
        public HashSet<string> firedTriggers => facts.firedTriggers;
        public Dictionary<string, BigNumber> barProgress => facts.barProgress;
        public Dictionary<string, int> fillCounts => facts.fillCounts;
        public Dictionary<string, HashSet<string>> activeBars => facts.activeBars;
        public Dictionary<string, int> modifierStacks => facts.modifierStacks;
        public List<TimedBuff> timedBuffs => facts.timedBuffs;
        public List<SongEntry> songs => facts.songs;

        public string ScopeId => Definition.Id;

        // The definition as the kind this node was built from. Only the save's
        // write path needs it: everything else reads Definition base-typed,
        // because a chain walk crosses all three kinds in one loop.
        public T DefinitionAs<T>() where T : ScopeDefinition => (T)Definition;

        // Stores references. Nothing else happens during construction, so no
        // virtual member runs before the object it belongs to exists.
        protected ScopeState(ScopeDefinition definition, ScopeState parent, ScopeFacts payload)
        {
            Definition = definition;
            Parent = parent;
            facts = payload;
        }

        // The payload a reset installs, named by the type argument the class
        // supplied. Never called from a constructor - the payload is built as a
        // constructor argument, so no virtual dispatch happens before the object
        // exists.
        protected abstract ScopeFacts NewFacts();

        // Builds the state tree the definition tree describes. Each definition
        // makes its own node, so a scope's kind is what it was authored as -
        // there is no depth test here inferring one.
        public static RootScopeState Build(RootDefinition rootDefinition)
        {
            var root = rootDefinition.CreateRoot();
            root.InitializeDeclared();
            foreach (var chapterDefinition in rootDefinition.children)
                BuildChild(chapterDefinition, root);
            return root;
        }

        private static ScopeState BuildChild(ScopeDefinition definition, ScopeState parent)
        {
            // Seeding is its own step, after the node exists: the declared
            // facts a subclass adds are read through a virtual, and nothing
            // virtual can run inside a constructor.
            var state = definition.CreateState(parent);
            state.InitializeDeclared();
            parent.Children.Add(state);
            foreach (var childDefinition in definition.children)
                BuildChild(childDefinition, state);
            return state;
        }

        // This scope's own factor for one number (design doc 12.6). The caller
        // multiplies what each scope on the chain returns and never learns what
        // a factor is made of, so a kind of scope that has a source the others
        // do not adds it here rather than in the walk.
        internal virtual BigNumber MultiplierFor(GameContext origin, Definition owner,
                                                 CurrencyDefinition currency, string stat)
        {
            var product = BigNumber.One;

            // Purchased upgrades, read through the DECLARATION list: the order
            // is the authored one, and a latch for an upgrade this scope never
            // declared cannot contribute.
            foreach (var upgrade in Definition.upgrades)
            {
                if (upgrade == null || !purchasedUpgrades.Contains(upgrade.Id))
                    continue;
                foreach (var effect in upgrade.effects)
                    if (Producer.Matches(effect.target, effect.currencyId, effect.stat, owner, currency, stat))
                        product *= Producer.FactorOf(effect, origin);
            }

            // Permanent memberships contribute an implicit application count of
            // 1, MERGED with this scope's stored stacks for the same modifier
            // and resolved through the modifier's own stacking kind - Replace
            // means permanent-plus-granted is still one application, so the two
            // paths can never double-apply outside the vocabulary (12.5). Ids
            // are chain-unique, so the stack's id names this same asset.
            foreach (var modifier in Definition.permanentModifiers)
            {
                if (modifier == null || !Applies(modifier, origin))
                    continue;
                modifierStacks.TryGetValue(modifier.Id, out var stacks);
                foreach (var effect in modifier.effects)
                    if (Producer.Matches(effect.target, effect.currencyId, effect.stat, owner, currency, stat))
                        product *= Producer.Stacked(Producer.FactorOf(effect, origin), 1 + stacks, modifier.stacking);
            }

            // Granted modifier stacks: the stored count scales the effect by the
            // definition's own stacking kind (design doc 12.5). The stack is a
            // count here; the definition resolves OUTWARD, since a chapter's
            // modifier can be granted anywhere inside it (design doc 8.2/12.5).
            foreach (var pair in modifierStacks)
            {
                var modifier = Producer.FindModifier(this, pair.Key);
                if (Definition.permanentModifiers.Contains(modifier))
                    continue;                       // merged into the permanent application above
                if (!Applies(modifier, origin))
                    continue;
                foreach (var effect in modifier.effects)
                    if (Producer.Matches(effect.target, effect.currencyId, effect.stat, owner, currency, stat))
                        product *= Producer.Stacked(Producer.FactorOf(effect, origin), pair.Value, modifier.stacking);
            }

            // Repeating-bar cascades: a completed fill applies the carrying
            // entry's effect again, scaled by the entry's own growth kind
            // (design doc 12.6/12.7). Read through the DECLARATION list, like
            // upgrades: a stray fillCount for a bar this scope never declared
            // cannot contribute.
            foreach (var group in Definition.barGroups)
            {
                if (group == null)
                    continue;
                foreach (var bar in group.bars)
                {
                    if (bar == null || !fillCounts.TryGetValue(bar.Id, out var fills) || fills <= 0)
                        continue;
                    foreach (var entry in bar.perFill)
                    {
                        if (entry == null)
                            continue;
                        if (Producer.Matches(entry.effect.target, entry.effect.currencyId, entry.effect.stat, owner, currency, stat))
                            product *= Producer.Grown(Producer.FactorOf(entry.effect, origin), fills, entry.growth);
                    }
                }
            }

            return product;
        }

        // Whether a modifier applies under this gather's circumstance, judged
        // against the ORIGIN context - the same evaluation-context ruling
        // formulas follow. Absent means always (design doc 12.5).
        private static bool Applies(Economy.ModifierDefinition modifier, GameContext origin) =>
            modifier.appliesWhen == null || modifier.appliesWhen.Evaluate(origin);

        // The rate this scope's own sources pay into one currency, before the
        // currency stage. Asked of every node in a subtree walk, same contract
        // as MultiplierFor: the caller sums, the scope decides what it has. The
        // context rebases rather than being rebuilt, so the circumstance rides
        // through to every entry condition and gather it implies.
        internal virtual BigNumber SourceTermsFor(GameContext ctx, CurrencyDefinition currency, string stat)
        {
            var declaringCtx = ctx.Rebase(this);
            var sum = BigNumber.Zero;

            foreach (var producer in Definition.producers)
            {
                if (producer == null)
                    continue;
                sum += Producer.SourceTerm(declaringCtx, producer, producer.produces, 1, currency, stat);
            }
            foreach (var generator in Definition.generators)
            {
                if (generator == null)
                    continue;
                if (!generatorCounts.TryGetValue(generator.Id, out var owned) || owned <= 0)
                    continue;
                sum += Producer.SourceTerm(declaringCtx, generator, generator.produces, owned, currency, stat);
            }
            return sum;
        }

        // Declared currencies get their balance and earned-total entries at the
        // home scope; a chain walk finds the holder by key presence. Virtual
        // because Clear re-runs it: a derived payload with its own keys seeds
        // them here or a reset leaves them missing.
        internal virtual void InitializeDeclared()
        {
            foreach (var currencyId in Definition.currencyIds)
            {
                facts.balances[currencyId] = BigNumber.Zero;
                facts.earnedTotals[currencyId] = BigNumber.Zero;
            }
        }

        // Reset semantics (design doc 12.3): swap in a fresh payload - complete
        // by construction - and re-initialize declared currency entries.
        // Downward closure is the CALLER's job (ResetScope recurses); this
        // clears one scope. The root refusal lives HERE, on the primitive, so no
        // caller can bypass it (12.12: "never the root"); reaching it is a code
        // bug, hence the throw rather than the action layer's log-and-refuse.
        public virtual void Clear(DateTime nowUtc)
        {
            if (Parent == null)
                throw new InvalidOperationException("The root scope is never resettable (design doc 12.12).");
            facts = NewFacts();
            InitializeDeclared();
        }

        // The node standing for this definition, searched downward from here
        // (self included). A definition and its state never point at each other,
        // so the walk is the only link - but what it matches on is the asset the
        // caller already holds, which is why nothing here depends on ids being
        // unique. A scope has no lookup BY NAME at all: the save owns its own,
        // privately, because a file holds text and nothing else (12.3).
        public ScopeState FindInSubtree(ScopeDefinition scope)
        {
            if (Definition == scope)
                return this;
            foreach (var child in Children)
            {
                var found = child.FindInSubtree(scope);
                if (found != null)
                    return found;
            }
            return null;
        }

        // Self or an ancestor standing for this definition; null when it is not
        // on the chain.
        public ScopeState FindOnChain(ScopeDefinition scope)
        {
            for (var node = this; node != null; node = node.Parent)
                if (node.Definition == scope)
                    return node;
            return null;
        }
    }

    // A scope that has named its payload type. The type argument IS the naming,
    // so allocation, reset and typed access all follow from it and none of them
    // can disagree. The definition needs no type parameter: each leaf's own
    // constructor types it, which is where a mismatched pair would be caught.
    public abstract class ScopeState<TFacts> : ScopeState where TFacts : ScopeFacts, new()
    {
        protected ScopeState(ScopeDefinition definition, ScopeState parent)
            : base(definition, parent, new TFacts()) { }

        // The one cast in the hierarchy. It has to be a cast rather than a typed
        // field because reset REPLACES the payload.
        public TFacts Facts => (TFacts)facts;

        protected override ScopeFacts NewFacts() => new TFacts();
    }

    // A scope that can host an event - the walk's answer when a caller needs a
    // host, so root is excluded by type rather than by a check (12.8). Holds
    // the record accessor and folds handicaps into the multiplier gather.
    public abstract class InteriorScopeState : ScopeState
    {
        protected InteriorScopeState(InteriorDefinition definition, ScopeState parent, InteriorFacts payload)
            : base(definition, parent, payload) { }

        // The payload-type invariant makes this the same one cast as
        // ScopeState<TFacts>.Facts: an interior node only ever holds
        // InteriorFacts.
        public ActiveEvent activeEvent
        {
            get => ((InteriorFacts)facts).activeEvent;
            set => ((InteriorFacts)facts).activeEvent = value;
        }

        // Handicaps ride on the record EXISTING - no expiry check, because a
        // failed attempt sits one tap from a reset and briefly lifting the
        // handicap there would be the worse state (12.8). Read through the
        // declaration list, like upgrades: a stray record id contributes
        // nothing. No count scaling - there is one record.
        internal override BigNumber MultiplierFor(GameContext origin, Definition owner,
                                                  CurrencyDefinition currency, string stat)
        {
            var product = base.MultiplierFor(origin, owner, currency, stat);
            var record = activeEvent;
            if (record == null)
                return product;
            foreach (var evt in ((InteriorDefinition)Definition).events)
            {
                if (evt == null || evt.Id != record.eventId)
                    continue;
                foreach (var effect in evt.handicaps)
                    if (Producer.Matches(effect.target, effect.currencyId, effect.stat, owner, currency, stat))
                        product *= Producer.FactorOf(effect, origin);
            }
            return product;
        }
    }

    // The typed-payload layer for interior scopes: the same three members as
    // ScopeState<TFacts>, duplicated because C# cannot interpose a non-generic
    // base under a generic one - and the walk needs InteriorScopeState as a
    // bare type to ask for.
    public abstract class InteriorScopeState<TFacts> : InteriorScopeState where TFacts : InteriorFacts, new()
    {
        protected InteriorScopeState(InteriorDefinition definition, ScopeState parent)
            : base(definition, parent, new TFacts()) { }

        public TFacts Facts => (TFacts)facts;

        protected override ScopeFacts NewFacts() => new TFacts();
    }

    // The one scope nothing resets, and the only holder of career facts.
    public class RootScopeState : ScopeState<RootFacts>
    {
        internal RootScopeState(RootDefinition definition)
            : base(definition, null) { }

        public Dictionary<string, int> roadieAllocation => Facts.roadieAllocation;
        public HashSet<string> entitlements => Facts.entitlements;
    }

    // Root's direct children. The idle claim and lastActiveUtc live here
    // because idle is a per-chapter concept (design doc 12.9).
    public class ChapterScopeState : InteriorScopeState<ChapterFacts>
    {
        internal ChapterScopeState(ChapterDefinition definition, ScopeState parent)
            : base(definition, parent) { }

        public DateTime lastActiveUtc;

        public PendingClaim pendingClaim
        {
            get => Facts.pendingClaim;
            set => Facts.pendingClaim = value;
        }

        // A reset re-stamps the idle clock rather than clearing it: a fresh
        // chapter owes no idle (design doc 12.3).
        public override void Clear(DateTime nowUtc)
        {
            base.Clear(nowUtc);
            lastActiveUtc = nowUtc;
        }
    }

    // Everything below a chapter, at any depth - the tree nests freely, and
    // one class covers every level because TierDefinition does.
    public class TierScopeState : InteriorScopeState<TierFacts>
    {
        internal TierScopeState(TierDefinition definition, ScopeState parent)
            : base(definition, parent) { }
    }
}
