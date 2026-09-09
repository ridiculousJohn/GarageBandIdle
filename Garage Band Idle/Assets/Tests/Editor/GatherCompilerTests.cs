using System;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The compiled gather (design doc 12.2/12.6): WHICH effects can ever apply
    // to a number is authored and validated, so it is decided once when the tree
    // is built; only each candidate's LIVENESS is a fact. These tests hold the
    // two enumerations that state what a scope declares, the liveness of every
    // link kind, and the plans a consumer reads off a node it already holds.
    //
    // The numbers themselves are the other suites' ground: tick, resolution,
    // bar, idle, fire-producer and walkthrough all read through these plans, and
    // that they produce the same numbers is the proof of the changeset.
    public class GatherCompilerTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // ---- what a scope declares, stated once ----

        // Sources() is the ONE place the kinds of base contribution are named:
        // producers then generators, each in declaration order, each carrying
        // the count fact that scales it. A caller never asks which kind it is
        // holding - that is the whole point of the count riding along.
        [Test]
        public void Sources_names_the_producers_then_the_generators_with_the_count_that_scales_each()
        {
            var tree = new TestTree();
            var sources = tree.Tier1Def.Sources().ToList();

            CollectionAssert.AreEqual(
                new Definition[] { tree.TapProducer, tree.Band, tree.PracticeAmp, tree.Drummer },
                sources.Select(source => source.Source).ToArray(),
                "producers in declaration order, then generators");
            Assert.AreSame(tree.TapProducer.produces, sources[0].Entries, "the source's own entry list");
            Assert.AreSame(tree.Drummer.produces, sources[3].Entries);

            tree.Tier1.generatorCounts["practice_amp"] = 3;

            Assert.AreEqual(1, sources[0].CountAt(tree.Tier1), "a producer is one");
            Assert.AreEqual(1, sources[1].CountAt(tree.Tier1), "always one - there is no count fact to hold");
            Assert.AreEqual(3, sources[2].CountAt(tree.Tier1), "a generator is its owned count");
            Assert.AreEqual(0, sources[3].CountAt(tree.Tier1), "and an unowned one is none");
        }

        // The order of EffectCarriers IS the multiplication order at a node:
        // upgrades, permanent modifiers, granted modifiers, bar cascades, then
        // handicaps - each kind in declaration order, effects in declaration
        // order within a carrier. A sixth carrier kind is an entry here and
        // nowhere else, which is what leaves no second site to forget.
        [Test]
        public void EffectCarriers_names_every_kind_in_the_multiplication_order()
        {
            var tier = TestTree.MakeTier("enumerated");
            var coin = TestTree.DeclareCurrency(tier, "coin");

            // Two effects on one carrier, so "declaration order within a
            // carrier" has something to be in.
            var upgrade = TestTree.MakeDefinition<UpgradeDefinition>("boost");
            upgrade.effects.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = 2 });
            upgrade.effects.Add(new Effect { target = "amp", stat = Stat.Yield, multiplier = 3 });
            tier.upgrades.Add(upgrade);

            var permanent = TestTree.MakeDefinition<ModifierDefinition>("standing");
            permanent.effects.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = 5 });
            tier.modifiers.Add(permanent);
            tier.permanentModifiers.Add(permanent);

            var granted = TestTree.MakeDefinition<ModifierDefinition>("grantable");
            granted.effects.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = 7 });
            tier.modifiers.Add(granted);

            var group = TestTree.MakeDefinition<BarGroupDefinition>("cascades");
            var bar = TestTree.MakeDefinition<BarDefinition>("cascade_bar");
            bar.fillCurrency = coin;
            bar.fillAmount = 10;
            bar.fillRate = 1;
            bar.repeating = true;
            bar.perFill.Add(new PerFillEntry
            {
                effect = new Effect { target = "amp", stat = Stat.Rate, multiplier = 11 },
                growth = GrowthKind.Linear,
            });
            group.bars.Add(bar);
            tier.barGroups.Add(group);

            var evt = TestTree.MakeDefinition<EventDefinition>("gig");
            evt.handicaps.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = 0 });
            tier.events.Add(evt);

            var carriers = tier.EffectCarriers(tier.modifiers).ToList();

            CollectionAssert.AreEqual(
                new[]
                {
                    LivenessKind.Upgrade, LivenessKind.Upgrade, LivenessKind.Permanent,
                    LivenessKind.Granted, LivenessKind.Cascade, LivenessKind.Handicap
                },
                carriers.Select(carrier => carrier.Liveness).ToArray(),
                "upgrades, permanent, granted, cascades, then handicaps");
            CollectionAssert.AreEqual(
                new Definition[] { upgrade, upgrade, permanent, granted, bar, evt },
                carriers.Select(carrier => carrier.Carrier).ToArray(),
                "each effect names the carrier whose fact decides it");

            Assert.AreEqual(Stat.Rate, carriers[0].Effect.stat, "the carrier's own effects, in order");
            Assert.AreEqual(Stat.Yield, carriers[1].Effect.stat);
            Assert.AreEqual(GrowthKind.Linear, carriers[4].Growth,
                "growth rides the carrying entry, never the Effect atom");
        }

        // Root has no events field at all (12.3): the kind it cannot host is
        // unauthorable rather than filtered, so the enumeration has nothing to
        // skip and the override is what adds handicaps for the scopes that can.
        [Test]
        public void A_root_scope_adds_no_handicaps()
        {
            var root = TestTree.MakeRoot("root");
            var coin = TestTree.DeclareCurrency(root, "coin");

            var upgrade = TestTree.MakeDefinition<UpgradeDefinition>("backstage");
            upgrade.effects.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = 2 });
            root.upgrades.Add(upgrade);

            var permanent = TestTree.MakeDefinition<ModifierDefinition>("standing");
            permanent.effects.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = 3 });
            root.modifiers.Add(permanent);
            root.permanentModifiers.Add(permanent);

            var granted = TestTree.MakeDefinition<ModifierDefinition>("grantable");
            granted.effects.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = 5 });
            root.modifiers.Add(granted);

            var group = TestTree.MakeDefinition<BarGroupDefinition>("cascades");
            var bar = TestTree.MakeDefinition<BarDefinition>("cascade_bar");
            bar.fillCurrency = coin;
            bar.fillAmount = 10;
            bar.fillRate = 1;
            bar.repeating = true;
            bar.perFill.Add(new PerFillEntry
            {
                effect = new Effect { target = "amp", stat = Stat.Rate, multiplier = 7 },
            });
            group.bars.Add(bar);
            root.barGroups.Add(group);

            CollectionAssert.AreEqual(
                new[]
                {
                    LivenessKind.Upgrade, LivenessKind.Permanent,
                    LivenessKind.Granted, LivenessKind.Cascade
                },
                root.EffectCarriers(root.modifiers).Select(carrier => carrier.Liveness).ToArray(),
                "the four kinds every scope has, and no handicap");
        }

        // Permanent membership and a granted stack of the SAME modifier are one
        // application, merged through the modifier's own stacking kind (12.5):
        // the enumeration hands out one carrier, so neither path can
        // double-apply outside the vocabulary.
        [Test]
        public void A_permanent_membership_is_never_enumerated_a_second_time_as_a_grant()
        {
            var tier = TestTree.MakeTier("merged");
            var both = TestTree.MakeDefinition<ModifierDefinition>("both");
            both.effects.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = 2 });
            tier.modifiers.Add(both);
            tier.permanentModifiers.Add(both);

            var carriers = tier.EffectCarriers(tier.modifiers).ToList();

            Assert.AreEqual(1, carriers.Count, "one carrier, not one per path");
            Assert.AreEqual(LivenessKind.Permanent, carriers[0].Liveness);
        }

        // ---- the liveness of one link ----

        // One tier under one chapter with a currency of its own and one
        // generator paying it: the smallest tree a coordinate plan needs. Every
        // carrier a test authors targets `amp` at Stat.Rate, so the amp's
        // stage-1 plan holds exactly the links that test declared. Carriers are
        // declared BEFORE Build, because that is when the plans are compiled.
        private class Coordinates
        {
            public readonly RootDefinition RootDef = TestTree.MakeRoot("root");
            public readonly ChapterDefinition Ch1Def = TestTree.MakeChapter("ch1");
            public readonly TierDefinition Tier1Def = TestTree.MakeTier("tier1");
            public readonly CurrencyDefinition Coin;
            public readonly GeneratorDefinition Amp;
            public readonly ProducesEntry AmpRate;

            public RootScopeState Root;
            public ChapterScopeState Ch1;
            public TierScopeState Tier1;

            public Coordinates()
            {
                Coin = TestTree.DeclareCurrency(Tier1Def, "coin");
                Amp = TestTree.MakeDefinition<GeneratorDefinition>("amp", "gear");
                Amp.availableWhen = new CurrencyAtLeast { currency = Coin, threshold = 0 };
                Amp.costCurrency = Coin;
                Amp.baseCost = 10;
                Amp.growth = 1.15;
                AmpRate = TestTree.Entry(Coin, Stat.Rate, 1);
                Amp.produces.Add(AmpRate);
                Tier1Def.generators.Add(Amp);
                Ch1Def.children.Add(Tier1Def);
            }

            public void Build()
            {
                Root = ScopeState.Build(ComposedContent.Compose(RootDef, new[] { Ch1Def }));
                Ch1 = (ChapterScopeState)TestNavigation.Node(Root, Ch1Def);
                Tier1 = (TierScopeState)TestNavigation.Node(Root, Tier1Def);
            }

            // The amp's stage-1 plan, read off the node that declares it.
            public CoordinatePlan AmpPlan => Tier1.Link<CoordinatePlan>(AmpRate);

            public GameContext Ctx(bool idle = false) => new GameContext(Tier1, Now, idleAccumulation: idle);

            public UpgradeDefinition Upgrade(string id, double multiplier)
            {
                var upgrade = TestTree.MakeDefinition<UpgradeDefinition>(id);
                upgrade.gate = new CurrencyAtLeast { currency = Coin, threshold = 0 };
                upgrade.costCurrency = Coin;
                upgrade.cost = 1;
                upgrade.effects.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = multiplier });
                Tier1Def.upgrades.Add(upgrade);
                return upgrade;
            }

            public ModifierDefinition Modifier(string id, double multiplier, StackingKind stacking,
                                               Condition appliesWhen = null, bool permanent = false)
            {
                var modifier = TestTree.MakeDefinition<ModifierDefinition>(id);
                modifier.stacking = stacking;
                modifier.appliesWhen = appliesWhen;
                modifier.effects.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = multiplier });
                Tier1Def.modifiers.Add(modifier);
                if (permanent)
                    Tier1Def.permanentModifiers.Add(modifier);
                return modifier;
            }

            public BarDefinition Cascade(string id, double multiplier, GrowthKind growth)
            {
                var group = TestTree.MakeDefinition<BarGroupDefinition>(id + "_group");
                var bar = TestTree.MakeDefinition<BarDefinition>(id);
                bar.fillCurrency = Coin;
                bar.fillAmount = 10;
                bar.fillRate = 1;
                bar.repeating = true;
                bar.perFill.Add(new PerFillEntry
                {
                    effect = new Effect { target = "amp", stat = Stat.Rate, multiplier = multiplier },
                    growth = growth,
                });
                group.bars.Add(bar);
                Tier1Def.barGroups.Add(group);
                return bar;
            }

            public EventDefinition Handicap(string id, double multiplier)
            {
                var evt = TestTree.MakeDefinition<EventDefinition>(id);
                evt.availableWhen = new Always();
                evt.goal = new CurrencyAtLeast { currency = Coin, threshold = 1 };
                evt.handicaps.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = multiplier });
                Tier1Def.events.Add(evt);
                return evt;
            }
        }

        // A latch is the whole fact: absent contributes One, present contributes
        // the effect's factor, with no count to scale it.
        [Test]
        public void An_upgrade_link_reads_the_purchase_latch_at_its_node()
        {
            var c = new Coordinates();
            c.Upgrade("boost", 2);
            c.Build();

            var link = c.AmpPlan.Links.Single();
            Assert.AreEqual(LivenessKind.Upgrade, link.Liveness);
            Assert.AreSame(c.Tier1, link.Node, "the node holding the fact, not the gather's origin");
            AssertClose(1, link.Factor(c.Ctx()), "unbought");

            c.Tier1.purchasedUpgrades.Add("boost");
            AssertClose(2, link.Factor(c.Ctx()), "bought");
        }

        // Permanent membership is declaration rather than state, so it is live
        // with no fact at all - subject only to appliesWhen, judged at the node
        // the membership sits on.
        [Test]
        public void A_permanent_link_is_live_with_no_fact_and_its_gate_is_judged_at_its_node()
        {
            var c = new Coordinates();
            c.Modifier("standing", 2, StackingKind.Multiply, permanent: true);
            c.Modifier("idle_only", 3, StackingKind.Multiply, new IdleAccumulation(), permanent: true);
            c.Build();

            var standing = c.AmpPlan.Links[0];
            var idleOnly = c.AmpPlan.Links[1];
            Assert.AreEqual(LivenessKind.Permanent, standing.Liveness);
            Assert.AreEqual(LivenessKind.Permanent, idleOnly.Liveness);

            AssertClose(2, standing.Factor(c.Ctx()), "no gate, so always");
            AssertClose(1, idleOnly.Factor(c.Ctx()), "the gate is shut live");
            AssertClose(3, idleOnly.Factor(c.Ctx(idle: true)), "and open under the idle circumstance");
        }

        // The stored count scales the effect by the modifier's OWN stacking
        // kind (12.5), and Replace holds a re-grant at one application rather
        // than scaling it - so a count on disk never buys extra.
        [Test]
        public void A_granted_link_reads_the_stack_at_its_node_and_scales_by_the_stacking_kind()
        {
            var c = new Coordinates();
            c.Modifier("compounding", 2, StackingKind.Multiply);
            c.Modifier("held", 2, StackingKind.Replace);
            c.Build();

            var compounding = c.AmpPlan.Links[0];
            var held = c.AmpPlan.Links[1];
            Assert.AreEqual(LivenessKind.Granted, compounding.Liveness);
            AssertClose(1, compounding.Factor(c.Ctx()), "no stack");
            AssertClose(1, held.Factor(c.Ctx()), "no stack");

            c.Tier1.modifierStacks["compounding"] = 3;
            c.Tier1.modifierStacks["held"] = 3;
            AssertClose(8, compounding.Factor(c.Ctx()), "2 ^ 3");
            AssertClose(2, held.Factor(c.Ctx()), "Replace ignores the count");
        }

        [Test]
        public void A_granted_links_gate_shuts_it_even_with_a_stack_stored()
        {
            var c = new Coordinates();
            c.Modifier("idle_only", 2, StackingKind.Multiply, new IdleAccumulation());
            c.Build();
            c.Tier1.modifierStacks["idle_only"] = 1;

            var link = c.AmpPlan.Links.Single();
            AssertClose(1, link.Factor(c.Ctx()), "stacked, but the gate is shut");
            AssertClose(2, link.Factor(c.Ctx(idle: true)), "and open under the idle circumstance");
        }

        // A completed fill applies the carrying entry's effect again, scaled by
        // the ENTRY's growth kind - growth lives on the entry, never on the
        // Effect atom (12.7).
        [TestCase(GrowthKind.Multiply, 1.331)]      // 1.1 ^ 3
        [TestCase(GrowthKind.Linear, 1.3)]          // 1 + 0.1 x 3
        public void A_cascade_link_reads_the_fill_count_in_its_own_growth_kind(GrowthKind growth, double expected)
        {
            var c = new Coordinates();
            var bar = c.Cascade("cascade_bar", 1.1, growth);
            c.Build();

            var link = c.AmpPlan.Links.Single();
            Assert.AreEqual(LivenessKind.Cascade, link.Liveness);
            AssertClose(1, link.Factor(c.Ctx()), "no fills yet");

            c.Tier1.fillCounts[bar.Id] = 3;
            AssertClose(expected, link.Factor(c.Ctx()));
        }

        // Handicaps ride on the record EXISTING and naming this event; there is
        // one record, so there is no count to scale by.
        [Test]
        public void A_handicap_link_is_live_only_while_the_record_names_its_event()
        {
            var c = new Coordinates();
            c.Handicap("gig", 0);
            c.Build();

            var link = c.AmpPlan.Links.Single();
            Assert.AreEqual(LivenessKind.Handicap, link.Liveness);
            AssertClose(1, link.Factor(c.Ctx()), "no record");

            c.Tier1.activeEvent = new ActiveEvent { eventId = "open_mic", remainingSeconds = 10 };
            AssertClose(1, link.Factor(c.Ctx()), "a record naming another event");

            c.Tier1.activeEvent = new ActiveEvent { eventId = "gig", remainingSeconds = 10 };
            AssertClose(0, link.Factor(c.Ctx()), "running");

            c.Tier1.activeEvent.remainingSeconds = 0;
            AssertClose(0, link.Factor(c.Ctx()), "and expiry does not lift it");
        }

        // The merge, at the link: one application under Replace however many
        // stacks the save carries, because the two paths are ONE carrier.
        [Test]
        public void A_permanent_modifier_that_is_also_stacked_is_one_application_under_Replace()
        {
            var c = new Coordinates();
            c.Modifier("held", 3, StackingKind.Replace, permanent: true);
            c.Build();

            var link = c.AmpPlan.Links.Single();
            Assert.AreEqual(LivenessKind.Permanent, link.Liveness, "one carrier, not two");
            AssertClose(3, link.Factor(c.Ctx()), "membership alone");

            c.Tier1.modifierStacks["held"] = 5;
            AssertClose(3, link.Factor(c.Ctx()), "permanent plus granted is still one application");
        }

        // The one behavior change this changeset makes: granted stacks were
        // visited in dictionary insertion order and are visited in DECLARATION
        // order afterward. No number can notice - the gather multiplies, and
        // multiplication does not care - so the order is asserted where it
        // actually lives, on the plan.
        [Test]
        public void Granted_links_come_back_in_declaration_order_whatever_order_the_stacks_were_written_in()
        {
            var c = new Coordinates();
            var first = c.Modifier("first", 2, StackingKind.Replace);
            var second = c.Modifier("second", 3, StackingKind.Replace);
            c.Build();

            // Written second-then-first, which is the order the dictionary
            // would have handed them back.
            c.Tier1.modifierStacks["second"] = 1;
            c.Tier1.modifierStacks["first"] = 1;

            CollectionAssert.AreEqual(
                new Definition[] { first, second },
                c.AmpPlan.Links.Select(link => link.Carrier).ToArray(),
                "declaration order, not the order the facts arrived in");
            AssertClose(2, c.AmpPlan.Links[0].Factor(c.Ctx()));
            AssertClose(3, c.AmpPlan.Links[1].Factor(c.Ctx()));
        }

        // ---- one plan, a link per node on the chain ----

        // A modifier declared at the chapter can be granted anywhere inside it,
        // so a plan compiled at the inner tier holds ONE LINK PER NODE that
        // could hold a stack of it - and each link reads the stack and the gate
        // of its own node, which is the site validation judges the gate from.
        [Test]
        public void A_plan_holds_a_link_per_tier_and_each_reads_its_own_tiers_stack_and_gate()
        {
            var rootDef = TestTree.MakeRoot("root");
            var chapterDef = TestTree.MakeChapter("chapter");
            var outerDef = TestTree.MakeTier("outer");
            var innerDef = TestTree.MakeTier("inner");
            chapterDef.children.Add(outerDef);
            outerDef.children.Add(innerDef);

            var coin = TestTree.DeclareCurrency(innerDef, "coin");
            var amp = TestTree.MakeDefinition<GeneratorDefinition>("amp");
            amp.availableWhen = new CurrencyAtLeast { currency = coin, threshold = 0 };
            amp.costCurrency = coin;
            amp.baseCost = 10;
            amp.growth = 1.15;
            var ampRate = TestTree.Entry(coin, Stat.Rate, 1);
            amp.produces.Add(ampRate);
            innerDef.generators.Add(amp);

            // The gate is a purchase latch declared at the CHAPTER, so each
            // tier's own facts answer it: a latch written at the inner tier is
            // invisible to the outer one, which is the whole difference.
            var latch = TestTree.MakeDefinition<UpgradeDefinition>("rehearsed");
            latch.gate = new CurrencyAtLeast { currency = coin, threshold = 0 };
            latch.costCurrency = coin;
            latch.cost = 1;
            chapterDef.upgrades.Add(latch);

            var boost = TestTree.MakeDefinition<ModifierDefinition>("boost");
            boost.stacking = StackingKind.Multiply;
            boost.appliesWhen = new UpgradePurchased { upgrade = latch };
            boost.effects.Add(new Effect { target = "amp", stat = Stat.Rate, multiplier = 2 });
            chapterDef.modifiers.Add(boost);

            var root = ScopeState.Build(ComposedContent.Compose(rootDef, new[] { chapterDef }));
            var outer = TestNavigation.Node(root, outerDef);
            var inner = TestNavigation.Node(root, innerDef);

            var chapter = TestNavigation.Node(root, chapterDef);
            var plan = inner.Link<CoordinatePlan>(ampRate);

            // One link per node that could ever hold a stack of it - the two
            // tiers and the chapter declaring it. Root cannot: a grant resolves
            // its modifier OUTWARD, so a modifier declared at the chapter is
            // not one root could have stacked.
            CollectionAssert.AreEqual(new[] { inner, outer, chapter },
                plan.Links.Select(link => link.Node).ToArray(), "chain order, origin outward");
            Assert.IsTrue(plan.Links.All(link => link.Liveness == LivenessKind.Granted));

            var ctx = new GameContext(inner, Now);
            inner.modifierStacks["boost"] = 2;
            outer.modifierStacks["boost"] = 1;
            AssertClose(1, plan.Links[0].Factor(ctx), "stacked, but the gate is shut at the inner tier");
            AssertClose(1, plan.Links[1].Factor(ctx), "and at the outer tier");

            inner.purchasedUpgrades.Add("rehearsed");
            AssertClose(4, plan.Links[0].Factor(ctx), "2 ^ 2, the inner tier's own stack");
            AssertClose(1, plan.Links[1].Factor(ctx), "the outer tier never saw a latch below it");

            outer.purchasedUpgrades.Add("rehearsed");
            AssertClose(2, plan.Links[1].Factor(ctx), "2 ^ 1, the outer tier's own stack");
            AssertClose(1, plan.Links[2].Factor(ctx), "and the chapter holds no stack of its own");
        }

        // ---- which plans exist ----

        // Which queries exist is decided by the CONTENT, and the compiler builds
        // exactly those: every source entry at its declaring node, every
        // currency at its home, every bar at its declaring node, game_speed at
        // every chapter, and a contributor plan at EVERY node, over that node's
        // own subtree.
        [Test]
        public void Every_plan_a_consumer_reads_exists_after_Build()
        {
            var tree = new TestTree();

            // A source at the chapter itself, so the chapter's aggregation and
            // its tier's are not the same list to begin with.
            var label = TestTree.MakeDefinition<ProducerDefinition>("label", "production");
            label.produces.Add(TestTree.Entry(tree.Ch1Records, Stat.Rate, 2));
            tree.Ch1Def.producers.Add(label);
            tree.Rebuild();

            Visit(tree.Root);

            Assert.IsNotNull(tree.Ch1.Link<CoordinatePlan>(GatherCompiler.GameSpeed), "the tick's read");

            // No kind of scope is special to the economy (12.14.8): every node
            // holds the aggregation of its OWN subtree, and which node a
            // consumer asks is its choice of subtree.
            Assert.IsNotNull(ContributorPlan.At(tree.Root), "root's aggregation");
            Assert.IsNotNull(ContributorPlan.At(tree.Ch1), "the chapter's aggregation");
            Assert.IsNotNull(ContributorPlan.At(tree.Tier1), "the tier's aggregation");

            // A tier's plan lists the payers under THAT tier; the chapter's
            // lists everything under the chapter, its own source included.
            Assert.IsNull(ContributorPlan.At(tree.Tier1).For(tree.Ch1Records),
                "nothing under tier1 pays the chapter's own currency");
            Assert.IsNotNull(ContributorPlan.At(tree.Ch1).For(tree.Ch1Records),
                "and the chapter's own source is in the chapter's plan");
            Assert.IsNotNull(ContributorPlan.At(tree.Tier1).For(tree.Cash), "the tier keeps its own payers");
            Assert.IsNotNull(ContributorPlan.At(tree.Ch1).For(tree.Cash), "which the chapter counts as well");

            void Visit(ScopeState node)
            {
                foreach (var source in node.Definition.Sources())
                    foreach (var entry in source.Entries)
                        Assert.IsNotNull(node.Link<CoordinatePlan>(entry),
                            $"{source.Source.Id} pays {entry.currency.Id} at {entry.stat}");

                foreach (var currency in node.Definition.declaredCurrencies)
                {
                    var plans = node.Link<CurrencyPlans>(currency);
                    Assert.IsNotNull(plans.Rate, $"{currency.Id} rate stage");
                    Assert.IsNotNull(plans.Yield, $"{currency.Id} yield stage");
                    Assert.AreSame(plans.Rate, plans.For(Stat.Rate));
                    Assert.AreSame(plans.Yield, plans.For(Stat.Yield));
                }

                foreach (var group in node.Definition.barGroups)
                    foreach (var bar in group.bars)
                        Assert.IsNotNull(node.Link<CoordinatePlan>(bar), $"bar {bar.Id}");

                foreach (var child in node.Children)
                    Visit(child);
            }
        }

        // A query the content never authors has no plan, and asking for one is a
        // thrown miss - never a silent One or zero. Reaching that means a
        // consumer asked a question the content does not pose, which is a code
        // bug and not a state a player's choices produce.
        [Test]
        public void A_query_the_content_never_authors_is_a_thrown_miss()
        {
            var tree = new TestTree();

            // An entry no scope declares, and a currency asked anywhere but at
            // its home: cash lives at tier1, so the chapter holds no stage-2
            // plan for it.
            Assert.Throws<InvalidOperationException>(
                () => tree.Tier1.Link<CoordinatePlan>(TestTree.Entry(tree.Cash, Stat.Rate, 1)));
            Assert.Throws<InvalidOperationException>(() => tree.Ch1.Link<CurrencyPlans>(tree.Cash));

            // game_speed is the tick's read and the tick runs a chapter, so no
            // other kind of node holds one.
            Assert.Throws<InvalidOperationException>(
                () => tree.Root.Link<CoordinatePlan>(GatherCompiler.GameSpeed));
            Assert.Throws<InvalidOperationException>(
                () => tree.Tier1.Link<CoordinatePlan>(GatherCompiler.GameSpeed));
        }

        // The store holds objects now, so the read names the kind it expects:
        // a holder filed under a different kind is the same code bug a miss is,
        // and it throws rather than casting its way into a wrong answer.
        [Test]
        public void A_link_asked_for_as_the_wrong_kind_throws()
        {
            var tree = new TestTree();

            // Filed as CurrencyPlans, asked for as a coordinate plan.
            Assert.Throws<InvalidOperationException>(() => tree.Tier1.Link<CoordinatePlan>(tree.Cash));

            // Filed as a ContributorPlan under the chapter's own definition.
            Assert.Throws<InvalidOperationException>(
                () => tree.Ch1.Link<CoordinatePlan>(tree.Ch1Def));

            Assert.IsNotNull(tree.Tier1.Link<CurrencyPlans>(tree.Cash), "and the right kind still reads");
        }

        // Nothing is written into an asset: the plans live on the runtime nodes,
        // so two games built from one content set hold their own and read their
        // own facts.
        [Test]
        public void Two_trees_from_one_content_set_compile_separate_plans_over_separate_facts()
        {
            var tree = new TestTree();
            var boost = TestTree.MakeDefinition<ModifierDefinition>("boost");
            boost.stacking = StackingKind.Multiply;
            boost.effects.Add(new Effect { target = "practice_amp", stat = Stat.Rate, multiplier = 2 });
            tree.Tier1Def.modifiers.Add(boost);
            tree.Rebuild();

            var aTier1 = tree.Tier1;
            var b = ScopeState.Build(tree.Content);
            var bTier1 = TestNavigation.Node(b, tree.Tier1Def);
            Assert.AreNotSame(aTier1, bTier1, "two trees, one content set");

            var ampRate = tree.PracticeAmp.produces[0];
            var aPlan = aTier1.Link<CoordinatePlan>(ampRate);
            var bPlan = bTier1.Link<CoordinatePlan>(ampRate);
            Assert.AreNotSame(aPlan, bPlan, "a plan per tree, from one authored entry");
            Assert.AreSame(aTier1, aPlan.Home, "each plan holds its own tree's currency home");
            Assert.AreSame(bTier1, bPlan.Home);

            var aBoost = aPlan.Links.Single(link => link.Carrier == boost);
            var bBoost = bPlan.Links.Single(link => link.Carrier == boost);
            Assert.AreSame(aTier1, aBoost.Node, "and each link its own tree's node");
            Assert.AreSame(bTier1, bBoost.Node);

            aTier1.modifierStacks["boost"] = 3;

            AssertClose(8, aBoost.Factor(new GameContext(aTier1, Now)), "A's own stack");
            AssertClose(1, bBoost.Factor(new GameContext(bTier1, Now)), "B saw none of it");
        }

        // A scope's aggregation, compiled: what pays each currency at
        // Stat.Rate in tree order then Sources() order, and where each is homed.
        // This is the walk the rate gather and bar demand rediscovered every
        // segment.
        [Test]
        public void A_contributor_plan_holds_its_payers_in_tree_order_with_their_homes()
        {
            var tree = new TestTree();
            var plan = ContributorPlan.At(tree.Ch1);

            // tier1's sources, in Sources() order: the tap pays rehearsal at a
            // rate, the band pays fans, and the two generators pay cash and
            // fans. First-seen order over that walk is the currency order.
            CollectionAssert.AreEqual(
                new[] { tree.Rehearsal, tree.Fans, tree.Cash },
                plan.Currencies.Select(entry => entry.Currency).ToArray(),
                "first seen, over tree order then declaration order");

            var cash = plan.For(tree.Cash);
            CollectionAssert.AreEqual(
                new Definition[] { tree.PracticeAmp, tree.Drummer },
                cash.Contributors.Select(contributor => contributor.Source.Source).ToArray());
            Assert.AreSame(tree.Tier1, cash.Home, "resolved outward from a paying node");
            Assert.IsNull(plan.For(tree.Roadies), "nothing in the chapter pays roadies");

            // The bars, in settlement order: scopes parent before child, then
            // groups, then bars, all in declaration order.
            CollectionAssert.AreEqual(
                new[] { tree.Cover1, tree.Cover2, tree.Cover3 },
                plan.Bars.Select(bar => bar.Bar).ToArray());
            Assert.AreSame(tree.LearnCovers, plan.Bars[0].Group);
            Assert.AreSame(tree.Tier1, plan.Bars[0].Node);
            Assert.AreSame(tree.Tier1, plan.Bars[0].PoolHome, "rehearsal is homed at tier1");
        }

        // A currency named off the acting chain has no home to find, and the
        // gather compiles inside the LINK PASS - so it is a content fault that
        // throws from Build, where every other unresolved static reference
        // surfaces (12.12/12.14.7). The validation pass reports the same entry
        // as a finding for a person; this is what stands when that dev-only
        // pass has been skipped.
        [Test]
        public void A_produces_entry_naming_a_sibling_chapters_currency_is_a_content_fault_from_Build()
        {
            var tree = new TestTree();

            // A second chapter with a currency of its own. Sibling subtrees
            // cannot see each other, so no walk outward from tier1 reaches it.
            var ch2Def = TestTree.MakeChapter("ch2");
            var tier2Def = TestTree.MakeTier("tier2");
            ch2Def.children.Add(tier2Def);
            var merch = TestTree.DeclareCurrency(tier2Def, "merch");
            tree.Chapters.Add(ch2Def);

            var stand = TestTree.MakeDefinition<ProducerDefinition>("merch_stand", "production");
            stand.produces.Add(TestTree.Entry(merch, Stat.Rate, 1));
            tree.Tier1Def.producers.Add(stand);

            var thrown = Assert.Throws<InvalidOperationException>(() => tree.Rebuild());
            StringAssert.Contains("merch", thrown.Message);
        }
    }
}
