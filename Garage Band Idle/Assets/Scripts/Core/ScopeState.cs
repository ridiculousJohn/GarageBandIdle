using System;
using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle
{
    // Facts a granted modifier leaves in state: a pointer plus a stack count.
    // The numbers stay on the ModifierDefinition (design doc 12.5).
    [Serializable]
    public class ActiveModifierEntry
    {
        public string modifierId;
        public int count;
    }

    // The one record an event leaves in its host scope (design doc 12.8).
    [Serializable]
    public class ActiveEvent
    {
        public string eventId;
        public double remainingSeconds;
        public bool goalReached;
        public bool claimed;
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

    // The idle dialog's exactly-once claim transaction (design doc 12.9).
    [Serializable]
    public class PendingClaim
    {
        public string claimId;
        public Dictionary<string, BigNumber> amounts = new();
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
        public List<ActiveModifierEntry> activeModifiers = new();
        public List<ActiveEvent> activeEvents = new();
        public List<TimedBuff> timedBuffs = new();
        public List<SongEntry> songs = new();
        public Dictionary<string, int> roadieAllocation = new();       // root only - chapterId to stationed count
        public HashSet<string> entitlements = new();                   // root only - store-written
        public PendingClaim pendingClaim;                              // chapters only
    }

    // A scope is a plain state container; the save IS the tree of these (design
    // doc 12.3/12.10). The COMPLETE mutable state is the facts payload plus
    // lastActiveUtc - which lives OUTSIDE the payload on purpose: it is the one
    // field a reset re-stamps rather than clears (a fresh chapter owes no idle).
    public class ScopeState
    {
        public readonly ScopeDefinition Definition;
        public readonly ScopeState Parent;
        public readonly List<ScopeState> Children = new();

        public ScopeFacts facts = new();
        public DateTime lastActiveUtc;                                 // chapters only

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
        public List<ActiveModifierEntry> activeModifiers => facts.activeModifiers;
        public List<ActiveEvent> activeEvents => facts.activeEvents;
        public List<TimedBuff> timedBuffs => facts.timedBuffs;
        public List<SongEntry> songs => facts.songs;
        public Dictionary<string, int> roadieAllocation => facts.roadieAllocation;
        public HashSet<string> entitlements => facts.entitlements;
        public PendingClaim pendingClaim
        {
            get => facts.pendingClaim;
            set => facts.pendingClaim = value;
        }

        public string ScopeId => Definition.Id;

        private ScopeState(ScopeDefinition definition, ScopeState parent)
        {
            Definition = definition;
            Parent = parent;
            InitializeDeclared();
        }

        // Builds the state tree the definition tree describes, recursively.
        public static ScopeState Build(ScopeDefinition definition, ScopeState parent = null)
        {
            var state = new ScopeState(definition, parent);
            parent?.Children.Add(state);
            foreach (var child in definition.children)
                ScopeState.Build(child, state);
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
        // by construction - re-initialize declared currency entries, and
        // re-stamp lastActiveUtc. Downward closure is the CALLER's job
        // (ResetScope recurses); this clears one scope. The root refusal lives
        // HERE, on the primitive, so no caller can bypass it (12.12: "never the
        // root"); reaching it is a code bug, hence the throw rather than the
        // action layer's log-and-refuse.
        public void Clear(DateTime nowUtc)
        {
            if (Parent == null)
                throw new InvalidOperationException("The root scope is never resettable (design doc 12.12).");
            facts = new ScopeFacts();
            lastActiveUtc = nowUtc;
            InitializeDeclared();
        }

        public ScopeState Root
        {
            get
            {
                var node = this;
                while (node.Parent != null)
                    node = node.Parent;
                return node;
            }
        }

        // Depth-first search of this scope's subtree (self included). Ids are
        // unique tree-wide, so the first hit is the only hit.
        public ScopeState FindInSubtree(string scopeId)
        {
            if (ScopeId == scopeId)
                return this;
            foreach (var child in Children)
            {
                var found = child.FindInSubtree(scopeId);
                if (found != null)
                    return found;
            }
            return null;
        }

        // Self or an ancestor by id; null when the id is not on the chain.
        public ScopeState FindOnChain(string scopeId)
        {
            for (var node = this; node != null; node = node.Parent)
                if (node.ScopeId == scopeId)
                    return node;
            return null;
        }
    }
}
