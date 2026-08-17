using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // One scope of the tree, instantiated (design doc section 12, rule 12): a
    // currency pool plus the systems that read and write it, built together and
    // discarded together, under a stable instance identity. The instance half
    // of ScopeDefinition's definition/instance split: a cleared chapter's
    // replay economy (rule 7) is a second INSTANCE of the same definition, and
    // slice 9's save is one block per instance - which is why identity is the
    // caller's to assign, deterministic, and never minted here.
    //
    // Mid-7.5 honesty: this class is EconomyContext reshaped in place. It still
    // carries the pieces the tree retires - Recipe, Capstone, the
    // ChapterDefinition reference and the run-scoped resets - each of which
    // leaves in step 3, 7, or 8. What is already the tree's shape: identity,
    // the parent link, the ordered children, and disposal that takes the
    // subtree with it.
    //
    // Two things are the point of the bundle, and neither is the field list.
    //
    // First, every top-level operation ends at Settle. A tick, a tap and a
    // purchase all mutate several systems and then declare the mutation
    // finished, once, in one place - so condition-dependent published values
    // re-evaluate exactly once, after the whole mutation, and an operation added
    // later cannot forget to. Nothing inside a system may call it: a tick has
    // bars still to drain and a purchase has unlocks still to evaluate, and
    // Settle is the statement that neither is true anymore.
    //
    // Second, the modifier store is only ever REBUILT, never edited (rule 6).
    // ProjectModifiers clears the grants and re-applies them from the facts that
    // currently exist, so a release resets the facts it owns and asks for the
    // projection again, and a load restores facts and asks for the projection
    // again. One mechanism, so the two boundaries cannot disagree about what a
    // permanent buff is worth.
    public class Scope : IDisposable
    {
        private readonly CurrencyRouter _router;
        private readonly List<IModifierFactSource> _factSources = new();
        private readonly Action _evaluateUnlocks;
        private readonly List<Scope> _children = new();
        private bool _childrenAttached;

        // Identity is three separable facts. The definition is WHAT this scope
        // is (null until step 7 authors scope assets - a scope built from a
        // chapter has no definition yet). The instance id is WHICH instantiation
        // of it this is: assigned by the caller, deterministic, stable across
        // sessions, because slice 9 rematches save blocks to instances by it and
        // a fresh GUID each boot would orphan every block. The parent is WHERE
        // it lives, which rule 12 makes the same thing as how long its facts
        // last.
        public ScopeDefinition Definition { get; }
        public string InstanceId { get; }
        public Scope Parent { get; }

        // ordered (the ladder of design doc section 1); attached once by the
        // factory, after which nothing changes the tree's shape
        public IReadOnlyList<Scope> Children => _children;

        // What is in scope from here (rule 12): this scope's link in the one
        // iteration, outward to the root. The flag and modifier resolutions,
        // and the inward-flowing aggregate change signals, live on it.
        public ScopeChain Chain { get; }

        public ChapterDefinition Chapter { get; }
        public EconomyRecipe Recipe { get; }

        // the balances this scope can reach: every pool in its chain, behind a
        // single surface that resolves ownership at construction (first owner
        // outward wins - rule 12's currency resolution)
        public ICurrencies Currencies => _router;

        // this scope's own pool, for the operations that are explicitly about
        // it rather than about "whatever holds this id" - a release resets these
        // balances and never anything outward
        public CurrencyManager Pool => _router.Local;

        public FlagSystem Flags { get; }
        public ModifierSystem Modifiers { get; }
        public GeneratorSystem Generators { get; }
        public UpgradeSystem Upgrades { get; }
        public ProductionSystem Production { get; }
        public BarSystem Bars { get; }
        public RewardManager Rewards { get; }
        public CapstoneSystem Capstone { get; }
        public ConditionContext Conditions { get; }

        // The chapter's sections in layout order, resolved from its id list.
        // Definitions only: section visibility is a pure function of each
        // visibleWhen over this economy's state, so there is no section state
        // to hold - persistence comes from what the conditions read (flags
        // carry the lifetimes), and the screen evaluates live on the settled
        // signal.
        public IReadOnlyList<SectionDefinition> Sections { get; }

        // Focus lifecycle (rule 7): constructed -> focused <-> unfocused ->
        // discarded. Only a focused economy receives the tick, and an unfocused
        // one accrues nothing live; GameManager owns the "exactly one focused"
        // rule because it is the thing holding more than one context.
        public bool IsFocused { get; private set; }

        // when this economy was last interacted with, stamped on focus loss.
        // Slice 9's idle earnings read it on focus gain to decide how much time
        // to pay for; null until the first time focus is lost, which is the
        // honest answer for an economy that has never been away.
        public DateTime? LastInteractionUtc { get; private set; }

        public Scope(ScopeDefinition definition, string instanceId, Scope parent, ScopeChain chain,
            ChapterDefinition chapter, EconomyRecipe recipe, CurrencyRouter router,
            FlagSystem flags, ModifierSystem modifiers, GeneratorSystem generators, UpgradeSystem upgrades,
            ProductionSystem production, BarSystem bars, RewardManager rewards, CapstoneSystem capstone,
            ConditionContext conditions, IReadOnlyList<SectionDefinition> sections)
        {
            Definition = definition;
            InstanceId = instanceId;
            Parent = parent;
            Chain = chain;
            Chapter = chapter;
            Recipe = recipe;
            _router = router;
            Flags = flags;
            Modifiers = modifiers;
            Generators = generators;
            Upgrades = upgrades;
            Production = production;
            Bars = bars;
            Rewards = rewards;
            Capstone = capstone;
            Conditions = conditions;
            Sections = sections ?? Array.Empty<SectionDefinition>();

            _evaluateUnlocks = EvaluateUnlocks;

            // The totality obligation re-projection takes on (rule 6): every
            // fact class that produces a modifier must be walkable here. It is
            // met by DERIVING the projection list from the systems the context
            // holds rather than by maintaining a second list beside them - a
            // system that holds facts must be constructed to hold them, and one
            // that is constructed is filtered in here. That is why this is not
            // an assertion: the mistake it would have asserted against, adding a
            // fact class and forgetting to project it, is not expressible.
            CollectFactSources();
        }

        // ---- lifecycle -------------------------------------------------------

        // Wired once by the factory, after the children exist - a child's
        // construction needs its parent (reads go outward), so the parent
        // cannot take them in its own constructor. Once, because nothing else
        // may change the tree's shape: no action, reset, or operation adds or
        // removes a scope, and a second attach is the factory being run against
        // a scope that already has a subtree.
        public void AttachChildren(IReadOnlyList<Scope> children)
        {
            if (_childrenAttached)
            {
                Debug.LogError($"Scope: AttachChildren on instance '{InstanceId}', whose children are already attached. The tree's shape is fixed at construction; ignoring.");
                return;
            }

            _childrenAttached = true;
            if (children != null)
                _children.AddRange(children);
        }

        public void Focus()
        {
            if (IsFocused)
                return;

            IsFocused = true;
        }

        // Stamps the last-interaction time on the way out. Recorded here rather
        // than by the caller because the timestamp's meaning is "when this
        // economy last ran", and the context is the only thing that knows -
        // GameManager routing the tick elsewhere is exactly the event.
        public void Unfocus()
        {
            if (!IsFocused)
                return;

            IsFocused = false;
            LastInteractionUtc = DateTime.UtcNow;
        }

        // A discarded scope must stop listening to systems that outlive it:
        // the condition context subscribes to the four condition inputs, and the
        // router subscribes to the outer pool, which outlives every instance.
        // Invisible with one economy; a leak the moment there are two - and at N
        // levels that disposal discipline is load-bearing (rule 12), which is
        // why disposal takes the SUBTREE: a discarded parent whose children kept
        // listening would feed a dead ladder's subscribers changes for a chapter
        // nobody is playing. Children first, since theirs subscribe outward into
        // state this scope holds.
        public void Dispose()
        {
            foreach (var child in _children)
                child.Dispose();

            Conditions?.Dispose();
            Production?.Dispose();
            _router?.Dispose();
            // last: the chain node cascades its ancestors' signals, and
            // everything above unhooked from it first
            Chain?.Dispose();
        }

        // ---- the projection --------------------------------------------------

        // Rebuilds the modifier store from the facts that exist right now (rule
        // 6). Called at construction - where it is a no-op on a fresh economy
        // and the whole restore on a loaded one - and at every boundary that
        // resets facts. Nothing else may clear the store: ResetGranted without a
        // projection after it leaves the game with the buffs silently missing.
        public void ProjectModifiers()
        {
            Modifiers.ResetGranted();

            foreach (var source in _factSources)
                source.ProjectModifiers();
        }

        // ---- capture and restore ---------------------------------------------

        // This scope's own facts (design doc section 12, rule 12). Reads Pool,
        // never Currencies: the router would reach every pool outward, and Records
        // and Roadies are not this scope's to claim - one pool, one writer, or an
        // event sandbox's capture becomes a second opinion on permanent progress.
        public EconomyLocalSnapshot CaptureLocalState()
            => new(
                Pool.CaptureAll(),
                Generators.CaptureOwned(),
                Upgrades.CaptureApplied(),
                Bars.CaptureProgress(),
                Flags.CaptureSetFlags(),
                Bars.CaptureActiveBars());

        // The seed another economy is built from, filtered by what that economy is
        // FOR. An event sandbox takes the chapter's permanent facts and none of the
        // run's - that absence is the fixed baseline (design doc section 6.1), and
        // it is declared here rather than produced by a reset somewhere.
        //
        // Filtering lives on the context rather than in a static over the snapshot
        // because every question it asks is a CONTENT question - is this flag
        // run-scoped, does this currency's group reset on release - and the systems
        // here are what already resolved the declarations. A filter reading a scope
        // recorded inside the snapshot would be reading a copy that goes stale.
        public EconomyLocalSnapshot CaptureSeedFor(EconomyRecipe recipe)
        {
            switch (recipe?.Kind)
            {
                case EconomyRecipeKind.EventSandbox:
                    return PermanentInChapterFacts();

                // A frontier economy seeded from another economy is a load, and a
                // load carries everything. A replay economy (rule 7) is not built
                // yet and must not silently get the frontier's whole state, so it
                // fails closed to the empty seed until its own rules exist.
                case EconomyRecipeKind.FrontierChapter:
                    return CaptureLocalState();
                default:
                    return EconomyLocalSnapshot.Empty;
            }
        }

        // Restores this economy's facts and settles, as ONE operation nothing can
        // observe halfway (the ordering the whole contract rests on):
        //
        //   raw facts, silently -> mark dirty -> project (modifiers deferred)
        //   -> settle -> replay notifications under suppression
        //
        // Every step earns its place. The primitives are silent because a
        // subscriber must never read a fleet restored with balances not yet
        // restored. MarkDirty is required precisely BECAUSE they are silent: the
        // condition context learns of every input through those same events, so
        // without it a restore into an already-settled context leaves the dirty
        // flag false and the drain evaluates nothing - and the fresh-context
        // default cannot cover it, since a second restore is exactly the case that
        // matters. The projection is deferred too, because it clears the modifier
        // store before rebuilding it and a row reading its rate mid-rebuild reads a
        // wrong one. The replay is suppressed so it cannot re-dirty what the settle
        // just consumed, which keeps this one pass with one terminal Settled rather
        // than a drain, a replay, and a second drain nobody can name the end of.
        //
        // The settle it ends at is the ordinary bounded fixpoint every settle is now
        // (see Settle), not a restore-only loop: a content unlock that sets a flag
        // opening another resolves before the terminal Settled. What restore adds is
        // only the wrapper - the silent fact restore and MarkDirty above, the deferred
        // projection, the Assemble below, and the suppressed replay - none of which the
        // fixpoint itself has to know about.
        public void Restore(EconomyLocalSnapshot snapshot)
        {
            snapshot ??= EconomyLocalSnapshot.Empty;

            Modifiers.BeginDeferredNotifications();

            // Each primitive replaces over its OWN content rather than over the
            // snapshot's keys, so anything the snapshot omits returns to its default
            // instead of surviving from before.
            Pool.RestoreAll(snapshot.Currencies, notify: false);
            Generators.RestoreOwned(snapshot.GeneratorsOwned, notify: false);
            Upgrades.RestoreApplied(snapshot.AppliedUpgradeIds, notify: false);
            // progress first, which also drops every standing selection; then the
            // snapshot's own selections, so a group is pouring into a bar if and only
            // if the snapshot said it was
            Bars.RestoreProgress(snapshot.BarProgress, notify: false);
            Bars.RestoreActiveBars(snapshot.ActiveBarByGroup, notify: false);
            Flags.Restore(snapshot.SetFlagIds, notify: false);

            Conditions.MarkDirty();
            ProjectModifiers();

            // The restore replaced the upgrade LATCHES silently, and an applied
            // upgrade's contributions are live exactly while its latch holds - so
            // the set of things feeding a producer just changed with no event to
            // hang it on. Assembling here is the counterpart to the projection
            // above: one rebuilds the modifiers the surviving facts imply, the other
            // rebuilds the production they imply.
            Production.Assemble();

            // The drains, the bound and the single deferred Settled are the ordinary
            // settle (see Settle); restore keeps no second copy of them. It runs
            // before the suppressed replay below and while modifier notifications are
            // still deferred, so the terminal Settled describes fully restored state.
            Settle();

            using (Conditions.SuppressInvalidation())
            {
                Modifiers.EndDeferredNotifications();
                Pool.RepublishAll();
                Generators.RepublishOwned();
                Bars.RepublishAll();
                // Flags are deliberately not replayed. A flag is only ever READ
                // through a Condition, so everything gating on one already re-asked
                // at the settle above - and FlagSet means "just latched", which a
                // restored latch is not, the same distinction that keeps
                // UpgradeApplied out of this replay.
            }
        }

        // The chapter's permanent facts, with every run fact dropped. Each rule
        // asks the declaration that owns the lifetime, so a chapter that scopes its
        // content differently filters differently with no change here.
        private EconomyLocalSnapshot PermanentInChapterFacts()
        {
            // a currency survives if its GROUP says a release does not take it
            var currencies = new Dictionary<string, CurrencyState>();
            foreach (var entry in Pool.CaptureAll())
            {
                if (!Pool.ResetsOnAlbumRelease(entry.Key))
                    currencies.Add(entry.Key, entry.Value);
            }

            var upgrades = new List<string>();
            foreach (var id in Upgrades.CaptureApplied())
            {
                if (Upgrades.ScopeOf(id) != ContentScope.Run)
                    upgrades.Add(id);
            }

            var flags = new List<string>();
            foreach (var id in Flags.CaptureSetFlags())
            {
                if (!Flags.IsRunScoped(id))
                    flags.Add(id);
            }

            var bars = new Dictionary<string, IReadOnlyDictionary<string, BigNumber>>();
            foreach (var entry in Bars.CaptureProgress())
            {
                var group = Bars.GetRuntime(entry.Key)?.Group;
                if (group != null && group.Scope != ContentScope.Run)
                    bars.Add(entry.Key, entry.Value);
            }

            // Generator counts are absent unconditionally, and that is a rule rather
            // than an omission: the fleet is re-bought every run (the release zeroes
            // it), so there is no such thing as a permanent owned count. A sandbox
            // therefore starts with no band, which is what "tap-only" means before
            // the debuff is even applied.
            //
            // Bar SELECTIONS are absent for the same kind of reason: a selection is a
            // decision about where to pour this run's fill currency, and a sandbox
            // has none of that currency to pour. Inheriting the frontier's target
            // would be a live pointer into a run the sandbox is not playing.
            return new EconomyLocalSnapshot(currencies, null, upgrades, bars, flags);
        }

        // ---- operations ------------------------------------------------------

        public void Tick(double seconds)
        {
            // an unfocused economy accrues nothing live (rule 7). GameManager
            // routes the tick to the focused context, so this is the guarantee
            // rather than the mechanism - it holds even if a caller keeps a
            // reference to a context that has since lost focus.
            if (!IsFocused)
                return;

            // Every currency's rate accrues in ONE pass - cash from the fleet, fans
            // from the band, rehearsal from the trickle - because a rate is a rate
            // whatever declared it (design doc section 12, rule 13). Each producer
            // composes its own modifiers, the Records buff among them, so the tick
            // passes no multipliers. One pass covers every contribution, generators
            // included, so a currency's rate has a single implementation.
            //
            // Then bars drain the pool into the active bar in the same tick, so a
            // selected bar advances with no pool lag. Fans take the fans rate's
            // composition, never the income multiplier, which holds as long as the
            // chapter's fans currency stays out of recordBuff.affects -
            // ContentValidator refuses it there, because time away must not shortcut
            // the Records payout (section 11).
            Production.Accrue(seconds);
            Bars.Tick();

            // the tick has fully settled - production, drains, completions,
            // whatever modifiers or flags they granted - so unlocks evaluate and
            // the composed yields publish only now (a bar completing mid-tick
            // could set a flag some contribution's gate reads, so no earlier
            // point is safe)
            Settle();
        }

        // Firing ONE surface: every currency that contributor feeds a yield to pays
        // out - cash (the base line plus whatever else contributes to cash's yield,
        // stage_presence among them, all composed by cash's producer) and the fill
        // currencies alike. Chapter 1 passes "jam", the only surface it authors; the
        // parameter exists so a second one (Merch/Sell) is a module entry and a
        // producer asset rather than a rewrite of what firing means.
        //
        // Named Fire rather than Jam because a jam is a chapter-1 noun: what the
        // economy knows is that something fired, never what (rule 13).
        public void Fire(string contributorId)
        {
            Production.Fire(contributorId);

            // drain immediately so the active bar visibly nudges on the tap, not
            // a tick later
            Bars.Tick();

            // the whole tap has settled (yields paid, bars drained, anything a
            // completion granted)
            Settle();
        }

        // The bar-selection action (player-directed fill, design doc section 6):
        // point the group's pour at a bar, or clear the target (null). A
        // top-level operation rather than a call on the runtime, because
        // retargeting mutates the economy like any other action: pool built up
        // while nothing was selected pours in immediately, and that pour can
        // complete the bar and apply its reward - condition inputs a gate reads.
        // The runtime cannot end the operation itself (nothing inside a system
        // may call Settle), so the seam is reached from here, the same shape as
        // Jam.
        public void SelectBar(string groupId, string barId)
        {
            // an unknown group id was already reported by GetRuntime
            var runtime = Bars.GetRuntime(groupId);
            if (runtime == null)
                return;

            if (runtime is not PerBarContinuousRuntime perBar)
            {
                Debug.LogError($"Scope: SelectBar on bar group '{groupId}', whose fill mode has no bar selection. Ignoring.");
                return;
            }

            perBar.SetActiveBar(barId);

            // the whole selection has settled: the immediate pour, and whatever
            // a completion it caused granted
            Settle();
        }

        public bool BuyUpgrade(Upgrade upgrade)
        {
            if (!Upgrades.TryBuy(upgrade, Conditions))
                return false;

            // the purchase has settled (buff granted, cost charged), so unlocks
            // evaluate and the composed yields publish here rather than from a
            // modifier callback midway through the operation. The spend moved a
            // balance, so the drain has something to do: a content unlock's gate
            // can be satisfied right now, and reveal must not wait for the tick.
            Settle();
            return true;
        }

        public bool BuyGenerator(Generator generator)
        {
            // asked live, the same question the row that offered the button
            // answers - so a generator can never be bought through a row the
            // player is only still looking at because something latched
            if (generator == null || !generator.IsUnlocked(Conditions))
                return false;
            if (!generator.TryBuy(Currencies))
                return false;

            // the purchase has settled, so the drain runs here and not a tick
            // later: it can satisfy another generator's ownedCount unlock or a
            // content unlock's gate (play_for_crowd: own 1 Drummer), and buying
            // a Drummer has to reveal Fans now. The composed yields publish
            // after, since an unlock just evaluated can have granted a yield
            // buff or set a flag a contribution's gate reads.
            Settle();
            return true;
        }

        // The Records a release performed right now would bank: the album
        // payout formula over the current fans balance (design doc section 5).
        // One home for the read, so the UI's preview and the release itself
        // cannot disagree about what a demo is worth. A chapter that declares
        // no fans currency banks nothing - the release is still a legal reset.
        public BigNumber PendingReleaseRecords()
        {
            var fansCurrencyId = Chapter?.Fans?.CurrencyId;
            return string.IsNullOrEmpty(fansCurrencyId)
                ? BigNumber.Zero
                : ProductionCalculator.RecordsEarned(Currencies.Get(fansCurrencyId));
        }

        // The album release (design doc section 5, prestige): bank the run's
        // fans as Records, then reset the run and rebuild the modifier store
        // from the facts that survived. Written as an operation on this bundled
        // context (rule 12) so the same orchestration later runs unchanged
        // against other instances; returns the Records banked so the caller can
        // present the payout.
        public BigNumber ReleaseAlbum()
        {
            var earned = ReleaseAlbumFacts();
            Settle();
            return earned;
        }

        // The release's facts without the seam: everything ReleaseAlbum does up
        // to but not including Settle. Split out because the capstone completion
        // is ONE operation that ends at ONE Settle - it releases, then applies
        // its own facts, and only then declares the whole mutation finished. If
        // it called ReleaseAlbum instead, the mid-operation settle would let an
        // unlock evaluate between the release and the completion's facts,
        // observing a banked run whose chapter has not finished banking.
        private BigNumber ReleaseAlbumFacts()
        {
            // the award reads the fans balance the reset is about to zero, so it
            // goes first. Routed through Currencies: the router resolves the
            // Records id to the pool that owns it (the permanent one), and Add
            // accrues the earned total the income buff and the capstone gate
            // both read - a total that outlives every demo because the Records
            // group is the thing declaring it permanent.
            var earned = PendingReleaseRecords();
            if (earned > BigNumber.Zero)
                Currencies.Add(Conditions.RecordsCurrencyId, earned);

            // Facts first, all of them, before the store is touched (rule 6): a
            // projection over half-reset facts would rebuild effects this release
            // is in the middle of removing. Each reset is scope/group-driven -
            // no name list - and each keeps exactly what its own declaration
            // says survives: permanent-in-chapter upgrade latches, permanent bar
            // groups, permanent flags. Run-scoped facts go, flags included -
            // there is no category a release spares wholesale. Balances, and the
            // earned totals measured off them, reset on THIS scope's own pool
            // only; nothing outward is a run's to reset.
            Pool.ResetCurrenciesOnAlbumRelease();
            Generators.ResetOwned();
            Upgrades.ResetRunScoped();
            Bars.ResetRunScopedGroups();
            // flags too: a run-scoped flag clears here and comes back only when
            // a setter whose own fact survives or re-fires asserts it again -
            // the projection below re-sets every flag whose setter's latch
            // survived, which is exactly rule 6's rebuild applied to reveals.
            // Sections need no walk of their own: their visibility derives from
            // these facts, so it resets because the facts did.
            Flags.ResetRunScoped();

            // the rebuild: run-scoped effects are absent because the facts behind
            // them are, never because anything filtered the store. The
            // seam is the caller's - ReleaseAlbum settles here, the capstone
            // completion after its own facts land.
            ProjectModifiers();
            return earned;
        }

        // The capstone completion (design doc sections 1-2 and 5): one atomic
        // operation ending at a single Settle. Every refusal comes BEFORE the
        // irreversible release below - the same charged-for-nothing rule TryBuy
        // applies, because a completion that banks the run and then fails to
        // award would strand it. The refusal set is fail-closed like TryBuy's,
        // not offer-gated like ReleaseAlbum's: a release is repeatable and
        // harmless anytime, while a completion latches a permanent flag, so the
        // gate is asked here and not only by the button that offered it.
        public bool CompleteCapstone()
        {
            if (Chapter?.Capstone == null || !Chapter.Capstone.IsAuthored)
            {
                Debug.LogError($"Scope: CompleteCapstone on chapter '{Chapter?.Id}', which authors no capstone. Ignoring.");
                return false;
            }

            // a finished chapter does not complete twice - silent, because the
            // UI calls this on a button press and a double-tap is not an error
            if (Capstone.IsCompleted)
                return false;

            // availability is the authored unlock, asked through the one
            // evaluator like every other gate - silent for the same reason a
            // TryBuy under the gate is silent
            if (!ConditionEvaluator.IsMet(Chapter.Capstone.Unlock, Conditions))
                return false;

            // loud: an action that cannot execute means broken content (boot
            // validation reports the specifics), and refusing here is what
            // keeps the release below from stranding the run
            if (!Capstone.CanExecuteActions())
            {
                Debug.LogError($"Scope: capstone '{Chapter.Capstone.Id}' has an action that cannot execute. Refusing the completion rather than releasing the album for nothing.");
                return false;
            }

            // the capstone implicitly cuts an album (design doc sections 1-2):
            // the run's fans bank as Records first, so no run value is stranded
            // at the chapter boundary. Then the completion's own facts - the
            // re-applicable OnComplete, the one-shot actions, the declared flag
            // - and one settle for the whole mutation.
            ReleaseAlbumFacts();
            Capstone.ExecuteCompletion();
            Settle();
            return true;
        }

        // ---- the settle seam -------------------------------------------------

        // The one point at which a completed mutation is declared finished and
        // everything downstream of it runs, in two phases under one deferred
        // Settled.
        //
        // Phase one drains the condition context's dirty signal (the condition
        // inputs raise it, replacing the per-tick poll) to a BOUNDED FIXPOINT.
        // Drain clears its flag before evaluating and stays one pass to a call
        // (ConditionInvalidationTests pins that), so a content unlock applied
        // during a pass - setting a flag that opens another unlock - leaves work
        // pending, and the loop here picks it up rather than leaving it for the
        // next tick. The bound is a diagnostic, not a tuning knob: a chapter's
        // unlock chain is a handful deep, so exhausting it means gates that
        // re-trigger each other rather than a legitimately long chain.
        //
        // Phase two refreshes the composed yields ONCE, after the drains have
        // converged - a yield can move for reasons no condition input reports (a
        // granted modifier), and refreshing per pass would publish an intermediate
        // composition a row must never see. Both phases sit inside one DeferSettled,
        // so however many passes it takes, subscribers hear one Settled describing
        // finished state.
        //
        // Public because a boundary the context does not own yet ends here too:
        // slice 9's restore mutates facts through this context and then declares
        // them settled through this same seam - rather than growing a second
        // pattern for saying the same thing, or the second copy of this loop it
        // used to hold.
        public void Settle()
        {
            const int maxPasses = 8;
            var passes = 0;
            using (Conditions.DeferSettled())
            {
                do
                {
                    Conditions.Drain(_evaluateUnlocks);
                    passes++;
                }
                while (Conditions.IsDirty && passes < maxPasses);

                if (Conditions.IsDirty)
                    Debug.LogError($"Scope: settle of chapter '{Chapter?.Id}' still had condition work pending after {maxPasses} drain passes - content whose unlocks re-trigger each other.");

                // unconditional and once, after the drains converge: a yield can
                // move for reasons no condition input reports (a granted modifier),
                // and RefreshYields already publishes only an actual move
                Production.RefreshYields();
            }
        }

        // What the drain evaluates: content unlocks, the only reveal that is
        // state. Sections and generator rows are deliberately absent - their
        // visibility is derived per settle by whoever renders them, from the
        // same conditions, so there is nothing here to keep in step. Held as a
        // cached delegate on the field above so the seam allocates nothing per
        // tick.
        private void EvaluateUnlocks()
        {
            Upgrades.EvaluateContentUnlocks(Conditions);
        }

        // Every system this context holds, filtered for the ones holding
        // modifier-producing facts. The list of systems is the constructor's
        // parameter list, so it cannot fall out of step with what exists.
        private void CollectFactSources()
        {
            foreach (var system in new object[] { Generators, Upgrades, Production, Bars, Rewards, Capstone, Flags, Modifiers })
            {
                if (system is IModifierFactSource source)
                    _factSources.Add(source);
            }

            // a frontier economy always has upgrades and bars, so an empty list
            // means the systems failed to construct and every buff would go
            // missing at the first boundary - reported here rather than
            // discovered as a silently unbuffed run
            if (_factSources.Count == 0 && Recipe?.Kind == EconomyRecipeKind.FrontierChapter)
                Debug.LogError($"Scope: chapter '{Chapter?.Id}' has no modifier fact sources - a release or restore would rebuild an empty modifier store. Check that the upgrade and bar systems constructed.");
        }
    }
}
