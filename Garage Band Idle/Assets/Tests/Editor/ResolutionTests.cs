using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The compute-on-read half: which factors apply to a number, and how the two
    // gather stages compose it (design doc 12.2). Numbers are compared with a
    // tolerance because every one of them is a product of authored decimals.
    public class ResolutionTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // ---- the match rule ----

        [Test]
        public void An_effect_matches_its_target_by_id_or_by_tag()
        {
            var tree = new TestTree();
            var byId = TestTree.MakeDefinition<ModifierDefinition>("by_id");
            byId.effects.Add(new Effect { target = "practice_amp", multiplier = 2 });
            var byTag = TestTree.MakeDefinition<ModifierDefinition>("by_tag");
            byTag.effects.Add(new Effect { target = "gear", multiplier = 3 });
            var byNothing = TestTree.MakeDefinition<ModifierDefinition>("by_nothing");
            byNothing.effects.Add(new Effect { target = "bassist", multiplier = 5 });
            tree.Defs.Add(byId).Add(byTag).Add(byNothing);

            var ctx = tree.Ctx(tree.Tier1);
            tree.Tier1.activeModifiers.Add(new ActiveModifierEntry { modifierId = "by_id", count = 1 });
            tree.Tier1.activeModifiers.Add(new ActiveModifierEntry { modifierId = "by_tag", count = 1 });
            tree.Tier1.activeModifiers.Add(new ActiveModifierEntry { modifierId = "by_nothing", count = 1 });

            // practice_amp is hit by the id AND the gear tag; drummer carries the
            // tag only; nothing matches an id the owner does not answer to.
            AssertClose(6, Producer.GetMultiplier(ctx, tree.PracticeAmp, "cash", Stat.Rate), "practice_amp");
            AssertClose(3, Producer.GetMultiplier(ctx, tree.Drummer, "cash", Stat.Rate), "drummer");
            AssertClose(1, Producer.GetMultiplier(ctx, tree.TapProducer, "cash", Stat.Yield), "tap_producer");
        }

        [Test]
        public void Coordinates_narrow_a_match_from_every_number_down_to_one()
        {
            var tree = new TestTree();
            var everything = TestTree.MakeDefinition<ModifierDefinition>("everything");
            everything.effects.Add(new Effect { target = "tap_producer", multiplier = 2 });
            var justCash = TestTree.MakeDefinition<ModifierDefinition>("just_cash");
            justCash.effects.Add(new Effect { target = "tap_producer", currencyId = "cash", multiplier = 3 });
            var justRate = TestTree.MakeDefinition<ModifierDefinition>("just_rate");
            justRate.effects.Add(new Effect { target = "tap_producer", stat = Stat.Rate, multiplier = 5 });
            var exactly = TestTree.MakeDefinition<ModifierDefinition>("exactly");
            exactly.effects.Add(new Effect { target = "tap_producer", currencyId = "rehearsal", stat = Stat.Rate, multiplier = 7 });
            tree.Defs.Add(everything).Add(justCash).Add(justRate).Add(exactly);

            var ctx = tree.Ctx(tree.Tier1);
            foreach (var id in new[] { "everything", "just_cash", "just_rate", "exactly" })
                tree.Tier1.activeModifiers.Add(new ActiveModifierEntry { modifierId = id, count = 1 });

            // Both coordinates empty matches everything the owner has; either one
            // narrows; both name one entry exactly.
            AssertClose(2 * 3, Producer.GetMultiplier(ctx, tree.TapProducer, "cash", Stat.Yield), "cash yield");
            AssertClose(2 * 5 * 7, Producer.GetMultiplier(ctx, tree.TapProducer, "rehearsal", Stat.Rate), "rehearsal rate");
            AssertClose(2, Producer.GetMultiplier(ctx, tree.TapProducer, "rehearsal", Stat.Yield), "rehearsal yield");
        }

        [Test]
        public void Modifier_stacks_scale_by_their_stacking_kind()
        {
            var tree = new TestTree();
            var replace = TestTree.MakeDefinition<ModifierDefinition>("replace_boost");
            replace.stacking = StackingKind.Replace;
            replace.effects.Add(new Effect { target = "tap_producer", multiplier = 2 });
            var linear = TestTree.MakeDefinition<ModifierDefinition>("linear_boost");
            linear.stacking = StackingKind.Linear;
            linear.effects.Add(new Effect { target = "tap_producer", multiplier = 2 });
            var multiply = TestTree.MakeDefinition<ModifierDefinition>("multiply_boost");
            multiply.stacking = StackingKind.Multiply;
            multiply.effects.Add(new Effect { target = "tap_producer", multiplier = 2 });
            tree.Defs.Add(replace).Add(linear).Add(multiply);

            var ctx = tree.Ctx(tree.Tier1);
            var stack = new ActiveModifierEntry { modifierId = "replace_boost", count = 3 };
            tree.Tier1.activeModifiers.Add(stack);

            // Replace ignores the count entirely - AddModifier holds it at 1, and
            // a count on disk never buys extra.
            AssertClose(2, Producer.GetMultiplier(ctx, tree.TapProducer, "cash", Stat.Yield), "replace");

            stack.modifierId = "linear_boost";
            AssertClose(4, Producer.GetMultiplier(ctx, tree.TapProducer, "cash", Stat.Yield), "linear");

            stack.modifierId = "multiply_boost";
            AssertClose(8, Producer.GetMultiplier(ctx, tree.TapProducer, "cash", Stat.Yield), "multiply");
        }

        [Test]
        public void A_linear_stack_saturates_at_zero_instead_of_going_negative()
        {
            var tree = new TestTree();
            var decay = TestTree.MakeDefinition<ModifierDefinition>("decay");
            decay.stacking = StackingKind.Linear;
            decay.effects.Add(new Effect { target = "tap_producer", multiplier = 0.5 });
            tree.Defs.Add(decay);

            var ctx = tree.Ctx(tree.Tier1);
            var stack = new ActiveModifierEntry { modifierId = "decay", count = 1 };
            tree.Tier1.activeModifiers.Add(stack);

            // A debuff that decays linearly is legal authoring, and 1 + (m-1)*n
            // crosses zero at n = 2 - beyond which a raw formula would run
            // production backwards.
            AssertClose(0.5, Producer.GetMultiplier(ctx, tree.TapProducer, "cash", Stat.Yield), "one stack");
            stack.count = 2;
            AssertClose(0, Producer.GetMultiplier(ctx, tree.TapProducer, "cash", Stat.Yield), "two stacks");
            stack.count = 5;
            AssertClose(0, Producer.GetMultiplier(ctx, tree.TapProducer, "cash", Stat.Yield), "five stacks");
        }

        // Count scaling happens in BigNumber, not in double arithmetic that
        // overflows on the way there. Absurd content, but the property is what
        // silently regresses if a parameter type drifts back.
        [Test]
        public void Count_scaling_never_overflows_on_its_way_into_BigNumber()
        {
            var tree = new TestTree();
            var huge = TestTree.MakeDefinition<ModifierDefinition>("huge");
            huge.stacking = StackingKind.Linear;
            huge.effects.Add(new Effect { target = "tap_producer", multiplier = double.MaxValue });
            tree.Defs.Add(huge);
            tree.Tier1.activeModifiers.Add(new ActiveModifierEntry { modifierId = "huge", count = 2 });

            // Past double range without ever having been an infinity: the
            // constructor would have thrown on the way through.
            var product = Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.TapProducer, "cash", Stat.Yield);
            Assert.IsTrue(product > (BigNumber)double.MaxValue, $"expected a value past double range, got {product}");
        }

        [Test]
        public void A_venue_boost_never_overflows_on_its_way_into_BigNumber()
        {
            var tree = new TestTree();
            tree.Garage.perRoadie = double.MaxValue;
            tree.Root.roadieAllocation["ch1"] = 2;

            var boost = tree.Garage.Boost(tree.Ctx(tree.Tier1));
            Assert.IsTrue(boost > (BigNumber)double.MaxValue, $"expected a value past double range, got {boost}");
        }

        // ---- the two stages ----

        [Test]
        public void An_upgrade_effect_multiplies_inside_its_source_term()
        {
            var tree = new TestTree();
            tree.Tier1.generatorCounts["practice_amp"] = 3;
            tree.Tier1.generatorCounts["drummer"] = 2;

            // 0.5 x 3 + 3 x 2
            AssertClose(7.5, Producer.GetRate(tree.Tier1, tree.Defs, Now, "cash"), "no upgrades");

            // amp_strings doubles the amp's term only - the drummer's is untouched.
            tree.Tier1.purchasedUpgrades.Add("amp_strings");
            AssertClose(9, Producer.GetRate(tree.Tier1, tree.Defs, Now, "cash"), "amp_strings");
        }

        [Test]
        public void A_currency_effect_multiplies_the_total_and_its_stat_narrowing_holds()
        {
            var tree = new TestTree();
            tree.Tier1.generatorCounts["practice_amp"] = 4;
            tree.Tier1.purchasedUpgrades.Add("tight_set");

            // tight_set targets cash with stat: rate, so it lifts the rate...
            AssertClose(0.5 * 4 * 1.5, Producer.GetRate(tree.Tier1, tree.Defs, Now, "cash"), "cash rate");

            // ...and leaves the tap yield alone.
            AssertClose(1, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.Defs.Get<CurrencyDefinition>("cash"), "cash", Stat.Yield), "cash yield");
        }

        [Test]
        public void Owned_counts_scale_a_generator_and_absent_counts_contribute_nothing()
        {
            var tree = new TestTree();
            AssertClose(0, Producer.GetRate(tree.Tier1, tree.Defs, Now, "cash"), "nothing owned");

            tree.Tier1.generatorCounts["practice_amp"] = 7;
            AssertClose(3.5, Producer.GetRate(tree.Tier1, tree.Defs, Now, "cash"), "seven amps");
        }

        [Test]
        public void Entry_conditions_are_judged_in_the_declaring_scope()
        {
            var tree = new TestTree();

            // The Jam's rehearsal rate is gated on the reveal: no pre-banking.
            AssertClose(0, Producer.GetRate(tree.Tier1, tree.Defs, Now, "rehearsal"), "before the reveal");
            tree.Tier1.flags.Add("rehearsal_revealed");
            AssertClose(0.5, Producer.GetRate(tree.Tier1, tree.Defs, Now, "rehearsal"), "after the reveal");
        }

        [Test]
        public void A_subtree_rate_sums_every_source_it_contains()
        {
            var tree = new TestTree();
            tree.Tier1.flags.Add("fans_revealed");
            tree.Tier1.generatorCounts["drummer"] = 3;

            // The band's base accrual plus each bandmate's own fans entry.
            AssertClose(0.35 + 0.02 * 3, Producer.GetRate(tree.Tier1, tree.Defs, Now, "fans"), "from tier1");

            // Asking from further out finds the same sources - the subtree root
            // decides what is counted, not where the currency lives.
            AssertClose(0.35 + 0.02 * 3, Producer.GetRate(tree.Ch1, tree.Defs, Now, "fans"), "from ch1");
            AssertClose(0, Producer.GetRate(tree.Root, tree.Defs, Now, "roadies"), "a currency nothing produces");
        }

        [Test]
        public void Source_stage_effects_do_not_reach_a_sibling_scope()
        {
            // Two sibling tiers under one chapter, each with a generator paying a
            // chapter-homed currency. The shape that makes isolation visible.
            var rootDef = TestTree.MakeScope("root");
            var chapterDef = TestTree.MakeScope("chapter");
            chapterDef.declaredCurrencyIds.Add("coin");
            var tierADef = TestTree.MakeScope("tier_a");
            var tierBDef = TestTree.MakeScope("tier_b");
            rootDef.children.Add(chapterDef);
            chapterDef.children.Add(tierADef);
            chapterDef.children.Add(tierBDef);

            var genA = MakeGenerator("gen_a", "coin");
            var genB = MakeGenerator("gen_b", "coin");
            var boostA = TestTree.MakeDefinition<UpgradeDefinition>("boost_a");
            boostA.gate = new CurrencyAtLeast { currencyId = "coin", threshold = 0 };
            boostA.costCurrencyId = "coin";
            boostA.effects.Add(new Effect { target = "gen_a", multiplier = 4 });
            tierADef.generators.Add(genA);
            tierADef.upgrades.Add(boostA);
            tierBDef.generators.Add(genB);

            var defs = new FakeDefs()
                .Add(rootDef).Add(chapterDef).Add(tierADef).Add(tierBDef)
                .Add(TestTree.MakeDefinition<CurrencyDefinition>("coin"))
                .Add(genA).Add(genB).Add(boostA);

            var root = ScopeState.Build(rootDef);
            var chapter = root.FindInSubtree("chapter");
            var tierA = root.FindInSubtree("tier_a");
            var tierB = root.FindInSubtree("tier_b");
            tierA.generatorCounts["gen_a"] = 1;
            tierB.generatorCounts["gen_b"] = 1;
            tierA.purchasedUpgrades.Add("boost_a");

            AssertClose(4, Producer.GetRate(tierA, defs, Now, "coin"), "tier_a carries its own boost");
            AssertClose(1, Producer.GetRate(tierB, defs, Now, "coin"), "tier_b never sees a sibling's effect");
            AssertClose(5, Producer.GetRate(chapter, defs, Now, "coin"), "the chapter total is the SUM of the terms");
        }

        // ---- career effects ----

        [Test]
        public void Records_income_lifts_every_number_the_income_tag_carries()
        {
            var tree = new TestTree();
            tree.Root.balances["records"] = 20;
            tree.Tier1.generatorCounts["practice_amp"] = 4;

            // 1 + 0.02 x 20 = 1.4, on the rate and on the tap yield alike - the
            // career effect sets no stat coordinate (walkthrough 13.2).
            AssertClose(0.5 * 4 * 1.4, Producer.GetRate(tree.Tier1, tree.Defs, Now, "cash"), "cash rate");
            AssertClose(1.4, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.Defs.Get<CurrencyDefinition>("cash"), "cash", Stat.Yield), "cash yield");

            // Fans are never income-tagged: the farm throttle stands on it.
            tree.Tier1.flags.Add("fans_revealed");
            AssertClose(0.35, Producer.GetRate(tree.Tier1, tree.Defs, Now, "fans"), "fans");
        }

        [Test]
        public void Roadie_boosts_multiply_across_venues_and_double_count_the_active_chapter()
        {
            var world = new RoadieWorld();
            world.Root.roadieAllocation["chapter_a"] = 3;
            world.Root.roadieAllocation["chapter_b"] = 1;

            // Across venues the boosts multiply: 1.15 x 1.05. The chapter being
            // worked applies its own factor a second time (design doc 8.2).
            AssertClose(1.15 * 1.05 * 1.15, Producer.GetRate(world.TierA, world.Defs, Now, "coin_a"), "chapter a");
            AssertClose(1.15 * 1.05 * 1.05, Producer.GetRate(world.TierB, world.Defs, Now, "coin_b"), "chapter b");
        }

        [Test]
        public void A_stationing_beyond_the_cap_pays_only_up_to_the_cap()
        {
            var world = new RoadieWorld();
            world.Root.roadieAllocation["chapter_a"] = 99;   // a tampered save, or a cap retuned downward

            // Venue A caps at 5: 1 + 0.05 x 5 = 1.25, total and active alike.
            AssertClose(1.25 * 1.25, Producer.GetRate(world.TierA, world.Defs, Now, "coin_a"), "clamped");
        }

        [Test]
        public void The_active_boost_is_one_where_no_chapter_sits_on_the_chain()
        {
            var world = new RoadieWorld();
            world.Root.roadieAllocation["chapter_a"] = 3;

            // A root-homed number resolves on root's chain, which holds no
            // chapter: the total boost applies, the active double-count does not.
            AssertClose(1.15, Producer.GetRate(world.Root, world.Defs, Now, "prestige"), "root-homed");
        }

        // Two chapters, each with its own run currency and venue - the shape
        // section 8.2's example describes.
        private class RoadieWorld
        {
            public readonly FakeDefs Defs = new();
            public readonly ScopeState Root;
            public readonly ScopeState TierA;
            public readonly ScopeState TierB;

            public RoadieWorld()
            {
                var rootDef = TestTree.MakeScope("root");
                rootDef.declaredCurrencyIds.Add("prestige");
                var chapterADef = TestTree.MakeScope("chapter_a");
                var chapterBDef = TestTree.MakeScope("chapter_b");
                var tierADef = TestTree.MakeScope("tier_a");
                var tierBDef = TestTree.MakeScope("tier_b");
                tierADef.declaredCurrencyIds.Add("coin_a");
                tierBDef.declaredCurrencyIds.Add("coin_b");
                rootDef.children.Add(chapterADef);
                rootDef.children.Add(chapterBDef);
                chapterADef.children.Add(tierADef);
                chapterBDef.children.Add(tierBDef);

                var genA = MakeGenerator("gen_a", "coin_a");
                var genB = MakeGenerator("gen_b", "coin_b");
                var genRoot = MakeGenerator("gen_root", "prestige");
                tierADef.generators.Add(genA);
                tierBDef.generators.Add(genB);
                rootDef.generators.Add(genRoot);

                var total = TestTree.MakeDefinition<CareerEffectDefinition>("roadie_total");
                total.target = "income";
                total.formula = new RoadieTotalBoost();
                var active = TestTree.MakeDefinition<CareerEffectDefinition>("roadie_active");
                active.target = "income";
                active.formula = new RoadieActiveBoost();
                rootDef.careerEffects.Add(total);
                rootDef.careerEffects.Add(active);

                var venueA = TestTree.MakeDefinition<RoadieVenueDefinition>("venue_a");
                venueA.chapterScopeId = "chapter_a";
                venueA.perRoadie = 0.05;
                venueA.cap = 5;
                var venueB = TestTree.MakeDefinition<RoadieVenueDefinition>("venue_b");
                venueB.chapterScopeId = "chapter_b";
                venueB.perRoadie = 0.05;
                venueB.cap = 20;

                Defs.Add(rootDef).Add(chapterADef).Add(chapterBDef).Add(tierADef).Add(tierBDef)
                    .Add(TestTree.MakeDefinition<CurrencyDefinition>("coin_a", "income"))
                    .Add(TestTree.MakeDefinition<CurrencyDefinition>("coin_b", "income"))
                    .Add(TestTree.MakeDefinition<CurrencyDefinition>("prestige", "income"))
                    .Add(genA).Add(genB).Add(genRoot)
                    .Add(total).Add(active).Add(venueA).Add(venueB);

                Root = ScopeState.Build(rootDef);
                TierA = Root.FindInSubtree("tier_a");
                TierB = Root.FindInSubtree("tier_b");
                Root.generatorCounts["gen_root"] = 1;
                TierA.generatorCounts["gen_a"] = 1;
                TierB.generatorCounts["gen_b"] = 1;
            }
        }

        // A generator paying one unit per second, so a test reads the multiplier
        // stack straight off the rate.
        private static GeneratorDefinition MakeGenerator(string id, string currencyId)
        {
            var generator = TestTree.MakeDefinition<GeneratorDefinition>(id);
            generator.availableWhen = new CurrencyAtLeast { currencyId = currencyId, threshold = 0 };
            generator.costCurrencyId = currencyId;
            generator.baseCost = 10;
            generator.growth = 1.15;
            generator.produces.Add(TestTree.Entry(currencyId, Stat.Rate, 1));
            return generator;
        }
    }
}
