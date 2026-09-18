using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Editor;
using RidiculousGaming.GarageBandIdle.Events;
using UnityEditor;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The authored Chapter 2 document as it lands on disk, read back through the
    // composition seam boot uses - root plus both chapters, since ch2's unlock
    // reads a root flag ch1's capstone writes and only the whole roster proves
    // that chain resolves.
    //
    // Spot checks, not a second copy of the chapter: one assertion per block
    // that a mis-authored number or a dropped reference would break. The numbers
    // stay the authored ones, and a delta between the two is a bug in whichever
    // edited last.
    public class Chapter2ContentTests
    {
        private static RootDefinition root;
        private static ChapterDefinition ch1;
        private static ChapterDefinition ch2;
        private static TierDefinition tier2;
        private static ComposedContent content;

        [OneTimeSetUp]
        public void LoadTheImportedRoster()
        {
            root = AssetDatabase.LoadAssetAtPath<RootDefinition>(
                ChapterJsonImporter.AssetRootPath + "/root/root.asset");
            ch1 = AssetDatabase.LoadAssetAtPath<ChapterDefinition>(
                ChapterJsonImporter.AssetRootPath + "/ch1/ch1.asset");
            ch2 = AssetDatabase.LoadAssetAtPath<ChapterDefinition>(
                ChapterJsonImporter.AssetRootPath + "/ch2/ch2.asset");
            Assert.IsNotNull(root, "root.json has not been imported - run Garage Band Idle/Import Content.");
            Assert.IsNotNull(ch1, "chapter-01.json has not been imported - run Garage Band Idle/Import Content.");
            Assert.IsNotNull(ch2, "chapter-02.json has not been imported - run Garage Band Idle/Import Content.");
            content = ComposedContent.Compose(root, new[] { ch1, ch2 });
            tier2 = (TierDefinition)ch2.children.Single();
        }

        // ---- the keystone ----

        // The whole authored roster, judged by the pass that gates the import
        // itself: every flag has a setter, every reference reaches on its own
        // chain, and the two chapters coexist without an id or tag collision.
        [Test]
        public void The_imported_roster_validates_clean()
        {
            var report = ContentValidator.Validate(content);

            Assert.AreEqual(0, report.Findings.Count, string.Join("\n", report.Findings));
        }

        // ---- the scope tree ----

        [Test]
        public void The_scope_shape_is_root_then_the_two_chapters()
        {
            Assert.AreEqual(new[] { "ch1", "ch2" }, content.Chapters.Select(c => c.Id).ToArray());
            Assert.AreEqual(0, root.children.Count, "the roster is the label, never root's serialized list (12.14.5)");
            Assert.AreEqual("tier2", tier2.Id);
            Assert.AreEqual(0, tier2.children.Count);
        }

        // The chapter is bought with chapter 1's capstone: the flag is root's, so
        // the gate reads outward from ch2 across a chain no reset reaches.
        [Test]
        public void Chapter_2_unlocks_on_chapter_ones_completion_flag()
        {
            Assert.AreEqual("ch1_complete", ((FlagSet)ch2.unlock).flagId);
            Assert.AreEqual("Play the Backyard Party", ch2.unlock.uiText);

            var state = ScopeState.Build(content);
            var node = TestNavigation.Node(state, ch2);
            Assert.IsFalse(ch2.unlock.Evaluate(new GameContext(node, System.DateTime.UtcNow)),
                "a fresh tree has not finished chapter 1");

            state.flags.Add("ch1_complete");
            Assert.IsTrue(ch2.unlock.Evaluate(new GameContext(node, System.DateTime.UtcNow)));
        }

        // ---- what each scope declares ----

        // Declaration counts per family: this is what catches a block that
        // silently loses an entry to a merge or a mis-nested brace.
        [Test]
        public void Each_scope_declares_the_families_the_chapter_files_on_it()
        {
            // The chapter's own Records counter, paid beside root's (design 5).
            Assert.AreEqual(new[] { "ch2_records" }, Ids(ch2.declaredCurrencies));
            Assert.AreEqual(new[] { "EPs" }, ch2.declaredCurrencies.Select(c => c.displayName).ToArray());

            // Everything that survives an EP: the booked bookings, the two
            // permanent switches, the Battle records, and every row reveal.
            Assert.AreEqual(new[]
            {
                "album",
                "botb1_done", "botb2_done", "botb3_done",
                "booked_open_mic", "booked_house_party", "booked_coffeehouse",
                "booked_backyard", "booked_bar_opener", "booked_botb_qualifier",
                "bulk_booked", "favors_owed",
                "bookings_revealed", "office_revealed",
                "house_party_draw_revealed", "coffeehouse_draw_revealed", "backyard_draw_revealed",
                "bar_opener_draw_revealed", "botb_qualifier_draw_revealed",
                "mailing_list_revealed", "photographer_friend_revealed", "zine_writeup_revealed",
                "standing_open_mic_revealed", "standing_house_party_revealed", "standing_coffeehouse_revealed",
                "standing_backyard_revealed", "standing_bar_opener_revealed", "standing_botb_qualifier_revealed",
                "crowd_work_1_revealed", "crowd_work_2_revealed", "street_team_revealed",
                "merch_table_revealed", "the_van_revealed",
            }, ch2.declaredFlags.ToArray());

            Assert.AreEqual(new[] { "gig_payout_x2", "bulk_booking_switch", "favors_discount" }, Ids(ch2.modifiers));
            Assert.AreEqual(new[] { "bulk_booking_switch", "favors_discount" }, Ids(ch2.permanentModifiers));
            Assert.AreEqual(new[] { "story_ch2_open", "story_ch2_end" }, Ids(ch2.storyBeats));
            Assert.AreEqual(7, ch2.sections.Count, "only a chapter has a screen, and this is all of it");
            Assert.AreEqual(0, ch2.events.Count, "the Battle chain hosts at tier2, where its resets reach");

            // The run: income is root's, so the tier declares only the four
            // vocabularies its own effects address.
            Assert.AreEqual(new[] { "gig", "gig_draw", "promo", "work_the_crowd" }, tier2.declaredTags.ToArray());
            Assert.AreEqual(new[] { "promo_revealed" }, tier2.declaredFlags.ToArray());
            Assert.AreEqual(new[] { "cash", "fans", "buzz" }, Ids(tier2.declaredCurrencies));
            Assert.AreEqual(new[] { "buzz_buff" }, Ids(tier2.modifiers));
            Assert.AreEqual(new[] { "buzz_buff" }, Ids(tier2.permanentModifiers));
            Assert.AreEqual(new[] { "crowd_open_mic", "crowd_house_party", "crowd_coffeehouse",
                                    "crowd_backyard", "crowd_bar_opener", "crowd_botb_qualifier" },
                Ids(tier2.producers));
            Assert.AreEqual(new[] { "open_mic", "house_party", "coffeehouse_set",
                                    "backyard_show", "bar_opener", "botb_qualifier" },
                Ids(tier2.bars));
            Assert.AreEqual(new[] { "the_calendar" }, Ids(tier2.groups));
            Assert.AreEqual(new[] { "open_mic_draw", "house_party_draw", "coffeehouse_draw",
                                    "backyard_draw", "bar_opener_draw", "botb_qualifier_draw",
                                    "flyers", "mailing_list", "photographer_friend", "zine_writeup" },
                Ids(tier2.generators));
            Assert.AreEqual(new[] { "standing_open_mic", "standing_house_party", "standing_coffeehouse",
                                    "standing_backyard", "standing_bar_opener", "standing_botb_qualifier",
                                    "bulk_booking", "favors",
                                    "crowd_work_1", "crowd_work_2", "street_team", "merch_table", "the_van" },
                Ids(tier2.upgrades));
            Assert.AreEqual(new[] { "battle_1", "battle_2", "battle_3" }, Ids(tier2.events));

            // The opening grant plus twenty-three reveals, hosted at tier2 where
            // the gates they copy read; the chapter hosts none.
            Assert.AreEqual(0, ch2.triggers.Count);
            Assert.AreEqual(24, tier2.triggers.Count);
            CollectionAssert.AreEquivalent(new[]
            {
                "opening_cash", "reveal_promo", "reveal_bookings", "reveal_office",
                "reveal_house_party_draw", "reveal_coffeehouse_draw", "reveal_backyard_draw",
                "reveal_bar_opener_draw", "reveal_botb_qualifier_draw",
                "reveal_mailing_list", "reveal_photographer_friend", "reveal_zine_writeup",
                "reveal_standing_open_mic", "reveal_standing_house_party", "reveal_standing_coffeehouse",
                "reveal_standing_backyard", "reveal_standing_bar_opener", "reveal_standing_botb_qualifier",
                "reveal_crowd_work_1", "reveal_crowd_work_2", "reveal_street_team",
                "reveal_merch_table", "reveal_the_van", "reveal_release",
            }, Ids(tier2.triggers));
        }

        // ---- the bars and the group ----

        // The gig that opens the chapter, checked whole: it is fed by time, its
        // completion fires the draw it is paired with, and it only goes again
        // once the standing booking's flag is set - until then the row is the
        // manual team the player re-selects.
        [Test]
        public void The_open_mic_is_time_fed_and_manual_until_the_booking_is_bought()
        {
            var bar = Find(tier2.bars, "open_mic");

            Assert.AreEqual((BigNumber)5, bar.fillAmount);
            Assert.AreEqual(0, bar.consumes.Count, "a gig takes time, not a currency");
            Assert.AreEqual("booked_open_mic", ((FlagSet)bar.repeatWhen).flagId);
            Assert.AreEqual("crowd_open_mic", bar.tap.Id, "Work the Crowd is the row's own tap");
            Assert.AreEqual("open_mic_draw", ((FireGeneratorYield)bar.onComplete.Single()).generator.Id);
            Assert.AreEqual("owned open_mic_draw 1", Describe(bar.availableWhen),
                "the gig exists once one is booked");
        }

        // Every gig runs at once - the calendar is a schedule, not a choice
        // between two nights (12.7).
        [Test]
        public void The_calendar_holds_every_gig_at_once()
        {
            var group = Find(tier2.groups, "the_calendar");

            Assert.AreEqual(6, group.maxActive);
            Assert.AreEqual(new[] { "open_mic", "house_party", "coffeehouse_set",
                                    "backyard_show", "bar_opener", "botb_qualifier" },
                Ids(group.members));
        }

        // ---- the generators ----

        // A draw pays two lines per completion and a promo generator trickles
        // Buzz: the two shapes the chapter's income is built from.
        [Test]
        public void The_draws_pay_a_lump_and_the_promos_trickle_buzz()
        {
            var draw = Find(tier2.generators, "open_mic_draw");

            Assert.AreEqual("cash", draw.costCurrency.Id);
            Assert.AreEqual((BigNumber)1.3, draw.growth);
            Assert.AreEqual(new[] { "cash", "fans" }, draw.produces.Select(e => e.currency.Id).ToArray());
            Assert.AreEqual(new[] { Stat.Yield, Stat.Yield }, draw.produces.Select(e => e.stat).ToArray());
            Assert.AreEqual((BigNumber)3, draw.produces[0].value);
            Assert.AreEqual((BigNumber)0.5, draw.produces[1].value);

            // Buzz is a rate, so it accrues while the bars run and the buff over
            // it climbs with the run rather than with a firing.
            var flyers = Find(tier2.generators, "flyers");
            var buzz = flyers.produces.Single();
            Assert.AreEqual(("buzz", Stat.Rate), (buzz.currency.Id, buzz.stat));
            Assert.AreEqual((BigNumber)0.05, buzz.value);
        }

        // ---- the modifiers ----

        // Every Cash in this chapter is a fired yield, so the Buzz factor sits
        // at the yield stat on the draw tag narrowed to cash. Roadies need
        // nothing here: root's pair carries yield as well as rate, and the
        // draws carry the production tag its active factor names (8.2).
        [Test]
        public void The_run_modifier_lifts_the_gig_cash_yield_by_buzz()
        {
            var buzz = Find(tier2.modifiers, "buzz_buff").effects.Single();
            Assert.AreEqual(("gig_draw", "cash", Stat.Yield), (buzz.target, buzz.currencyId, buzz.stat));
            var curve = (LinearOnBalance)buzz.formula;
            Assert.AreEqual("buzz", curve.currency.Id);
            Assert.AreEqual((BigNumber)0.01, curve.coefficient);

            Assert.IsTrue(tier2.generators.Where(g => g.Id.EndsWith("_draw"))
                .All(g => g.Tags.SequenceEqual(new[] { "gig_draw", "production" })),
                "every draw is a production source root's roadie_active reaches");
        }

        // The chapter's three: one stacking grant the Battle pays three times,
        // and two switches an upgrade's flag turns on for the rest of the
        // chapter. A switch reads liveness, never its factor (12.2).
        [Test]
        public void The_chapter_modifiers_are_the_battle_stack_and_the_two_flag_switches()
        {
            var payout = Find(ch2.modifiers, "gig_payout_x2");
            Assert.AreEqual(StackingKind.Multiply, payout.stacking, "three clears are x8 from one definition");
            var doubled = payout.effects.Single();
            Assert.AreEqual(("gig_draw", Stat.Yield), (doubled.target, doubled.stat));
            Assert.AreEqual((BigNumber)2, doubled.multiplier);

            var bulk = Find(ch2.modifiers, "bulk_booking_switch");
            Assert.AreEqual("bulk_booked", ((FlagSet)bulk.appliesWhen).flagId);
            var autobuy = bulk.effects.Single();
            Assert.AreEqual(("gig_draw", Stat.AutoBuy), (autobuy.target, autobuy.stat));

            var favors = Find(ch2.modifiers, "favors_discount");
            Assert.AreEqual("favors_owed", ((FlagSet)favors.appliesWhen).flagId);
            var discount = favors.effects.Single();
            Assert.AreEqual(("gig_draw", Stat.Cost), (discount.target, discount.stat));
            Assert.AreEqual((BigNumber)0.5, discount.multiplier);
        }

        // ---- the upgrades ----

        // A standing booking carries no effect at all: what it buys is the flag
        // the bar's repeatWhen reads, homed at the chapter so the automation
        // outlives the EP that clears the levels.
        [Test]
        public void The_standing_booking_buys_a_chapter_flag_and_nothing_else()
        {
            var standing = Find(tier2.upgrades, "standing_open_mic");

            Assert.AreEqual("cash", standing.costCurrency.Id);
            Assert.AreEqual((BigNumber)120, standing.cost);
            Assert.AreEqual("owned open_mic_draw 3", Describe(standing.gate));
            Assert.AreEqual(0, standing.effects.Count);
            Assert.AreEqual("booked_open_mic", ((SetFlag)standing.actions.Single()).flagId);
        }

        // ---- the rungs ----

        // One evaluation, both targets: the EP pays root records and the
        // chapter's gate counter amounts that can never drift (design 5).
        [Test]
        public void The_EP_pays_both_record_currencies_from_one_root_curve()
        {
            var ep = tier2.rung;
            var pay = (AddCurrency)ep.actions[0];

            Assert.AreEqual("Record an EP", ep.label);
            Assert.AreEqual(new[] { "records", "ch2_records" }, Ids(pay.currencies));
            var curve = (RootCurveFormula)pay.formula;
            Assert.AreEqual("fans", curve.currency.Id);
            // Fans are never spent and clear with the tier, so what the round
            // earned and what stands are the same number here (12.5).
            Assert.AreEqual(PayoutTotal.EarnedThisRound, curve.reads);
            Assert.AreEqual((BigNumber)10, curve.divisor);
            Assert.AreEqual(0.5, curve.exponent);
            Assert.AreEqual(tier2, ((ResetScope)ep.actions[1]).scope, "the payout banks before the clear");

            // The second leg is the armed-reward guard: a Battle stage's
            // modifier has to be collected before the run it was won in ends.
            var legs = ((All)ep.offerCondition).conditions;
            Assert.AreEqual(new[] { "balance fans", "not reward pending" }, legs.Select(Describe).ToArray());
            Assert.AreEqual((BigNumber)300, Threshold(legs[0]));
        }

        [Test]
        public void The_capstone_banks_the_live_run_then_pays_the_roadie()
        {
            var capstone = ch2.rung;

            // One leg, so the gate is that leg and not an All wrapping it: the
            // chapter counter is the whole pacing knob.
            Assert.AreEqual("Win the Talent Show", capstone.label);
            Assert.AreEqual("balance ch2_records", Describe(capstone.offerCondition));
            Assert.AreEqual((BigNumber)120, Threshold(capstone.offerCondition));

            Assert.AreEqual(tier2, ((ExecuteRung)capstone.actions[0]).tier,
                "the live run banks through the EP's own gate before the wipe");
            var roadie = (AddCurrency)capstone.actions[1];
            Assert.AreEqual(new[] { "roadies" }, Ids(roadie.currencies));
            Assert.AreEqual((BigNumber)1, roadie.amount);
            Assert.IsNull(roadie.formula, "the chapter reward is the constant 1");
            Assert.AreEqual("ch2_complete", ((SetFlag)capstone.actions[2]).flagId);
            Assert.AreEqual(ch2, ((ResetScope)capstone.actions[3]).scope);
        }

        // ---- the Battle chain ----

        // The third stage takes away the automation instead of a multiplier: the
        // switch is off for the run, so five minutes of the ladder is bought by
        // hand (chapter-primitives F).
        [Test]
        public void The_third_battle_handicaps_the_bulk_booking_switch()
        {
            var handicap = Find(tier2.events, "battle_3").handicaps.Single();

            Assert.AreEqual(("gig_draw", Stat.AutoBuy), (handicap.target, handicap.stat));
        }

        // ---- root ----

        // The chapter's completion flag and its two story latches are homed
        // where no reset reaches them, appended beside chapter 1's.
        [Test]
        public void Root_declares_the_chapter_two_latches()
        {
            Assert.AreEqual(new[] { "ch2_complete", "story_ch2_open_seen", "story_ch2_end_seen" },
                root.declaredFlags.Skip(root.declaredFlags.Count - 3).ToArray());
            Assert.AreEqual(new[] { "story_ch2_open_seen", "story_ch2_end_seen" },
                ch2.storyBeats.Select(b => b.seenFlag).ToArray());
        }

        // ---- the screen ----

        // The release region evaluates at ch2, where the album flag lives, while
        // the button it holds presses the TIER's rung - so the module authors
        // its own scope rather than taking the section's (12.11).
        [Test]
        public void The_release_region_evaluates_at_the_chapter_and_presses_the_tier_rung()
        {
            var release = ch2.sections[4];

            Assert.AreEqual("The Release", release.title);
            Assert.AreEqual("flag album", Describe(release.visibleWhen));
            Assert.AreEqual("ch2", release.scope.Id);
            Assert.AreEqual(new[] { "rung_button - @tier2" }, Modules(4));

            // The capstone button presses its own scope's rung, so the default -
            // the section's ch2 - is what it wants.
            Assert.AreEqual("The Talent Show", ch2.sections[6].title);
            Assert.AreEqual("ch2", ch2.sections[6].scope.Id);
            Assert.AreEqual(new[] { "rung_button - @ch2" }, Modules(6));
        }

        // ---- helpers ----

        // "prefabId boundContent @normalizedScope" per module, in order - one
        // string, because the three answers only mean anything together.
        private static string[] Modules(int section) =>
            ch2.sections[section].modules
                .Select(m => $"{m.prefabId} {(m.content == null ? "-" : m.content.Id)} @{m.scope.Id}")
                .ToArray();

        private static string[] Ids<T>(IEnumerable<T> definitions) where T : Definition =>
            definitions.Select(d => d.Id).ToArray();

        private static T Find<T>(IEnumerable<T> definitions, string id) where T : Definition =>
            definitions.Single(d => d.Id == id);

        // The gate SHAPE plus whatever operand is a count - an authored count is
        // whole, so a BigNumber one prints through ToDouble as the integer it was
        // authored as. A BigNumber threshold stays out of the string and gets its
        // own typed assertion, since what a BigNumber prints is not part of any
        // contract here.
        private static string Describe(Condition condition) => condition switch
        {
            CurrencyAtLeast c => $"balance {c.currency.Id}",
            EarnedTotalAtLeast c => $"earned {c.currency.Id}",
            OwnedCountAtLeast c => $"owned {c.generator.Id} {c.count.ToDouble()}",
            FlagSet c => $"flag {c.flagId}",
            BarsCompleted c => $"bars {c.group.Id} {c.count}",
            Not { condition: EventRewardPending } => "not reward pending",
            Not { condition: FlagSet exclusion } => $"not flag {exclusion.flagId}",
            _ => condition.GetType().Name,
        };

        private static BigNumber Threshold(Condition condition) => condition switch
        {
            CurrencyAtLeast c => c.threshold,
            EarnedTotalAtLeast c => c.threshold,
            _ => throw new AssertionException($"{condition.GetType().Name} carries no threshold."),
        };
    }
}
