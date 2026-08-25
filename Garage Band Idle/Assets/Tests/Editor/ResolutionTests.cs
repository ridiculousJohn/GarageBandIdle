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

        // ---- the cascade row (12.6) ----

        [TestCase(GrowthKind.Multiply, 1.331)]      // 1.1 ^ 3
        [TestCase(GrowthKind.Linear, 1.3)]          // 1 + 0.1 * 3
        public void A_cascade_scales_by_the_fill_count_in_its_own_growth_kind(GrowthKind growth, double expected)
        {
            var tree = new TestTree();
            var bar = MakeCascadeBar(tree, 1.1, growth);
            tree.Tier1.fillCounts[bar.Id] = 3;

            AssertClose(expected, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.PracticeAmp, tree.Cash, Stat.Rate));
        }

        // Reduced to nothing is a semantic; reduced past nothing is not one, and
        // the rule binds both consumers of the growth vocabulary.
        [Test]
        public void Linear_cascade_growth_saturates_at_zero()
        {
            var tree = new TestTree();
            var bar = MakeCascadeBar(tree, 0.5, GrowthKind.Linear);
            tree.Tier1.fillCounts[bar.Id] = 5;      // 1 + (-0.5) * 5 = -1.5

            AssertClose(0, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.PracticeAmp, tree.Cash, Stat.Rate));
        }

        // Read through the DECLARATION list, like a purchased upgrade: a count
        // for a bar this scope never declared cannot contribute.
        [Test]
        public void A_fill_count_for_an_undeclared_bar_contributes_nothing()
        {
            var tree = new TestTree();
            MakeCascadeBar(tree, 1.1, GrowthKind.Multiply);
            tree.Tier1.fillCounts["ghost_bar"] = 10;

            AssertClose(1, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.PracticeAmp, tree.Cash, Stat.Rate));
        }

        // ---- the active-event row (12.6) ----

        // Handicaps ride on the record EXISTING: an expired, undismissed record
        // still applies them, because a failed attempt sits one tap from a
        // reset and briefly lifting the handicap there would be the worse state
        // (12.8).
        [Test]
        public void An_event_record_applies_its_handicaps_and_expiry_does_not_lift_them()
        {
            var tree = new TestTree();
            AssertClose(1, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.PracticeAmp, tree.Cash, Stat.Rate), "no record");

            tree.Tier1.activeEvent = new ActiveEvent { eventId = "timed_gig", remainingSeconds = 100 };
            AssertClose(0, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.PracticeAmp, tree.Cash, Stat.Rate), "running");

            tree.Tier1.activeEvent.remainingSeconds = 0;
            AssertClose(0, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.PracticeAmp, tree.Cash, Stat.Rate), "expired");
        }

        // Read through the DECLARATION list, like an upgrade latch: a record
        // naming an event this scope never declared contributes nothing.
        [Test]
        public void A_record_for_an_undeclared_event_contributes_nothing()
        {
            var tree = new TestTree();
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "ghost_event", remainingSeconds = 100 };

            AssertClose(1, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.PracticeAmp, tree.Cash, Stat.Rate));
        }

        // ---- the match rule ----

        [Test]
        public void An_effect_matches_its_target_by_id_or_by_tag()
        {
            var tree = new TestTree();
            var byId = TestTree.MakeDefinition<ModifierDefinition>("by_id");
            tree.Tier1Def.modifiers.Add(byId);
            byId.effects.Add(new Effect { target = "practice_amp", multiplier = 2 });
            var byTag = TestTree.MakeDefinition<ModifierDefinition>("by_tag");
            tree.Tier1Def.modifiers.Add(byTag);
            byTag.effects.Add(new Effect { target = "gear", multiplier = 3 });
            var byNothing = TestTree.MakeDefinition<ModifierDefinition>("by_nothing");
            tree.Tier1Def.modifiers.Add(byNothing);
            byNothing.effects.Add(new Effect { target = "bassist", multiplier = 5 });

            var ctx = tree.Ctx(tree.Tier1);
            tree.Tier1.modifierStacks["by_id"] = 1;
            tree.Tier1.modifierStacks["by_tag"] = 1;
            tree.Tier1.modifierStacks["by_nothing"] = 1;

            // practice_amp is hit by the id AND the gear tag; drummer carries the
            // tag only; nothing matches an id the owner does not answer to.
            AssertClose(6, Producer.GetMultiplier(ctx, tree.PracticeAmp, tree.Cash, Stat.Rate), "practice_amp");
            AssertClose(3, Producer.GetMultiplier(ctx, tree.Drummer, tree.Cash, Stat.Rate), "drummer");
            AssertClose(1, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Yield), "tap_producer");
        }

        [Test]
        public void Coordinates_narrow_a_match_from_every_number_down_to_one()
        {
            var tree = new TestTree();
            var everything = TestTree.MakeDefinition<ModifierDefinition>("everything");
            tree.Tier1Def.modifiers.Add(everything);
            everything.effects.Add(new Effect { target = "tap_producer", multiplier = 2 });
            var justCash = TestTree.MakeDefinition<ModifierDefinition>("just_cash");
            tree.Tier1Def.modifiers.Add(justCash);
            justCash.effects.Add(new Effect { target = "tap_producer", currencyId = "cash", multiplier = 3 });
            var justRate = TestTree.MakeDefinition<ModifierDefinition>("just_rate");
            tree.Tier1Def.modifiers.Add(justRate);
            justRate.effects.Add(new Effect { target = "tap_producer", stat = Stat.Rate, multiplier = 5 });
            var exactly = TestTree.MakeDefinition<ModifierDefinition>("exactly");
            tree.Tier1Def.modifiers.Add(exactly);
            exactly.effects.Add(new Effect { target = "tap_producer", currencyId = "rehearsal", stat = Stat.Rate, multiplier = 7 });

            var ctx = tree.Ctx(tree.Tier1);
            foreach (var id in new[] { "everything", "just_cash", "just_rate", "exactly" })
                tree.Tier1.modifierStacks[id] = 1;

            // Both coordinates empty matches everything the owner has; either one
            // narrows; both name one entry exactly.
            AssertClose(2 * 3, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Yield), "cash yield");
            AssertClose(2 * 5 * 7, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Rehearsal, Stat.Rate), "rehearsal rate");
            AssertClose(2, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Rehearsal, Stat.Yield), "rehearsal yield");
        }

        [Test]
        public void Modifier_stacks_scale_by_their_stacking_kind()
        {
            var tree = new TestTree();
            var replace = TestTree.MakeDefinition<ModifierDefinition>("replace_boost");
            tree.Tier1Def.modifiers.Add(replace);
            replace.stacking = StackingKind.Replace;
            replace.effects.Add(new Effect { target = "tap_producer", multiplier = 2 });
            var linear = TestTree.MakeDefinition<ModifierDefinition>("linear_boost");
            tree.Tier1Def.modifiers.Add(linear);
            linear.stacking = StackingKind.Linear;
            linear.effects.Add(new Effect { target = "tap_producer", multiplier = 2 });
            var multiply = TestTree.MakeDefinition<ModifierDefinition>("multiply_boost");
            tree.Tier1Def.modifiers.Add(multiply);
            multiply.stacking = StackingKind.Multiply;
            multiply.effects.Add(new Effect { target = "tap_producer", multiplier = 2 });

            var ctx = tree.Ctx(tree.Tier1);
            tree.Tier1.modifierStacks["replace_boost"] = 3;

            // Replace ignores the count entirely - AddModifier holds it at 1, and
            // a count on disk never buys extra.
            AssertClose(2, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Yield), "replace");

            tree.Tier1.modifierStacks.Remove("replace_boost");
            tree.Tier1.modifierStacks["linear_boost"] = 3;
            AssertClose(4, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Yield), "linear");

            tree.Tier1.modifierStacks.Remove("linear_boost");
            tree.Tier1.modifierStacks["multiply_boost"] = 3;
            AssertClose(8, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Yield), "multiply");
        }

        [Test]
        public void A_linear_stack_saturates_at_zero_instead_of_going_negative()
        {
            var tree = new TestTree();
            var decay = TestTree.MakeDefinition<ModifierDefinition>("decay");
            tree.Tier1Def.modifiers.Add(decay);
            decay.stacking = StackingKind.Linear;
            decay.effects.Add(new Effect { target = "tap_producer", multiplier = 0.5 });

            var ctx = tree.Ctx(tree.Tier1);
            tree.Tier1.modifierStacks["decay"] = 1;

            // A debuff that decays linearly is legal authoring, and 1 + (m-1)*n
            // crosses zero at n = 2 - beyond which a raw formula would run
            // production backwards.
            AssertClose(0.5, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Yield), "one stack");
            tree.Tier1.modifierStacks["decay"] = 2;
            AssertClose(0, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Yield), "two stacks");
            tree.Tier1.modifierStacks["decay"] = 5;
            AssertClose(0, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Yield), "five stacks");
        }

        // Count scaling happens in BigNumber, not in double arithmetic that
        // overflows on the way there. Absurd content, but the property is what
        // silently regresses if a parameter type drifts back.
        [Test]
        public void Count_scaling_never_overflows_on_its_way_into_BigNumber()
        {
            var tree = new TestTree();
            var huge = TestTree.MakeDefinition<ModifierDefinition>("huge");
            tree.Tier1Def.modifiers.Add(huge);
            huge.stacking = StackingKind.Linear;
            huge.effects.Add(new Effect { target = "tap_producer", multiplier = double.MaxValue });
            tree.Tier1.modifierStacks["huge"] = 2;

            // Past double range without ever having been an infinity: the
            // constructor would have thrown on the way through.
            var product = Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.TapProducer, tree.Cash, Stat.Yield);
            Assert.IsTrue(product > (BigNumber)double.MaxValue, $"expected a value past double range, got {product}");
        }

        [Test]
        public void A_roadie_boost_never_overflows_on_its_way_into_BigNumber()
        {
            var tree = new TestTree();
            ((RoadieActiveBoost)tree.RoadieActive.formula).perRoadie = double.MaxValue;
            tree.Root.roadieAllocation["ch1"] = 2;

            var boost = tree.RoadieActive.formula.Compute(tree.Ctx(tree.Tier1));
            Assert.IsTrue(boost > (BigNumber)double.MaxValue, $"expected a value past double range, got {boost}");
        }

        // The roadie buff aims at the sources, so a bandmate that pays Cash AND
        // Fans from one definition would carry it into the fans line - which
        // 8.1's wall-clock farm throttle forbids. The currencyId: income
        // narrowing is what keeps it out, and fans stays out by not declaring
        // the tag.
        [Test]
        public void The_roadie_buff_lifts_income_rates_and_never_the_fan_rate()
        {
            var tree = new TestTree();
            tree.Tier1.flags.Add("fans_revealed");
            tree.Tier1.generatorCounts["drummer"] = 1;
            tree.Root.roadieAllocation["ch1"] = 2;              // 1 + 0.05 x 2 on both roadie factors

            AssertClose(3 * 1.1 * 1.1, Producer.GetRate(tree.Tier1, Now, tree.Cash), "cash rate");
            AssertClose(0.35 + 0.02, Producer.GetRate(tree.Tier1, Now, tree.Fans), "fan rate");
        }

        [Test]
        public void The_roadie_buff_lifts_no_yield()
        {
            var tree = new TestTree();
            tree.Root.roadieAllocation["ch1"] = 2;

            // stat: rate on both, so a tap is the player's own contribution.
            Producer.FireProducer(tree.Ctx(tree.Tier1), tree.TapProducer);
            AssertClose(1, tree.Tier1.balances["cash"], "tap yield");
        }

        // ---- the two stages ----

        [Test]
        public void An_upgrade_effect_multiplies_inside_its_source_term()
        {
            var tree = new TestTree();
            tree.Tier1.generatorCounts["practice_amp"] = 3;
            tree.Tier1.generatorCounts["drummer"] = 2;

            // 0.5 x 3 + 3 x 2
            AssertClose(7.5, Producer.GetRate(tree.Tier1, Now, tree.Cash), "no upgrades");

            // amp_strings doubles the amp's term only - the drummer's is untouched.
            tree.Tier1.purchasedUpgrades.Add("amp_strings");
            AssertClose(9, Producer.GetRate(tree.Tier1, Now, tree.Cash), "amp_strings");
        }

        [Test]
        public void A_currency_effect_multiplies_the_total_and_its_stat_narrowing_holds()
        {
            var tree = new TestTree();
            tree.Tier1.generatorCounts["practice_amp"] = 4;
            tree.Tier1.purchasedUpgrades.Add("tight_set");

            // tight_set targets cash with stat: rate, so it lifts the rate...
            AssertClose(0.5 * 4 * 1.5, Producer.GetRate(tree.Tier1, Now, tree.Cash), "cash rate");

            // ...and leaves the tap yield alone.
            AssertClose(1, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.Cash, tree.Cash, Stat.Yield), "cash yield");
        }

        [Test]
        public void Owned_counts_scale_a_generator_and_absent_counts_contribute_nothing()
        {
            var tree = new TestTree();
            AssertClose(0, Producer.GetRate(tree.Tier1, Now, tree.Cash), "nothing owned");

            tree.Tier1.generatorCounts["practice_amp"] = 7;
            AssertClose(3.5, Producer.GetRate(tree.Tier1, Now, tree.Cash), "seven amps");
        }

        [Test]
        public void Entry_conditions_are_judged_in_the_declaring_scope()
        {
            var tree = new TestTree();

            // The Jam's rehearsal rate is gated on the reveal: no pre-banking.
            AssertClose(0, Producer.GetRate(tree.Tier1, Now, tree.Rehearsal), "before the reveal");
            tree.Tier1.flags.Add("rehearsal_revealed");
            AssertClose(0.5, Producer.GetRate(tree.Tier1, Now, tree.Rehearsal), "after the reveal");
        }

        [Test]
        public void A_subtree_rate_sums_every_source_it_contains()
        {
            var tree = new TestTree();
            tree.Tier1.flags.Add("fans_revealed");
            tree.Tier1.generatorCounts["drummer"] = 3;

            // The band's base accrual plus each bandmate's own fans entry.
            AssertClose(0.35 + 0.02 * 3, Producer.GetRate(tree.Tier1, Now, tree.Fans), "from tier1");

            // Asking from further out finds the same sources - the subtree root
            // decides what is counted, not where the currency lives.
            AssertClose(0.35 + 0.02 * 3, Producer.GetRate(tree.Ch1, Now, tree.Fans), "from ch1");
            AssertClose(0, Producer.GetRate(tree.Root, Now, tree.Roadies), "a currency nothing produces");
        }

        [Test]
        public void Source_stage_effects_do_not_reach_a_sibling_scope()
        {
            // Two sibling tiers under one chapter, each with a generator paying a
            // chapter-homed currency. The shape that makes isolation visible.
            var rootDef = TestTree.MakeRoot("root");
            var chapterDef = TestTree.MakeChapter("chapter");
            var coin = TestTree.DeclareCurrency(chapterDef, "coin", "income");
            var tierADef = TestTree.MakeTier("tier_a");
            var tierBDef = TestTree.MakeTier("tier_b");
            rootDef.children.Add(chapterDef);
            chapterDef.children.Add(tierADef);
            chapterDef.children.Add(tierBDef);

            var genA = MakeGenerator("gen_a", coin);
            var genB = MakeGenerator("gen_b", coin);
            var boostA = TestTree.MakeDefinition<UpgradeDefinition>("boost_a");
            boostA.gate = new CurrencyAtLeast { currency = coin, threshold = 0 };
            boostA.costCurrency = coin;
            boostA.effects.Add(new Effect { target = "gen_a", multiplier = 4 });
            tierADef.generators.Add(genA);
            tierADef.upgrades.Add(boostA);
            tierBDef.generators.Add(genB);

            var root = ScopeState.Build(rootDef);
            var chapter = root.FindInSubtree(chapterDef);
            var tierA = root.FindInSubtree(tierADef);
            var tierB = root.FindInSubtree(tierBDef);
            tierA.generatorCounts["gen_a"] = 1;
            tierB.generatorCounts["gen_b"] = 1;
            tierA.purchasedUpgrades.Add("boost_a");

            AssertClose(4, Producer.GetRate(tierA, Now, coin), "tier_a carries its own boost");
            AssertClose(1, Producer.GetRate(tierB, Now, coin), "tier_b never sees a sibling's effect");
            AssertClose(5, Producer.GetRate(chapter, Now, coin), "the chapter total is the SUM of the terms");
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
            AssertClose(0.5 * 4 * 1.4, Producer.GetRate(tree.Tier1, Now, tree.Cash), "cash rate");
            AssertClose(1.4, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.Cash, tree.Cash, Stat.Yield), "cash yield");

            // Fans are never income-tagged: the farm throttle stands on it.
            tree.Tier1.flags.Add("fans_revealed");
            AssertClose(0.35, Producer.GetRate(tree.Tier1, Now, tree.Fans), "fans");
        }

        [Test]
        public void Roadie_boosts_multiply_across_chapters_and_double_count_the_played_one()
        {
            var world = new RoadieWorld();
            world.Root.roadieAllocation["chapter_a"] = 3;
            world.Root.roadieAllocation["chapter_b"] = 1;

            // Additive within a chapter, multiplicative across them: 1.15 x 1.05
            // everywhere, and the chapter being worked applies its own factor a
            // second time (design doc 8.2).
            AssertClose(1.15 * 1.05 * 1.15, Producer.GetRate(world.TierA, Now, world.CoinA), "chapter a");
            AssertClose(1.15 * 1.05 * 1.05, Producer.GetRate(world.TierB, Now, world.CoinB), "chapter b");
        }

        [Test]
        public void Spreading_roadies_beats_stacking_them_for_the_global_factor()
        {
            // The concavity section 8.2 stands on: four Roadies split two ways
            // beat four in one chapter, judged off any chapter's chain so only
            // the global factor is in play.
            var stacked = new RoadieWorld();
            stacked.Root.roadieAllocation["chapter_a"] = 4;
            var spread = new RoadieWorld();
            spread.Root.roadieAllocation["chapter_a"] = 2;
            spread.Root.roadieAllocation["chapter_b"] = 2;

            AssertClose(1.20, Producer.GetRate(stacked.Root, Now, stacked.Prestige), "stacked");
            AssertClose(1.10 * 1.10, Producer.GetRate(spread.Root, Now, spread.Prestige), "spread");
        }

        [Test]
        public void The_active_boost_is_one_where_no_chapter_sits_on_the_chain()
        {
            var world = new RoadieWorld();
            world.Root.roadieAllocation["chapter_a"] = 3;

            // A root-homed number resolves on root's chain, which holds no
            // chapter: the total boost applies, the active double-count does not.
            AssertClose(1.15, Producer.GetRate(world.Root, Now, world.Prestige), "root-homed");
        }

        // Two chapters, each with its own run currency - the shape section 8.2's
        // example describes.
        private class RoadieWorld
        {
            public readonly RootScopeState Root;
            public readonly ScopeState TierA;
            public readonly ScopeState TierB;
            public readonly CurrencyDefinition CoinA;
            public readonly CurrencyDefinition CoinB;
            public readonly CurrencyDefinition Prestige;

            public RoadieWorld()
            {
                var rootDef = TestTree.MakeRoot("root");
                Prestige = TestTree.DeclareCurrency(rootDef, "prestige", "income");
                var chapterADef = TestTree.MakeChapter("chapter_a");
                var chapterBDef = TestTree.MakeChapter("chapter_b");
                var tierADef = TestTree.MakeTier("tier_a");
                var tierBDef = TestTree.MakeTier("tier_b");
                CoinA = TestTree.DeclareCurrency(tierADef, "coin_a", "income");
                CoinB = TestTree.DeclareCurrency(tierBDef, "coin_b", "income");
                rootDef.children.Add(chapterADef);
                rootDef.children.Add(chapterBDef);
                chapterADef.children.Add(tierADef);
                chapterBDef.children.Add(tierBDef);

                var genA = MakeGenerator("gen_a", CoinA, "production");
                var genB = MakeGenerator("gen_b", CoinB, "production");
                var genRoot = MakeGenerator("gen_root", Prestige, "production");
                tierADef.generators.Add(genA);
                tierBDef.generators.Add(genB);
                rootDef.generators.Add(genRoot);

                var total = TestTree.MakeDefinition<CareerEffectDefinition>("roadie_total");
                total.target = "income";
                total.formula = new RoadieTotalBoost { perRoadie = 0.05 };
                var active = TestTree.MakeDefinition<CareerEffectDefinition>("roadie_active");
                active.target = "production";          // the SOURCE knows its chapter; a currency total does not
                active.currencyId = "income";
                active.formula = new RoadieActiveBoost { perRoadie = 0.05 };
                rootDef.careerEffects.Add(total);
                rootDef.careerEffects.Add(active);

                Root = ScopeState.Build(rootDef);
                TierA = Root.FindInSubtree(tierADef);
                TierB = Root.FindInSubtree(tierBDef);
                Root.generatorCounts["gen_root"] = 1;
                TierA.generatorCounts["gen_a"] = 1;
                TierB.generatorCounts["gen_b"] = 1;
            }
        }

        // A repeating bar carrying one cascade entry, filed at tier1.
        private static BarDefinition MakeCascadeBar(TestTree tree, double multiplier, GrowthKind growth)
        {
            var group = TestTree.MakeDefinition<BarGroupDefinition>("cascades");

            var bar = TestTree.MakeDefinition<BarDefinition>("cascade_bar");
            bar.fillCurrency = tree.Rehearsal;
            bar.fillAmount = 10;
            bar.fillRate = 1;
            bar.repeating = true;
            bar.perFill.Add(new PerFillEntry
            {
                effect = new Effect { target = "practice_amp", multiplier = multiplier },
                growth = growth,
            });
            group.bars.Add(bar);
            tree.Tier1Def.barGroups.Add(group);
            return bar;
        }

        // A generator paying one unit per second, so a test reads the multiplier
        // stack straight off the rate.
        private static GeneratorDefinition MakeGenerator(string id, CurrencyDefinition currency, params string[] tags)
        {
            var generator = TestTree.MakeDefinition<GeneratorDefinition>(id, tags);
            generator.availableWhen = new CurrencyAtLeast { currency = currency, threshold = 0 };
            generator.costCurrency = currency;
            generator.baseCost = 10;
            generator.growth = 1.15;
            generator.produces.Add(TestTree.Entry(currency, Stat.Rate, 1));
            return generator;
        }
    }
}
