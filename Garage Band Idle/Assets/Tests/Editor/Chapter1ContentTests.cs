using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Editor;
using RidiculousGaming.GarageBandIdle.Events;
using UnityEditor;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The authored Chapter 1 documents as they land on disk (content doc
    // sections 3-10), read back through the composition seam boot uses. The
    // verify loop runs the import before the suite, so a stale asset cannot
    // pass as green here.
    //
    // Spot checks, not a second copy of the content doc: one assertion per
    // block that a mis-authored number or a dropped reference would break. The
    // numbers stay the content doc's, and a delta between the two is a bug in
    // whichever edited last.
    public class Chapter1ContentTests
    {
        private static RootDefinition root;
        private static ChapterDefinition ch1;
        private static TierDefinition tier1;
        private static ComposedContent content;

        [OneTimeSetUp]
        public void LoadTheImportedPair()
        {
            root = AssetDatabase.LoadAssetAtPath<RootDefinition>(
                ChapterJsonImporter.AssetRootPath + "/root/root.asset");
            ch1 = AssetDatabase.LoadAssetAtPath<ChapterDefinition>(
                ChapterJsonImporter.AssetRootPath + "/ch1/ch1.asset");
            Assert.IsNotNull(root, "root.json has not been imported - run Garage Band Idle/Import Content.");
            Assert.IsNotNull(ch1, "chapter-01.json has not been imported - run Garage Band Idle/Import Content.");
            content = ComposedContent.Compose(root, new[] { ch1 });
            tier1 = (TierDefinition)ch1.children.Single();
        }

        // ---- the keystone ----

        // The whole authored set, judged by the pass that gates the import
        // itself. The two warnings are the story latches, whose setter
        // (AcknowledgeStory) is step 10's - the whitelist retires with it.
        [Test]
        public void The_imported_pair_validates_with_only_the_story_latch_warnings()
        {
            var report = ContentValidator.Validate(content);

            var all = string.Join("\n", report.Findings);
            Assert.IsFalse(report.HasErrors, all);
            Assert.AreEqual(2, report.Findings.Count, all);
            Assert.AreEqual(new[] { "story_ch1_end_seen", "story_ch1_open_seen" },
                report.OfCheck(ValidationCheck.FlagNoSetter)
                    .Select(f => f.Message.Split('\'')[1]).OrderBy(id => id).ToArray(),
                all);
        }

        // ---- section 2: the scope tree ----

        [Test]
        public void The_scope_shape_is_root_then_ch1_then_tier1()
        {
            Assert.AreEqual(new[] { "ch1" }, content.Chapters.Select(c => c.Id).ToArray());
            Assert.AreEqual(0, root.children.Count, "the roster is the label, never root's serialized list (12.14.5)");
            Assert.AreEqual("tier1", tier1.Id);
            Assert.AreEqual(0, tier1.children.Count);
        }

        // ---- sections 3-8: what each scope declares ----

        // Declaration counts per family: this is what catches a block that
        // silently loses an entry to a merge or a mis-nested brace.
        [Test]
        public void Each_scope_declares_the_families_the_content_doc_files_on_it()
        {
            Assert.AreEqual(new[] { "records", "roadies" }, Ids(root.declaredCurrencies));
            Assert.AreEqual(new[] { "income", "production" }, root.declaredTags.ToArray());

            Assert.AreEqual(new[] { "ch1_records" }, Ids(ch1.declaredCurrencies));
            Assert.AreEqual(new[] { "album", "gj1_done", "gj2_done", "gj3_done" }, ch1.declaredFlags.ToArray());
            Assert.AreEqual(new[] { "gj_tap_1", "gj_tap_2", "gj_tap_3" }, Ids(ch1.modifiers));
            Assert.AreEqual(0, ch1.events.Count, "the Garage Jam chain hosts at tier1");

            Assert.AreEqual(new[] { "cash", "fans", "rehearsal" }, Ids(tier1.declaredCurrencies));
            Assert.AreEqual(new[] { "fans_revealed", "rehearsal_revealed" }, tier1.declaredFlags.ToArray());
            Assert.AreEqual(new[] { "gear", "bandmate" }, tier1.declaredTags.ToArray());
            Assert.AreEqual(new[] { "tap_producer", "band" }, Ids(tier1.producers));
            Assert.AreEqual(new[] { "practice_amp", "drummer", "bassist", "guitarist" }, Ids(tier1.generators));
            Assert.AreEqual(new[] { "stage_presence", "amp_strings", "kit_upgrade", "tight_set",
                                    "play_for_crowd", "unlock_covers", "cut_demo" }, Ids(tier1.upgrades));
            Assert.AreEqual(new[] { "cover_bonus_1", "cover_bonus_2", "cover_bonus_3" }, Ids(tier1.modifiers));
            Assert.AreEqual(new[] { "learn_covers" }, Ids(tier1.barGroups));
            Assert.AreEqual(new[] { "cover_1", "cover_2", "cover_3" }, Ids(tier1.barGroups[0].bars));
            Assert.AreEqual(new[] { "garage_jam_1", "garage_jam_2", "garage_jam_3" }, Ids(tier1.events));

            // Chapter 1 authors zero triggers (content doc section 11) - the
            // family exists for later chapters.
            Assert.AreEqual(0, ch1.triggers.Count);
            Assert.AreEqual(0, tier1.triggers.Count);
        }

        // The income tag is what the Records and Roadie modifiers target, and
        // fans carrying it would open the farm throttle (section 8.2).
        [Test]
        public void Cash_carries_income_and_fans_carries_nothing()
        {
            Assert.IsTrue(Find(tier1.declaredCurrencies, "cash").HasTag("income"));
            Assert.AreEqual(0, Find(tier1.declaredCurrencies, "fans").Tags.Count);
        }

        // ---- section 4: producers ----

        // The tap's four entries in order: the flat cash line, the
        // stage_presence bonus as a CONDITIONED entry rather than an
        // upgrade-owned contribution, then the two rehearsal lines - which
        // carry no condition, because the reveal is the currency's own.
        [Test]
        public void The_tap_producer_pays_the_four_entries()
        {
            var tap = Find(tier1.producers, "tap_producer");

            Assert.AreEqual(new[] { "cash", "cash", "rehearsal", "rehearsal" },
                tap.produces.Select(e => e.currency.Id).ToArray());
            Assert.AreEqual(new[] { Stat.Yield, Stat.Yield, Stat.Yield, Stat.Rate },
                tap.produces.Select(e => e.stat).ToArray());
            Assert.AreEqual((BigNumber)1, tap.produces[0].value);
            Assert.AreEqual((BigNumber)0.5, tap.produces[3].value);
            Assert.IsNull(tap.produces[0].condition, "the base tap line is unconditional");
            Assert.AreEqual("stage_presence", ((UpgradePurchased)tap.produces[1].condition).upgrade.Id);
            Assert.IsNull(tap.produces[2].condition, "rehearsal's own gate covers the yield line");
            Assert.IsNull(tap.produces[3].condition, "and the rate line with it");

            // Base fan accrual; band size adds to it from the bandmates' own
            // entries, and none of them repeats the reveal.
            var band = Find(tier1.producers, "band");
            var fans = band.produces.Single();
            Assert.AreEqual(("fans", Stat.Rate), (fans.currency.Id, fans.stat));
            Assert.AreEqual((BigNumber)0.35, fans.value);
            Assert.IsNull(fans.condition);
        }

        // The reveals live on the currencies (12.2). Fans is why: its sources
        // are band plus every bandmate generator, and play_for_crowd gates on
        // owning a drummer - so with the gate on the entries, a bandmate
        // necessarily exists before the flag is set and trickles fans behind
        // the reveal.
        [Test]
        public void The_reveals_are_declared_on_the_currencies_not_on_the_entries()
        {
            Assert.AreEqual("fans_revealed",
                ((FlagSet)Find(tier1.declaredCurrencies, "fans").activeWhen).flagId);
            Assert.AreEqual("rehearsal_revealed",
                ((FlagSet)Find(tier1.declaredCurrencies, "rehearsal").activeWhen).flagId);
            Assert.IsNull(Find(tier1.declaredCurrencies, "cash").activeWhen, "cash is live from the first press");

            // No source repeats the gate - that is the whole point of moving it.
            foreach (var entry in tier1.producers.SelectMany(p => p.produces)
                         .Concat(tier1.generators.SelectMany(g => g.produces)))
                Assert.IsFalse(entry.condition is FlagSet flag
                        && (flag.flagId == "fans_revealed" || flag.flagId == "rehearsal_revealed"),
                    $"a {entry.currency.Id} entry repeats a reveal the currency already states");
        }

        // ---- section 5: generators ----

        // Costs and gates together: the ladder is the pacing, and a gate
        // pointing at the wrong rung is invisible to every other check.
        [Test]
        public void Generator_costs_and_gates_ladder_as_the_content_doc_has_them()
        {
            var expected = new (string id, double cost, string gate)[]
            {
                ("practice_amp", 60, "earned cash"),
                ("drummer", 250, "owned practice_amp 3"),
                ("bassist", 4000, "owned drummer 5"),
                ("guitarist", 30000, "owned bassist 5"),
            };

            foreach (var (id, cost, gate) in expected)
            {
                var generator = Find(tier1.generators, id);
                Assert.AreEqual("cash", generator.costCurrency.Id, id);
                Assert.AreEqual((BigNumber)cost, generator.baseCost, id);
                Assert.AreEqual((BigNumber)1.15, generator.growth, id);
                Assert.AreEqual(gate, Describe(generator.availableWhen), id);
            }

            // The one balance threshold in the ladder; the rest count rungs.
            Assert.AreEqual((BigNumber)100, Threshold(Find(tier1.generators, "practice_amp").availableWhen));

            // Band size drives the fan rate: each bandmate carries the same
            // 0.02 entry and ownedCount does the scaling, so no per-bandmate
            // constant exists anywhere.
            foreach (var id in new[] { "drummer", "bassist", "guitarist" })
            {
                var bandmate = Find(tier1.generators, id);
                Assert.IsTrue(bandmate.HasTag("bandmate"), id);
                Assert.AreEqual((BigNumber)0.02,
                    bandmate.produces.Single(e => e.currency.Id == "fans").value, id);
            }

            // The gear tag is the event handicap's whole reach - a generator
            // missing it would keep producing through a Garage Jam.
            Assert.IsTrue(tier1.generators.All(g => g.HasTag("gear")));
        }

        // ---- section 6: upgrades ----

        [Test]
        public void Upgrade_costs_and_payloads_match_the_content_doc()
        {
            var costs = new (string id, double cost)[]
            {
                ("stage_presence", 250), ("amp_strings", 500), ("kit_upgrade", 5000),
                ("tight_set", 20000), ("play_for_crowd", 100), ("unlock_covers", 200),
                ("cut_demo", 0),
            };
            foreach (var (id, cost) in costs)
            {
                var upgrade = Find(tier1.upgrades, id);
                Assert.AreEqual("cash", upgrade.costCurrency.Id, id);
                Assert.AreEqual((BigNumber)cost, upgrade.cost, id);
            }

            Assert.AreEqual("fans_revealed", OnlyFlagSet(Find(tier1.upgrades, "play_for_crowd")));
            Assert.AreEqual("rehearsal_revealed", OnlyFlagSet(Find(tier1.upgrades, "unlock_covers")));
            // The album flag is the chapter's, so the release region persists
            // across runs while the upgrade that sets it clears with the tier.
            Assert.AreEqual("album", OnlyFlagSet(Find(tier1.upgrades, "cut_demo")));

            // stage_presence carries no payload at all: it is a pure latch, and
            // the tap's conditioned entry is what reads it.
            var latch = Find(tier1.upgrades, "stage_presence");
            Assert.AreEqual(0, latch.effects.Count);
            Assert.AreEqual(0, latch.actions.Count);

            // kit_upgrade narrows to the drummer's cash line; the fans line
            // rides the band-size scaling untouched.
            var kit = Find(tier1.upgrades, "kit_upgrade").effects.Single();
            Assert.AreEqual(("drummer", "cash", Stat.Rate), (kit.target, kit.currencyId, kit.stat));
            Assert.AreEqual((BigNumber)2, kit.multiplier);
        }

        // ---- section 7: bars ----

        [Test]
        public void The_covers_fill_from_rehearsal_and_grant_their_own_bonus()
        {
            var group = Find(tier1.barGroups, "learn_covers");
            Assert.AreEqual(1, group.maxActive, "choosing the next cover is the mechanic (12.7)");

            var amounts = new[] { 100, 300, 600 };
            for (var i = 0; i < group.bars.Count; i++)
            {
                var bar = group.bars[i];
                Assert.AreEqual("rehearsal", bar.fillCurrency.Id, bar.Id);
                Assert.AreEqual((BigNumber)amounts[i], bar.fillAmount, bar.Id);
                Assert.AreEqual((BigNumber)2, bar.fillRate, bar.Id);
                Assert.IsFalse(bar.repeating, bar.Id);
                // A non-repeating completion leaves no derivable effect-fact,
                // so the fan-rate reward is a grant that clears with tier1.
                var grant = (AddModifier)bar.onComplete.Single();
                Assert.AreEqual(tier1, grant.scope, bar.Id);
                Assert.AreEqual($"cover_bonus_{i + 1}", grant.modifier.Id, bar.Id);
            }

            // Three distinct ids, so all three stack multiplicatively.
            var bonuses = new[] { 1.15, 1.15, 1.20 };
            for (var i = 0; i < bonuses.Length; i++)
            {
                var effect = tier1.modifiers[i].effects.Single();
                Assert.AreEqual(("fans", Stat.Rate), (effect.target, effect.stat), tier1.modifiers[i].Id);
                Assert.AreEqual((BigNumber)bonuses[i], effect.multiplier, tier1.modifiers[i].Id);
            }
        }

        // ---- section 9: the rungs ----

        // One evaluation, both targets: the album pays root records and the
        // chapter's gate counter amounts that can never drift.
        [Test]
        public void The_release_pays_both_record_currencies_from_one_root_curve()
        {
            var release = tier1.rung;
            var pay = (AddCurrency)release.actions[0];

            Assert.AreEqual(new[] { "records", "ch1_records" }, Ids(pay.currencies));
            var curve = (RootCurveFormula)pay.formula;
            Assert.AreEqual("fans", curve.currency.Id);
            Assert.AreEqual((BigNumber)5, curve.divisor);
            Assert.AreEqual(0.5, curve.exponent);
            Assert.AreEqual(tier1, ((ResetScope)release.actions[1]).scope, "the payout banks before the clear");

            var legs = ((All)release.offerCondition).conditions;
            Assert.AreEqual(new[] { "balance fans", "bars learn_covers 1", "not tier1 reward pending" },
                legs.Select(Describe).ToArray());
            Assert.AreEqual((BigNumber)50, Threshold(legs[0]));
        }

        [Test]
        public void The_capstone_banks_the_live_run_then_pays_the_roadie()
        {
            var capstone = ch1.rung;

            var legs = ((All)capstone.offerCondition).conditions;
            Assert.AreEqual(new[] { "balance ch1_records", "not tier1 reward pending" },
                legs.Select(Describe).ToArray());
            // The primary pacing knob (section 11).
            Assert.AreEqual((BigNumber)30, Threshold(legs[0]));

            Assert.AreEqual(tier1, ((ExecuteRung)capstone.actions[0]).tier,
                "the live run banks through the release's own gate before the wipe");
            var roadie = (AddCurrency)capstone.actions[1];
            Assert.AreEqual(new[] { "roadies" }, Ids(roadie.currencies));
            Assert.AreEqual((BigNumber)1, roadie.amount);
            Assert.IsNull(roadie.formula, "chapter 1's reward formula is the constant 1");
            Assert.AreEqual("ch1_complete", ((SetFlag)capstone.actions[2]).flagId);
            Assert.AreEqual(ch1, ((ResetScope)capstone.actions[3]).scope);
        }

        // ---- section 10: the Garage Jam chain ----

        // Each level gates on the previous one's completion flag AND a banked
        // Records threshold, which is what makes "come back later" the
        // experience rather than a wall.
        [Test]
        public void The_garage_jam_chain_ladders_on_the_previous_flag_and_records()
        {
            Assert.AreEqual("balance records", Describe(Event("garage_jam_1").availableWhen));
            Assert.AreEqual((BigNumber)1, Threshold(Event("garage_jam_1").availableWhen));

            var later = new (string id, string flag, double records)[]
            {
                ("garage_jam_2", "flag gj1_done", 15),
                ("garage_jam_3", "flag gj2_done", 30),
            };
            foreach (var (id, flag, records) in later)
            {
                var legs = ((All)Event(id).availableWhen).conditions;
                Assert.AreEqual(new[] { flag, "balance records" }, legs.Select(Describe).ToArray(), id);
                Assert.AreEqual((BigNumber)records, Threshold(legs[1]), id);
            }

            var goals = new[] { 150, 300, 600 };
            var timers = new[] { 60d, 90d, 90d };
            for (var i = 0; i < tier1.events.Count; i++)
            {
                var evt = tier1.events[i];
                Assert.AreEqual("balance cash", Describe(evt.goal), evt.Id);
                Assert.AreEqual((BigNumber)goals[i], Threshold(evt.goal), evt.Id);
                Assert.AreEqual(timers[i], evt.timeLimitSeconds, evt.Id);
                // Tap only: the handicap zeroes every generator line by
                // derivation, and band's base trickle keeps running.
                var handicap = evt.handicaps.Single();
                Assert.AreEqual(("gear", Stat.Rate), (handicap.target, handicap.stat), evt.Id);
                Assert.AreEqual((BigNumber)0, handicap.multiplier, evt.Id);
                // A gate-met run banks exactly as a release would; an
                // unfinished one is discarded. onEnd runs either way.
                Assert.AreEqual(tier1, ((RestartScope)evt.onEntry.Single()).scope, evt.Id);
                Assert.AreEqual(tier1, ((ResetScope)evt.onEnd.Single()).scope, evt.Id);
            }
        }

        // One live at a time: each level after the first removes its
        // predecessor's stack before granting its own.
        [Test]
        public void The_tap_bonus_swaps_rather_than_stacking()
        {
            Assert.AreEqual(new[] { "AddModifier gj_tap_1", "SetFlag gj1_done" }, Rewards("garage_jam_1"));
            Assert.AreEqual(new[] { "RemoveModifier gj_tap_1", "AddModifier gj_tap_2", "SetFlag gj2_done" },
                Rewards("garage_jam_2"));
            Assert.AreEqual(new[] { "RemoveModifier gj_tap_2", "AddModifier gj_tap_3", "SetFlag gj3_done" },
                Rewards("garage_jam_3"));

            // The stacks live at ch1, so they survive tier resets and die at
            // the capstone.
            foreach (var evt in tier1.events)
                foreach (var action in evt.rewards)
                    if (action is AddModifier grant)
                        Assert.AreEqual(ch1, grant.scope, evt.Id);

            var multipliers = new[] { 1.25, 1.5, 2 };
            for (var i = 0; i < multipliers.Length; i++)
            {
                var effect = ch1.modifiers[i].effects.Single();
                Assert.AreEqual(("tap_producer", "cash", Stat.Yield),
                    (effect.target, effect.currencyId, effect.stat), ch1.modifiers[i].Id);
                Assert.AreEqual((BigNumber)multipliers[i], effect.multiplier, ch1.modifiers[i].Id);
            }
        }

        // ---- helpers ----

        private static EventDefinition Event(string id) => Find(tier1.events, id);

        private static string[] Rewards(string id) =>
            Event(id).rewards.Select(a => a switch
            {
                AddModifier grant => $"AddModifier {grant.modifier.Id}",
                RemoveModifier remove => $"RemoveModifier {remove.modifier.Id}",
                SetFlag flag => $"SetFlag {flag.flagId}",
                _ => a.GetType().Name,
            }).ToArray();

        private static string OnlyFlagSet(UpgradeDefinition upgrade) =>
            ((SetFlag)upgrade.actions.Single()).flagId;

        private static string[] Ids<T>(IEnumerable<T> definitions) where T : Definition =>
            definitions.Select(d => d.Id).ToArray();

        private static T Find<T>(IEnumerable<T> definitions, string id) where T : Definition =>
            definitions.Single(d => d.Id == id);

        // The gate SHAPE plus whatever operand is an int - a BigNumber
        // threshold stays out of the string and gets its own typed assertion,
        // since what a BigNumber prints is not part of any contract here.
        private static string Describe(Condition condition) => condition switch
        {
            CurrencyAtLeast c => $"balance {c.currency.Id}",
            EarnedTotalAtLeast c => $"earned {c.currency.Id}",
            OwnedCountAtLeast c => $"owned {c.generator.Id} {c.count}",
            FlagSet c => $"flag {c.flagId}",
            BarsCompleted c => $"bars {c.group.Id} {c.count}",
            Not { condition: EventRewardPending pending } => $"not {pending.host.Id} reward pending",
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
