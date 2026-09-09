using System;
using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle
{
    // The one record an event leaves in its host scope (design doc 12.8).
    public class ActiveEvent
    {
        public string eventId;
        public double remainingSeconds;
        public bool goalReached;
    }

    // Why a clear was refused (design doc 12.5): the scope holding an armed,
    // unclaimed reward, the record itself, and the event that record names,
    // read from the HOST's own events list - the same declaration read every
    // other record question takes. Built by InteriorScopeState.RefusesClear and
    // nowhere else, so holding one means a scope was asked.
    public sealed class Refusal
    {
        public readonly InteriorScopeState Host;
        public readonly ActiveEvent Record;

        // Null only for a record naming an event its host does not declare,
        // which the save filter drops at load (12.10).
        public readonly Events.EventDefinition Event;

        internal Refusal(InteriorScopeState host, ActiveEvent record, Events.EventDefinition evt)
        {
            Host = host;
            Record = record;
            Event = evt;
        }
    }

    // A timed buff (Encore) - absolute expiry, burns real time app-closed (design doc 9).
    public class TimedBuff
    {
        public string buffId;
        public DateTime expiresAtUtc;
    }

    // A song written during a run (tier = the run's Catalog) or kept forever
    // (root = Discography). Chapter 6 machinery; the field exists because the
    // schema is complete from day one (design doc 12.3).
    public class SongEntry
    {
        public string songId;
        public string name;
    }

    // Every mutable fact a reset destroys, in one replaceable payload. Reset
    // REPLACES this object wholesale, which is what makes clearing complete by
    // construction (design doc 12.3): a field added here next month is cleared
    // because it is here - no clear method to forget to update.
    // Abstract, like every payload above a leaf: a scope class names the
    // concrete type it wants, so nothing can hold a payload by default.
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
    public abstract class InteriorFacts : ScopeFacts
    {
        public ActiveEvent activeEvent;
    }

    // A tier's payload. Nothing beyond what hosting brings; it exists because
    // a tier names its own concrete type like every other scope class does.
    public class TierFacts : InteriorFacts
    {
    }

    // Facts only the root holds. A separate payload rather than fields every
    // scope carries: a tier that cannot use them should not be able to hold
    // them, and the type says so instead of a load-time filter.
    public class RootFacts : ScopeFacts
    {
        public Dictionary<string, int> roadieAllocation = new();       // chapterId to stationed count
        public HashSet<string> entitlements = new();                   // store-written

        // Where play left off - written by SwitchChapter on entry, left by
        // backgrounding, and where boot returns (design doc 12.9). A durable
        // fact so it travels with the save, cloud restore included; this is
        // what makes an unsettled claim unstrandable.
        public string currentChapterId;
    }

    // A chapter's payload. Nothing beyond what hosting brings: the idle stamp
    // (lastActiveUtc) lives BESIDE the payload on ChapterScopeState, re-stamped
    // rather than cleared, and IS the pending claim (design doc 12.9) - nothing
    // about an idle offer is ever saved.
    public class ChapterFacts : InteriorFacts
    {
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
        // there is no depth test here inferring one. Root's children come from
        // the composed roster and every deeper child from a serialized list
        // (12.14.5).
        public static RootScopeState Build(ComposedContent content)
        {
            var root = content.Root.CreateRoot();
            root.InitializeDeclared();

            // Construction-only: the link pass resolves every static scope
            // reference through this map and it is dropped when Build returns.
            // A map that survived would be the id index nothing may hold
            // (12.14.8) - what makes it legal is that it never outlives the
            // construction it serves.
            var nodes = new Dictionary<ScopeDefinition, ScopeState> { { content.Root, root } };
            foreach (var chapterDefinition in content.Chapters)
                BuildChild(chapterDefinition, root, nodes);

            // Every node exists and its declared facts are seeded, so the
            // static wiring resolves here, once, in every build - it is wiring
            // and not diagnosis, and its failures are content faults (12.14.7).
            ScopeLinker.Link(root, nodes);
            return root;
        }

        private static ScopeState BuildChild(ScopeDefinition definition, ScopeState parent,
                                             Dictionary<ScopeDefinition, ScopeState> nodes)
        {
            // Seeding is its own step, after the node exists: the declared
            // facts a subclass adds are read through a virtual, and nothing
            // virtual can run inside a constructor.
            var state = definition.CreateState(parent);
            state.InitializeDeclared();
            parent.Children.Add(state);
            nodes[definition] = state;
            foreach (var childDefinition in definition.children)
                BuildChild(childDefinition, state, nodes);
            return state;
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
        // Downward closure is ClearSubtree's, below; this clears one scope. The
        // root refusal lives HERE, on the primitive, so no
        // caller can bypass it (12.12: "never the root"); reaching it is a code
        // bug, hence the throw rather than the action layer's log-and-refuse.
        public virtual void Clear(DateTime nowUtc)
        {
            if (Parent == null)
                throw new InvalidOperationException("The root scope is never resettable (design doc 12.12).");
            facts = NewFacts();
            InitializeDeclared();
        }

        // The downward-closed clear (design doc 12.5), self first: a parent
        // INFORMING its subtree, which resolves nothing - so it is a scope
        // operation and an action calls it rather than carrying the walk.
        public void ClearSubtree(DateTime nowUtc)
        {
            Clear(nowUtc);
            foreach (var child in Children)
                child.ClearSubtree(nowUtc);
        }

        // The answer coming back up that same walk (design doc 12.5): the first
        // scope at or below here refusing to be cleared, or null when none
        // does. `ignoring` is the one node whose OWN record is not asked - its
        // children still are - which is what lets a dismissal ask as if the
        // record it is about to remove were already gone (12.8). Nothing else
        // passes it.
        public Refusal RefusalInSubtree(ScopeState ignoring = null)
        {
            if (this != ignoring && this is InteriorScopeState host)
            {
                var refusal = host.RefusesClear();
                if (refusal != null)
                    return refusal;
            }
            foreach (var child in Children)
            {
                var refusal = child.RefusalInSubtree(ignoring);
                if (refusal != null)
                    return refusal;
            }
            return null;
        }

        // What each static reference and each compiled plan held at this node
        // means in THIS tree, written by the link pass at Build and never
        // afterward (12.14.8). Keyed by the object HOLDING the reference - a
        // ResetScope instance, a SectionDefinition, a ProducesEntry, a
        // CurrencyDefinition, GatherCompiler.GameSpeed - so a read is a
        // dictionary hit at a node the caller already has, and two trees built
        // from one content set hold separate links. Lazily allocated: most
        // nodes hold none.
        private Dictionary<object, object> links;

        internal void StoreLink(object holder, object value)
        {
            links ??= new Dictionary<object, object>();
            links[holder] = value;
        }

        // What a static reference names, resolved when this tree was built. A
        // miss means construction omitted a site - a code bug, not content - so
        // it throws and nothing falls back to a search (12.14.8). A link of the
        // wrong kind is the same bug seen from the reading end.
        public T Link<T>(object holder) where T : class
        {
            if (links == null || !links.TryGetValue(holder, out var value))
                throw new InvalidOperationException(
                    $"Scope '{ScopeId}' holds no {typeof(T).Name} link for a {(holder == null ? "<null>" : holder.GetType().Name)} - the link pass never visited this site.");
            if (value is not T typed)
                throw new InvalidOperationException(
                    $"Scope '{ScopeId}' links a {value.GetType().Name} for a {holder.GetType().Name}, not a {typeof(T).Name}.");
            return typed;
        }

        // The node a static scope reference names - the shape every scope
        // reference takes, since a link that is a node is the common case.
        public ScopeState Link(object holder) => Link<ScopeState>(holder);

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
    // the record accessor and the refusal it implies; the handicaps that record
    // carries reach a gather as compiled links, since InteriorDefinition is
    // what names them (12.6).
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

        // Whether this scope refuses to be cleared, judged from its OWN facts
        // (design doc 12.5): an armed, unclaimed reward would die with the
        // record, so the scope holding one says no and the answer travels back
        // up the walk the clear came down. The event is read through this
        // host's declaration list, like every other record read - a stray id
        // leaves the refusal's event null, which the save filter already drops.
        // The only place a Refusal is constructed.
        public Refusal RefusesClear()
        {
            var record = activeEvent;
            if (record == null || !record.goalReached)
                return null;
            Events.EventDefinition named = null;
            foreach (var evt in ((InteriorDefinition)Definition).events)
            {
                if (evt == null || evt.Id != record.eventId)
                    continue;
                named = evt;
                break;
            }
            return new Refusal(this, record, named);
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

        public string currentChapterId
        {
            get => Facts.currentChapterId;
            set => Facts.currentChapterId = value;
        }
    }

    // Root's direct children. The idle stamp lives here because idle is a
    // per-chapter concept (design doc 12.9), and the stamp IS the pending
    // claim - everything specific about an offer is computed from it.
    public class ChapterScopeState : InteriorScopeState<ChapterFacts>
    {
        internal ChapterScopeState(ChapterDefinition definition, ScopeState parent)
            : base(definition, parent) { }

        public DateTime lastActiveUtc;

        // Every lastActiveUtc write is MONOTONIC (design doc 12.9/12.10): the
        // clamp on the read alone is not enough - stamping a rolled-back clock
        // into state would mint the difference the moment the clock recovers.
        // A monotonic stamp may under-pay across a rollback; it can never pay
        // for time that did not pass.
        public void StampActive(DateTime nowUtc)
        {
            if (nowUtc > lastActiveUtc)
                lastActiveUtc = nowUtc;
        }

        // A reset re-stamps the idle clock rather than clearing it: a fresh
        // chapter owes no idle (design doc 12.3).
        public override void Clear(DateTime nowUtc)
        {
            base.Clear(nowUtc);
            StampActive(nowUtc);
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
