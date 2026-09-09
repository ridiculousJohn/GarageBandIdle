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

        // Encore's own shape: a wildcard game_speed carrier declared and applied
        // at ROOT whose membership is a record with its own id. The tick reads
        // speed from the foreground chapter outward, so a root record reaches
        // whichever chapter is in front.
        private static void DeclareEncore(TestTree tree)
        {
            var encore = TestTree.MakeDefinition<ModifierDefinition>("encore");
            encore.effects.Add(new Effect { stat = Stat.GameSpeed, multiplier = 2 });
            encore.appliesWhen = new BuffActive { modifier = encore };
            tree.RootDef.modifiers.Add(encore);
            tree.RootDef.permanentModifiers.Add(encore);
        }

        private static void Record(TestTree tree, double expiresInSeconds) =>
            tree.Root.timedBuffs.Add(new TimedBuff
                { buffId = "encore", expiresAtUtc = tree.Now.AddSeconds(expiresInSeconds) });

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
            tree.Root.timedBuffs.Add(new TimedBuff { buffId = "encore", expiresAtUtc = tree.Now.AddSeconds(50) });

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
        }

        // The Encore knobs take the same Require. The cap bounds remaining time
        // and the ad grants it, so a cap below one grant would clamp every ad
        // short - a tuning fault that has to fail at boot rather than quietly.
        [Test]
        public void The_encore_knobs_must_be_finite_positive_and_a_cap_at_least_the_grant()
        {
            var tree = new TestTree();
            var end = tree.Now.AddSeconds(10);

            Assert.DoesNotThrow(() => TickSystem.Tick(tree.Root, tree.Ch1, Config(), 10, end),
                "the authored defaults are a legal pair");

            void RefusesAd(double seconds)
            {
                var config = Config();
                config.encoreAdSeconds = seconds;
                Assert.Throws<System.InvalidOperationException>(
                    () => TickSystem.Tick(tree.Root, tree.Ch1, config, 10, end));
            }

            void RefusesCap(double seconds)
            {
                var config = Config();
                config.encoreCapSeconds = seconds;
                Assert.Throws<System.InvalidOperationException>(
                    () => TickSystem.Tick(tree.Root, tree.Ch1, config, 10, end));
            }

            RefusesAd(0);
            RefusesAd(-1);
            RefusesAd(double.NaN);
            RefusesAd(double.PositiveInfinity);
            RefusesCap(0);
            RefusesCap(-1);
            RefusesCap(double.NaN);
            RefusesCap(double.PositiveInfinity);
            RefusesCap(3600);   // finite and positive, but under the 14400 one ad grants
        }
    }
}
