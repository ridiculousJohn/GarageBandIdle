using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The smallest tree a draw needs: root -> ch1 -> tier1, with one pool homed
    // at tier1 and one at root for the shared-pool cases. Each test authors its
    // own groups and THEN builds the state tree, because ScopeState.Build
    // initializes declared facts from the definitions.
    internal class BarFixture
    {
        public readonly DateTime Now = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

        public readonly RootDefinition RootDef;
        public readonly ChapterDefinition Ch1Def;
        public readonly TierDefinition Tier1Def;
        public readonly CurrencyDefinition Rehearsal;   // tier1's own pool
        public readonly CurrencyDefinition Shared;      // root's, so every chapter draws the same one
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
            Ch1 = (ChapterScopeState)Root.FindInSubtree(Ch1Def);
            Tier1 = Root.FindInSubtree(Tier1Def);
        }

        // A bar names what it drinks. Most tests give a group's bars the same
        // currency, so the fixture takes it once here as the DEFAULT for members;
        // a test that needs otherwise assigns `bar.fillCurrency` itself. Pass
        // null for bars that fill from time alone.
        public BarGroupDefinition Group(ScopeDefinition scope, string id, CurrencyDefinition pool,
                                        int maxActive = 4)
        {
            var group = TestTree.MakeDefinition<BarGroupDefinition>(id);
            group.maxActive = maxActive;
            scope.barGroups.Add(group);
            pools[group] = pool;
            return group;
        }

        private readonly Dictionary<BarGroupDefinition, CurrencyDefinition> pools = new();

        public BarDefinition Bar(BarGroupDefinition group, string id, double fillAmount, double fillRate,
                                 bool repeating = false)
        {
            var bar = TestTree.MakeDefinition<BarDefinition>(id);
            bar.fillCurrency = pools[group];
            bar.fillAmount = fillAmount;
            bar.fillRate = fillRate;
            bar.repeating = repeating;
            group.bars.Add(bar);
            return bar;
        }

        // Selection written as a FACT, bypassing the entry point: a draw test is
        // not a SetActiveBars test.
        public void Select(ScopeState scope, BarGroupDefinition group, params BarDefinition[] bars)
        {
            var set = new HashSet<string>();
            foreach (var bar in bars)
                set.Add(bar.Id);
            scope.activeBars[group.Id] = set;
        }

        public void Grant(ScopeState scope, string target, double multiplier)
        {
            var modifier = TestTree.MakeDefinition<ModifierDefinition>("mod_" + target + "_" + multiplier);
            modifier.effects.Add(new Effect { target = target, stat = Stat.Rate, multiplier = multiplier });
            scope.Definition.modifiers.Add(modifier);
            scope.modifierStacks[modifier.Id] = 1;
        }

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
            BarSystem.ConsumeAndSettle(demand, dt, Now.AddSeconds(dt));

        public void Segment(double dt) => Settle(Resolve(), dt);
    }

    // The consumption half of the economy (design doc 12.7): what a bar drinks,
    // what happens when its pool runs short, and what a crossing fires.
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
        // declaration order until the pool runs out. The total delivered is the
        // same either way - it is whatever was in the pool - so the only
        // difference is that one bar visibly moves instead of three inching.
        [Test]
        public void A_short_pool_feeds_bars_in_declaration_order_until_it_runs_out()
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
            AssertClose(0, f.Balance(f.Tier1, f.Rehearsal), "pool");
        }

        // The rule the whole shape turns on: a bar names its OWN currency, so one
        // group holds bars drinking different things - including one that drinks
        // nothing. An implementation resolving one currency per GROUP would pass
        // every other test in this file.
        [Test]
        public void Bars_in_one_group_may_name_different_currencies()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "mixed", f.Rehearsal);
            var drinksRehearsal = f.Bar(group, "drinks_rehearsal", 1000, 3);
            var drinksShared = f.Bar(group, "drinks_shared", 1000, 5);
            drinksShared.fillCurrency = f.Shared;       // homed at root, not tier1
            var drinksNothing = f.Bar(group, "drinks_nothing", 1000, 7);
            drinksNothing.fillCurrency = null;          // fills from time alone
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 100);
            f.Pour(f.Root, f.Shared, 100);
            f.Select(f.Tier1, group, drinksRehearsal, drinksShared, drinksNothing);

            f.Segment(2);

            AssertClose(6, f.Progress(f.Tier1, drinksRehearsal), "drinks rehearsal");
            AssertClose(10, f.Progress(f.Tier1, drinksShared), "drinks shared");
            AssertClose(14, f.Progress(f.Tier1, drinksNothing), "drinks nothing");

            // Each spent from its OWN home, and the time-filled one spent nothing.
            AssertClose(94, f.Balance(f.Tier1, f.Rehearsal), "rehearsal pool");
            AssertClose(90, f.Balance(f.Root, f.Shared), "shared pool");
        }

        // One pool, two groups at different scopes: the currency's balance is the
        // only thing arbitrating, and it needs no group-level bookkeeping to do it.
        [Test]
        public void Bars_in_different_groups_draw_the_same_pool_in_tree_order()
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
            AssertClose(0, f.Balance(f.Root, f.Shared), "pool");
        }

        [Test]
        public void An_exhausted_pool_pays_what_it_has_and_no_more()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 1000, 5);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 1);
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            AssertClose(1, f.Progress(f.Tier1, bar), "progress");
            AssertClose(0, f.Balance(f.Tier1, f.Rehearsal), "pool");
        }

        [Test]
        public void A_fill_spends_the_pool_without_touching_its_earned_total()
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
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, bar);
            f.Grant(f.Tier1, "cover_a", 3);

            f.Segment(1);
            AssertClose(6, f.Progress(f.Tier1, bar), "per-bar speed is buffable by id or tag");
        }

        [Test]
        public void A_currency_total_buff_never_speeds_the_drain_it_supplies()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 1000, 2);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, bar);
            f.Grant(f.Tier1, "rehearsal", 3);       // an effect on the POOL currency's total

            f.Segment(1);

            // The rate resolves stage 1 only. Stage 2 is "effects on this
            // currency's total production", and a bar consumes rather than
            // produces - letting it through would speed the drain as well as the
            // supply, which is not what either buff means.
            AssertClose(2, f.Progress(f.Tier1, bar), "unchanged");
        }

        [Test]
        public void An_effect_may_narrow_to_a_bars_own_currency_and_one_without_has_none()
        {
            var f = new BarFixture();
            var poured = f.Group(f.Tier1Def, "poured", f.Rehearsal);
            var drinker = f.Bar(poured, "drinker", 1000, 2);
            var timed = f.Group(f.Tier1Def, "timed", null);
            var ticker = f.Bar(timed, "ticker", 1000, 2);
            f.Build();
            var modifier = TestTree.MakeDefinition<ModifierDefinition>("narrowed");
            modifier.effects.Add(new Effect { target = "rehearsal_fill", currencyId = "rehearsal", stat = Stat.Rate, multiplier = 4 });
            f.Tier1Def.declaredTags.Add("rehearsal_fill");
            drinker.EditorInit("drinker", "rehearsal_fill");
            ticker.EditorInit("ticker", "rehearsal_fill");
            f.Tier1Def.modifiers.Add(modifier);
            f.Tier1.modifierStacks[modifier.Id] = 1;
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, poured, drinker);
            f.Select(f.Tier1, timed, ticker);

            f.Segment(1);

            // The same effect, the same tag: it narrows to a currency, so it
            // reaches the bar that drinks it and nothing at all in the bar that
            // fills from time.
            AssertClose(8, f.Progress(f.Tier1, drinker), "narrowed to the pool it drinks");
            AssertClose(2, f.Progress(f.Tier1, ticker), "no currency, so no coordinate to match");
        }

        // ---- bars that fill from time ----

        [Test]
        public void A_bar_with_no_currency_fills_from_time_alone()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "timers", null);
            var bar = f.Bar(group, "timer_a", 100, 3);
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 7);
            f.Select(f.Tier1, group, bar);

            f.Segment(2);

            AssertClose(6, f.Progress(f.Tier1, bar), "progress");
            AssertClose(7, f.Balance(f.Tier1, f.Rehearsal), "no pool is drained");
        }

        [Test]
        public void A_bar_with_no_currency_still_obeys_selection_availability_and_completion()
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

            // With no pool to run dry, selection is the whole throttle - so it is
            // the one test a currency-free bar must NOT skip.
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
            var bar = f.Bar(group, "loop_a", 10, 25, repeating: true);
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
            var shortcut = f.Bar(group, "shortcut", 10, 35, repeating: true);
            var iterative = f.Bar(group, "iterative", 10, 35, repeating: true);
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
            var bar = f.Bar(group, "loop_a", 10, 100, repeating: true);
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

        [Test]
        public void A_reset_during_settlement_drops_the_rest_of_that_scope_life()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var first = f.Bar(group, "first", 5, 10);
            var second = f.Bar(group, "second", 5, 10);
            f.Build();
            first.onComplete.Add(new ResetScope { scope = f.Tier1Def });
            CountFires(second, f.Shared);          // homed at root, so it survives the reset
            f.Pour(f.Tier1, f.Rehearsal, 1000);
            f.Select(f.Tier1, group, first, second);

            f.Segment(1);

            AssertClose(0, f.Balance(f.Root, f.Shared), "the second completion belongs to a dead scope-life");
            AssertClose(0, f.Progress(f.Tier1, second), "and its progress went with the payload");
        }

        [Test]
        public void Settlement_order_is_scopes_then_groups_then_bars_in_declaration_order()
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

            // Scopes parent before child, then groups, then bars - all in
            // declaration order, whatever the ids sort as.
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
            f.Build();
            bar.onComplete.Add(new ResetScope { scope = f.Ch1Def });
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
        public void A_deposit_between_the_two_calls_moves_the_pool_but_opens_no_gate()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var gated = f.Bar(group, "gated", 100, 5);
            gated.availableWhen = new CurrencyAtLeast { currency = f.Fans, threshold = 10 };
            var open = f.Bar(group, "open", 100, 5);
            f.Build();
            f.Select(f.Tier1, group, gated, open);

            var demand = f.Resolve();               // pool empty, fans at zero
            f.Tier1.balances[f.Fans.Id] = 100;      // this segment's own production
            f.Tier1.balances[f.Rehearsal.Id] = 3;
            f.Settle(demand, 1);

            // The balance read is live by design; the RATE and the GATE are not.
            AssertClose(0, f.Progress(f.Tier1, gated), "a gate opened mid-segment does not draw");
            AssertClose(3, f.Progress(f.Tier1, open), "the pool it was fed is spent");
            AssertClose(0, f.Balance(f.Tier1, f.Rehearsal), "pool");
        }

        [Test]
        public void A_backlogged_repeating_bar_whose_gate_opens_mid_segment_pays_nothing()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "loops", f.Rehearsal);
            var bar = f.Bar(group, "loop_a", 10, 5, repeating: true);
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
            f.Build();
            f.Pour(f.Tier1, f.Rehearsal, 50);
            f.Select(f.Tier1, group, bar);
            f.Grant(f.Tier1, "cover_a", 0);         // an event handicap is x0, and x0 is legal

            f.Segment(1);

            AssertClose(0, f.Progress(f.Tier1, bar), "progress");
            AssertClose(50, f.Balance(f.Tier1, f.Rehearsal), "pool");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void A_repeating_bar_with_a_nonpositive_threshold_neither_draws_nor_settles(bool withActions)
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "loops", f.Rehearsal);
            var bar = f.Bar(group, "loop_a", 0, 5, repeating: true);
            f.Build();
            if (withActions)
                CountFires(bar, f.Shared);
            f.Pour(f.Tier1, f.Rehearsal, 100);
            f.Select(f.Tier1, group, bar);

            f.Segment(1);

            // The drawing test is what protects the pool: settlement would refuse
            // to pay this bar, so admitting it would spend forever and settle
            // none of it.
            AssertClose(100, f.Balance(f.Tier1, f.Rehearsal), "pool");
            AssertClose(0, f.Progress(f.Tier1, bar), "progress");
            Assert.AreEqual(0, f.Fills(f.Tier1, bar), "fill count");
            AssertClose(0, f.Balance(f.Root, f.Shared), "nothing fired");
        }

        // ---- SetActiveBars ----

        [Test]
        public void SetActiveBars_writes_the_set_at_the_groups_declaring_scope()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal, 2);
            var a = f.Bar(group, "cover_a", 100, 2);
            var b = f.Bar(group, "cover_b", 100, 2);
            f.Build();

            // Asked from a DESCENDANT of nothing - the acting scope is the tier
            // itself here, but the write lands by declaration either way.
            Assert.IsTrue(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, new[] { a, b }));

            Assert.AreEqual(new HashSet<string> { a.Id, b.Id }, f.Tier1.activeBars[group.Id]);
        }

        [Test]
        public void SetActiveBars_resolves_the_group_outward_from_the_acting_scope()
        {
            var f = new BarFixture();
            var group = f.Group(f.Ch1Def, "covers", f.Shared);
            var bar = f.Bar(group, "cover_a", 100, 2);
            f.Build();

            // Acting at the tier, group declared at the chapter: the outward walk
            // finds it, and the fact lands at the chapter that owns it.
            Assert.IsTrue(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, new[] { bar }));

            Assert.AreEqual(new HashSet<string> { bar.Id }, f.Ch1.activeBars[group.Id]);
            Assert.IsFalse(f.Tier1.activeBars.ContainsKey(group.Id));
        }

        [Test]
        public void SetActiveBars_collapses_duplicates_before_counting_them()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal, 1);
            var bar = f.Bar(group, "cover_a", 100, 2);
            f.Build();

            Assert.IsTrue(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, new[] { bar, bar }));
            Assert.AreEqual(1, f.Tier1.activeBars[group.Id].Count);
        }

        [Test]
        public void SetActiveBars_clears_the_selection_on_an_empty_set()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 100, 2);
            f.Build();
            f.Select(f.Tier1, group, bar);

            Assert.IsTrue(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, new BarDefinition[0]));
            Assert.AreEqual(0, f.Tier1.activeBars[group.Id].Count);
        }

        [Test]
        public void SetActiveBars_refuses_a_set_over_maxActive()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal, 1);
            var a = f.Bar(group, "cover_a", 100, 2);
            var b = f.Bar(group, "cover_b", 100, 2);
            f.Build();
            f.Select(f.Tier1, group, a);

            Assert.IsFalse(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, new[] { a, b }));
            Assert.AreEqual(new HashSet<string> { a.Id }, f.Tier1.activeBars[group.Id], "a refusal changes nothing");
        }

        [Test]
        public void SetActiveBars_refuses_a_bar_outside_the_group()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var mine = f.Bar(group, "cover_a", 100, 2);
            var other = f.Group(f.Tier1Def, "others", f.Rehearsal);
            var theirs = f.Bar(other, "other_a", 100, 2);
            f.Build();

            Assert.IsFalse(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, new[] { mine, theirs }));
            Assert.IsFalse(f.Tier1.activeBars.ContainsKey(group.Id), "all or nothing");
        }

        [Test]
        public void SetActiveBars_refuses_an_unavailable_bar()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 100, 2);
            bar.availableWhen = new FlagSet { flagId = "encore" };
            f.Build();

            Assert.IsFalse(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, new[] { bar }));

            f.Tier1.flags.Add("encore");
            Assert.IsTrue(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, new[] { bar }),
                "the same call succeeds once the gate opens");
        }

        [Test]
        public void SetActiveBars_refuses_a_completed_non_repeating_bar_but_not_a_repeating_one()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var once = f.Bar(group, "once", 100, 2);
            var loop = f.Bar(group, "loop", 100, 2, repeating: true);
            f.Build();
            f.Tier1.barProgress[once.Id] = 100;
            f.Tier1.barProgress[loop.Id] = 100;

            Assert.IsFalse(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, new[] { once }));
            Assert.IsTrue(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, new[] { loop }),
                "a repeating bar at full progress is between fills, not finished");
        }

        [Test]
        public void SetActiveBars_refuses_a_null_bar_and_a_null_list()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            var bar = f.Bar(group, "cover_a", 100, 2);
            f.Build();

            Assert.IsFalse(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, null));
            Assert.IsFalse(BarSystem.SetActiveBars(new GameContext(f.Tier1, f.Now), group, new[] { bar, null }));
            Assert.IsFalse(f.Tier1.activeBars.ContainsKey(group.Id));
        }

        [Test]
        public void SetActiveBars_throws_on_a_group_off_the_acting_chain()
        {
            var f = new BarFixture();
            var group = f.Group(f.Tier1Def, "covers", f.Rehearsal);
            f.Bar(group, "cover_a", 100, 2);
            f.Build();

            // Asked from the CHAPTER, which cannot see its own tier's
            // declarations: content or a caller bug, not a state the player made.
            Assert.Throws<InvalidOperationException>(
                () => BarSystem.SetActiveBars(new GameContext(f.Ch1, f.Now), group, new BarDefinition[0]));
        }
    }
}
