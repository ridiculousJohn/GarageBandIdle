using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The smallest tree a draw needs: root -> ch1 -> tier1, with one consumed
    // currency homed at tier1 and one at root for the shared cases. Each test
    // authors its own bars and groups and THEN builds the state tree, because
    // ScopeState.Build initializes declared facts from the definitions.
    internal class BarFixture
    {
        public readonly DateTime Now = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

        public readonly RootDefinition RootDef;
        public readonly ChapterDefinition Ch1Def;
        public readonly TierDefinition Tier1Def;
        public readonly CurrencyDefinition Rehearsal;   // tier1's own
        public readonly CurrencyDefinition Shared;      // root's, so every chapter drinks the same one
        public readonly CurrencyDefinition Fans;
        public readonly List<ChapterDefinition> Chapters = new();

        public RootScopeState Root;
        public ChapterScopeState Ch1;
        public ScopeState Tier1;

        public BarFixture()
        {
            Tier1Def = TestTree.MakeTier("tier1");
            Rehearsal = TestTree.DeclareCurrency(Tier1Def, "rehearsal");
            Fans = TestTree.DeclareCurrency(Tier1Def, "fans");
            Tier1Def.declaredFlags.Add("encore");

            Ch1Def = TestTree.MakeChapter("ch1");
            Ch1Def.children.Add(Tier1Def);

            RootDef = TestTree.MakeRoot("root");
            Shared = TestTree.DeclareCurrency(RootDef, "shared");
            Chapters.Add(Ch1Def);
        }

        public void Build()
        {
            Root = ScopeState.Build(ComposedContent.Compose(RootDef, Chapters));
            Ch1 = (ChapterScopeState)TestNavigation.Node(Root, Ch1Def);
            Tier1 = TestNavigation.Node(Root, Tier1Def);
        }

        // A group lists what its own scope declares (12.7), so the fixture
        // remembers the scope a group was declared on and files its bars there.
        // `consumed` is the currency the members drink by default, taken once
        // here; a test that needs otherwise writes `bar.consumes` itself, and
        // null is a group of bars that fill from time alone.
        public GroupDefinition Group(ScopeDefinition scope, string id, CurrencyDefinition consumed,
                                     int maxActive = 4)
        {
            var group = TestTree.MakeDefinition<GroupDefinition>(id);
            group.maxActive = maxActive;
            scope.groups.Add(group);
            declared[group] = (scope, consumed);
            return group;
        }

        private readonly Dictionary<GroupDefinition, (ScopeDefinition scope, CurrencyDefinition consumed)> declared = new();

        // The repeat is a condition judged at the bar's home (12.7), so the
        // fixture takes one: null is the bar that fills once, Always is the
        // repeating one, and a condition that refuses is the manual team.
        public BarDefinition Bar(GroupDefinition group, string id, double fillAmount, double fillRate,
                                 Condition repeatWhen = null)
        {
            var (scope, consumed) = declared[group];
            var bar = TestTree.MakeDefinition<BarDefinition>(id);
            if (consumed != null)
                bar.consumes.Add(new ConsumesEntry { currency = consumed, amount = 1 });
            bar.fillAmount = fillAmount;
            bar.fillRate = fillRate;
            bar.repeatWhen = repeatWhen;
            scope.bars.Add(bar);
            group.members.Add(bar);
            return bar;
        }

        // One entry of a bar's consumes list, per unit of fill (12.7).
        public static void Drinks(BarDefinition bar, CurrencyDefinition currency, double amount) =>
            bar.consumes.Add(new ConsumesEntry { currency = currency, amount = amount });

        // The upgrade a repeat condition can read, priced at nothing because no
        // test here buys it - the latch is written as a fact.
        public UpgradeDefinition Upgrade(ScopeDefinition scope, string id)
        {
            var upgrade = TestTree.MakeDefinition<UpgradeDefinition>(id);
            upgrade.gate = new Always();
            upgrade.costCurrency = Rehearsal;
            upgrade.cost = 0;
            scope.upgrades.Add(upgrade);
            return upgrade;
        }

        // Selection written as a FACT, bypassing the entry point: a draw test is
        // not a SetActiveMembers test.
        public void Select(ScopeState scope, GroupDefinition group, params Definition[] members)
        {
            var set = new HashSet<string>();
            foreach (var member in members)
                set.Add(member.Id);
            scope.activeMembers[group.Id] = set;
        }

        // Declared BEFORE the build, because the gather is compiled when the
        // tree is built: a modifier authored afterward sits on no plan. The
        // stack is a FACT, so Stack writes it once the nodes exist.
        public ModifierDefinition Declare(ScopeDefinition scope, string target, double multiplier)
        {
            var modifier = TestTree.MakeDefinition<ModifierDefinition>("mod_" + target + "_" + multiplier);
            modifier.effects.Add(new Effect { target = target, stat = Stat.Rate, multiplier = multiplier });
            scope.modifiers.Add(modifier);
            return modifier;
        }

        // A consumption factor: stage 1 at the bar's own coordinate, narrowed to
        // the currency it drinks, so "this bar consumes 90% less X" is one
        // authored effect (12.7). A null currencyId is the wildcard.
        public ModifierDefinition DeclareConsumption(ScopeDefinition scope, string id, string target,
                                                     string currencyId, double multiplier)
        {
            var modifier = TestTree.MakeDefinition<ModifierDefinition>(id);
            modifier.effects.Add(new Effect
            {
                target = target,
                currencyId = currencyId,
                stat = Stat.Consumption,
                multiplier = multiplier
            });
            scope.modifiers.Add(modifier);
            return modifier;
        }

        public void Stack(ScopeState scope, ModifierDefinition modifier) =>
            scope.modifierStacks[modifier.Id] = 1;

        public void Pour(ScopeState home, CurrencyDefinition currency, double amount) =>
            home.balances[currency.Id] = amount;

        public BigNumber Balance(ScopeState home, CurrencyDefinition currency) => home.balances[currency.Id];

        public BigNumber Earned(ScopeState home, CurrencyDefinition currency) => home.earnedTotals[currency.Id];

        public BigNumber Progress(ScopeState scope, BarDefinition bar) =>
            scope.barProgress.TryGetValue(bar.Id, out var value) ? value : BigNumber.Zero;

        public int Fills(ScopeState scope, BarDefinition bar) =>
            scope.fillCounts.TryGetValue(bar.Id, out var value) ? value : 0;

        public BarDemand Resolve() => BarSystem.ResolveDemand(Root, Now);

        public void Settle(BarDemand demand, double dt) =>
            BarSystem.ConsumeAndSettle(demand, dt, Now.AddSeconds(dt), new TickReport(dt));

        public void Segment(double dt) => Settle(Resolve(), dt);
    }

    // The consumption half of the economy (design doc 12.7): what a bar drinks,
    // what happens when what it drinks runs short, and what a crossing fires.
    public class BarSystemTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // A fire counter: each completion pays one unit of a currency, so the
        // balance IS the number of times onComplete ran.
        private static void CountFires(BarDefinition bar, CurrencyDefinition into) =>
            bar.onComplete.Add(new AddCurrency { currencies = { into }, amount = 1 });

        // ---- the draw ----

        // What replaces the proportional split: bars take what they want in
        // declaration order until the currency runs out. The total delivered is
        // the same either way - it is whatever was banked - so the only
        // difference is that one bar visibly moves instead of three inching.
        [Test]
        public void A_short_consumed_currency_feeds_bars_in_declaration_order_until_it_runs_out()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var first = f.Bar(group, "first", 100, 4);
            var second = f.Bar(group, "second", 100, 4);
            var third = f.Bar(group, "third", 100, 4);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 6);
            f.Select(f.Tier1, group, first, second, third);

            f.Segment(1);

            AssertClose(4, f.Progress(f.Tier1, first), "took its whole rate");
            AssertClose(2, f.Progress(f.Tier1, second), "took what was left");
            AssertClose(0, f.Progress(f.Tier1, third), "stalled");
            AssertClose(0, f.Balance(f.Tier1, f.Rehearsal), "rehearsal");
        }

        // The rule the whole shape turns on: a bar names its OWN currencies, so
        // one group holds bars drinking different things - including one that
        // drinks nothing. An implementation resolving one currency per GROUP
        // would pass every other test in this file.
        [Test]
        public void Bars_in_one_group_may_name_different_currencies()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "mixed", f.Rehearsal);
            var drinksRehearsal = f.Bar(group, "drinks_rehearsal", 1000, 3);
            var drinksShared = f.Bar(group, "drinks_shared", 1000, 5);
            drinksShared.consumes.Clear();
            BarFixture.Drinks(drinksShared, f.Shared, 1);       // homed at root, not tier1
            var drinksNothing = f.Bar(group, "drinks_nothing", 1000, 7);
            drinksNothing.consumes.Clear();                     // fills from time alone
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 100);
            f.Pour(f.Root, f.Shared, 100);
            f.Select(f.Tier1, group, drinksRehearsal, drinksShared, drinksNothing);

            f.Segment(2);

            AssertClose(6, f.Progress(f.Tier1, drinksRehearsal), "drinks rehearsal");
            AssertClose(10, f.Progress(f.Tier1, drinksShared), "drinks shared");
            AssertClose(14, f.Progress(f.Tier1, drinksNothing), "drinks nothing");

            // Each spent at its OWN home, and the time-filled one spent nothing.
            AssertClose(94, f.Balance(f.Tier1, f.Rehearsal), "rehearsal");
            AssertClose(90, f.Balance(f.Root, f.Shared), "shared");
        }

        // One currency, two groups at different scopes: the balance is the only
        // thing arbitrating, and it needs no group-level bookkeeping to do it.
        [Test]
        public void Bars_in_different_groups_draw_the_same_currency_in_tree_order()
        {
            var f = new BarFixture();
            var outer = f.Group(f.Ch1Def, "outer", f.Shared);
            var inner = f.Group(f.Tier1Def, "inner", f.Shared);
            var chapterBar = f.Bar(outer, "bar_outer", 1000, 6);
            var tierBar = f.Bar(inner, "bar_inner", 1000, 4);
            f.Build();
            f.Pour(f.Root, f.Shared, 8);
            f.Select(f.Ch1, outer, chapterBar);
            f.Select(f.Tier1, inner, tierBar);

            f.Segment(1);

            // Parent before child, so the chapter's bar drinks first.
            AssertClose(6, f.Progress(f.Ch1, chapterBar), "bar_outer");
            AssertClose(2, f.Progress(f.Tier1, tierBar), "bar_inner");
            AssertClose(0, f.Balance(f.Root, f.Shared), "shared");
        }

        [Test]
        public void An_exhausted_currency_pays_what_it_has_and_no_more()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 1000, 5);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 1);
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            AssertClose(1, f.Progress(f.Tier1, bar), "progress");
            AssertClose(0, f.Balance(f.Tier1, f.Rehearsal), "rehearsal");
        }

        [Test]
        public void A_fill_spends_the_consumed_currency_without_touching_its_earned_total()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 1000, 5);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 50);
            f.Tier1.earnedTotals[f.Rehearsal.Id] = 50;
            f.Select(f.Tier1, group, bar);

            f.Segment(2);

            AssertClose(40, f.Balance(f.Tier1, f.Rehearsal), "balance");
            AssertClose(50, f.Earned(f.Tier1, f.Rehearsal), "earned total is not a spend record");
        }

        [Test]
        public void Overfill_is_allowed_and_retained()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 5, 100);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            // A bar takes its whole rate, not its remaining need - overfill is
            // allowed and readable (12.7).
            AssertClose(100, f.Progress(f.Tier1, bar), "progress");
        }

        [Test]
        public void A_bar_targeted_buff_reaches_the_fill_rate()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 1000, 2);
            var buff = f.Declare(f.Tier1Def, "cover_a", 3);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, bar);
            f.Stack(f.Tier1, buff);

            f.Segment(1);
            AssertClose(6, f.Progress(f.Tier1, bar), "per-bar speed is buffable by id or tag");
        }

        [Test]
        public void A_currency_total_buff_never_speeds_the_drain_it_supplies()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 1000, 2);
            var supplyBuff = f.Declare(f.Tier1Def, "rehearsal", 3);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, bar);
            f.Stack(f.Tier1, supplyBuff);           // an effect on the drunk currency's total

            f.Segment(1);

            // The rate resolves stage 1 only. Stage 2 is "effects on this
            // currency's total production", and a bar consumes rather than
            // produces - letting it through would speed the drain as well as the
            // supply, which is not what either buff means.
            AssertClose(2, f.Progress(f.Tier1, bar), "unchanged");
        }

        // The fill-rate plan carries no currency coordinate (12.7): a bar's own
        // speed is one number however many currencies it drinks, so an effect
        // narrowed to a currency addresses the CONSUMPTION stat instead and the
        // rate never sees it.
        [Test]
        public void A_currency_narrowed_effect_never_matches_a_bars_fill_rate()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "poured", f.Rehearsal);
            var drinker = f.Bar(group, "drinker", 1000, 2);
            var timed = f.Group(f.Tier1Def, "timed", null);
            var ticker = f.Bar(timed, "ticker", 1000, 2);
            var modifier = TestTree.MakeDefinition<ModifierDefinition>("narrowed");
            modifier.effects.Add(new Effect { target = "rehearsal_fill", currencyId = "rehearsal", stat = Stat.Rate, multiplier = 4 });
            f.Tier1Def.declaredTags.Add("rehearsal_fill");
            drinker.EditorInit("drinker", "rehearsal_fill");
            ticker.EditorInit("ticker", "rehearsal_fill");
            f.Tier1Def.modifiers.Add(modifier);
            // The tags decide what MATCHES, and matching is compile time: the
            // whole selector has to stand before the tree is built.
            f.Build();
            f.Tier1.modifierStacks[modifier.Id] = 1;
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, drinker);
            f.Select(f.Tier1, timed, ticker);

            f.Segment(1);

            AssertClose(2, f.Progress(f.Tier1, drinker), "no currency coordinate on a fill rate");
            AssertClose(2, f.Progress(f.Tier1, ticker), "and none on a bar that fills from time");
        }

        // ---- consumption (12.7) ----

        // The fill is the smallest of the want and EVERY entry's cover, and each
        // entry then spends what that fill cost it.
        [Test]
        public void A_two_entry_bar_fills_the_tightest_cover_and_spends_both()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", null);
            var bar = f.Bar(group, "cover_a", 1000, 10);
            BarFixture.Drinks(bar, f.Rehearsal, 1);
            BarFixture.Drinks(bar, f.Shared, 2);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 100);      // covers 100 units
            f.Pour(f.Root, f.Shared, 12);           // covers 6, which is the binding one
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            AssertClose(6, f.Progress(f.Tier1, bar), "the tightest cover decides the fill");
            AssertClose(94, f.Balance(f.Tier1, f.Rehearsal), "6 units at 1 each");
            AssertClose(0, f.Balance(f.Root, f.Shared), "6 units at 2 each");
        }

        [Test]
        public void A_bar_whose_consumed_currency_is_empty_fills_nothing()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 1000, 5);
            f.Build();
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            AssertClose(0, f.Progress(f.Tier1, bar), "nothing banked is nothing to drink");
            AssertClose(0, f.Balance(f.Tier1, f.Rehearsal), "and nothing was spent");
        }

        // Consumption is a stat (12.7), so "this bar consumes 90% less Rehearsal"
        // is one authored effect and the same fill costs a tenth.
        [Test]
        public void A_consumption_effect_leaves_the_fill_alone_and_spends_a_tenth()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 1000, 5);
            var efficient = f.DeclareConsumption(f.Tier1Def, "efficient", "cover_a", "rehearsal", 0.1);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 100);
            f.Select(f.Tier1, group, bar);
            f.Stack(f.Tier1, efficient);

            f.Segment(1);

            AssertClose(5, f.Progress(f.Tier1, bar), "the rate is untouched");
            AssertClose(99.5, f.Balance(f.Tier1, f.Rehearsal), "5 units at a tenth of one each");
        }

        // Stage 2 is the supply side (12.7): a buff on Rehearsal's total means
        // more of it produced, never less of it drunk.
        [Test]
        public void A_currency_total_buff_never_changes_what_a_bar_spends()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 1000, 5);
            var supplyBuff = f.Declare(f.Tier1Def, "rehearsal", 3);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 100);
            f.Select(f.Tier1, group, bar);
            f.Stack(f.Tier1, supplyBuff);

            f.Segment(1);

            AssertClose(95, f.Balance(f.Tier1, f.Rehearsal), "five units at one each");
        }

        // A wildcard on the stat is efficiency on everything that drinks: the
        // effect names no target and no currency, so it reaches every bar's
        // consumption coordinate (12.2).
        [Test]
        public void A_wildcard_consumption_effect_reaches_every_drinking_bar()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var first = f.Bar(group, "cover_a", 1000, 5);
            var second = f.Bar(group, "cover_b", 1000, 5);
            second.consumes.Clear();
            BarFixture.Drinks(second, f.Shared, 1);
            var thrifty = f.DeclareConsumption(f.Tier1Def, "thrifty", null, null, 0.5);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 100);
            f.Pour(f.Root, f.Shared, 100);
            f.Select(f.Tier1, group, first, second);
            f.Stack(f.Tier1, thrifty);

            f.Segment(1);

            AssertClose(97.5, f.Balance(f.Tier1, f.Rehearsal), "half of five");
            AssertClose(97.5, f.Balance(f.Root, f.Shared), "and half of the other five");
        }

        // ---- bars that fill from time ----

        [Test]
        public void A_bar_that_consumes_nothing_fills_from_time_alone()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "timers", null);
            var bar = f.Bar(group, "timer_a", 100, 3);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 7);
            f.Select(f.Tier1, group, bar);

            f.Segment(2);

            AssertClose(6, f.Progress(f.Tier1, bar), "progress");
            AssertClose(7, f.Balance(f.Tier1, f.Rehearsal), "nothing is drained");
        }

        [Test]
        public void A_bar_that_consumes_nothing_still_obeys_selection_availability_and_completion()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "timers", null);
            var unselected = f.Bar(group, "unselected", 100, 3);
            var unavailable = f.Bar(group, "unavailable", 100, 3);
            unavailable.availableWhen = new FlagSet { flagId = "encore" };
            var finished = f.Bar(group, "finished", 100, 3);
            var running = f.Bar(group, "running", 100, 3);
            f.Build();
            f.Tier1.barProgress[finished.Id] = 100;
            f.Select(f.Tier1, group, unavailable, finished, running);

            f.Segment(1);

            // With nothing to run dry, selection is the whole throttle - so it is
            // the one test a bar that drinks nothing must NOT skip.
            AssertClose(0, f.Progress(f.Tier1, unselected), "unselected");
            AssertClose(0, f.Progress(f.Tier1, unavailable), "gate closed");
            AssertClose(100, f.Progress(f.Tier1, finished), "already complete");
            AssertClose(3, f.Progress(f.Tier1, running), "selected and open");
        }

        // ---- settlement ----

        [Test]
        public void A_non_repeating_bar_fires_on_the_crossing_and_never_again()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 5, 5);
            f.Build();
            CountFires(bar, f.Shared);
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, bar);

            f.Segment(1);
            AssertClose(1, f.Balance(f.Root, f.Shared), "the crossing fires once");
            AssertClose(5, f.Progress(f.Tier1, bar), "progress");

            // Complete, it has nothing left to run, so it leaves the active set
            // and its slot is free for the next choice (12.7).
            Assert.IsFalse(f.Tier1.activeMembers[group.Id].Contains(bar.Id), "released on completion");

            // A second segment: the bar is complete, so it never draws again and
            // nothing crosses. No completed-set is stored and none is needed.
            f.Segment(1);
            AssertClose(1, f.Balance(f.Root, f.Shared), "no second fire");
            AssertClose(5, f.Progress(f.Tier1, bar), "and no second draw");
        }

        [Test]
        public void A_bar_loaded_at_full_progress_never_fires()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 5, 5);
            f.Build();
            CountFires(bar, f.Shared);
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Tier1.barProgress[bar.Id] = 5;      // a save taken the moment it completed
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            AssertClose(0, f.Balance(f.Root, f.Shared), "it was below nothing");
        }

        [Test]
        public void A_repeating_bar_settles_iteratively_and_keeps_the_residual()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "loops", f.Rehearsal);
            var bar = f.Bar(group, "loop_a", 10, 25, repeatWhen: new Always());
            f.Build();
            CountFires(bar, f.Shared);
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            AssertClose(2, f.Balance(f.Root, f.Shared), "two crossings pay twice");
            Assert.AreEqual(2, f.Fills(f.Tier1, bar), "fill count");
            AssertClose(5, f.Progress(f.Tier1, bar), "residual is retained");
        }

        [Test]
        public void The_empty_completion_shortcut_matches_the_iterative_result()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "loops", f.Rehearsal);
            var shortcut = f.Bar(group, "shortcut", 10, 35, repeatWhen: new Always());
            var iterative = f.Bar(group, "iterative", 10, 35, repeatWhen: new Always());
            f.Build();
            CountFires(iterative, f.Shared);
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, shortcut, iterative);

            f.Segment(1);

            Assert.AreEqual(3, f.Fills(f.Tier1, shortcut), "shortcut fill count");
            Assert.AreEqual(3, f.Fills(f.Tier1, iterative), "iterative fill count");
            AssertClose(5, f.Progress(f.Tier1, shortcut), "shortcut residual");
            AssertClose(5, f.Progress(f.Tier1, iterative), "iterative residual");
            AssertClose(3, f.Balance(f.Root, f.Shared), "only the iterative one has actions to run");
        }

        [Test]
        public void A_completion_that_closes_the_gate_stops_the_loop()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "loops", f.Rehearsal);
            var bar = f.Bar(group, "loop_a", 10, 100, repeatWhen: new Always());
            bar.availableWhen = new Not { condition = new FlagSet { flagId = "encore" } };
            f.Build();
            CountFires(bar, f.Shared);
            bar.onComplete.Add(new SetFlag { flagId = "encore" });
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            // Ten crossings were paid for; the first completion shuts the gate,
            // and the loop stops honestly instead of running precomputed fires.
            AssertClose(1, f.Balance(f.Root, f.Shared), "one fire");
            Assert.AreEqual(1, f.Fills(f.Tier1, bar), "fill count");
            AssertClose(90, f.Progress(f.Tier1, bar), "the rest stays as residual");
        }

        // A repeatWhen that refuses is the manual team (12.7): it pays once,
        // returns to zero and leaves the active set, so selecting it again is
        // the whole of how it runs a second cycle.
        [Test]
        public void A_manual_team_pays_once_returns_to_zero_and_leaves_the_active_set()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "teams", f.Rehearsal);
            var bar = f.Bar(group, "team_a", 10, 25, repeatWhen: new Not { condition = new Always() });
            f.Build();
            CountFires(bar, f.Shared);
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            AssertClose(1, f.Balance(f.Root, f.Shared), "one cycle, one payout");
            Assert.AreEqual(1, f.Fills(f.Tier1, bar), "a present repeatWhen counts its fills either way");
            AssertClose(0, f.Progress(f.Tier1, bar), "the excess past the threshold is discarded");
            Assert.IsFalse(f.Tier1.activeMembers[group.Id].Contains(bar.Id), "and it is no longer selected");
        }

        // A definition may be listed by several groups (12.7), and the manual
        // team's settlement leaves EVERY one of them - the member is off the
        // moment any group holding it lets go, so leaving one would be a
        // selection the player cannot see.
        [Test]
        public void A_manual_team_leaves_every_group_that_lists_it()
        {
            var f = new BarFixture();
            var teams = f.Group(f.Tier1Def, "teams", f.Rehearsal);
            var bar = f.Bar(teams, "team_a", 10, 25, repeatWhen: new Not { condition = new Always() });
            var tonight = f.Group(f.Tier1Def, "tonight", f.Rehearsal);
            tonight.members.Add(bar);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, teams, bar);
            f.Select(f.Tier1, tonight, bar);

            f.Segment(1);

            Assert.IsFalse(f.Tier1.activeMembers[teams.Id].Contains(bar.Id), "teams");
            Assert.IsFalse(f.Tier1.activeMembers[tonight.Id].Contains(bar.Id), "tonight");
        }

        // One condition, so the same bar is a team or a loop by what the player
        // has bought - which is what the repeat being a condition buys (12.7).
        [Test]
        public void A_repeatWhen_gated_on_an_upgrade_flips_a_team_from_manual_to_repeating()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "teams", f.Rehearsal);
            var overtime = f.Upgrade(f.Tier1Def, "overtime");
            var bar = f.Bar(group, "team_a", 10, 25, repeatWhen: new UpgradePurchased { upgrade = overtime });
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            AssertClose(0, f.Progress(f.Tier1, bar), "unbought, the condition refuses and one cycle ran");
            Assert.AreEqual(1, f.Fills(f.Tier1, bar), "fill count");
            Assert.IsFalse(f.Tier1.activeMembers[group.Id].Contains(bar.Id), "and it left the set");

            f.Tier1.purchasedUpgrades.Add(overtime.Id);
            f.Select(f.Tier1, group, bar);
            f.Segment(1);

            // The same bar over the same second: the condition holds now, so
            // both crossings pay and the residual is kept.
            Assert.AreEqual(3, f.Fills(f.Tier1, bar), "two more crossings");
            AssertClose(5, f.Progress(f.Tier1, bar), "residual is retained");
            Assert.IsTrue(f.Tier1.activeMembers[group.Id].Contains(bar.Id), "a repeating bar stays selected");
        }

        [Test]
        public void A_reset_during_settlement_drops_the_rest_of_that_scope_life()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var first = f.Bar(group, "first", 5, 10);
            var second = f.Bar(group, "second", 5, 10);
            // Authored before the build, like every scope reference: the link
            // pass resolves it there and Execute reads the link.
            first.onComplete.Add(new ResetScope { scope = f.Tier1Def });
            CountFires(second, f.Shared);          // homed at root, so it survives the reset
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, first, second);

            f.Segment(1);

            AssertClose(0, f.Balance(f.Root, f.Shared), "the second completion belongs to a dead scope-life");
            AssertClose(0, f.Progress(f.Tier1, second), "and its progress went with the payload");
        }

        [Test]
        public void Settlement_order_is_scopes_parent_before_child_then_bars_in_declaration_order()
        {
            var f = new BarFixture();
            var atRoot = f.Group(f.RootDef, "g_root", f.Shared);
            var chapterFirst = f.Group(f.Ch1Def, "g_ch1_a", f.Shared);
            var chapterSecond = f.Group(f.Ch1Def, "g_ch1_b", f.Shared);
            var atTier = f.Group(f.Tier1Def, "g_tier", f.Shared);
            var rootBar = f.Bar(atRoot, "bar_root", 100, 1);
            var ch1a = f.Bar(chapterFirst, "bar_ch1_a", 100, 1);
            var ch1b = f.Bar(chapterSecond, "bar_ch1_b", 100, 1);
            var third = f.Bar(atTier, "bar_third", 100, 1);
            var first = f.Bar(atTier, "bar_first", 100, 1);
            var second = f.Bar(atTier, "bar_second", 100, 1);
            f.Build();
            f.Select(f.Root, atRoot, rootBar);
            f.Select(f.Ch1, chapterFirst, ch1a);
            f.Select(f.Ch1, chapterSecond, ch1b);
            f.Select(f.Tier1, atTier, first, second, third);

            var order = new List<string>();
            foreach (var entry in f.Resolve().bars)
                order.Add(entry.bar.Id);

            // Scopes parent before child, then the scope's own bar list in
            // declaration order, whatever the ids sort as and whichever group
            // lists them.
            Assert.AreEqual(
                new[] { rootBar.Id, ch1a.Id, ch1b.Id, third.Id, first.Id, second.Id },
                order);
        }

        [Test]
        public void A_completion_action_stamps_the_segments_end_boundary()
        {
            var f = new BarFixture();
            var group = f.Group(f.Ch1Def, "covers", f.Shared);
            var bar = f.Bar(group, "cover_a", 5, 5);
            bar.onComplete.Add(new ResetScope { scope = f.Ch1Def });
            f.Build();
            f.Pour(f.Root, f.Shared, 1000);
            f.Ch1.lastActiveUtc = f.Now.AddHours(-4);
            f.Select(f.Ch1, group, bar);

            f.Segment(4);

            // A chapter reset re-stamps its idle clock from the context's clock,
            // and the segment END is the real boundary the tick advances to -
            // never anything derived from the scaled dt.
            Assert.AreEqual(f.Now.AddSeconds(4), f.Ch1.lastActiveUtc);
        }

        // ---- the snapshot seam ----

        [Test]
        public void A_deposit_between_the_two_calls_moves_the_balance_but_opens_no_gate()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var gated = f.Bar(group, "gated", 100, 5);
            gated.availableWhen = new CurrencyAtLeast { currency = f.Fans, threshold = 10 };
            var open = f.Bar(group, "open", 100, 5);
            f.Build();
            f.Select(f.Tier1, group, gated, open);

            var demand = f.Resolve();               // nothing banked, fans at zero
            f.Tier1.balances[f.Fans.Id] = 100;      // this segment's own production
            f.Tier1.balances[f.Rehearsal.Id] = 3;
            f.Settle(demand, 1);

            // The balance read is live by design; the RATE and the GATE are not.
            AssertClose(0, f.Progress(f.Tier1, gated), "a gate opened mid-segment does not draw");
            AssertClose(3, f.Progress(f.Tier1, open), "what it was fed is spent");
            AssertClose(0, f.Balance(f.Tier1, f.Rehearsal), "rehearsal");
        }

        [Test]
        public void A_backlogged_repeating_bar_whose_gate_opens_mid_segment_pays_nothing()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "loops", f.Rehearsal);
            var bar = f.Bar(group, "loop_a", 10, 5, repeatWhen: new Always());
            bar.availableWhen = new FlagSet { flagId = "encore" };
            f.Build();
            CountFires(bar, f.Shared);
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Tier1.barProgress[bar.Id] = 100;      // its own completion closed the gate last segment
            f.Select(f.Tier1, group, bar);

            var demand = f.Resolve();               // gate closed: not drawing, so not settling
            f.Tier1.flags.Add("encore");
            f.Settle(demand, 1);

            AssertClose(0, f.Balance(f.Root, f.Shared), "the backlog is not paid out");
            Assert.AreEqual(0, f.Fills(f.Tier1, bar), "fill count");
            AssertClose(100, f.Progress(f.Tier1, bar), "residual untouched");
        }

        // ---- degenerate numbers ----

        [Test]
        public void A_bar_whose_rate_resolves_to_zero_draws_nothing()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 100, 2);
            var silence = f.Declare(f.Tier1Def, "cover_a", 0);   // an event handicap is x0, and x0 is legal
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 50);
            f.Select(f.Tier1, group, bar);
            f.Stack(f.Tier1, silence);

            f.Segment(1);

            AssertClose(0, f.Progress(f.Tier1, bar), "progress");
            AssertClose(50, f.Balance(f.Tier1, f.Rehearsal), "rehearsal");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void A_repeating_bar_with_a_nonpositive_threshold_neither_draws_nor_settles(bool withActions)
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "loops", f.Rehearsal);
            var bar = f.Bar(group, "loop_a", 0, 5, repeatWhen: new Always());
            f.Build();
            if (withActions)
                CountFires(bar, f.Shared);
            f.Pour(f.Tier1, f.Rehearsal, 100);
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            // The drawing test is what protects the balance: settlement would
            // refuse to pay this bar, so admitting it would spend forever and
            // settle none of it.
            AssertClose(100, f.Balance(f.Tier1, f.Rehearsal), "rehearsal");
            AssertClose(0, f.Progress(f.Tier1, bar), "progress");
            Assert.AreEqual(0, f.Fills(f.Tier1, bar), "fill count");
            AssertClose(0, f.Balance(f.Root, f.Shared), "nothing fired");
        }

        // ---- a payment into a bar (12.7) ----

        // A yield settles at the WRITE: the crossing fires the moment the
        // payment lands, because the tick only settles the bars its draw
        // admitted and would read a bar filled between ticks as one that fired
        // earlier. A non-repeating bar keeps no excess - a tap cannot overshoot
        // full.
        [Test]
        public void A_yield_into_a_non_repeating_bar_clamps_at_full_and_fires_the_crossing()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 100, 0);
            f.Build();
            CountFires(bar, f.Shared);
            f.Select(f.Tier1, group, bar);

            BarSystem.Deposit(new GameContext(f.Tier1, f.Now), bar, 250);

            AssertClose(100, f.Progress(f.Tier1, bar), "clamped at the fill amount");
            AssertClose(1, f.Balance(f.Root, f.Shared), "the crossing fired at the write");

            // Already full: the next payment crosses nothing.
            BarSystem.Deposit(new GameContext(f.Tier1, f.Now), bar, 100);

            AssertClose(100, f.Progress(f.Tier1, bar), "progress");
            AssertClose(1, f.Balance(f.Root, f.Shared), "no second fire");
        }

        // Selection governs DRINKING, and a payment is not a drink (12.7), so an
        // unselected bar takes what is paid into it and settles.
        [Test]
        public void A_yield_into_an_unselected_bar_still_fires_its_crossing()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 100, 0);
            f.Build();
            CountFires(bar, f.Shared);

            BarSystem.Deposit(new GameContext(f.Tier1, f.Now), bar, 100);

            AssertClose(100, f.Progress(f.Tier1, bar), "progress");
            AssertClose(1, f.Balance(f.Root, f.Shared), "selection gates the draw, not the payment");
        }

        [Test]
        public void A_yield_into_a_repeating_bar_settles_every_crossing_and_keeps_the_excess()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "loops", f.Rehearsal);
            var bar = f.Bar(group, "loop_a", 10, 0, repeatWhen: new Always());
            f.Build();
            CountFires(bar, f.Shared);
            f.Select(f.Tier1, group, bar);

            BarSystem.Deposit(new GameContext(f.Tier1, f.Now), bar, 25);

            AssertClose(2, f.Balance(f.Root, f.Shared), "two thresholds crossed");
            Assert.AreEqual(2, f.Fills(f.Tier1, bar), "fill count");
            AssertClose(5, f.Progress(f.Tier1, bar), "the residual is retained");
        }

        // The clamp belongs to the bar that completes once, whose progress is
        // monotonic (12.7). A manual team takes the whole payment and then runs
        // its one cycle, so the overshoot goes with the return to zero.
        [Test]
        public void A_payment_past_full_into_a_manual_team_pays_once_and_zeroes()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "teams", f.Rehearsal);
            var bar = f.Bar(group, "team_a", 100, 0, repeatWhen: new Not { condition = new Always() });
            f.Build();
            CountFires(bar, f.Shared);
            f.Select(f.Tier1, group, bar);

            BarSystem.Deposit(new GameContext(f.Tier1, f.Now), bar, 250);

            AssertClose(1, f.Balance(f.Root, f.Shared), "one crossing, settled at the write");
            Assert.AreEqual(1, f.Fills(f.Tier1, bar), "fill count");
            AssertClose(0, f.Progress(f.Tier1, bar), "back to zero, the excess discarded");
            Assert.IsFalse(f.Tier1.activeMembers[group.Id].Contains(bar.Id), "and out of the active set");
        }

        // Each mover settles only the crossing its own fill made (12.7). Here the
        // first bar's completion fires a generator yield that fills the second
        // bar past full inside the tick's own settlement: the payment settles
        // that crossing once, and the pass, reaching the second bar with a draw
        // that crossed nothing, fires nothing more.
        [Test]
        public void A_payment_landing_inside_a_settlement_fires_its_crossing_once()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "teams", null);
            var lead = f.Bar(group, "lead", 10, 1);
            var follow = f.Bar(group, "follow", 100, 1);
            CountFires(follow, f.Shared);

            var crew = TestTree.MakeDefinition<GeneratorDefinition>("crew");
            crew.availableWhen = new Always();
            crew.produces.Add(TestTree.Entry(follow, Stat.Yield, 100));
            f.Tier1Def.generators.Add(crew);
            lead.onComplete.Add(new FireGeneratorYield { generator = crew });
            f.Build();
            f.Tier1.generatorCounts["crew"] = 1;
            f.Select(f.Tier1, group, lead, follow);

            // Ten seconds: lead crosses its 10, follow's own draw reaches 10 of
            // 100, and lead's completion pays follow the other 90 and more.
            f.Segment(10);

            AssertClose(100, f.Progress(f.Tier1, follow), "paid to full and clamped");
            AssertClose(1, f.Balance(f.Root, f.Shared), "one crossing, one reward");
        }

        [Test]
        public void A_negative_payment_into_a_bar_throws()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 100, 0);
            f.Build();

            Assert.Throws<InvalidOperationException>(
                () => BarSystem.Deposit(new GameContext(f.Tier1, f.Now), bar, -1));
            Assert.IsFalse(f.Tier1.barProgress.ContainsKey(bar.Id));
        }

        // A refused completion list leaves the payment undelivered (12.5),
        // exactly as the draw excludes that bar for the whole segment. The
        // standing tree is what has an event to arm: only an armed reward
        // refuses a clear.
        [Test]
        public void A_refused_completion_leaves_a_payment_into_the_bar_undelivered()
        {
            var tree = new TestTree();
            tree.Cover1.onComplete.Add(new ResetScope { scope = tree.Tier1Def });
            tree.Rebuild();
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "open_mic", goalReached = true };

            BarSystem.Deposit(tree.Ctx(tree.Tier1), tree.Cover1, 100);

            Assert.IsFalse(tree.Tier1.barProgress.ContainsKey("cover_1"), "no progress was written");
            Assert.IsFalse(tree.Tier1.modifierStacks.ContainsKey("cover_bonus_1"), "and nothing fired");

            // The reward claimed, the same payment lands and the completion runs.
            var facts = tree.Tier1.facts;
            tree.Tier1.activeEvent = null;
            BarSystem.Deposit(tree.Ctx(tree.Tier1), tree.Cover1, 100);

            Assert.AreNotSame(facts, tree.Tier1.facts, "the completion ran, and its reset took the payload");
        }

        // ---- SetActiveMembers ----

        [Test]
        public void SetActiveMembers_writes_the_set_at_the_groups_declaring_scope()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal, 2);
            var a = f.Bar(group, "cover_a", 100, 2);
            var b = f.Bar(group, "cover_b", 100, 2);
            f.Build();

            // Asked from a DESCENDANT of nothing - the acting scope is the tier
            // itself here, but the write lands by declaration either way.
            Assert.IsTrue(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new[] { a, b }));

            Assert.AreEqual(new HashSet<string> { a.Id, b.Id }, f.Tier1.activeMembers[group.Id]);
        }

        // The command takes the whole set, so adding and removing are the same
        // write with a different list - which is what lets the row's press be a
        // toggle with no second entry point (12.7).
        [Test]
        public void SetActiveMembers_adds_removes_and_refuses_into_a_full_group()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal, 1);
            var a = f.Bar(group, "cover_a", 100, 2);
            var b = f.Bar(group, "cover_b", 100, 2);
            f.Build();

            Assert.IsTrue(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new[] { a }));
            Assert.AreEqual(new HashSet<string> { a.Id }, f.Tier1.activeMembers[group.Id], "added");

            Assert.IsFalse(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new[] { a, b }),
                "the group is full, so the player deselects first");
            Assert.AreEqual(new HashSet<string> { a.Id }, f.Tier1.activeMembers[group.Id], "a refusal changes nothing");

            Assert.IsTrue(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new Definition[0]));
            Assert.IsEmpty(f.Tier1.activeMembers[group.Id], "removed");
        }

        [Test]
        public void SetActiveMembers_resolves_the_group_outward_from_the_acting_scope()
        {
            var f = new BarFixture();
            var group = f.Group(f.Ch1Def, "covers", f.Shared);
            var bar = f.Bar(group, "cover_a", 100, 2);
            f.Build();

            // Acting at the tier, group declared at the chapter: the outward walk
            // finds it, and the fact lands at the chapter that owns it.
            Assert.IsTrue(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new[] { bar }));

            Assert.AreEqual(new HashSet<string> { bar.Id }, f.Ch1.activeMembers[group.Id]);
            Assert.IsFalse(f.Tier1.activeMembers.ContainsKey(group.Id));
        }

        [Test]
        public void SetActiveMembers_collapses_duplicates_before_counting_them()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal, 1);
            var bar = f.Bar(group, "cover_a", 100, 2);
            f.Build();

            Assert.IsTrue(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new[] { bar, bar }));
            Assert.AreEqual(1, f.Tier1.activeMembers[group.Id].Count);
        }

        [Test]
        public void SetActiveMembers_refuses_a_member_outside_the_group()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var mine = f.Bar(group, "cover_a", 100, 2);
            var other = f.Group(f.Tier1Def, "others", f.Rehearsal);
            var theirs = f.Bar(other, "other_a", 100, 2);
            f.Build();

            Assert.IsFalse(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new[] { mine, theirs }));
            Assert.IsFalse(f.Tier1.activeMembers.ContainsKey(group.Id), "all or nothing");
        }

        [Test]
        public void SetActiveMembers_refuses_an_unavailable_bar()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 100, 2);
            bar.availableWhen = new FlagSet { flagId = "encore" };
            f.Build();

            Assert.IsFalse(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new[] { bar }));

            f.Tier1.flags.Add("encore");
            Assert.IsTrue(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new[] { bar }),
                "the same call succeeds once the gate opens");
        }

        [Test]
        public void SetActiveMembers_refuses_a_completed_non_repeating_bar_but_not_a_repeating_one()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var once = f.Bar(group, "once", 100, 2);
            var loop = f.Bar(group, "loop", 100, 2, repeatWhen: new Always());
            f.Build();
            f.Tier1.barProgress[once.Id] = 100;
            f.Tier1.barProgress[loop.Id] = 100;

            Assert.IsFalse(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new[] { once }));
            Assert.IsTrue(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new[] { loop }),
                "a repeating bar at full progress is between fills, not finished");
        }

        [Test]
        public void SetActiveMembers_refuses_a_null_member_and_a_null_list()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 100, 2);
            f.Build();

            Assert.IsFalse(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, null));
            Assert.IsFalse(BarSystem.SetActiveMembers(new GameContext(f.Tier1, f.Now), group, new[] { bar, null }));
            Assert.IsFalse(f.Tier1.activeMembers.ContainsKey(group.Id));
        }

        [Test]
        public void SetActiveMembers_throws_on_a_group_off_the_acting_chain()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            f.Bar(group, "cover_a", 100, 2);
            f.Build();

            // Asked from the CHAPTER, which cannot see its own tier's
            // declarations: content or a caller bug, not a state the player made.
            Assert.Throws<InvalidOperationException>(
                () => BarSystem.SetActiveMembers(new GameContext(f.Ch1, f.Now), group, new Definition[0]));
        }
    }
}
