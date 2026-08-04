using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // One economy, bundled (design doc section 12, rule 12): a currency pool
    // plus the systems that read and write it, built together and discarded
    // together. The frontier chapter is one instance; an event sandbox (slice 8)
    // and a cleared chapter's replay economy (rule 7) are further instances of
    // this same class, which is what makes isolation a matter of construction
    // rather than of scope tags inside shared managers.
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
    public class EconomyContext : IDisposable
    {
        private readonly CurrencyRouter _router;
        private readonly List<IModifierFactSource> _factSources = new();
        private readonly Action _evaluateUnlocks;

        public ChapterDefinition Chapter { get; }
        public EconomyRecipe Recipe { get; }

        // the balances this economy can reach: its own pool plus the global one,
        // behind a single surface that resolves ownership at construction
        public ICurrencies Currencies => _router;

        // this economy's own pool, for the operations that are explicitly about
        // it rather than about "whatever holds this id" - a release resets these
        // balances and never the global pool's
        public CurrencyManager Pool => _router.Local;

        public FlagSystem Flags { get; }
        public ModifierSystem Modifiers { get; }
        public GeneratorSystem Generators { get; }
        public UpgradeSystem Upgrades { get; }
        public FanSystem Fans { get; }
        public ProductionSystem Production { get; }
        public BarSystem Bars { get; }
        public RewardManager Rewards { get; }
        public ConditionContext Conditions { get; }

        // the chapter's sections in layout order, resolved from its id list
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

        public EconomyContext(ChapterDefinition chapter, EconomyRecipe recipe, CurrencyRouter router,
            FlagSystem flags, ModifierSystem modifiers, GeneratorSystem generators, UpgradeSystem upgrades,
            FanSystem fans, ProductionSystem production, BarSystem bars, RewardManager rewards,
            ConditionContext conditions, IReadOnlyList<SectionDefinition> sections)
        {
            Chapter = chapter;
            Recipe = recipe;
            _router = router;
            Flags = flags;
            Modifiers = modifiers;
            Generators = generators;
            Upgrades = upgrades;
            Fans = fans;
            Production = production;
            Bars = bars;
            Rewards = rewards;
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

        // A discarded context must stop listening to systems that outlive it:
        // the condition context subscribes to the four condition inputs, and the
        // router subscribes to the global pool, which every context shares.
        // Invisible with one economy; a leak the moment there are two.
        public void Dispose()
        {
            Conditions?.Dispose();
            _router?.Dispose();
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

        // ---- operations ------------------------------------------------------

        public void Tick(double seconds)
        {
            // an unfocused economy accrues nothing live (rule 7). GameManager
            // routes the tick to the focused context, so this is the guarantee
            // rather than the mechanism - it holds even if a caller keeps a
            // reference to a context that has since lost focus.
            if (!IsFocused)
                return;

            // production composes its own modifiers per currency (the Records
            // buff among them), so the tick passes no multipliers
            Generators.Tick(seconds);

            // fans never take the income multiplier - fan rate is band size and
            // time only
            Fans.Tick(seconds);

            // fill currencies accrue, then bars drain the pool into the active
            // bar in the same tick, so a selected bar advances with no pool lag
            Production.Tick(seconds);
            Bars.Tick();

            // the tick has fully settled - production, drains, completions,
            // whatever modifiers or flags they granted - so unlocks evaluate and
            // the tap value publishes only now (a bar completing mid-tick could
            // set a flag some config's gate reads, so no earlier point is safe)
            Settle();
        }

        // the tap action: every tap-triggered production config fires - the cash
        // yield (composed with every modifier targeting tap value: flat adds
        // like stage_presence, event-tier multipliers) and the fill currencies
        // alike, all authored on the jam producer
        public void Jam()
        {
            Production.FireTap();

            // drain immediately so the active bar visibly nudges on the tap, not
            // a tick later
            Bars.Tick();

            // the whole tap has settled (yields paid, bars drained, anything a
            // completion granted)
            Settle();
        }

        public bool BuyUpgrade(Upgrade upgrade)
        {
            if (!Upgrades.TryBuy(upgrade, Conditions))
                return false;

            // the purchase has settled (buff granted, cost charged), so unlocks
            // evaluate and the tap value publishes here rather than from a
            // modifier callback midway through the operation. The spend moved a
            // balance, so the drain has something to do: a content unlock's gate
            // can be satisfied right now, and reveal must not wait for the tick.
            Settle();
            return true;
        }

        public bool BuyGenerator(Generator generator)
        {
            if (generator == null || !generator.Unlocked)
                return false;
            if (!generator.TryBuy(Currencies))
                return false;

            // the purchase has settled, so the drain runs here and not a tick
            // later: it can satisfy another generator's ownedCount unlock or a
            // content unlock's gate (play_for_crowd: own 1 Drummer), and buying
            // a Drummer has to reveal Fans now. The tap value publishes after,
            // since an unlock just evaluated can have granted a tap buff or set
            // a flag a config's gate reads.
            Settle();
            return true;
        }

        // ---- the settle seam -------------------------------------------------

        // The one point at which a completed mutation is declared finished and
        // everything downstream of it runs. Unlock evaluation drains the
        // condition context's dirty signal (which the condition inputs raise,
        // replacing the per-tick poll), and the tap value republishes. Both used
        // to keep their own list of call sites - the same points, maintained
        // twice - and two such lists drift.
        //
        // Public because a boundary the context does not own yet ends here too:
        // slice 6's release and slice 9's restore mutate facts through this
        // context and then declare them settled, rather than growing a second
        // pattern for saying the same thing.
        public void Settle()
        {
            Conditions.Drain(_evaluateUnlocks);

            // unconditional, unlike the drain: the tap value can move for
            // reasons no condition input reports (a granted modifier), and
            // RefreshTapValue already publishes only an actual move
            Production.RefreshTapValue();
        }

        // What the drain evaluates - generator reveals, then content unlocks, the
        // order the poll ran them in. Held as a cached delegate on the field
        // above so the seam allocates nothing per tick.
        private void EvaluateUnlocks()
        {
            Generators.EvaluateUnlocks(Conditions);
            Upgrades.EvaluateContentUnlocks(Conditions);
        }

        // Every system this context holds, filtered for the ones holding
        // modifier-producing facts. The list of systems is the constructor's
        // parameter list, so it cannot fall out of step with what exists.
        private void CollectFactSources()
        {
            foreach (var system in new object[] { Generators, Upgrades, Fans, Production, Bars, Rewards, Flags, Modifiers })
            {
                if (system is IModifierFactSource source)
                    _factSources.Add(source);
            }

            // a frontier economy always has upgrades and bars, so an empty list
            // means the systems failed to construct and every buff would go
            // missing at the first boundary - reported here rather than
            // discovered as a silently unbuffed run
            if (_factSources.Count == 0 && Recipe?.Kind == EconomyRecipeKind.FrontierChapter)
                Debug.LogError($"EconomyContext: chapter '{Chapter?.Id}' has no modifier fact sources - a release or restore would rebuild an empty modifier store. Check that the upgrade and bar systems constructed.");
        }
    }
}
