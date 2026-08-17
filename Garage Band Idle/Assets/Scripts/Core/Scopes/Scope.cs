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
    // carries the pieces the tree retires - Recipe, the ChapterDefinition
    // reference and the run-scoped resets - each of which leaves in step 7 or
    // 8. What is already the tree's shape: identity, the parent link, the
    // ordered children, the rung press, and disposal that takes the subtree
    // with it.
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
        public PrestigeSystem Prestige { get; }
        public ConditionContext Conditions { get; }

        // the tree's root, walked live: cheap (a chapter is a handful deep) and
        // never stale, where a cached root would be one more fact a re-parented
        // scope could contradict - and the tree's shape is fixed anyway
        public Scope Root
        {
            get
            {
                var scope = this;
                while (scope.Parent != null)
                    scope = scope.Parent;
                return scope;
            }
        }

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
            ProductionSystem production, BarSystem bars, RewardManager rewards,
            PrestigeSystem prestige, ConditionContext conditions, IReadOnlyList<SectionDefinition> sections)
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
            Prestige = prestige;
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
            Generators?.Dispose();
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

        // ---- the prestige press (design doc rule 14) ---------------------------

        // What one press touches, resolved once and consumed by the press and
        // the preview alike - a button's promise and the payout it banks come
        // from the SAME plan, or they disagree. A planned entry pairs a rung
        // with the scope it is filed on, because a rung's actions read and pay
        // through its own scope's surface.
        private readonly struct PlannedRung
        {
            public readonly Scope Scope;
            public readonly PrestigeTierDefinition Rung;

            public PlannedRung(Scope scope, PrestigeTierDefinition rung)
            {
                Scope = scope;
                Rung = rung;
            }
        }

        // The rung press: one operation ending at one settle, however many
        // scopes it touched. Refusals come BEFORE anything irreversible, in
        // strictness order - silent for the states a double-tap or a stale row
        // reaches (already completed, gate unmet), loud for broken content
        // (an action that cannot execute). `offer` is never asked here: it
        // governs presentation, not legality.
        public bool CompleteRung(string rungId)
        {
            if (Prestige == null || !Prestige.TryGet(rungId, out var rung))
            {
                Debug.LogError($"Scope: CompleteRung('{rungId}') on instance '{InstanceId}', which files no such rung. Ignoring.");
                return false;
            }

            // a finished rung does not complete twice - silent, because the UI
            // calls this on a button press and a double-tap is not an error
            if (Prestige.IsCompleted(rung))
                return false;

            // fail-closed when authored, asked by the operation and not only by
            // the button that offered it; a rung with no gate is ungated -
            // repeatable and harmless at any time
            if (rung.OperationGate != null && !ConditionEvaluator.IsMet(rung.OperationGate, Conditions))
                return false;

            var selected = SelectTargets(rung);
            var plan = BuildActionPlan(rung, selected);

            // preflight EVERY planned rung before anything executes or latches:
            // one unexecutable action refuses the whole press, because a press
            // that clears the run and then fails to award would strand it.
            // Each entry's check covers its own latch too - participants are
            // latchless by construction, so this is exactly "every planned
            // action plus the initiator's latch".
            foreach (var planned in plan)
            {
                if (!planned.Scope.Prestige.CanExecuteActions(planned.Rung))
                {
                    Debug.LogError($"Scope: rung '{planned.Rung.Id}' on instance '{planned.Scope.InstanceId}' has an action that cannot execute. Refusing the whole press rather than clearing the run for nothing.");
                    return false;
                }
            }

            // actions while the state their formulas read still exists:
            // deepest scope first, same depth in the tree's authored traversal
            // order, the initiating rung last (the plan's order); then the
            // latch, from the slot, as the last fact - so nothing evaluating
            // mid-press observes a completed rung whose awards have not landed
            foreach (var planned in plan)
                planned.Scope.Prestige.ExecuteActions(planned.Rung);

            Prestige.ExecuteLatch(rung);

            // clear the SELECTED set only - a scope that merely initiated is
            // not cleared, which is what keeps a capstone-shaped rung from
            // wiping the completion flag it just latched
            foreach (var scope in selected)
                scope.ClearForReset();

            // the rebuild (rule 6): cleared facts are gone, the initiator's
            // latch just moved, so every touched scope re-projects - which is
            // where onComplete re-applies, from the flag, never from the press
            foreach (var scope in selected)
                scope.ProjectModifiers();
            if (!selected.Contains(this))
                ProjectModifiers();

            Root.SettleTree();
            return true;
        }

        // What a press of this rung would grant, over the SAME resolved plan
        // the press executes - so a capstone-shaped preview includes the
        // payouts of the participating rungs, not merely its own actions. Each
        // formula evaluates over the balances the EARLIER planned grants will
        // have banked, through a read-only overlay - a later formula measures
        // what the press has already moved by the time it runs, and a preview
        // reading original balances would promise a different number than the
        // press pays. Per-currency totals in plan order, zeros included: a
        // rung at zero fans still advertises "+0" of what it pays.
        public List<RungGrant> PendingRungGrants(string rungId)
        {
            var totals = new List<RungGrant>();
            if (Prestige == null || !Prestige.TryGet(rungId, out var rung))
            {
                Debug.LogError($"Scope: PendingRungGrants('{rungId}') on instance '{InstanceId}', which files no such rung. Nothing pending.");
                return totals;
            }

            // one delta map across every planned scope: ids are unique
            // tree-wide, so a grant's shift is the same fact whichever chain
            // reads it back
            var deltas = new Dictionary<string, BigNumber>();
            foreach (var planned in BuildActionPlan(rung, SelectTargets(rung)))
            {
                var overlay = new PreviewCurrencies(planned.Scope.Currencies, deltas);
                foreach (var action in planned.Rung.Actions)
                {
                    if (!planned.Scope.Prestige.TryPendingGrant(action, overlay, out var currencyId, out var amount))
                        continue;

                    deltas[currencyId] = (deltas.TryGetValue(currencyId, out var standing) ? standing : BigNumber.Zero) + amount;

                    var index = totals.FindIndex(total => total.CurrencyId == currencyId);
                    if (index < 0)
                        totals.Add(new RungGrant(currencyId, amount));
                    else
                        totals[index] = new RungGrant(currencyId, totals[index].Amount + amount);
                }
            }
            return totals;
        }

        // one currency's slice of the same walk, for callers that already know
        // what they are asking about
        public BigNumber PendingRungGrant(string rungId, string currencyId)
        {
            foreach (var grant in PendingRungGrants(rungId))
            {
                if (grant.CurrencyId == currencyId)
                    return grant.Amount;
            }
            return BigNumber.Zero;
        }

        // The preview's read surface: the real balances of one planned scope's
        // chain, shifted by the grants planned so far - and nothing else. Reads
        // only; a preview that could write would be a press with worse manners,
        // so the mutators report and refuse. Earned totals pass through
        // unshifted: no authored formula reads them, and inventing how a
        // pending grant moves an earned total is the press's business to
        // demonstrate, not this overlay's to guess.
        private sealed class PreviewCurrencies : ICurrencies
        {
            private readonly ICurrencies _inner;
            private readonly Dictionary<string, BigNumber> _deltas;

            public PreviewCurrencies(ICurrencies inner, Dictionary<string, BigNumber> deltas)
            {
                _inner = inner;
                _deltas = deltas;
            }

            public event Action<string, BigNumber> BalanceChanged { add { } remove { } }

            public BigNumber Get(string id)
                => _inner.Get(id) + (_deltas.TryGetValue(id, out var delta) ? delta : BigNumber.Zero);

            public bool Contains(string id) => _inner.Contains(id);

            public void Add(string id, BigNumber amount)
                => Debug.LogError($"Scope: a preview tried to WRITE currency '{id}' - the overlay is read-only. Ignoring.");

            public void Set(string id, BigNumber value)
                => Debug.LogError($"Scope: a preview tried to WRITE currency '{id}' - the overlay is read-only. Ignoring.");

            public BigNumber GetEarned(string id) => _inner.GetEarned(id);

            public CurrencyDefinition GetDefinition(string id) => _inner.GetDefinition(id);

            public bool ValidateReference(string id, string context) => _inner.ValidateReference(id, context);

            public bool ResetsOnAlbumRelease(string currencyId) => _inner.ResetsOnAlbumRelease(currencyId);
        }

        // the selected set: the rung's authored targets, downward-closed by the
        // selector family. No selector selects nothing - a pure award rung is
        // expressible, and whether every rung must clear something is boot
        // validation's question, not the press's.
        private HashSet<Scope> SelectTargets(PrestigeTierDefinition rung)
        {
            var selected = new HashSet<Scope>();
            rung.ResetTargets?.Select(this, selected);
            return selected;
        }

        // The press's whole plan, in execution order. The participant pass is
        // PER RUNG: from the selected scopes, every latchless rung except the
        // initiating rung itself - excluding the initiating RUNG rather than
        // the initiating scope, so that scope's other latchless rungs stay
        // eligible (a capstone-shaped press banks the album payout filed
        // beside it). Latch-bearing rungs never ride along on another rung's
        // press, and that is general rather than a tie-break: a completion's
        // awards are inseparable from its latch, only the initiator latches,
        // and a one-shot paid as a participant would pay again on every press.
        // The initiating rung is appended exactly once, last, so it measures
        // what the participants have already banked (a capstone awarding on
        // cumulative Records runs after the demo it implicitly cuts).
        private List<PlannedRung> BuildActionPlan(PrestigeTierDefinition rung, HashSet<Scope> selected)
        {
            // deepest first, because reads go outward: an outer rung running
            // first would write state an inner rung's formula then measures.
            // Depth is a partial order, so same-depth scopes - across branches
            // too - run in the tree's authored traversal order, never in a
            // selector's list order or an incidental enumeration's.
            var ordered = new List<Scope>(selected);
            var traversal = new Dictionary<Scope, int>();
            var depths = new Dictionary<Scope, int>();
            IndexTree(Root, 0, traversal, depths);
            ordered.Sort((a, b) =>
            {
                var byDepth = depths[b].CompareTo(depths[a]);
                return byDepth != 0 ? byDepth : traversal[a].CompareTo(traversal[b]);
            });

            var plan = new List<PlannedRung>();
            foreach (var scope in ordered)
            {
                if (scope.Prestige == null)
                    continue;

                foreach (var participant in scope.Prestige.Rungs)
                {
                    if (participant != rung && !participant.HasLatch)
                        plan.Add(new PlannedRung(scope, participant));
                }
            }

            plan.Add(new PlannedRung(this, rung));
            return plan;
        }

        // preorder over the whole tree: one authored ordering answers every
        // same-depth question, instead of two that can disagree
        private static int IndexTree(Scope scope, int next, Dictionary<Scope, int> traversal, Dictionary<Scope, int> depths)
        {
            traversal[scope] = next++;
            depths[scope] = scope.Parent == null ? 0 : depths[scope.Parent] + 1;
            foreach (var child in scope.Children)
                next = IndexTree(child, next, traversal, depths);
            return next;
        }

        // Clears this scope's contents IN PLACE: the instance and every
        // subscription on it survive, each system resets what it owns. Three
        // things rest on the in-place rule - stable save identity, live UI
        // bindings, and the surviving dirty flag (step 0).
        //
        // INTERIM: while lifetime is still a declaration (ContentScope, until
        // 7.5 steps 7-8), clearing means the run-scoped resets - exactly the
        // block today's release runs, so the chapter path is unchanged through
        // this step. When placement replaces lifetime, this becomes "reset
        // everything the scope owns" and the per-system run-scoped walks die.
        // One method, so that replacement lands in one place.
        public void ClearForReset()
        {
            Pool.ResetCurrenciesOnAlbumRelease();
            Generators.ResetOwned();
            Upgrades.ResetRunScoped();
            Bars.ResetRunScopedGroups();
            Flags.ResetRunScoped();
        }

        // Settles this scope, then its subtree, parent before child - reads go
        // outward, so an inner evaluation must see final outer state. The
        // step-3 stand-in for step 4's root settle (per-scope dirty flags,
        // drained outermost-first under one bound): one method on the root, so
        // the real boundary replaces one body. Call on Root.
        public void SettleTree()
        {
            Settle();
            foreach (var child in _children)
                child.SettleTree();
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
            foreach (var system in new object[] { Generators, Upgrades, Production, Bars, Rewards, Prestige, Flags, Modifiers })
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

    // One currency's share of a rung press, as the preview reports it: what
    // the press would pay into this id, earlier planned grants included. A
    // value type because it is an answer, not a thing with identity.
    public readonly struct RungGrant
    {
        public readonly string CurrencyId;
        public readonly BigNumber Amount;

        public RungGrant(string currencyId, BigNumber amount)
        {
            CurrencyId = currencyId;
            Amount = amount;
        }
    }
}
