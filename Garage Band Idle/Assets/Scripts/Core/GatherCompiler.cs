using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle
{
    // The compiled answer to the ONE question every multiplier read asks: at
    // this origin, for this owner, this currency, and this stat, which effects
    // can ever apply, and in what order (design doc 12.2/12.6)? The candidates
    // are authored and validated, so they are fixed when the tree is built;
    // only each candidate's liveness is a fact, read on every gather.
    //
    // There is no "stage-1 plan", "currency-stage plan", "bar plan" or
    // "game_speed plan": those are this plan asked with a different owner.
    public sealed class CoordinatePlan
    {
        // The coordinate's currency, used for matching and for the activeWhen
        // gate - null when the query has no currency coordinate (a generator or
        // bar target, a bar filling from time, the tick's game_speed read).
        // Home is the TARGET's: the currency's home for a currency target, the
        // generator's or the bar's for those, found outward from the origin.
        // Every consumer that needs a home takes it from here rather than
        // walking for one.
        public CurrencyDefinition Currency { get; }
        public ScopeState Home { get; }

        // Chain order (origin outward), then EffectCarriers order at each node.
        public IReadOnlyList<EffectLink> Links => links;

        private readonly EffectLink[] links;

        internal CoordinatePlan(CurrencyDefinition currency, ScopeState home, List<EffectLink> links)
        {
            Currency = currency;
            Home = home;
            this.links = links.ToArray();
        }
    }

    // One definition's compiled plans at its home, one per stat it is ever asked
    // for (12.2): a currency's rate and yield totals, a bar's fill rate (also
    // what a rate paid into it collects) and the yield paid into it, a
    // generator's granted count and its cost, an upgrade's cost. Null where a
    // kind is never asked; For throws on a null, since asking is a code bug
    // (requirement 7). Filed under ONE key - the definition itself - because a
    // definition has one home and its totals are modified per stat.
    public sealed class StatPlans
    {
        public CoordinatePlan Rate { get; }
        public CoordinatePlan Yield { get; }
        public CoordinatePlan Count { get; }
        public CoordinatePlan Cost { get; }

        internal StatPlans(CoordinatePlan rate, CoordinatePlan yield, CoordinatePlan count, CoordinatePlan cost)
        {
            Rate = rate;
            Yield = yield;
            Count = count;
            Cost = cost;
        }

        // The plan for one stat. A stat this holder was never compiled for has
        // no total to modify, so asking is a code bug rather than an empty
        // answer (requirement 7).
        public CoordinatePlan For(string stat)
        {
            var plan = stat switch
            {
                Stat.Rate => Rate,
                Stat.Yield => Yield,
                Stat.Count => Count,
                Stat.Cost => Cost,
                _ => null
            };
            return plan ?? throw new InvalidOperationException(
                $"No compiled plan for stat '{stat}' - this holder answers {Answered()} (12.2).");
        }

        // Which stats the compiler filed for this holder, which is the whole of
        // what its kind is asked for - the message's way of saying so.
        private string Answered()
        {
            var answered = new List<string>();
            if (Rate != null) answered.Add(Stat.Rate);
            if (Yield != null) answered.Add(Stat.Yield);
            if (Count != null) answered.Add(Stat.Count);
            if (Cost != null) answered.Add(Stat.Cost);
            return answered.Count == 0 ? "no stat" : string.Join(", ", answered);
        }
    }

    // One source paying one target at Stat.Rate, compiled: the node it is
    // declared at, the source with the count fact that scales it, the entries
    // that pay this target, and the stage-1 plan they share - one coordinate,
    // one plan, however many entries name it.
    public sealed class RateContributor
    {
        public ScopeState Node { get; }
        public ScopeSource Source { get; }
        public IReadOnlyList<ProducesEntry> Entries { get; }
        public CoordinatePlan Plan { get; }

        internal RateContributor(ScopeState node, ScopeSource source,
                                 IReadOnlyList<ProducesEntry> entries, CoordinatePlan plan)
        {
            Node = node;
            Source = source;
            Entries = entries;
            Plan = plan;
        }
    }

    // Everything a scope's subtree pays into one target at Stat.Rate, in
    // tree order (parent before child) then Sources() order, with the home the
    // total is deposited at - resolved outward from a PAYING node, never from
    // the subtree root, since the target may be homed below where the walk
    // started (design doc 12.2).
    public sealed class TargetContributors
    {
        public Definition Target { get; }
        public ScopeState Home { get; internal set; }
        public IReadOnlyList<RateContributor> Contributors => contributors;

        internal readonly List<RateContributor> contributors = new();

        internal TargetContributors(Definition target)
        {
            Target = target;
        }
    }

    // One bar in a scope's settlement order, with everything the draw needs
    // that is static: where it lives, the group whose selection admits it, the
    // pool it drinks from, and its fill-rate plan (design doc 12.7).
    public sealed class BarPlan
    {
        public ScopeState Node { get; }
        public BarGroupDefinition Group { get; }
        public BarDefinition Bar { get; }

        // The pool currency's own home, carried beside the fill-rate plan
        // because that plan is homed at the BAR (12.2). Null when the bar fills
        // from time alone.
        public ScopeState PoolHome { get; }

        public CoordinatePlan Rate { get; }

        internal BarPlan(ScopeState node, BarGroupDefinition group, BarDefinition bar, CoordinatePlan rate,
                         ScopeState poolHome)
        {
            Node = node;
            Group = group;
            Bar = bar;
            PoolHome = poolHome;
            Rate = rate;
        }
    }

    // A scope's compiled aggregation (design doc 12.2): which sources in its
    // OWN subtree pay which target at Stat.Rate and where each is homed, and
    // its bars in settlement order. This is what the rate gather and bar demand
    // rediscovered by walking the subtree on every segment.
    public sealed class ContributorPlan
    {
        // First-seen order over the same walk: tree order, then Sources() order,
        // then entry order - declaration-shaped, so a target whose only payer
        // is currently unowned is still listed and simply pays zero. Bars are
        // listed like any target; Producer.RatePairs is what excludes them.
        public IReadOnlyList<TargetContributors> Targets => targets;

        // Scopes parent before child, then barGroups in declaration order, then
        // bars in declaration order (12.7).
        public IReadOnlyList<BarPlan> Bars => bars;

        private readonly TargetContributors[] targets;
        private readonly BarPlan[] bars;

        internal ContributorPlan(List<TargetContributors> targets, List<BarPlan> bars)
        {
            this.targets = targets.ToArray();
            this.bars = bars.ToArray();
        }

        // What pays this target here, or null when nothing in the subtree
        // does - which is not a miss but the answer: a target no source pays
        // has a rate of zero, and the plan is what says so.
        public TargetContributors For(Definition target)
        {
            for (var i = 0; i < targets.Length; i++)
                if (targets[i].Target == target)
                    return targets[i];
            return null;
        }

        // The plan a node holds, under its own definition. A read at a node the
        // caller already has, like every other link - the subtree meant is the
        // one rooted at that node.
        public static ContributorPlan At(ScopeState node)
        {
            if (node == null)
                throw new ArgumentNullException(nameof(node),
                    "a contributor plan is read off the node whose subtree it was compiled over (12.2).");
            return node.Link<ContributorPlan>(node.Definition);
        }
    }

    // The second half of the load-time link pass (design doc 12.14.8): the
    // gather's static half, resolved once when a tree is built. It runs after
    // the scope references are linked, so Build has exactly one post-
    // construction step and the plans are compiled over a wired tree.
    //
    // This is the ONE place Producer.Matches is called and the one place a
    // subtree is walked to discover what it declares - at load, once, which is
    // exactly the exception 12.14.8 grants validation and for the same reason:
    // it is what lets every runtime read trust the plan it already holds.
    public static class GatherCompiler
    {
        // The one holder every chapter's game_speed plan is filed under. The
        // tick's read is owner-less and currency-less (12.2), so there is no
        // authored object to key it by - one static object is the whole
        // identity, shared by every tree because a link is stored per NODE.
        public static readonly object GameSpeed = new object();

        // Every plan the content authors, compiled onto the nodes that will be
        // asked for them. Coordinate plans first, since a scope's contributor
        // plan is assembled out of them.
        public static void Compile(ScopeState root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            var grantable = new Dictionary<ScopeState, List<ModifierDefinition>>();
            CompileCoordinates(root, grantable);
            CompileContributors(root);
        }

        // Which queries exist is decided by the content, and exactly those are
        // built: every source entry at its declaring node (stage 1), every
        // currency at its home for rate and yield (stage 2), every generator for
        // its granted count and its cost, every upgrade for its cost, every bar
        // at its declaring node (its fill rate and the yield paid into it -
        // 12.7), and game_speed at every chapter node.
        private static void CompileCoordinates(ScopeState node,
                                               Dictionary<ScopeState, List<ModifierDefinition>> grantable)
        {
            foreach (var source in node.Definition.Sources())
            {
                // Entries of one source naming one target and stat are one
                // coordinate, so they share one plan: an earlier entry on the
                // same coordinate already compiled it, and this entry is filed
                // under that same object rather than walking the chain again.
                var done = new List<ProducesEntry>();
                foreach (var entry in source.Entries)
                {
                    // An entry naming no target names no coordinate, so there is
                    // nothing to compile; validation reports it (12.2).
                    if (entry == null || entry.Target == null)
                        continue;
                    var same = done.Find(e => e.Target == entry.Target && e.stat == entry.stat);
                    node.StoreLink(entry, same != null
                        ? node.Link<CoordinatePlan>(same)
                        : Plan(node, source.Source, entry.currency, entry.stat, HomeOf(node, entry.Target), grantable));
                    done.Add(entry);
                }
            }

            foreach (var currency in node.Definition.declaredCurrencies)
            {
                if (currency == null)
                    continue;
                // The currency's own definition is the owner, so its tags
                // match - which is how the income tag carries the Records
                // and Roadie factors (design doc 8.2).
                var home = HomeOf(node, currency);
                node.StoreLink(currency, new StatPlans(
                    Plan(node, currency, currency, Stat.Rate, home, grantable),
                    Plan(node, currency, currency, Stat.Yield, home, grantable),
                    null, null));
            }

            // A generator's count plan carries no currency coordinate - one
            // number however the grant arrives - and its cost plan names the
            // cost currency, whose home the read never uses (12.2).
            foreach (var generator in node.Definition.generators)
            {
                if (generator == null)
                    continue;
                node.StoreLink(generator, new StatPlans(null, null,
                    Plan(node, generator, null, Stat.Count, node, grantable),
                    Plan(node, generator, generator.costCurrency, Stat.Cost,
                         HomeOf(node, generator.costCurrency), grantable)));
            }

            foreach (var upgrade in node.Definition.upgrades)
            {
                if (upgrade == null)
                    continue;
                node.StoreLink(upgrade, new StatPlans(null, null, null,
                    Plan(node, upgrade, upgrade.costCurrency, Stat.Cost,
                         HomeOf(node, upgrade.costCurrency), grantable)));
            }

            foreach (var group in node.Definition.barGroups)
            {
                if (group == null)
                    continue;
                foreach (var bar in group.bars)
                    if (bar != null)
                        // The rate plan IS the fill-rate plan, and it is also
                        // what a rate paid into the bar collects: "10x this bar"
                        // speeds its own fill and what is paid into it alike
                        // (12.7). Both are homed at the bar itself.
                        node.StoreLink(bar, new StatPlans(
                            Plan(node, bar, bar.fillCurrency, Stat.Rate, node, grantable),
                            Plan(node, bar, null, Stat.Yield, node, grantable),
                            null, null));
            }

            if (node is ChapterScopeState)
                node.StoreLink(GameSpeed, Plan(node, null, null, Stat.GameSpeed, null, grantable));

            foreach (var child in node.Children)
                CompileCoordinates(child, grantable);
        }

        // One query: walk outward from the origin; at each node take
        // EffectCarriers in order; keep every effect the selector rule of 12.2
        // accepts. The kept links, chain order then carrier order, ARE the plan
        // - and Matches never runs again. The home is the target's, resolved by
        // the caller, since which definition a query is ABOUT is the caller's.
        private static CoordinatePlan Plan(ScopeState origin, Definition owner, CurrencyDefinition currency,
                                           string stat, ScopeState home,
                                           Dictionary<ScopeState, List<ModifierDefinition>> grantable)
        {
            var links = new List<EffectLink>();
            for (var node = origin; node != null; node = node.Parent)
                foreach (var carrier in node.Definition.EffectCarriers(Grantable(node, grantable)))
                    if (Producer.Matches(carrier.Effect.target, carrier.Effect.currencyId, carrier.Effect.stat,
                                         owner, currency, stat))
                        links.Add(new EffectLink(carrier, node));
            return new CoordinatePlan(currency, home, links);
        }

        // The modifiers a grant at this node could ever have stacked: the ones
        // declared on its chain, self outward, first declaration winning - the
        // same answer the outward walk gave the stored id (design doc 12.5).
        // Cached per node because every plan at that node asks for it.
        private static List<ModifierDefinition> Grantable(ScopeState node,
                                                          Dictionary<ScopeState, List<ModifierDefinition>> cache)
        {
            if (cache.TryGetValue(node, out var chain))
                return chain;
            chain = new List<ModifierDefinition>();
            for (var current = node; current != null; current = current.Parent)
                foreach (var modifier in current.Definition.modifiers)
                    if (modifier != null && !chain.Contains(modifier))
                        chain.Add(modifier);
            cache[node] = chain;
            return chain;
        }

        // The target's home: the first scope OUTWARD from here declaring this
        // exact asset (design doc 12.3). An entry, a currency stage, a bar or a
        // cost naming an asset off its chain is a content fault, so it throws
        // HERE, at build, like every other unresolved static reference in this
        // pass (12.14.7): the validation pass reports it as a finding for a
        // person, and this is what stands when that pass has been skipped. No
        // target at all is not a fault - a bar filling from time and the
        // game_speed read have no currency coordinate, so they have no home
        // either.
        private static ScopeState HomeOf(ScopeState from, Definition target)
        {
            if (target == null)
                return null;
            var home = Producer.FindDeclaringScope<ScopeState>(from, target);
            if (home == null)
                throw new InvalidOperationException(
                    $"No scope on the chain from '{from.ScopeId}' declares {Kind(target)} '{target.Id}' (12.12).");
            return home;
        }

        // The word the message calls a target by, so a fault reads the way the
        // design doc names the thing (12.12).
        private static string Kind(Definition target) => target switch
        {
            CurrencyDefinition => "currency",
            GeneratorDefinition => "generator",
            BarDefinition => "bar",
            UpgradeDefinition => "upgrade",
            _ => target.GetType().Name
        };

        // No kind of scope is special to the economy (12.14.8): every node holds
        // the aggregation of its OWN subtree, keyed by its own definition, and
        // which node a consumer asks is the consumer's choice of subtree.
        private static void CompileContributors(ScopeState node)
        {
            node.StoreLink(node.Definition, ContributorsOf(node));
            foreach (var child in node.Children)
                CompileContributors(child);
        }

        // One scope's aggregation, over its own subtree: what pays each
        // target at Stat.Rate, and the bars in settlement order.
        private static ContributorPlan ContributorsOf(ScopeState subtreeRoot)
        {
            var targets = new List<TargetContributors>();
            var bars = new List<BarPlan>();
            Walk(subtreeRoot);
            return new ContributorPlan(targets, bars);

            void Walk(ScopeState node)
            {
                foreach (var source in node.Definition.Sources())
                    Contribute(node, source);

                foreach (var group in node.Definition.barGroups)
                {
                    if (group == null)
                        continue;
                    foreach (var bar in group.bars)
                        if (bar != null)
                            bars.Add(new BarPlan(node, group, bar, node.Link<StatPlans>(bar).Rate,
                                                 HomeOf(node, bar.fillCurrency)));
                }

                foreach (var child in node.Children)
                    Walk(child);
            }

            // One source's rate entries, grouped by the target they pay in
            // authored order: entries naming one target share one coordinate,
            // so they share one plan and sum into one term.
            void Contribute(ScopeState node, ScopeSource source)
            {
                var paid = new List<Definition>();
                var grouped = new List<List<ProducesEntry>>();
                foreach (var entry in source.Entries)
                {
                    if (entry == null || entry.Target == null || entry.stat != Stat.Rate)
                        continue;
                    var index = paid.IndexOf(entry.Target);
                    if (index < 0)
                    {
                        paid.Add(entry.Target);
                        grouped.Add(new List<ProducesEntry>());
                        index = paid.Count - 1;
                    }
                    grouped[index].Add(entry);
                }

                for (var i = 0; i < paid.Count; i++)
                {
                    var plan = node.Link<CoordinatePlan>(grouped[i][0]);
                    var bucket = Bucket(paid[i]);
                    bucket.Home ??= plan.Home;
                    bucket.contributors.Add(new RateContributor(node, source, grouped[i], plan));
                }
            }

            TargetContributors Bucket(Definition target)
            {
                for (var i = 0; i < targets.Count; i++)
                    if (targets[i].Target == target)
                        return targets[i];
                var fresh = new TargetContributors(target);
                targets.Add(fresh);
                return fresh;
            }
        }
    }
}
