using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // TickSystem called directly, no session: the segment boundaries, the fixed
    // phase order inside one segment, the game_speed clamp, and the guards.
    public class TickSystemTests
    {
        // Built inline the way tests build every asset; the real one is step 8's.
        private static GameConfig Config(double maxGameSpeed = 4)
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.maxGameSpeed = maxGameSpeed;
            return config;
        }

        // A rate source with binary-exact numbers and NO tags, so the authored
        // income and production factors stay out of the arithmetic.
        private static ProducerDefinition AddRateSource(ScopeDefinition scope, string id,
            CurrencyDefinition currency, double rate, Condition condition = null)
        {
            var producer = TestTree.MakeDefinition<ProducerDefinition>(id);
            producer.produces.Add(TestTree.Entry(currency, Stat.Rate, rate, condition));
            scope.producers.Add(producer);
            return producer;
        }

        // A wildcard game_speed carrier DECLARED at the chapter - the tick's
        // owner-less read collects it from the foreground chain. Declaration
        // and grant are two steps because the gather is compiled when the tree
        // is built: the carrier has to stand before the rebuild, and the stack
        // is a fact written onto the node that rebuild left.
        private static void DeclareSpeed(TestTree tree, string id, double multiplier)
        {
            var carrier = TestTree.MakeDefinition<ModifierDefinition>(id);
            carrier.effects.Add(new Effect { stat = Stat.GameSpeed, multiplier = multiplier });
            tree.Ch1Def.modifiers.Add(carrier);
        }

        private static void StackSpeed(TestTree tree, params string[] ids)
        {
            foreach (var id in ids)
                tree.Ch1.modifierStacks[id] = 1;
        }

        // Encore's own shape: root declares the timer and a wildcard game_speed
        // carrier applied at ROOT reads it. The tick reads speed from the
        // foreground chapter outward, so a root record reaches whichever chapter
        // is in front.
        private static void DeclareEncore(TestTree tree) => TestTree.DeclareEncore(tree.RootDef);

        // The ladder's second rung, authored on root over the SAME timer with a
        // 24-hour band (section 9): it counts only while more than that remains,
        // and the moment it stops counting is an edge the walk cuts like an
        // expiry. Nothing in code names it.
        private static void DeclareBandedTier(TestTree tree)
        {
            var tier = TestTree.MakeDefinition<ModifierDefinition>("encore_4x");
            tier.timer = "encore_timer";
            tier.activeAfterSeconds = 86400;
            tier.effects.Add(new Effect { stat = Stat.GameSpeed, multiplier = 2 });
            tier.appliesWhen = new BuffActive { modifier = tier };
            tree.RootDef.modifiers.Add(tier);
            tree.RootDef.permanentModifiers.Add(tier);
        }

        private static void Record(TestTree tree, double expiresInSeconds) =>
            tree.Root.timedBuffs.Add(new TimedBuff
                { buffId = "encore_timer", expiresAtUtc = tree.Now.AddSeconds(expiresInSeconds) });

        // ---- rate production ----

        [Test]
        public void Rate_deposits_land_at_their_homes_scaled_by_dt()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "tier_press", tree.Fans, 0.5);
            AddRateSource(tree.Ch1Def, "ch1_press", tree.Ch1Records, 2);
            tree.Rebuild();

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));

            Assert.AreEqual((BigNumber)5, tree.Tier1.balances["fans"]);
            Assert.AreEqual((BigNumber)5, tree.Tier1.earnedTotals["fans"]);
            Assert.AreEqual((BigNumber)20, tree.Ch1.balances["ch1_records"]);
        }

        [Test]
        public void Sizing_is_judged_against_pre_deposit_state()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "base_press", tree.Fans, 1);
            AddRateSource(tree.Tier1Def, "bonus_press", tree.Fans, 1,
                new EarnedTotalAtLeast { currency = tree.Fans, threshold = 5 });
            tree.Rebuild();

            // The base entry's own deposit crosses the threshold mid-tick, but
            // the gated entry was judged at segment start and pays nothing yet.
            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));
            Assert.AreEqual((BigNumber)10, tree.Tier1.earnedTotals["fans"]);

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(20));
            Assert.AreEqual((BigNumber)30, tree.Tier1.earnedTotals["fans"]);
        }

        // ---- targets other than a currency (12.2) ----

        // A generator is a rate target like any other: the phase deposits into
        // its granted count and records the deposit, so the row's count follows
        // the same slope a currency readout does (12.11).
        [Test]
        public void The_tick_pays_a_generator_target_and_records_the_deposit()
        {
            var tree = new TestTree();
            var crew = TestTree.MakeDefinition<ProducerDefinition>("road_crew");
            crew.produces.Add(TestTree.Entry(tree.PracticeAmp, Stat.Rate, 0.25));
            tree.Tier1Def.producers.Add(crew);
            tree.Rebuild();

            var report = TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));

            Assert.AreEqual((BigNumber)2.5, tree.Tier1.grantedCounts["practice_amp"]);
            Assert.AreEqual((BigNumber)2.5, report.DepositNet(tree.Tier1, "practice_amp"), "the deposit is recorded at the home");
        }

        // A rate paid into a bar is collected by the BAR at its own draw (12.7),
        // beside its fillRate - so a bar fed 3/s on top of its own 1/s fills at
        // four, and the rate phase never deposits into it.
        [Test]
        public void A_rate_into_a_bar_is_collected_at_its_draw_beside_its_own_fill_rate()
        {
            var tree = new TestTree();
            var drill = TestTree.MakeDefinition<BarDefinition>("drill");
            drill.fillAmount = 1000;
            drill.fillRate = 1;
            tree.LearnCovers.bars.Add(drill);
            var crew = TestTree.MakeDefinition<ProducerDefinition>("road_crew");
            crew.produces.Add(TestTree.Entry(drill, Stat.Rate, 3));
            tree.Tier1Def.producers.Add(crew);
            tree.Rebuild();
            tree.Tier1.activeBars["learn_covers"] = new HashSet<string> { "drill" };

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));

            Assert.AreEqual((BigNumber)40, tree.Tier1.barProgress["drill"], "its own 1/s plus the 3/s paid in");
        }

        [Test]
        public void An_unselected_bar_collects_nothing_paid_into_it()
        {
            var tree = new TestTree();
            var drill = TestTree.MakeDefinition<BarDefinition>("drill");
            drill.fillAmount = 1000;
            drill.fillRate = 1;
            tree.LearnCovers.bars.Add(drill);
            var crew = TestTree.MakeDefinition<ProducerDefinition>("road_crew");
            crew.produces.Add(TestTree.Entry(drill, Stat.Rate, 3));
            tree.Tier1Def.producers.Add(crew);
            tree.Rebuild();

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));

            // The draw is what a rate into a bar arrives through, and the draw
            // admits the selected bars alone.
            Assert.IsFalse(tree.Tier1.barProgress.ContainsKey("drill"));
        }

        // ---- phase order ----

        [Test]
        public void Production_precedes_consumption_within_one_tick()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "riff_press", tree.Rehearsal, 1);
            tree.Rebuild();
            tree.Tier1.activeBars["learn_covers"] = new HashSet<string> { "cover_1" };

            // The pool starts empty; the bar drinks this tick's own deposit.
            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));

            Assert.AreEqual((BigNumber)10, tree.Tier1.barProgress["cover_1"]);
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["rehearsal"]);
        }

        [Test]
        public void Demand_precedes_deposits_within_one_segment()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "riff_press", tree.Rehearsal, 1);
            tree.Cover1.availableWhen = new CurrencyAtLeast { currency = tree.Rehearsal, threshold = 1 };
            tree.Rebuild();
            tree.Tier1.activeBars["learn_covers"] = new HashSet<string> { "cover_1" };

            // The gate this tick's deposits open was judged closed in the
            // snapshot, so the bar draws nothing until the next tick sees it.
            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));
            Assert.IsFalse(tree.Tier1.barProgress.ContainsKey("cover_1"));
            Assert.AreEqual((BigNumber)10, tree.Tier1.balances["rehearsal"]);

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(20));
            Assert.AreEqual((BigNumber)20, tree.Tier1.barProgress["cover_1"]);
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["rehearsal"]);
        }

        // ---- autobuy (12.9) ----

        // An autobuy carrier declared at tier1. Declaration and grant are two
        // steps for the reason DeclareSpeed gives, and the multiplier is never
        // read: the switch is the effect's liveness and nothing else (12.2).
        private static void DeclareAutoBuy(TestTree tree, string id, string target)
        {
            var carrier = TestTree.MakeDefinition<ModifierDefinition>(id);
            carrier.effects.Add(new Effect { target = target, stat = Stat.AutoBuy, multiplier = 1 });
            tree.Tier1Def.modifiers.Add(carrier);
        }

        [Test]
        public void The_tick_buys_the_largest_affordable_count_for_a_switched_on_generator()
        {
            var tree = new TestTree();
            DeclareAutoBuy(tree, "hands_free", "gear");
            tree.Rebuild();
            tree.Tier1.modifierStacks["hands_free"] = 1;
            tree.Tier1.balances["cash"] = 1000;
            tree.Tier1.earnedTotals["cash"] = 1000;      // the amp's gate wants 100 earned

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 1, tree.Now.AddSeconds(1));

            // The same count MaxAffordable answers: 60 x 1.15^n summed, eight
            // units fit under 1000 and nine do not.
            Assert.AreEqual(8, tree.Tier1.generatorCounts["practice_amp"], "bought to max");
            Assert.IsFalse(Purchasing.CanBuy(new GameContext(tree.Tier1, tree.Now), tree.PracticeAmp, 1),
                "and the balance moved by the series sum");
            // The drummer carries gear too and its gate opened on the eighth
            // amp, but 250 is past what the amps left - a refusal is a no-op.
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("drummer"), "nothing it could not pay for");
        }

        [Test]
        public void A_generator_no_autobuy_effect_names_is_never_bought_by_the_tick()
        {
            var tree = new TestTree();
            DeclareAutoBuy(tree, "hands_free", "gear");
            tree.Rebuild();
            tree.Tier1.balances["cash"] = 1000;
            tree.Tier1.earnedTotals["cash"] = 1000;

            // The carrier stands but nothing stacked it, so no link is live.
            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 1, tree.Now.AddSeconds(1));

            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("practice_amp"));
            Assert.AreEqual((BigNumber)1000, tree.Tier1.balances["cash"], "and nothing was spent");
        }

        // The purchase phase is the tick's last (12.9): every segment's bars
        // have drunk before anything is spent, so a generator priced in the
        // pool a bar drinks never starves it.
        [Test]
        public void The_tick_buys_after_every_bar_has_drunk()
        {
            var tree = new TestTree();
            var kit = TestTree.MakeDefinition<GeneratorDefinition>("kit");
            kit.availableWhen = new Always();
            kit.costCurrency = tree.Rehearsal;
            kit.baseCost = 30;
            kit.growth = 1;                              // a flat price, so the count reads off the pool
            tree.Tier1Def.generators.Add(kit);
            DeclareAutoBuy(tree, "hands_free", "kit");
            tree.Rebuild();
            tree.Tier1.modifierStacks["hands_free"] = 1;
            tree.Tier1.balances["rehearsal"] = 100;
            tree.Tier1.activeBars["learn_covers"] = new HashSet<string> { "cover_1" };

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));

            Assert.AreEqual((BigNumber)20, tree.Tier1.barProgress["cover_1"], "the cover drank its whole 2/s window");
            Assert.AreEqual(2, tree.Tier1.generatorCounts["kit"], "two at 30 out of the 80 left");
            Assert.AreEqual((BigNumber)20, tree.Tier1.balances["rehearsal"], "and 20 stays in the pool");
        }

        // ---- game_speed ----

        [Test]
        public void Game_speed_scales_production_and_bar_fill_but_never_a_timer()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "riff_press", tree.Fans, 1);
            DeclareSpeed(tree, "encore_x2", 2);

            // A time-filled bar shows the scaled dt with no pool in the way.
            var drill = TestTree.MakeDefinition<BarDefinition>("drill");
            drill.fillAmount = 1000;
            drill.fillRate = 1;
            tree.LearnCovers.bars.Add(drill);
            tree.Rebuild();

            StackSpeed(tree, "encore_x2");
            tree.Tier1.activeBars["learn_covers"] = new HashSet<string> { "drill" };
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "timed_gig", remainingSeconds = 300 };

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));

            Assert.AreEqual((BigNumber)20, tree.Tier1.balances["fans"]);
            Assert.AreEqual((BigNumber)20, tree.Tier1.barProgress["drill"]);
            Assert.AreEqual(290d, tree.Tier1.activeEvent.remainingSeconds);
        }

        [Test]
        public void The_clamp_holds_at_both_bounds()
        {
            // A x0 wildcard would stall time; the floor runs the segment at x1.
            var stalled = new TestTree();
            AddRateSource(stalled.Tier1Def, "riff_press", stalled.Fans, 1);
            DeclareSpeed(stalled, "dead_air", 0);
            stalled.Rebuild();
            StackSpeed(stalled, "dead_air");
            TickSystem.Tick(stalled.Root, stalled.Ch1, Config(), 10, stalled.Now.AddSeconds(10));
            Assert.AreEqual((BigNumber)10, stalled.Tier1.balances["fans"]);

            // Stacked carriers multiply to x9; the ceiling caps the segment at 4.
            var capped = new TestTree();
            AddRateSource(capped.Tier1Def, "riff_press", capped.Fans, 1);
            DeclareSpeed(capped, "opener", 3);
            DeclareSpeed(capped, "headliner", 3);
            capped.Rebuild();
            StackSpeed(capped, "opener", "headliner");
            TickSystem.Tick(capped.Root, capped.Ch1, Config(), 10, capped.Now.AddSeconds(10));
            Assert.AreEqual((BigNumber)40, capped.Tier1.balances["fans"]);
        }

        // ---- segmentation ----

        // The gig's authored goal is earned fans >= 100; riff_press carries no
        // production tag, so the gig's handicap leaves it alone and the fan
        // total is pure dt arithmetic.

        [Test]
        public void A_goal_first_met_after_the_expiry_inside_one_tick_never_latches()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "riff_press", tree.Fans, 1);
            tree.Rebuild();
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "timed_gig", remainingSeconds = 50 };

            // The expiry at +50 is a segment edge: the first segment earns 50
            // and fails the latch, and the second earns 250 against a timer
            // already at zero.
            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 300, tree.Now.AddSeconds(300));

            Assert.AreEqual((BigNumber)300, tree.Tier1.earnedTotals["fans"]);
            Assert.IsFalse(tree.Tier1.activeEvent.goalReached);
            Assert.AreEqual(0d, tree.Tier1.activeEvent.remainingSeconds);
        }

        [Test]
        public void A_goal_met_before_the_expiry_latches()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "riff_press", tree.Fans, 1);
            tree.Rebuild();
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "timed_gig", remainingSeconds = 150 };

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 300, tree.Now.AddSeconds(300));

            Assert.IsTrue(tree.Tier1.activeEvent.goalReached);
            Assert.AreEqual(0d, tree.Tier1.activeEvent.remainingSeconds);
        }

        [Test]
        public void The_boundary_tie_latches_for_the_player()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "riff_press", tree.Fans, 1);
            tree.Rebuild();
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "timed_gig", remainingSeconds = 100 };

            // Earned hits exactly 100 at the edge that also expires the timer;
            // latch-before-decrement sends the tie to the player.
            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 300, tree.Now.AddSeconds(300));

            Assert.IsTrue(tree.Tier1.activeEvent.goalReached);
        }

        [Test]
        public void A_buff_expiry_inside_the_tick_is_a_boundary()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "riff_press", tree.Fans, 1);
            // The authored open-mic handicap halves the fan rate; cleared so
            // the deposits stay pure dt arithmetic.
            tree.OpenMic.handicaps.Clear();
            // A window goal only the boundary moment satisfies: the latch at
            // +50 sees a balance of exactly 50, and an unsegmented tick's only
            // latch would see 100 and fail the upper leg.
            tree.OpenMic.goal = new All
            {
                conditions =
                {
                    new CurrencyAtLeast { currency = tree.Fans, threshold = 50 },
                    new Not { condition = new CurrencyAtLeast { currency = tree.Fans, threshold = 60 } },
                }
            };
            // The handicap is an effect carrier, so clearing it is content: it
            // has to stand before the build that compiles the plans.
            tree.Rebuild();
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "open_mic", remainingSeconds = 0 };
            tree.Root.timedBuffs.Add(new TimedBuff { buffId = "encore_timer", expiresAtUtc = tree.Now.AddSeconds(50) });

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 100, tree.Now.AddSeconds(100));

            Assert.IsTrue(tree.Tier1.activeEvent.goalReached);
        }

        // ---- the record as a speed membership ----

        [Test]
        public void A_live_root_record_doubles_the_effective_dt()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "riff_press", tree.Fans, 0.5);
            DeclareEncore(tree);
            tree.Rebuild();
            Record(tree, 3600);

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));

            Assert.AreEqual((BigNumber)10, tree.Tier1.balances["fans"]);
        }

        [Test]
        public void A_tick_crossing_the_expiry_pays_each_segment_at_its_own_speed()
        {
            // The expiry at +4 cuts the tick: 4 seconds of doubled dt and 6 of
            // real dt, at 0.5/s.
            var crossed = new TestTree();
            AddRateSource(crossed.Tier1Def, "riff_press", crossed.Fans, 0.5);
            DeclareEncore(crossed);
            crossed.Rebuild();
            Record(crossed, 4);
            TickSystem.Tick(crossed.Root, crossed.Ch1, Config(), 10, crossed.Now.AddSeconds(10));
            Assert.AreEqual((BigNumber)7, crossed.Tier1.balances["fans"]);
            Assert.IsEmpty(crossed.Root.timedBuffs, "the tick's end collects what it expired");

            // An expiry AT the tick's end is not a boundary - it would cut an
            // empty segment - so the whole tick is doubled, and the prune still
            // takes the record.
            var edge = new TestTree();
            AddRateSource(edge.Tier1Def, "riff_press", edge.Fans, 0.5);
            DeclareEncore(edge);
            edge.Rebuild();
            Record(edge, 10);
            TickSystem.Tick(edge.Root, edge.Ch1, Config(), 10, edge.Now.AddSeconds(10));
            Assert.AreEqual((BigNumber)10, edge.Tier1.balances["fans"]);
            Assert.IsEmpty(edge.Root.timedBuffs);

            // One second of life past the end keeps it.
            var surviving = new TestTree();
            AddRateSource(surviving.Tier1Def, "riff_press", surviving.Fans, 0.5);
            DeclareEncore(surviving);
            surviving.Rebuild();
            Record(surviving, 11);
            TickSystem.Tick(surviving.Root, surviving.Ch1, Config(), 10, surviving.Now.AddSeconds(10));
            Assert.AreEqual((BigNumber)10, surviving.Tier1.balances["fans"]);
            Assert.AreEqual(1, surviving.Root.timedBuffs.Count);
        }

        // The record reaches production only through dt, exactly as a granted
        // carrier does: a bar's fill doubles with it, and a yield - which has no
        // time component at all - does not.
        [Test]
        public void A_live_record_scales_a_bars_fill_through_dt_and_never_a_yield()
        {
            var tree = new TestTree();
            DeclareEncore(tree);
            var drill = TestTree.MakeDefinition<BarDefinition>("drill");
            drill.fillAmount = 1000;
            drill.fillRate = 1;
            tree.LearnCovers.bars.Add(drill);
            tree.Rebuild();
            Record(tree, 3600);
            tree.Tier1.activeBars["learn_covers"] = new HashSet<string> { "drill" };

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));
            Assert.AreEqual((BigNumber)20, tree.Tier1.barProgress["drill"]);

            // The Jam's flat cash line, fired under the same live record.
            Producer.FireProducer(new GameContext(tree.Tier1, tree.Now.AddSeconds(10)), tree.TapProducer);
            Assert.AreEqual(BigNumber.One, tree.Tier1.balances["cash"]);
        }

        // Truth is the timestamp, never presence, so a record nothing pruned is
        // dead the moment it is dead.
        [Test]
        public void A_missed_prune_changes_no_answer()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "riff_press", tree.Fans, 0.5);
            DeclareEncore(tree);
            tree.Rebuild();
            Record(tree, -1);

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, tree.Now.AddSeconds(10));

            Assert.AreEqual((BigNumber)5, tree.Tier1.balances["fans"]);
        }

        // The window's own edges are not boundaries; only the expiries strictly
        // inside it are, and the segments they cut cover the window end to end.
        [Test]
        public void Segments_cuts_the_window_at_the_interior_expiries_only()
        {
            var tree = new TestTree();
            var start = tree.Now;
            var end = tree.Now.AddSeconds(10);
            // Segments reads the stamps and nothing else, so these stand as four
            // separate records rather than one modifier's timer.
            foreach (var (id, at) in new[] { ("at_start", 0d), ("cut_a", 3d), ("cut_b", 7d), ("at_end", 10d) })
                tree.Root.timedBuffs.Add(new TimedBuff { buffId = id, expiresAtUtc = start.AddSeconds(at) });

            var segments = TickSystem.Segments(tree.Root, tree.Ch1, start, end).ToList();

            Assert.AreEqual(3, segments.Count);
            Assert.AreEqual((start, start.AddSeconds(3)), segments[0]);
            Assert.AreEqual((start.AddSeconds(3), start.AddSeconds(7)), segments[1]);
            Assert.AreEqual((start.AddSeconds(7), end), segments[2]);

            Assert.IsEmpty(TickSystem.Segments(tree.Root, tree.Ch1, start, start), "an empty window");
            Assert.IsEmpty(TickSystem.Segments(tree.Root, tree.Ch1, end, start), "an inverted one");
        }

        // ---- guards ----

        [Test]
        public void Zero_negative_dt_and_a_null_chapter_no_op()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "riff_press", tree.Fans, 1);
            tree.Rebuild();
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "timed_gig", remainingSeconds = 300 };

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 0, tree.Now);
            TickSystem.Tick(tree.Root, tree.Ch1, Config(), -5, tree.Now);
            TickSystem.Tick(tree.Root, null, Config(), 10, tree.Now.AddSeconds(10));

            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["fans"]);
            Assert.AreEqual(300d, tree.Tier1.activeEvent.remainingSeconds);
        }

        [Test]
        public void A_null_or_invalid_config_throws()
        {
            var tree = new TestTree();
            var end = tree.Now.AddSeconds(10);

            Assert.Throws<System.InvalidOperationException>(
                () => TickSystem.Tick(tree.Root, tree.Ch1, null, 10, end));
            Assert.Throws<System.InvalidOperationException>(
                () => TickSystem.Tick(tree.Root, tree.Ch1, Config(0.5), 10, end));
            Assert.Throws<System.InvalidOperationException>(
                () => TickSystem.Tick(tree.Root, tree.Ch1, Config(double.NaN), 10, end));
            Assert.Throws<System.InvalidOperationException>(
                () => TickSystem.Tick(tree.Root, tree.Ch1, Config(double.PositiveInfinity), 10, end));

            // The idle thresholds are guarded by the same Require.
            var negativeAway = Config();
            negativeAway.minimumAwaySeconds = -1;
            Assert.Throws<System.InvalidOperationException>(
                () => TickSystem.Tick(tree.Root, tree.Ch1, negativeAway, 10, end));

            // So is the tick cadence: zero would restore per-frame ticking, and
            // a negative or non-finite interval is no cadence at all.
            void RefusesInterval(double interval)
            {
                var config = Config();
                config.tickIntervalSeconds = interval;
                Assert.Throws<System.InvalidOperationException>(
                    () => TickSystem.Tick(tree.Root, tree.Ch1, config, 10, end));
            }

            RefusesInterval(0);
            RefusesInterval(-1);
            RefusesInterval(double.NaN);
            RefusesInterval(double.PositiveInfinity);

            // And the hold that opens a row's info screen: a zero, negative or
            // non-finite threshold is no gesture at all.
            void RefusesHold(double seconds)
            {
                var config = Config();
                config.longPressSeconds = seconds;
                Assert.Throws<System.InvalidOperationException>(
                    () => TickSystem.Tick(tree.Root, tree.Ch1, config, 10, end));
            }

            RefusesHold(0);
            RefusesHold(-1);
            RefusesHold(double.NaN);
            RefusesHold(double.PositiveInfinity);

            var halfSecondHold = Config();
            halfSecondHold.longPressSeconds = 0.5;
            Assert.DoesNotThrow(() => TickSystem.Tick(tree.Root, tree.Ch1, halfSecondHold, 10, end),
                "the authored default is a legal hold");
        }

        // ---- two buffs over one timer ----

        // A ladder is two modifiers reading one timer with different bands, and
        // the code knows neither of them: with 25 hours left the banded rung
        // counts too, so the speeds multiply to x4 and the clamp is what holds
        // it there; an hour later only the base rung is left.
        [Test]
        public void A_banded_buff_over_the_same_timer_reads_its_own_side_of_the_edge()
        {
            var tree = new TestTree();
            DeclareEncore(tree);
            DeclareBandedTier(tree);
            tree.Rebuild();
            Record(tree, 90000);

            Assert.AreEqual(4d, TickSystem.GameSpeed(tree.Ctx(tree.Ch1), tree.Ch1, Config()),
                "both rungs count, and the ceiling is where the product lands");
            Assert.AreEqual(2d, TickSystem.GameSpeed(
                new GameContext(tree.Ch1, tree.Now.AddSeconds(3601)), tree.Ch1, Config()),
                "past the band's edge only the base rung is left");
        }

        // The band's edge is a segment edge exactly as an expiry is: the tick
        // spans it and pays each side its own speed, at 0.5/s.
        [Test]
        public void A_tick_crossing_the_bands_edge_pays_each_segment_at_its_own_speed()
        {
            var laddered = new TestTree();
            AddRateSource(laddered.Tier1Def, "riff_press", laddered.Fans, 0.5);
            DeclareEncore(laddered);
            DeclareBandedTier(laddered);
            laddered.Rebuild();
            // The edge is the expiry minus 86400, which this record puts 5
            // seconds into a 10-second tick.
            Record(laddered, 86405);

            TickSystem.Tick(laddered.Root, laddered.Ch1, Config(), 10, laddered.Now.AddSeconds(10));

            Assert.AreEqual((BigNumber)15, laddered.Tier1.balances["fans"], "5s at x4, then 5s at x2");

            // The same tick with the banded rung simply absent: the ladder is
            // content, so its edge does not exist and the speed never moves.
            var plain = new TestTree();
            AddRateSource(plain.Tier1Def, "riff_press", plain.Fans, 0.5);
            DeclareEncore(plain);
            plain.Rebuild();
            Record(plain, 86405);

            TickSystem.Tick(plain.Root, plain.Ch1, Config(), 10, plain.Now.AddSeconds(10));

            Assert.AreEqual((BigNumber)10, plain.Tier1.balances["fans"], "10s at x2");
        }

        // Read off Segments directly: the edge a banded buff flips at is the
        // timer's expiry minus the band, admitted from the scope declaring the
        // buff, and the record's own expiry lies outside this window.
        [Test]
        public void Segments_cuts_at_the_timers_expiry_minus_the_band()
        {
            var tree = new TestTree();
            DeclareEncore(tree);
            DeclareBandedTier(tree);
            tree.Rebuild();
            Record(tree, 86405);
            var start = tree.Now;
            var end = start.AddSeconds(10);

            var segments = TickSystem.Segments(tree.Root, tree.Ch1, start, end).ToList();

            Assert.AreEqual(2, segments.Count);
            Assert.AreEqual((start, start.AddSeconds(5)), segments[0]);
            Assert.AreEqual((start.AddSeconds(5), end), segments[1]);
        }
    }
}
