using System;
using System.Collections.Generic;

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
    [Serializable]
    public class ScopeFacts
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
    [Serializable]
    public class EventHostFacts : ScopeFacts
    {
        public ActiveEvent activeEvent;
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
    public class ChapterFacts : EventHostFacts
    {
        public PendingClaim pendingClaim;
    }

    // A scope is a plain state container; the save IS the tree of these (design
    // doc 12.3/12.10). The COMPLETE mutable state is the facts payload; a
    // chapter adds lastActiveUtc OUTSIDE its payload on purpose - it is the one
    // field a reset re-stamps rather than clears (a fresh chapter owes no idle).
    public class ScopeState
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
        // Null on the root, which cannot host an event. Every caller reaches a
        // host through the outward declaration walk, and a root declaration is
        // refused at load, so a null here is a content or caller fault rather
        // than a branch anything takes.
        public EventHostFacts eventHost => facts as EventHostFacts;
        public List<TimedBuff> timedBuffs => facts.timedBuffs;
        public List<SongEntry> songs => facts.songs;

        public string ScopeId => Definition.Id;

        // A tier's payload. Allocated here rather than by a field initializer so
        // every subclass hands in its own instead. A tier can host an event.
        private ScopeState(ScopeDefinition definition, ScopeState parent)
            : this(definition, parent, new EventHostFacts()) { }

        protected ScopeState(ScopeDefinition definition, ScopeState parent, ScopeFacts payload)
        {
            Definition = definition;
            Parent = parent;
            facts = payload;
            InitializeDeclared();
        }

        // Installs a loaded payload. The save reads each node against the type
        // its tree position dictates; this refuses anything else rather than
        // trusting the caller got it right.
        internal void ApplyLoadedFacts(ScopeFacts payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (payload.GetType() != facts.GetType())
                throw new InvalidOperationException(
                    $"Scope '{ScopeId}' holds {facts.GetType().Name}; a {payload.GetType().Name} payload cannot be applied to it.");
            facts = payload;
        }

        // The payload a reset installs. Never called from a constructor - each
        // class's constructor allocates its own, so no virtual dispatch happens
        // before the object exists.
        protected virtual ScopeFacts NewFacts() => new EventHostFacts();

        // Builds the state tree the definition tree describes. The public entry
        // builds a whole tree from its root, so depth decides each node's class
        // instead of a caller passing a parent.
        public static RootScopeState Build(ScopeDefinition rootDefinition)
        {
            var root = new RootScopeState(rootDefinition);
            foreach (var chapterDefinition in rootDefinition.children)
                BuildChild(chapterDefinition, root);
            return root;
        }

        private static ScopeState BuildChild(ScopeDefinition definition, ScopeState parent)
        {
            // Chapters are root's children (12.3); everything deeper is a tier.
            var state = parent.Parent == null
                ? new ChapterScopeState(definition, parent)
                : new ScopeState(definition, parent);
            parent.Children.Add(state);
            foreach (var childDefinition in definition.children)
                BuildChild(childDefinition, state);
            return state;
        }

        // Declared currencies get their balance and earned-total entries at the
        // home scope; a chain walk finds the holder by key presence.
        private void InitializeDeclared()
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

    // The one scope nothing resets, and the only holder of career facts.
    public class RootScopeState : ScopeState
    {
        internal RootScopeState(ScopeDefinition definition)
            : base(definition, null, new RootFacts()) { }

        public RootFacts Facts => (RootFacts)facts;

        public Dictionary<string, int> roadieAllocation => Facts.roadieAllocation;
        public HashSet<string> entitlements => Facts.entitlements;

        protected override ScopeFacts NewFacts() => new RootFacts();
    }

    // Root's direct children. The idle claim and lastActiveUtc live here
    // because idle is a per-chapter concept (design doc 12.9).
    public class ChapterScopeState : ScopeState
    {
        internal ChapterScopeState(ScopeDefinition definition, ScopeState parent)
            : base(definition, parent, new ChapterFacts()) { }

        public ChapterFacts Facts => (ChapterFacts)facts;

        public DateTime lastActiveUtc;

        public PendingClaim pendingClaim
        {
            get => Facts.pendingClaim;
            set => Facts.pendingClaim = value;
        }

        protected override ScopeFacts NewFacts() => new ChapterFacts();

        // A reset re-stamps the idle clock rather than clearing it: a fresh
        // chapter owes no idle (design doc 12.3).
        public override void Clear(DateTime nowUtc)
        {
            base.Clear(nowUtc);
            lastActiveUtc = nowUtc;
        }
    }
}
