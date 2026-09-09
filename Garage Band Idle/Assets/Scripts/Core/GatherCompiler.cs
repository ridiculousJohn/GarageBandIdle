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
        // The coordinate's currency, and the node declaring it as found outward
        // from the origin - null when the query has no currency coordinate (a
        // bar filling from time, the tick's game_speed read). Every consumer
        // that needs a home takes it from here rather than walking for one.
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

    // A currency's two stage-2 plans, filed at its home under ONE key - the
    // CurrencyDefinition itself - because a currency has one home and its
    // total is modified per stat (design doc 12.2).
    public sealed class CurrencyPlans
    {
        public CoordinatePlan Rate { get; }
        public CoordinatePlan Yield { get; }

        internal CurrencyPlans(CoordinatePlan rate, CoordinatePlan yield)
        {
            Rate = rate;
            Yield = yield;
        }

        // The plan for one produced stat. A stat outside the produced
        // vocabulary has no currency total to modify, so asking is a code bug
        // rather than an empty answer (requirement 7).
        public CoordinatePlan For(string stat)
        {
            if (stat == Stat.Rate)
                return Rate;
            if (stat == Stat.Yield)
                return Yield;
            throw new InvalidOperationException(
                $"No currency-stage plan for stat '{stat}' - the currency stage answers {Stat.ProducedNames} (12.2).");
        }
    }

    // One source paying one currency at Stat.Rate, compiled: the node it is
    // declared at, the source with the count fact that scales it, the entries
    // that pay this currency, and the stage-1 plan they share - one coordinate,
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

    // Everything a scope's subtree pays into one currency at Stat.Rate, in
    // tree order (parent before child) then Sources() order, with the home the
    // total is deposited at - resolved outward from a PAYING node, never from
    // the subtree root, since the currency may be homed below where the walk
    // started (design doc 12.2).
    public sealed class CurrencyContributors
    {
        public CurrencyDefinition Currency { get; }
        public ScopeState Home { get; internal set; }
        public IReadOnlyList<RateContributor> Contributors => contributors;

        internal readonly List<RateContributor> contributors = new();

        internal CurrencyContributors(CurrencyDefinition currency)
        {
            Currency = currency;
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

        // Null when the bar fills from time alone.
        public ScopeState PoolHome { get; }

        public CoordinatePlan Rate { get; }

        internal BarPlan(ScopeState node, BarGroupDefinition group, BarDefinition bar, CoordinatePlan rate)
        {
            Node = node;
            Group = group;
            Bar = bar;
            PoolHome = rate.Home;
            Rate = rate;
        }
    }

    // A scope's compiled aggregation (design doc 12.2): which sources in its
    // OWN subtree pay which currency at Stat.Rate and where each is homed, and
    // its bars in settlement order. This is what the rate gather and bar demand
    // rediscovered by walking the subtree on every segment.
    public sealed class ContributorPlan
    {
        // First-seen order over the same walk: tree order, then Sources() order,
        // then entry order - declaration-shaped, so a currency whose only payer
        // is currently unowned is still listed and simply pays zero.
        public IReadOnlyList<CurrencyContributors> Currencies => currencies;

        // Scopes parent before child, then barGroups in declaration order, then
        // bars in declaration order (12.7).
        public IReadOnlyList<BarPlan> Bars => bars;

        private readonly CurrencyContributors[] currencies;
        private readonly BarPlan[] bars;

        internal ContributorPlan(List<CurrencyContributors> currencies, List<BarPlan> bars)
        {
            this.currencies = currencies.ToArray();
            this.bars = bars.ToArray();
        }

        // What pays this currency here, or null when nothing in the subtree
        // does - which is not a miss but the answer: a currency no source pays
        // has a rate of zero, and the plan is what says so.
        public CurrencyContributors For(CurrencyDefinition currency)
        {
            for (var i = 0; i < currencies.Length; i++)
                if (currencies[i].Currency == currency)
                    return currencies[i];
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
        // currency at its home for rate and yield (stage 2), every bar at its
        // declaring node (its fill rate, stage 1 only - 12.7), and game_speed at
        // every chapter node.
        private static void CompileCoordinates(ScopeState node,
                                               Dictionary<ScopeState, List<ModifierDefinition>> grantable)
        {
            foreach (var source in node.Definition.Sources())
                foreach (var entry in source.Entries)
                    if (entry != null)
                        node.StoreLink(entry, Plan(node, source.Source, entry.currency, entry.stat, grantable));

            foreach (var currency in node.Definition.declaredCurrencies)
                if (currency != null)
                    // The currency's own definition is the owner, so its tags
                    // match - which is how the income tag carries the Records
                    // and Roadie factors (design doc 8.2).
                    node.StoreLink(currency, new CurrencyPlans(
                        Plan(node, currency, currency, Stat.Rate, grantable),
                        Plan(node, currency, currency, Stat.Yield, grantable)));

            foreach (var group in node.Definition.barGroups)
            {
                if (group == null)
                    continue;
                foreach (var bar in group.bars)
                    if (bar != null)
                        node.StoreLink(bar, Plan(node, bar, bar.fillCurrency, Stat.Rate, grantable));
            }

            if (node is ChapterScopeState)
                node.StoreLink(GameSpeed, Plan(node, null, null, Stat.GameSpeed, grantable));

            foreach (var child in node.Children)
                CompileCoordinates(child, grantable);
        }

        // One query: walk outward from the origin; at each node take
        // EffectCarriers in order; keep every effect the selector rule of 12.2
        // accepts. The kept links, chain order then carrier order, ARE the plan
        // - and Matches never runs again.
        private static CoordinatePlan Plan(ScopeState origin, Definition owner, CurrencyDefinition currency,
                                           string stat, Dictionary<ScopeState, List<ModifierDefinition>> grantable)
        {
            var links = new List<EffectLink>();
            for (var node = origin; node != null; node = node.Parent)
                foreach (var carrier in node.Definition.EffectCarriers(Grantable(node, grantable)))
                    if (Producer.Matches(carrier.Effect.target, carrier.Effect.currencyId, carrier.Effect.stat,
                                         owner, currency, stat))
                        links.Add(new EffectLink(carrier, node));
            return new CoordinatePlan(currency, HomeOf(origin, currency), links);
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

        // The currency's home: the first scope OUTWARD from here declaring this
        // exact asset (design doc 12.3). An entry, a currency stage or a bar
        // naming a currency off its chain is a content fault, so it throws HERE,
        // at build, like every other unresolved static reference in this pass
        // (12.14.7): the validation pass reports it as a finding for a person,
        // and this is what stands when that pass has been skipped. No currency
        // at all is not a fault - a bar filling from time and the game_speed
        // read have no currency coordinate, so they have no home either.
        private static ScopeState HomeOf(ScopeState from, CurrencyDefinition currency)
        {
            if (currency == null)
                return null;
            var home = Producer.FindDeclaringScope<ScopeState>(from, currency);
            if (home == null)
                throw new InvalidOperationException(
                    $"No scope on the chain from '{from.ScopeId}' declares currency '{currency.Id}' (12.12).");
            return home;
        }

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
        // currency at Stat.Rate, and the bars in settlement order.
        private static ContributorPlan ContributorsOf(ScopeState subtreeRoot)
        {
            var currencies = new List<CurrencyContributors>();
            var bars = new List<BarPlan>();
            Walk(subtreeRoot);
            return new ContributorPlan(currencies, bars);

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
                            bars.Add(new BarPlan(node, group, bar, node.Link<CoordinatePlan>(bar)));
                }

                foreach (var child in node.Children)
                    Walk(child);
            }

            // One source's rate entries, grouped by the currency they pay in
            // authored order: entries naming one currency share one coordinate,
            // so they share one plan and sum into one term.
            void Contribute(ScopeState node, ScopeSource source)
            {
                var paid = new List<CurrencyDefinition>();
                var grouped = new List<List<ProducesEntry>>();
                foreach (var entry in source.Entries)
                {
                    if (entry == null || entry.currency == null || entry.stat != Stat.Rate)
                        continue;
                    var index = paid.IndexOf(entry.currency);
                    if (index < 0)
                    {
                        paid.Add(entry.currency);
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

            CurrencyContributors Bucket(CurrencyDefinition currency)
            {
                for (var i = 0; i < currencies.Count; i++)
                    if (currencies[i].Currency == currency)
                        return currencies[i];
                var fresh = new CurrencyContributors(currency);
                currencies.Add(fresh);
                return fresh;
            }
        }
    }
}
