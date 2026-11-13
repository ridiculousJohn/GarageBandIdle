using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // A generator's count thresholds (design doc 12.2/12.7): "at this owned
    // count, these effects", a carrier kind of the generator's declaring scope,
    // so the live tick and the idle offer gather them as they gather every
    // other carrier. The tree is the standing one: tier1 declares the bar and
    // the crew whose count reaches the thresholds. The arithmetic: the bar
    // fills at 1/s live, and root's idle base halves it to 0.5/s idle.
    public class ThresholdTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        private static GameConfig Config()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.minimumAwaySeconds = 180;
            config.idleCapSeconds = 14400;
            return config;
        }

        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly GameSession Session;
            public readonly BarDefinition Slow;
            public readonly GeneratorDefinition Crew;

            // A bar too long to complete inside a test, so its progress is its
            // fill; a crew whose count reaches the thresholds: x2 on the bar's
            // fill at 10, and x3 on the crew's own yield at 25 (no target).
            public Fixture()
            {
                Slow = TestTree.MakeDefinition<BarDefinition>("slow");
                Slow.fillAmount = 1000000;
                Slow.fillRate = 1;
                Tree.Tier1Def.bars.Add(Slow);
                var group = TestTree.MakeDefinition<GroupDefinition>("slow_group");
                group.maxActive = 1;
                group.members.Add(Slow);
                Tree.Tier1Def.groups.Add(group);

                Crew = TestTree.MakeDefinition<GeneratorDefinition>("road_crew");
                Crew.availableWhen = new Always();
                Crew.costCurrency = Tree.Cash;
                Crew.baseCost = 10;
                Crew.growth = 1.15;
                Crew.produces.Add(TestTree.Entry(Tree.Fans, Stat.Yield, 5));
                Crew.thresholds.Add(Threshold(10, new ThresholdEffect { target = Slow, stat = Stat.Rate, multiplier = 2 }));
                Crew.thresholds.Add(Threshold(25, new ThresholdEffect { target = Crew, stat = Stat.Yield, multiplier = 3 }));
                Crew.thresholds.Add(Threshold(50, new ThresholdEffect { target = Slow, stat = Stat.Rate, multiplier = 2 }));
                Tree.Tier1Def.generators.Add(Crew);

                Tree.Rebuild();
                Tree.Tier1.activeMembers["slow_group"] = new HashSet<string> { Slow.Id };
                Session = new GameSession(Tree.Root, Config());
            }

            public void Own(int purchased, double granted = 0)
            {
                Tree.Tier1.generatorCounts[Crew.Id] = purchased;
                Tree.Tier1.grantedCounts[Crew.Id] = granted;
            }

            public BigNumber Progress =>
                Tree.Tier1.barProgress.TryGetValue(Slow.Id, out var stored) ? stored : BigNumber.Zero;

            // Live at the tree's clock, with no window owed.
            public void GoLive()
            {
                Tree.Ch1.lastActiveUtc = Tree.Now;
                Session.SwitchChapter(Tree.Ch1, Tree.Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
            }

            public BigNumber FansPerFiring() =>
                Producer.ResolveGeneratorYield(Tree.Ctx(Tree.Tier1), Crew).Single(p => p.target == Tree.Fans).amount;
        }

        private static CountThreshold Threshold(int count, params ThresholdEffect[] effects)
        {
            var threshold = new CountThreshold { count = count };
            threshold.effects.AddRange(effects);
            return threshold;
        }

        // ---- the queries ----

        [Test]
        public void The_next_threshold_is_the_first_count_not_yet_reached()
        {
            var f = new Fixture();
            Assert.AreEqual(10, f.Crew.NextThreshold(0).count);
            Assert.AreEqual(10, f.Crew.NextThreshold(9).count);
            Assert.AreEqual(25, f.Crew.NextThreshold(10).count, "a reached count is not next");
            Assert.AreEqual(50, f.Crew.NextThreshold(49.5).count, "a granted fraction short of it still wants it");
            Assert.IsNull(f.Crew.NextThreshold(50), "none is left once the last is reached");
        }

        [Test]
        public void The_next_threshold_at_a_scope_reads_the_owned_count_there()
        {
            var f = new Fixture();
            f.Own(8, 3);
            Assert.AreEqual(25, f.Crew.NextThreshold(f.Tree.Ctx(f.Tree.Tier1)).count,
                "8 bought and 3 granted is 11 owned, past the first");
        }

        [Test]
        public void The_crossed_thresholds_lie_above_the_first_count_and_at_most_the_second()
        {
            var f = new Fixture();
            CollectionAssert.AreEqual(new[] { 10, 25 },
                f.Crew.ThresholdsCrossed(9, 25).Select(t => t.count).ToArray());
            CollectionAssert.AreEqual(new[] { 25 },
                f.Crew.ThresholdsCrossed(10, 30).Select(t => t.count).ToArray(), "one already reached is not crossed");
            Assert.IsEmpty(f.Crew.ThresholdsCrossed(11, 24), "a move between two thresholds crosses none");
            Assert.IsEmpty(f.Crew.ThresholdsCrossed(60, 0), "a move down crosses none");
        }

        // ---- the gather ----

        // The live tick: the bar's fill is doubled from the tick its count
        // reaches 10, and not a tick before.
        [Test]
        public void A_threshold_speeds_the_live_fill_at_its_count_and_not_before()
        {
            var f = new Fixture();
            f.Own(9);
            f.GoLive();

            f.Session.Tick(10, f.Tree.Now.AddSeconds(10));
            AssertClose(10, f.Progress, "nine crew: the bar's own 1/s");

            f.Own(10);
            f.Session.Tick(10, f.Tree.Now.AddSeconds(20));
            AssertClose(30, f.Progress, "ten crew: 2/s from here");
        }

        // The idle window gathers the same plan under the idle circumstance, so
        // the threshold applies there exactly as it does live. The bar never
        // completes, so the window changes no balance and settles on entry
        // with no dialog, writing the progress it filled.
        [Test]
        public void A_threshold_speeds_the_idle_fill_at_its_count_and_not_before()
        {
            var below = new Fixture();
            below.Own(9);
            below.Tree.Ch1.lastActiveUtc = below.Tree.Now.AddSeconds(-1000);
            below.Session.SwitchChapter(below.Tree.Ch1, below.Tree.Now);
            Assert.IsNull(below.Session.CurrentOffer, "nothing paid, so no dialog");
            AssertClose(500, below.Progress, "0.5/s idle over 1000s");

            var at = new Fixture();
            at.Own(10);
            at.Tree.Ch1.lastActiveUtc = at.Tree.Now.AddSeconds(-1000);
            at.Session.SwitchChapter(at.Tree.Ch1, at.Tree.Now);
            AssertClose(1000, at.Progress, "doubled at ten crew");
        }

        // The count the threshold reads is the one OwnedCountAtLeast reads:
        // purchased plus granted.
        [Test]
        public void Granted_copies_count_toward_a_threshold()
        {
            var f = new Fixture();
            f.Own(5, 5);
            f.GoLive();

            f.Session.Tick(10, f.Tree.Now.AddSeconds(10));

            AssertClose(20, f.Progress, "five bought and five granted reach ten");
        }

        // An entry naming no target is the generator's own; its yield is paid
        // with the factor from the count on, and a count that falls back below
        // takes the factor with it - reached-ness is never stored.
        [Test]
        public void An_untargeted_threshold_multiplies_the_generators_own_yield_while_reached()
        {
            var f = new Fixture();
            f.Own(24);
            AssertClose(120, f.FansPerFiring(), "24 crew at 5 fans each");

            f.Own(25);
            AssertClose(375, f.FansPerFiring(), "25 crew, x3 from the threshold");

            f.Own(24);
            AssertClose(120, f.FansPerFiring(), "back below it, back to the plain payout");
        }

        // Thresholds stack as they are reached: 10 and 50 each double the fill.
        [Test]
        public void Reached_thresholds_on_one_owner_multiply_together()
        {
            var f = new Fixture();
            f.Own(50);
            f.GoLive();

            f.Session.Tick(10, f.Tree.Now.AddSeconds(10));

            AssertClose(40, f.Progress, "x2 at 10 and x2 at 50");
        }
    }
}
