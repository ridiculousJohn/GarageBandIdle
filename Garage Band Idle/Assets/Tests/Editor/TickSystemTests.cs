using System.Collections.Generic;
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

        // A wildcard game_speed carrier granted at the chapter - the tick's
        // owner-less read collects it from the foreground chain.
        private static void GrantSpeed(TestTree tree, string id, double multiplier)
        {
            var carrier = TestTree.MakeDefinition<ModifierDefinition>(id);
            carrier.effects.Add(new Effect { stat = Stat.GameSpeed, multiplier = multiplier });
            tree.Ch1Def.modifiers.Add(carrier);
            tree.Ch1.modifierStacks[id] = 1;
        }

        // ---- rate production ----

        [Test]
        public void Rate_deposits_land_at_their_homes_scaled_by_dt()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "tier_press", tree.Fans, 0.5);
            AddRateSource(tree.Ch1Def, "ch1_press", tree.Ch1Records, 2);

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
            GrantSpeed(tree, "encore_x2", 2);

            // A time-filled bar shows the scaled dt with no pool in the way.
            var drill = TestTree.MakeDefinition<BarDefinition>("drill");
            drill.fillAmount = 1000;
            drill.fillRate = 1;
            tree.LearnCovers.bars.Add(drill);
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
            GrantSpeed(stalled, "dead_air", 0);
            TickSystem.Tick(stalled.Root, stalled.Ch1, Config(), 10, stalled.Now.AddSeconds(10));
            Assert.AreEqual((BigNumber)10, stalled.Tier1.balances["fans"]);

            // Stacked carriers multiply to x9; the ceiling caps the segment at 4.
            var capped = new TestTree();
            AddRateSource(capped.Tier1Def, "riff_press", capped.Fans, 1);
            GrantSpeed(capped, "opener", 3);
            GrantSpeed(capped, "headliner", 3);
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
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "open_mic", remainingSeconds = 0 };
            tree.Root.timedBuffs.Add(new TimedBuff { buffId = "encore", expiresAtUtc = tree.Now.AddSeconds(50) });

            TickSystem.Tick(tree.Root, tree.Ch1, Config(), 100, tree.Now.AddSeconds(100));

            Assert.IsTrue(tree.Tier1.activeEvent.goalReached);
        }

        // ---- guards ----

        [Test]
        public void Zero_negative_dt_and_a_null_chapter_no_op()
        {
            var tree = new TestTree();
            AddRateSource(tree.Tier1Def, "riff_press", tree.Fans, 1);
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
    }
}
