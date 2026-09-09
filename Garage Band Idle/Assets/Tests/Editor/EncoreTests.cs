using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Encore as the session sees it: the ExtendBuff command that writes the
    // record, and the claim's walk over a paid window the record cuts. The
    // arithmetic everywhere is IdleTests': the amp pays cash at 0.5/s live, and
    // the authored idle base halves it to 0.25/s idle.
    public class EncoreTests
    {
        private static GameConfig Config(double idleCap = 14400, double encoreCap = 86400,
                                         double tickInterval = 0.25)
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.minimumAwaySeconds = 180;
            config.idleCapSeconds = idleCap;
            config.encoreCapSeconds = encoreCap;
            config.tickIntervalSeconds = tickInterval;
            return config;
        }

        // Computed amounts are asserted within tolerance, never bit-exact:
        // BigDouble's base-10 mantissa is binary-inexact for most values, so an
        // exact compare would pass or fail on the luck of the inputs.
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        private static IdleOfferLine Line(GameSession session, CurrencyDefinition currency) =>
            session.CurrentOffer.lines.Find(l => l.currency == currency);

        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly ModifierDefinition Encore;
            public readonly GameSession Session;
            public int Refreshes;

            // The Encore shape root.json authors: a wildcard game_speed x2
            // declared and applied at root, whose membership is its own record.
            // Declaration precedes Rebuild because the gather is compiled when
            // the tree is built; the record is a fact written afterward.
            public Fixture(GameConfig config = null, Action<TestTree> author = null)
            {
                Encore = TestTree.MakeDefinition<ModifierDefinition>("encore");
                Encore.effects.Add(new Effect { stat = Stat.GameSpeed, multiplier = 2 });
                Encore.appliesWhen = new BuffActive { modifier = Encore };
                Tree.RootDef.modifiers.Add(Encore);
                Tree.RootDef.permanentModifiers.Add(Encore);
                author?.Invoke(Tree);
                Tree.Rebuild();
                Tree.Tier1.generatorCounts["practice_amp"] = 1;
                Session = new GameSession(Tree.Root, config != null ? config : Config());
                Session.Refreshed += () => Refreshes++;
            }

            // A record written directly onto root, the way any fact is: what a
            // row that is not about ExtendBuff needs standing before it starts.
            public void Record(double expiresInSeconds) =>
                Tree.Root.timedBuffs.Add(new TimedBuff
                    { buffId = "encore", expiresAtUtc = Tree.Now.AddSeconds(expiresInSeconds) });

            public TimedBuff OnlyRecord()
            {
                Assert.AreEqual(1, Tree.Root.timedBuffs.Count, "one record per modifier id on a scope");
                return Tree.Root.timedBuffs[0];
            }
        }

        // ---- ExtendBuff ----

        [Test]
        public void An_absent_record_is_created_at_now_plus_the_grant()
        {
            var f = new Fixture();

            f.Session.ExtendBuff(f.Tree.Root, f.Encore, 14400, f.Tree.Now);

            var record = f.OnlyRecord();
            Assert.AreEqual("encore", record.buffId, "the record is keyed by the MODIFIER's id");
            Assert.AreEqual(f.Tree.Now.AddSeconds(14400), record.expiresAtUtc);
        }

        [Test]
        public void A_present_record_extends_from_the_later_of_its_expiry_and_now()
        {
            // Still live: the grant stacks onto the time left.
            var live = new Fixture();
            live.Record(1000);
            live.Session.ExtendBuff(live.Tree.Root, live.Encore, 14400, live.Tree.Now);
            Assert.AreEqual(live.Tree.Now.AddSeconds(15400), live.OnlyRecord().expiresAtUtc);

            // Lapsed: dead time is no credit toward the next ad.
            var lapsed = new Fixture();
            lapsed.Record(-1000);
            lapsed.Session.ExtendBuff(lapsed.Tree.Root, lapsed.Encore, 14400, lapsed.Tree.Now);
            Assert.AreEqual(lapsed.Tree.Now.AddSeconds(14400), lapsed.OnlyRecord().expiresAtUtc);
        }

        [Test]
        public void The_cap_clamps_remaining_time_and_a_regrant_never_makes_a_second_record()
        {
            var f = new Fixture(Config(encoreCap: 20000));

            // Two 14400 grants at the same moment want 28800 of remaining time;
            // the cap bounds what is LEFT, measured from now.
            f.Session.ExtendBuff(f.Tree.Root, f.Encore, 14400, f.Tree.Now);
            f.Session.ExtendBuff(f.Tree.Root, f.Encore, 14400, f.Tree.Now);
            Assert.AreEqual(f.Tree.Now.AddSeconds(20000), f.OnlyRecord().expiresAtUtc);

            // A third changes nothing and still finds the one record.
            f.Session.ExtendBuff(f.Tree.Root, f.Encore, 14400, f.Tree.Now);
            Assert.AreEqual(f.Tree.Now.AddSeconds(20000), f.OnlyRecord().expiresAtUtc);
        }

        // The dialog's refusal of ordinary commands exists so a sweep cannot
        // reset an unpaid window away; a root record write sweeps nothing, since
        // the sweep is conditional on the resulting phase. Refusing here would
        // discard a watched ad whenever the app resumed into the dialog first.
        [Test]
        public void A_grant_under_the_idle_dialog_sweeps_nothing_and_leaves_the_offer_standing()
        {
            var f = new Fixture(author: tree =>
            {
                var rootTrigger = TestTree.MakeDefinition<TriggerDefinition>("root_latch");
                rootTrigger.condition = new Always();
                rootTrigger.actions.Add(new SetFlag { flagId = "ch1_complete" });
                tree.RootDef.triggers.Add(rootTrigger);
            });
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            var offer = f.Session.CurrentOffer;
            var refreshes = f.Refreshes;

            f.Session.ExtendBuff(f.Tree.Root, f.Encore, 14400, f.Tree.Now);

            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            Assert.AreSame(offer, f.Session.CurrentOffer, "the unpaid window is still the one on screen");
            Assert.IsEmpty(f.Tree.Root.firedTriggers);
            Assert.IsEmpty(f.Tree.Root.flags);
            Assert.AreEqual(f.Tree.Now.AddSeconds(14400), f.OnlyRecord().expiresAtUtc);
            Assert.AreEqual(refreshes + 1, f.Refreshes, "the refresh is unconditional where the sweep is not");
        }

        [Test]
        public void A_grant_with_no_chapter_in_front_is_legal_and_refreshes_once()
        {
            var f = new Fixture();
            Assert.AreEqual(SessionPhase.NoChapter, f.Session.Phase);

            f.Session.ExtendBuff(f.Tree.Root, f.Encore, 14400, f.Tree.Now);

            Assert.AreEqual(SessionPhase.NoChapter, f.Session.Phase);
            Assert.AreEqual(f.Tree.Now.AddSeconds(14400), f.OnlyRecord().expiresAtUtc);
            Assert.AreEqual(1, f.Refreshes);
        }

        // A grant only ever moves an expiry later, so a duration that could not
        // is a caller bug rather than a shorter buff.
        [Test]
        public void A_nonpositive_or_non_finite_grant_throws()
        {
            var f = new Fixture();

            Assert.Throws<InvalidOperationException>(
                () => f.Session.ExtendBuff(f.Tree.Root, f.Encore, 0, f.Tree.Now));
            Assert.Throws<InvalidOperationException>(
                () => f.Session.ExtendBuff(f.Tree.Root, f.Encore, -1, f.Tree.Now));
            Assert.Throws<InvalidOperationException>(
                () => f.Session.ExtendBuff(f.Tree.Root, f.Encore, double.NaN, f.Tree.Now));
            Assert.IsEmpty(f.Tree.Root.timedBuffs);
        }

        // The step 9 regression, for a ROOT command: the banked window belongs
        // to the state the command FOUND, so the 0.5s settles at one speed
        // before the record that doubles the next window exists.
        [Test]
        public void A_grant_in_the_frame_that_banked_a_sub_interval_settles_it_first()
        {
            var f = new Fixture(Config(tickInterval: 1));
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now;
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            f.Session.Accumulate(f.Tree.Now);

            f.Session.Accumulate(f.Tree.Now.AddSeconds(0.5));
            f.Session.ExtendBuff(f.Tree.Root, f.Encore, 14400, f.Tree.Now.AddSeconds(0.5));
            AssertClose(0.25, f.Tree.Tier1.balances["cash"], "0.5s at 0.5/s, unscaled");

            // The bank was settled by the grant, so 0.5 more is still under the
            // interval and the crossing frame runs the whole 1.1s at x2.
            f.Session.Accumulate(f.Tree.Now.AddSeconds(1.0));
            AssertClose(0.25, f.Tree.Tier1.balances["cash"]);
            f.Session.Accumulate(f.Tree.Now.AddSeconds(1.6));
            AssertClose(0.25 + 1.1, f.Tree.Tier1.balances["cash"], "1.1s of doubled dt at 0.5/s");
        }

        // ---- the claim's walk ----

        // The 12:00 / 13:00 / 16:00 case: an hour of Encore left on a four-hour
        // window pays that hour twice and the rest once. Removing the record
        // before the walk would delete the boundary and pay all four at x1.
        [Test]
        public void The_claims_walk_pays_the_covered_hour_at_twice_and_leaves_the_record_standing()
        {
            var f = new Fixture();
            var stamp = f.Tree.Now;
            f.Tree.Ch1.lastActiveUtc = stamp;
            f.Record(3600);
            var switchIn = stamp.AddSeconds(14400);

            f.Session.SwitchChapter(f.Tree.Ch1, switchIn);

            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            AssertClose(4500, Line(f.Session, f.Tree.Cash).amount, "0.25/s over 3600x2 + 10800 rate-seconds");
            Assert.AreEqual(switchIn, f.Session.CurrentOffer.windowEndUtc);

            // The record has to survive the walk to cut the boundary; the next
            // tick's end is what collects it.
            Assert.AreEqual(stamp.AddSeconds(3600), f.OnlyRecord().expiresAtUtc);
            Assert.IsFalse(new BuffActive { modifier = f.Encore }
                .Evaluate(new GameContext(f.Tree.Ch1, switchIn)), "dead at the switch's own moment");
        }

        // The cap is REAL seconds: the player accrues for the first cap seconds
        // after the stamp, and speed multiplies what those pay.
        [Test]
        public void Speed_multiplies_what_the_capped_seconds_pay_never_how_many_there_are()
        {
            var f = new Fixture();
            var stamp = f.Tree.Now;
            f.Tree.Ch1.lastActiveUtc = stamp;
            f.Record(21600);                                  // still live an hour past the window

            f.Session.SwitchChapter(f.Tree.Ch1, stamp.AddSeconds(18000));   // five hours away, four paid

            AssertClose(7200, Line(f.Session, f.Tree.Cash).amount, "0.25/s over 14400 seconds at x2");
        }

        [Test]
        public void A_record_that_expired_before_the_stamp_changes_nothing()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Record(-2000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            AssertClose(250, Line(f.Session, f.Tree.Cash).amount);
        }

        // What the claim reads is PRESENT state: a record standing at switch-in
        // is judged live across the window it covers, and the claim does not
        // reconstruct when it was written. The generosity is bounded by the cap,
        // and John settled it - "do we even care?" - no.
        [Test]
        public void A_record_that_began_after_the_stamp_reads_live_over_the_whole_window()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Record(1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            AssertClose(500, Line(f.Session, f.Tree.Cash).amount, "0.25/s over 1000 seconds at x2");
        }

        // The claim and the tick divide a window with the same method, so the
        // same mid-window expiry buys the same rate-seconds on both paths - the
        // idle base's x0.5 is the only difference between the two numbers.
        [Test]
        public void The_claims_line_and_the_ticks_deposits_are_the_same_segment_walk()
        {
            var ticked = new Fixture();
            ticked.Tree.Ch1.lastActiveUtc = ticked.Tree.Now;
            ticked.Session.SwitchChapter(ticked.Tree.Ch1, ticked.Tree.Now);
            ticked.Record(400);
            ticked.Session.Tick(1000, ticked.Tree.Now.AddSeconds(1000));
            AssertClose(700, ticked.Tree.Tier1.balances["cash"], "0.5/s over 400x2 + 600 rate-seconds");

            var claimed = new Fixture();
            claimed.Tree.Ch1.lastActiveUtc = claimed.Tree.Now;
            claimed.Record(400);
            claimed.Session.SwitchChapter(claimed.Tree.Ch1, claimed.Tree.Now.AddSeconds(1000));
            var line = Line(claimed.Session, claimed.Tree.Cash);
            AssertClose(350, line.amount);

            AssertClose((line.amount * 2).ToDouble(), ticked.Tree.Tier1.balances["cash"],
                "the idle base halves the claim and nothing else differs");
        }
    }
}
