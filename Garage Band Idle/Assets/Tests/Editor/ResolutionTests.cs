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

        // ---- the currency gate (12.2) ----

        // One gate on the currency covers every source, which is the whole
        // reason it sits there: fans is paid by band AND by each bandmate, and
        // play_for_crowd cannot be bought without a drummer, so a per-entry
        // rule leaves the drummer trickling fans behind the reveal.
        [Test]
        public void An_inactive_currency_takes_nothing_from_any_source()
        {
            var tree = new TestTree();
            tree.Fans.activeWhen = new FlagSet { flagId = "fans_revealed" };
            tree.Tier1.generatorCounts[tree.Drummer.Id] = 1;

            // The drummer's fans line carries NO condition of its own, and it
            // is the one the currency gate has to cover: play_for_crowd gates
            // on owning a drummer, so a bandmate always exists before the flag.
            AssertClose(0, Producer.GetRate(tree.Ctx(tree.Ch1), tree.Fans), "band and the bandmate alike");

            tree.Tier1.flags.Add("fans_revealed");
            AssertClose(0.37, Producer.GetRate(tree.Ctx(tree.Ch1), tree.Fans), "0.35 base plus the drummer's 0.02");
        }

        // What one more unit adds, per currency in authored order, with both
        // stages riding in - the generator row's yield line (12.11).
        [Test]
        public void A_units_rate_is_its_own_term_under_the_multipliers_that_reach_it()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);

            var amp = Producer.UnitRate(ctx, tree.PracticeAmp);
            Assert.AreEqual(1, amp.Count);
            Assert.AreSame(tree.Cash, amp[0].currency);
            AssertClose(0.5, amp[0].amount, "one amp's authored rate");

            var drummer = Producer.UnitRate(ctx, tree.Drummer);
            Assert.AreEqual(2, drummer.Count, "a bandmate pays two currencies");
            Assert.AreSame(tree.Cash, drummer[0].currency);
            AssertClose(3, drummer[0].amount);
            Assert.AreSame(tree.Fans, drummer[1].currency);
            AssertClose(0.02, drummer[1].amount);

            // amp_strings is a stage-1 factor on the amp alone; records_income
            // is stage 2 on the income tag, so it lifts both cash terms and
            // leaves the drummer's fans line alone.
            tree.Tier1.purchasedUpgrades.Add("amp_strings");
            tree.Root.balances["records"] = 50;                  // 1 + 0.02 * 50 = x2
            AssertClose(2, Producer.UnitRate(ctx, tree.PracticeAmp)[0].amount, "0.5 x 2 x 2");
            AssertClose(6, Producer.UnitRate(ctx, tree.Drummer)[0].amount, "3 x 2");
            AssertClose(0.02, Producer.UnitRate(ctx, tree.Drummer)[1].amount, "fans carry no income tag");
        }

        // The gate rides SourceTerm, which the yield path shares, so one
        // firing pays its ungated currency and withholds its gated one.
        [Test]
        public void An_inactive_currency_pays_no_yield_either()
        {
            var tree = new TestTree();
            tree.Rehearsal.activeWhen = new FlagSet { flagId = "rehearsal_revealed" };
            // The currency states the reveal now, so the entries stop repeating
            // it - which is what leaves the gate as the only thing refusing.
            foreach (var entry in tree.TapProducer.produces)
                if (entry.currency == tree.Rehearsal)
                    entry.condition = null;

            Producer.FireProducer(tree.Ctx(tree.Tier1), tree.TapProducer);
            AssertClose(0, tree.Tier1.balances["rehearsal"], "the tap's rehearsal yield is the currency's to refuse");
            AssertClose(1, tree.Tier1.balances["cash"], "cash is ungated and paid in the same firing");

            tree.Tier1.flags.Add("rehearsal_revealed");
            Producer.FireProducer(tree.Ctx(tree.Tier1), tree.TapProducer);
            AssertClose(1, tree.Tier1.balances["rehearsal"]);
        }

        // The half the gather cannot reach: an authored AddCurrency computes no
        // term, so the refusal has to be at the write, and it throws rather
        // than swallowing - a dropped grant is a lost run, not a quiet no-op.
        [Test]
        public void An_inactive_currency_refuses_an_authored_deposit()
        {
            var tree = new TestTree();
            tree.Fans.activeWhen = new FlagSet { flagId = "fans_revealed" };
            var pay = new AddCurrency { currencies = { tree.Fans }, amount = 10 };

            var thrown = Assert.Throws<InvalidOperationException>(() => pay.Execute(tree.Ctx(tree.Tier1)));
            StringAssert.Contains("not active", thrown.Message);
            AssertClose(0, tree.Tier1.balances["fans"]);

            tree.Tier1.flags.Add("fans_revealed");
            pay.Execute(tree.Ctx(tree.Tier1));
            AssertClose(10, tree.Tier1.balances["fans"]);
        }

        [Test]
        public void A_currency_with_no_gate_is_always_active()
        {
            var tree = new TestTree();
            tree.Tier1.generatorCounts[tree.PracticeAmp.Id] = 1;

            AssertClose(0.5, Producer.GetRate(tree.Ctx(tree.Ch1), tree.Cash), "no flags set, and cash never needed one");
        }

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
            byId.effects.Add(new Effect { target = "practice_amp", stat = Stat.Rate, multiplier = 2 });
            var byTag = TestTree.MakeDefinition<ModifierDefinition>("by_tag");
            tree.Tier1Def.modifiers.Add(byTag);
            byTag.effects.Add(new Effect { target = "gear", stat = Stat.Rate, multiplier = 3 });
            var byNothing = TestTree.MakeDefinition<ModifierDefinition>("by_nothing");
            tree.Tier1Def.modifiers.Add(byNothing);
            byNothing.effects.Add(new Effect { target = "bassist", stat = Stat.Rate, multiplier = 5 });

            var ctx = tree.Ctx(tree.Tier1);
            tree.Tier1.modifierStacks["by_id"] = 1;
            tree.Tier1.modifierStacks["by_tag"] = 1;
            tree.Tier1.modifierStacks["by_nothing"] = 1;

            // practice_amp is hit by the id AND the gear tag; drummer carries the
            // tag only; nothing matches an id the owner does not answer to.
            AssertClose(6, Producer.GetMultiplier(ctx, tree.PracticeAmp, tree.Cash, Stat.Rate), "practice_amp");
            AssertClose(3, Producer.GetMultiplier(ctx, tree.Drummer, tree.Cash, Stat.Rate), "drummer");
            AssertClose(1, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Rate), "tap_producer");
        }

        [Test]
        public void Coordinates_narrow_a_match_from_every_entry_of_a_stat_down_to_one()
        {
            var tree = new TestTree();
            var everyYield = TestTree.MakeDefinition<ModifierDefinition>("every_yield");
            tree.Tier1Def.modifiers.Add(everyYield);
            everyYield.effects.Add(new Effect { target = "tap_producer", stat = Stat.Yield, multiplier = 2 });
            var justCash = TestTree.MakeDefinition<ModifierDefinition>("just_cash");
            tree.Tier1Def.modifiers.Add(justCash);
            justCash.effects.Add(new Effect { target = "tap_producer", currencyId = "cash", stat = Stat.Yield, multiplier = 3 });
            var everyRate = TestTree.MakeDefinition<ModifierDefinition>("every_rate");
            tree.Tier1Def.modifiers.Add(everyRate);
            everyRate.effects.Add(new Effect { target = "tap_producer", stat = Stat.Rate, multiplier = 5 });
            var exactly = TestTree.MakeDefinition<ModifierDefinition>("exactly");
            tree.Tier1Def.modifiers.Add(exactly);
            exactly.effects.Add(new Effect { target = "tap_producer", currencyId = "rehearsal", stat = Stat.Rate, multiplier = 7 });

            var ctx = tree.Ctx(tree.Tier1);
            foreach (var id in new[] { "every_yield", "just_cash", "every_rate", "exactly" })
                tree.Tier1.modifierStacks[id] = 1;

            // The stat is exact and required; the optional currency coordinate
            // narrows within it, from every entry of that stat down to one.
            AssertClose(2 * 3, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Yield), "cash yield");
            AssertClose(5 * 7, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Rehearsal, Stat.Rate), "rehearsal rate");
            AssertClose(2, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Rehearsal, Stat.Yield), "rehearsal yield");
        }

        // ---- the wildcard and the consumer-owned stats (12.2) ----

        [Test]
        public void A_wildcard_lifts_every_currency_and_is_collected_once()
        {
            var tree = new TestTree();
            var everyCurrency = TestTree.MakeDefinition<ModifierDefinition>("every_currency");
            everyCurrency.effects.Add(new Effect { stat = Stat.Rate, multiplier = 2 });
            tree.RootDef.modifiers.Add(everyCurrency);
            tree.Root.modifierStacks["every_currency"] = 1;
            tree.Tier1.generatorCounts["practice_amp"] = 4;
            tree.Tier1.flags.Add("fans_revealed");

            // Root sits on BOTH gather walks; one stage per effect is what
            // keeps this x2 rather than x4 - and the stat is exact, so the tap
            // yield stays out of it.
            AssertClose(0.5 * 4 * 2, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "cash rate");
            AssertClose(0.35 * 2, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Fans), "every currency");
            AssertClose(1, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.Cash, tree.Cash, Stat.Yield), "the stat is exact");
        }

        [Test]
        public void A_wildcard_never_reaches_a_bars_fill_rate()
        {
            var tree = new TestTree();
            var everyCurrency = TestTree.MakeDefinition<ModifierDefinition>("every_currency");
            everyCurrency.effects.Add(new Effect { stat = Stat.Rate, multiplier = 2 });
            tree.RootDef.modifiers.Add(everyCurrency);
            tree.Root.modifierStacks["every_currency"] = 1;

            // A bar consumes: its rate resolves stage 1 only, with the bar as
            // the owner, which a currency-stage wildcard never matches.
            AssertClose(1, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.Cover1, tree.Rehearsal, Stat.Rate));
        }

        [Test]
        public void A_wildcard_narrowed_by_currency_reaches_that_currency_alone()
        {
            var tree = new TestTree();
            var cashOnly = TestTree.MakeDefinition<ModifierDefinition>("cash_only");
            cashOnly.effects.Add(new Effect { currencyId = "cash", stat = Stat.Rate, multiplier = 0.5 });
            tree.RootDef.modifiers.Add(cashOnly);
            tree.Root.modifierStacks["cash_only"] = 1;

            // The currency coordinate narrows the wildcard's "every currency"
            // down to one; the stat stays exact.
            var ctx = tree.Ctx(tree.Tier1);
            AssertClose(0.5, Producer.GetMultiplier(ctx, tree.Cash, tree.Cash, Stat.Rate), "cash rate");
            AssertClose(1, Producer.GetMultiplier(ctx, tree.Fans, tree.Fans, Stat.Rate), "fans untouched");
            AssertClose(1, Producer.GetMultiplier(ctx, tree.Cash, tree.Cash, Stat.Yield), "yield untouched");
        }

        [Test]
        public void An_ownerless_query_matches_wildcards_only()
        {
            var tree = new TestTree();
            var encore = TestTree.MakeDefinition<ModifierDefinition>("encore");
            encore.effects.Add(new Effect { stat = Stat.GameSpeed, multiplier = 2 });
            var targeted = TestTree.MakeDefinition<ModifierDefinition>("targeted");
            targeted.effects.Add(new Effect { target = "tap_producer", stat = Stat.GameSpeed, multiplier = 3 });
            tree.RootDef.modifiers.Add(encore);
            tree.RootDef.modifiers.Add(targeted);
            tree.Root.modifierStacks["encore"] = 1;
            tree.Root.modifierStacks["targeted"] = 1;

            // The tick's read: no owner, no currency, matched by name alone.
            AssertClose(2, Producer.GetMultiplier(new GameContext(tree.Ch1, Now), null, null, Stat.GameSpeed));
        }

        // Load-refused content, but the runtime backstop is what keeps a
        // {target: cash, x2}-shaped effect from answering a question of a kind
        // its author never chose.
        [Test]
        public void A_statless_effect_matches_nothing_at_runtime()
        {
            var tree = new TestTree();
            var statless = TestTree.MakeDefinition<ModifierDefinition>("statless");
            statless.effects.Add(new Effect { target = "cash", multiplier = 2 });
            tree.Tier1Def.modifiers.Add(statless);
            tree.Tier1.modifierStacks["statless"] = 1;

            var ctx = tree.Ctx(tree.Tier1);
            AssertClose(1, Producer.GetMultiplier(ctx, tree.Cash, tree.Cash, Stat.Rate), "matches no rate");
            AssertClose(1, Producer.GetMultiplier(ctx, tree.Cash, tree.Cash, Stat.Yield), "matches no yield");
        }

        [Test]
        public void Modifier_stacks_scale_by_their_stacking_kind()
        {
            var tree = new TestTree();
            var replace = TestTree.MakeDefinition<ModifierDefinition>("replace_boost");
            tree.Tier1Def.modifiers.Add(replace);
            replace.stacking = StackingKind.Replace;
            replace.effects.Add(new Effect { target = "tap_producer", stat = Stat.Yield, multiplier = 2 });
            var linear = TestTree.MakeDefinition<ModifierDefinition>("linear_boost");
            tree.Tier1Def.modifiers.Add(linear);
            linear.stacking = StackingKind.Linear;
            linear.effects.Add(new Effect { target = "tap_producer", stat = Stat.Yield, multiplier = 2 });
            var multiply = TestTree.MakeDefinition<ModifierDefinition>("multiply_boost");
            tree.Tier1Def.modifiers.Add(multiply);
            multiply.stacking = StackingKind.Multiply;
            multiply.effects.Add(new Effect { target = "tap_producer", stat = Stat.Yield, multiplier = 2 });

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
            decay.effects.Add(new Effect { target = "tap_producer", stat = Stat.Yield, multiplier = 0.5 });

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
            huge.effects.Add(new Effect { target = "tap_producer", stat = Stat.Yield, multiplier = double.MaxValue });
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
            ((RoadieActiveBoost)tree.RoadieActive.effects[0].formula).perRoadie = double.MaxValue;
            tree.Root.roadieAllocation["ch1"] = 2;

            var boost = tree.RoadieActive.effects[0].formula.Compute(tree.Ctx(tree.Tier1));
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

            AssertClose(3 * 1.1 * 1.1, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "cash rate");
            AssertClose(0.35 + 0.02, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Fans), "fan rate");
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
            AssertClose(7.5, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "no upgrades");

            // amp_strings doubles the amp's term only - the drummer's is untouched.
            tree.Tier1.purchasedUpgrades.Add("amp_strings");
            AssertClose(9, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "amp_strings");
        }

        [Test]
        public void A_currency_effect_multiplies_the_total_and_its_stat_narrowing_holds()
        {
            var tree = new TestTree();
            tree.Tier1.generatorCounts["practice_amp"] = 4;
            tree.Tier1.purchasedUpgrades.Add("tight_set");

            // tight_set targets cash with stat: rate, so it lifts the rate...
            AssertClose(0.5 * 4 * 1.5, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "cash rate");

            // ...and leaves the tap yield alone.
            AssertClose(1, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.Cash, tree.Cash, Stat.Yield), "cash yield");
        }

        [Test]
        public void Owned_counts_scale_a_generator_and_absent_counts_contribute_nothing()
        {
            var tree = new TestTree();
            AssertClose(0, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "nothing owned");

            tree.Tier1.generatorCounts["practice_amp"] = 7;
            AssertClose(3.5, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "seven amps");
        }

        [Test]
        public void Entry_conditions_are_judged_in_the_declaring_scope()
        {
            var tree = new TestTree();

            // The Jam's rehearsal rate is gated on the reveal: no pre-banking.
            AssertClose(0, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Rehearsal), "before the reveal");
            tree.Tier1.flags.Add("rehearsal_revealed");
            AssertClose(0.5, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Rehearsal), "after the reveal");
        }

        [Test]
        public void A_subtree_rate_sums_every_source_it_contains()
        {
            var tree = new TestTree();
            tree.Tier1.flags.Add("fans_revealed");
            tree.Tier1.generatorCounts["drummer"] = 3;

            // The band's base accrual plus each bandmate's own fans entry.
            AssertClose(0.35 + 0.02 * 3, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Fans), "from tier1");

            // Asking from further out finds the same sources - the subtree root
            // decides what is counted, not where the currency lives.
            AssertClose(0.35 + 0.02 * 3, Producer.GetRate(tree.Ctx(tree.Ch1), tree.Fans), "from ch1");
            AssertClose(0, Producer.GetRate(tree.Ctx(tree.Root), tree.Roadies), "a currency nothing produces");
        }

        [Test]
        public void Source_stage_effects_do_not_reach_a_sibling_scope()
        {
            // Two sibling tiers under one chapter, each with a generator paying a
            // chapter-homed currency. The shape that makes isolation visible.
            var rootDef = TestTree.MakeRoot("root");
            rootDef.declaredTags.Add("income");
            var chapterDef = TestTree.MakeChapter("chapter");
            var coin = TestTree.DeclareCurrency(chapterDef, "coin", "income");
            var tierADef = TestTree.MakeTier("tier_a");
            var tierBDef = TestTree.MakeTier("tier_b");
            chapterDef.children.Add(tierADef);
            chapterDef.children.Add(tierBDef);

            var genA = MakeGenerator("gen_a", coin);
            var genB = MakeGenerator("gen_b", coin);
            var boostA = TestTree.MakeDefinition<UpgradeDefinition>("boost_a");
            boostA.gate = new CurrencyAtLeast { currency = coin, threshold = 0 };
            boostA.costCurrency = coin;
            boostA.effects.Add(new Effect { target = "gen_a", stat = Stat.Rate, multiplier = 4 });
            tierADef.generators.Add(genA);
            tierADef.upgrades.Add(boostA);
            tierBDef.generators.Add(genB);

            var root = ScopeState.Build(ComposedContent.Compose(rootDef, new[] { chapterDef }));
            var chapter = root.FindInSubtree(chapterDef);
            var tierA = root.FindInSubtree(tierADef);
            var tierB = root.FindInSubtree(tierBDef);
            tierA.generatorCounts["gen_a"] = 1;
            tierB.generatorCounts["gen_b"] = 1;
            tierA.purchasedUpgrades.Add("boost_a");

            AssertClose(4, Producer.GetRate(new GameContext(tierA, Now), coin), "tier_a carries its own boost");
            AssertClose(1, Producer.GetRate(new GameContext(tierB, Now), coin), "tier_b never sees a sibling's effect");
            AssertClose(5, Producer.GetRate(new GameContext(chapter, Now), coin), "the chapter total is the SUM of the terms");
        }

        // ---- permanent modifiers and formula effects ----

        [Test]
        public void Records_income_lifts_every_number_the_income_tag_carries()
        {
            var tree = new TestTree();
            tree.Root.balances["records"] = 20;
            tree.Tier1.generatorCounts["practice_amp"] = 4;

            // 1 + 0.02 x 20 = 1.4, on the rate and on the tap yield alike - the
            // stat leg is exact, so walkthrough 13.2's "alike" is one entry per
            // stat, both on the income tag.
            AssertClose(0.5 * 4 * 1.4, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "cash rate");
            AssertClose(1.4, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.Cash, tree.Cash, Stat.Yield), "cash yield");

            // Fans are never income-tagged: the farm throttle stands on it.
            tree.Tier1.flags.Add("fans_revealed");
            AssertClose(0.35, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Fans), "fans");
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
            AssertClose(1.15 * 1.05 * 1.15, Producer.GetRate(new GameContext(world.TierA, Now), world.CoinA), "chapter a");
            AssertClose(1.15 * 1.05 * 1.05, Producer.GetRate(new GameContext(world.TierB, Now), world.CoinB), "chapter b");
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

            AssertClose(1.20, Producer.GetRate(new GameContext(stacked.Root, Now), stacked.Prestige), "stacked");
            AssertClose(1.10 * 1.10, Producer.GetRate(new GameContext(spread.Root, Now), spread.Prestige), "spread");
        }

        [Test]
        public void The_active_boost_is_one_where_no_chapter_sits_on_the_chain()
        {
            var world = new RoadieWorld();
            world.Root.roadieAllocation["chapter_a"] = 3;

            // A root-homed number resolves on root's chain, which holds no
            // chapter: the total boost applies, the active double-count does not.
            AssertClose(1.15, Producer.GetRate(new GameContext(world.Root, Now), world.Prestige), "root-homed");
        }

        [Test]
        public void A_formula_effect_in_a_granted_modifier_computes_against_the_origin()
        {
            var tree = new TestTree();
            var scaled = TestTree.MakeDefinition<ModifierDefinition>("scaled");
            scaled.stacking = StackingKind.Linear;
            scaled.effects.Add(new Effect { target = "practice_amp", stat = Stat.Rate,
                formula = new LinearOnBalance { currency = tree.Cash, coefficient = 0.1 } });
            tree.Tier1Def.modifiers.Add(scaled);
            tree.Tier1.modifierStacks["scaled"] = 1;
            tree.Tier1.balances["cash"] = 5;

            // 1 + 0.1 x 5, read off the origin's chain like every formula.
            AssertClose(1.5, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.PracticeAmp, tree.Cash, Stat.Rate), "one stack");

            // Count scaling composes on the COMPUTED value: 1 + (1.5 - 1) x 2.
            tree.Tier1.modifierStacks["scaled"] = 2;
            AssertClose(2, Producer.GetMultiplier(tree.Ctx(tree.Tier1), tree.PracticeAmp, tree.Cash, Stat.Rate), "two stacks");
        }

        [Test]
        public void A_chapters_permanent_modifier_applies_on_its_own_chain_and_not_a_siblings()
        {
            var rootDef = TestTree.MakeRoot("root");
            var chapterADef = TestTree.MakeChapter("chapter_a");
            var chapterBDef = TestTree.MakeChapter("chapter_b");
            var tierADef = TestTree.MakeTier("tier_a");
            var tierBDef = TestTree.MakeTier("tier_b");
            var coinA = TestTree.DeclareCurrency(tierADef, "coin_a");
            var coinB = TestTree.DeclareCurrency(tierBDef, "coin_b");
            chapterADef.children.Add(tierADef);
            chapterBDef.children.Add(tierBDef);
            tierADef.generators.Add(MakeGenerator("gen_a", coinA));
            tierBDef.generators.Add(MakeGenerator("gen_b", coinB));

            // The chapter-unique buff: declared and applied at chapter_a, so
            // only a gather walking through chapter_a ever collects it.
            var chapterBoost = TestTree.MakeDefinition<ModifierDefinition>("chapter_boost");
            chapterBoost.effects.Add(new Effect { stat = Stat.Rate, multiplier = 2 });
            chapterADef.modifiers.Add(chapterBoost);
            chapterADef.permanentModifiers.Add(chapterBoost);

            var root = ScopeState.Build(ComposedContent.Compose(rootDef, new[] { chapterADef, chapterBDef }));
            var tierA = root.FindInSubtree(tierADef);
            var tierB = root.FindInSubtree(tierBDef);
            tierA.generatorCounts["gen_a"] = 1;
            tierB.generatorCounts["gen_b"] = 1;

            AssertClose(2, Producer.GetRate(new GameContext(tierA, Now), coinA), "its own chain");
            AssertClose(1, Producer.GetRate(new GameContext(tierB, Now), coinB), "a sibling's chain");
        }

        // Permanent membership is declaration, not state: there is no fact for
        // a reset to clear, unlike the granted stack it would wipe.
        [Test]
        public void A_permanent_modifier_survives_a_reset()
        {
            var tree = new TestTree();
            var standing = TestTree.MakeDefinition<ModifierDefinition>("standing");
            standing.effects.Add(new Effect { currencyId = "cash", stat = Stat.Rate, multiplier = 2 });
            tree.Ch1Def.modifiers.Add(standing);
            tree.Ch1Def.permanentModifiers.Add(standing);
            tree.Tier1.generatorCounts["practice_amp"] = 1;

            AssertClose(1, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "applies");

            tree.Ch1.Clear(Now);
            tree.Tier1.Clear(Now);
            tree.Tier1.generatorCounts["practice_amp"] = 1;
            AssertClose(1, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "survives the reset");
        }

        [Test]
        public void A_modifier_both_permanent_and_granted_resolves_through_its_own_stacking_kind()
        {
            var tree = new TestTree();
            var linear = TestTree.MakeDefinition<ModifierDefinition>("both_linear");
            linear.stacking = StackingKind.Linear;
            linear.effects.Add(new Effect { target = "tap_producer", stat = Stat.Yield, multiplier = 2 });
            tree.Tier1Def.modifiers.Add(linear);
            tree.Tier1Def.permanentModifiers.Add(linear);

            // Permanent membership alone is one application.
            var ctx = tree.Ctx(tree.Tier1);
            AssertClose(2, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Yield), "implicit 1");

            // Granted stacks MERGE with the implicit 1: count 3, 1 + (2-1) x 3.
            tree.Tier1.modifierStacks["both_linear"] = 2;
            AssertClose(4, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Yield), "1 + 2 stacks");

            // Replace means permanent-plus-granted is still ONE application.
            var replace = TestTree.MakeDefinition<ModifierDefinition>("both_replace");
            replace.effects.Add(new Effect { target = "tap_producer", stat = Stat.Rate, multiplier = 3 });
            tree.Tier1Def.modifiers.Add(replace);
            tree.Tier1Def.permanentModifiers.Add(replace);
            tree.Tier1.modifierStacks["both_replace"] = 5;
            AssertClose(3, Producer.GetMultiplier(ctx, tree.TapProducer, tree.Cash, Stat.Rate), "replace stays one");
        }

        // ---- appliesWhen (12.5) ----

        [Test]
        public void An_idle_only_modifier_contributes_under_the_idle_circumstance_alone()
        {
            var tree = new TestTree();
            var idleOnly = TestTree.MakeDefinition<ModifierDefinition>("idle_only");
            idleOnly.appliesWhen = new IdleAccumulation();
            idleOnly.effects.Add(new Effect { currencyId = "cash", stat = Stat.Rate, multiplier = 0.5 });
            tree.RootDef.modifiers.Add(idleOnly);
            tree.RootDef.permanentModifiers.Add(idleOnly);
            tree.Tier1.generatorCounts["practice_amp"] = 1;

            // The authored idle base (x0.5) joins every idle gather, so the
            // idle number carries both factors.
            AssertClose(0.5, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "live");
            AssertClose(0.125, Producer.GetRate(new GameContext(tree.Tier1, Now, idleAccumulation: true), tree.Cash), "idle");
        }

        // The inverse composes from the same two primitives, and the condition
        // binds a granted stack exactly as it binds a permanent membership.
        [Test]
        public void A_live_only_modifier_excuses_itself_from_the_idle_gather()
        {
            var tree = new TestTree();
            var liveOnly = TestTree.MakeDefinition<ModifierDefinition>("live_only");
            liveOnly.appliesWhen = new Not { condition = new IdleAccumulation() };
            liveOnly.effects.Add(new Effect { currencyId = "cash", stat = Stat.Rate, multiplier = 2 });
            tree.Tier1Def.modifiers.Add(liveOnly);
            tree.Tier1.modifierStacks["live_only"] = 1;
            tree.Tier1.generatorCounts["practice_amp"] = 1;

            // Live sees the buff and not the base; idle sees the base and not
            // the buff.
            AssertClose(1, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "live");
            AssertClose(0.25, Producer.GetRate(new GameContext(tree.Tier1, Now, idleAccumulation: true), tree.Cash), "idle");
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
                rootDef.declaredTags.AddRange(new[] { "income", "production" });
                Prestige = TestTree.DeclareCurrency(rootDef, "prestige", "income");
                var chapterADef = TestTree.MakeChapter("chapter_a");
                var chapterBDef = TestTree.MakeChapter("chapter_b");
                var tierADef = TestTree.MakeTier("tier_a");
                var tierBDef = TestTree.MakeTier("tier_b");
                CoinA = TestTree.DeclareCurrency(tierADef, "coin_a", "income");
                CoinB = TestTree.DeclareCurrency(tierBDef, "coin_b", "income");
                chapterADef.children.Add(tierADef);
                chapterBDef.children.Add(tierBDef);

                var genA = MakeGenerator("gen_a", CoinA, "production");
                var genB = MakeGenerator("gen_b", CoinB, "production");
                var genRoot = MakeGenerator("gen_root", Prestige, "production");
                tierADef.generators.Add(genA);
                tierBDef.generators.Add(genB);
                rootDef.generators.Add(genRoot);

                var total = TestTree.MakeDefinition<ModifierDefinition>("roadie_total");
                total.effects.Add(new Effect { target = "income", stat = Stat.Rate,
                    formula = new RoadieTotalBoost { perRoadie = 0.05 } });
                var active = TestTree.MakeDefinition<ModifierDefinition>("roadie_active");
                // the SOURCE knows its chapter; a currency total does not
                active.effects.Add(new Effect { target = "production", currencyId = "income", stat = Stat.Rate,
                    formula = new RoadieActiveBoost { perRoadie = 0.05 } });
                rootDef.modifiers.Add(total);
                rootDef.modifiers.Add(active);
                rootDef.permanentModifiers.Add(total);
                rootDef.permanentModifiers.Add(active);

                Root = ScopeState.Build(ComposedContent.Compose(rootDef, new[] { chapterADef, chapterBDef }));
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
                effect = new Effect { target = "practice_amp", stat = Stat.Rate, multiplier = multiplier },
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
