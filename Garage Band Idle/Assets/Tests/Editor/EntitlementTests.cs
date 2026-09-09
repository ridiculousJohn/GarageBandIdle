using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Meta;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The Backstage Pass as the session sees it: the entitlement writes the
    // store callback completes into, and the three benefits - permanent Encore,
    // the always-doubled claim, and the raised cap. The arithmetic everywhere is
    // IdleTests': the amp pays cash at 0.5/s live, and the authored idle base
    // halves it to 0.25/s idle.
    public class EntitlementTests
    {
        private static GameConfig Config(double idleCap = 14400, double passCap = 28800)
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.minimumAwaySeconds = 180;
            config.idleCapSeconds = idleCap;
            config.backstagePassIdleCapSeconds = passCap;
            return config;
        }

        // Computed amounts are asserted within tolerance, never bit-exact:
        // BigDouble's base-10 mantissa is binary-inexact for most values, so an
        // exact compare would pass or fail on the luck of the inputs.
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly ModifierDefinition Encore;
            public readonly GameSession Session;
            public int Refreshes;

            // root.json's Encore shape: a wildcard game_speed x2 declared and
            // applied at root, whose membership counts two ways in - the Pass,
            // or a live record. Declaration precedes Rebuild because the gather
            // is compiled when the tree is built.
            public Fixture(GameConfig config = null, Action<TestTree> author = null)
            {
                Encore = TestTree.MakeDefinition<ModifierDefinition>("encore");
                Encore.effects.Add(new Effect { stat = Stat.GameSpeed, multiplier = 2 });
                Encore.appliesWhen = new Any
                {
                    conditions =
                    {
                        new HasEntitlement { entitlementId = BackstagePass.EntitlementId },
                        new BuffActive { modifier = Encore },
                    }
                };
                Tree.RootDef.modifiers.Add(Encore);
                Tree.RootDef.permanentModifiers.Add(Encore);
                author?.Invoke(Tree);
                Tree.Rebuild();
                Tree.Tier1.generatorCounts["practice_amp"] = 1;
                Session = new GameSession(Tree.Root, config != null ? config : Config());
                Session.Refreshed += () => Refreshes++;
            }
        }

        // ---- the write ----

        // An authenticated callback is phase-eligible in every phase (12.9), and
        // the write is root's own fact, so nothing about the chapter-local
        // boundary applies.
        [Test]
        public void GrantEntitlement_writes_the_id_with_no_chapter_and_while_live()
        {
            var idle = new Fixture();
            Assert.AreEqual(SessionPhase.NoChapter, idle.Session.Phase);

            idle.Session.GrantEntitlement(BackstagePass.EntitlementId, idle.Tree.Now);

            Assert.IsTrue(idle.Tree.Root.entitlements.Contains(BackstagePass.EntitlementId));
            Assert.AreEqual(SessionPhase.NoChapter, idle.Session.Phase);
            Assert.AreEqual(1, idle.Refreshes);

            var live = new Fixture();
            live.Tree.Ch1.lastActiveUtc = live.Tree.Now;
            live.Session.SwitchChapter(live.Tree.Ch1, live.Tree.Now);
            Assert.AreEqual(SessionPhase.Live, live.Session.Phase);
            var refreshes = live.Refreshes;

            live.Session.GrantEntitlement(BackstagePass.EntitlementId, live.Tree.Now);

            Assert.IsTrue(live.Tree.Root.entitlements.Contains(BackstagePass.EntitlementId));
            Assert.AreEqual(SessionPhase.Live, live.Session.Phase);
            Assert.AreEqual(refreshes + 1, live.Refreshes);
        }

        // A callback that leaves the phase alone sweeps nothing yet still
        // repaints (12.9): the offer stays what was computed and is what OK
        // pays - shown and paid never differ, and only the button set changes.
        [Test]
        public void An_entitlement_written_under_the_dialog_sweeps_nothing_and_leaves_the_offer_undoubled()
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

            f.Session.GrantEntitlement(BackstagePass.EntitlementId, f.Tree.Now);

            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            Assert.AreSame(offer, f.Session.CurrentOffer, "the unpaid window is still the one on screen");
            AssertClose(250, offer.lines[0].amount, "a restored entitlement does not re-price a computed offer");
            Assert.IsTrue(f.Tree.Root.entitlements.Contains(BackstagePass.EntitlementId));
            Assert.IsEmpty(f.Tree.Root.firedTriggers);
            Assert.IsEmpty(f.Tree.Root.flags);
            Assert.AreEqual(refreshes + 1, f.Refreshes, "the refresh is unconditional where the sweep is not");
        }

        // The save filter would drop an id root does not declare on the next
        // load, so writing one is a silent loss - the SetFlag rule (12.3).
        [Test]
        public void GrantEntitlement_of_an_undeclared_id_throws_and_writes_nothing()
        {
            var f = new Fixture();

            Assert.Throws<InvalidOperationException>(
                () => f.Session.GrantEntitlement("ghost_pass", f.Tree.Now));

            Assert.IsEmpty(f.Tree.Root.entitlements);
            Assert.AreEqual(0, f.Refreshes, "a throw closes no transaction");
        }

        // ---- the purchase from the dialog ----

        // Bought FROM the dialog it is one transaction (12.9): the entitlement
        // write, the offer doubled, and the claim - ending Live, so the dialog
        // closes with the phase and a kill mid-way leaves the stamp unmoved.
        [Test]
        public void PurchasePassFromDialog_writes_pays_twice_and_ends_live()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            var windowEnd = f.Session.CurrentOffer.windowEndUtc;
            var refreshes = f.Refreshes;

            f.Session.PurchasePassFromDialog(f.Tree.Now.AddSeconds(300));

            Assert.IsTrue(f.Tree.Root.entitlements.Contains(BackstagePass.EntitlementId));
            AssertClose(500, f.Tree.Tier1.balances["cash"], "0.25/s x 1000, doubled");
            Assert.AreEqual(windowEnd, f.Tree.Ch1.lastActiveUtc, "the stamp advanced to the window paid");
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            Assert.IsNull(f.Session.CurrentOffer);
            Assert.AreEqual(refreshes + 1, f.Refreshes, "one transaction, one repaint");
        }

        // Bought anywhere else, the write is the whole command: there is no
        // offer to double and the phase is already what it was.
        [Test]
        public void PurchasePassFromDialog_with_no_offer_standing_writes_only()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now;
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);

            f.Session.PurchasePassFromDialog(f.Tree.Now);

            Assert.IsTrue(f.Tree.Root.entitlements.Contains(BackstagePass.EntitlementId));
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            Assert.AreEqual(BigNumber.Zero, f.Tree.Tier1.balances["cash"], "nothing was owed, so nothing was paid");
        }

        // ---- the ad callback's claim ----

        [Test]
        public void DoubleAndClaimIdle_pays_twice_and_stamps_in_one_transaction()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            var windowEnd = f.Session.CurrentOffer.windowEndUtc;

            Assert.IsTrue(f.Session.DoubleAndClaimIdle(f.Tree.Now.AddSeconds(300)));

            AssertClose(500, f.Tree.Tier1.balances["cash"], "0.25/s x 1000, doubled");
            Assert.AreEqual(windowEnd, f.Tree.Ch1.lastActiveUtc, "the stamp advanced with the payment");
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            Assert.IsNull(f.Session.CurrentOffer);
        }

        // Refused like ClaimIdle when no offer stands: the mid-ad kill relaunches
        // through the entry's own switch-in, which recomputes before any callback
        // can land, and a recompute during Live would mint play time as idle.
        [Test]
        public void DoubleAndClaimIdle_with_no_offer_standing_is_refused_and_changes_nothing()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now;
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            var refreshes = f.Refreshes;

            Assert.IsFalse(f.Session.DoubleAndClaimIdle(f.Tree.Now));

            Assert.AreEqual(BigNumber.Zero, f.Tree.Tier1.balances["cash"]);
            Assert.AreEqual(f.Tree.Now, f.Tree.Ch1.lastActiveUtc);
            Assert.AreEqual(refreshes, f.Refreshes, "a refusal runs no pipeline");
        }

        // ---- the roadie bundle ----

        // A root-context deposit through the AUTHORED write, so the currency's
        // activeWhen is honored (12.2). Bought Roadies are identical to earned
        // ones, so the earned total moves with the balance.
        [Test]
        public void GrantRoadies_deposits_the_bundle_at_roots_home()
        {
            var f = new Fixture();

            f.Session.GrantRoadies(3, f.Tree.Now);

            Assert.AreEqual((BigNumber)3, f.Tree.Root.balances["roadies"]);
            Assert.AreEqual((BigNumber)3, f.Tree.Root.earnedTotals["roadies"]);
            Assert.AreEqual(1, f.Refreshes);
        }

        // A nonpositive count is a caller bug, not a smaller bundle - Require
        // refuses one before any store can report success.
        [Test]
        public void GrantRoadies_refuses_a_nonpositive_count()
        {
            var f = new Fixture();

            Assert.Throws<InvalidOperationException>(() => f.Session.GrantRoadies(0, f.Tree.Now));
            Assert.Throws<InvalidOperationException>(() => f.Session.GrantRoadies(-1, f.Tree.Now));

            Assert.AreEqual(BigNumber.Zero, f.Tree.Root.balances["roadies"]);
        }

        // ---- the three benefits ----

        // The two code-side ones read the entitlement at computation (section 9):
        // the window is the raised cap, and the lines are computed doubled. Five
        // hours away pays all five, where a free player's four-hour cap stops at
        // four - and permanent Encore doubles what each of those seconds pays.
        [Test]
        public void A_Pass_owners_offer_is_doubled_and_computed_over_the_raised_cap()
        {
            var owner = new Fixture();
            owner.Tree.Root.entitlements.Add(BackstagePass.EntitlementId);
            owner.Tree.Ch1.lastActiveUtc = owner.Tree.Now.AddSeconds(-18000);

            owner.Session.SwitchChapter(owner.Tree.Ch1, owner.Tree.Now);

            AssertClose(18000, owner.Session.CurrentOffer.lines[0].amount,
                "0.25/s over all 18000 seconds at Encore x2, doubled again as the Pass's claim - the four-hour cap would have paid 14400");

            var free = new Fixture();
            free.Tree.Ch1.lastActiveUtc = free.Tree.Now.AddSeconds(-18000);

            free.Session.SwitchChapter(free.Tree.Ch1, free.Tree.Now);

            AssertClose(3600, free.Session.CurrentOffer.lines[0].amount, "0.25/s over the 14400 second cap");
        }

        // Permanent Encore is CONTENT, not code: the Pass is the first leg of
        // encore's own appliesWhen, so a Pass owner applies the same one
        // membership a free player's record applies, with no record anywhere.
        [Test]
        public void The_Pass_leg_alone_doubles_the_ticks_effective_dt()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now;
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            f.Tree.Root.entitlements.Add(BackstagePass.EntitlementId);
            Assert.IsEmpty(f.Tree.Root.timedBuffs, "the entitlement is the whole membership here");

            f.Session.Tick(1000, f.Tree.Now.AddSeconds(1000));

            AssertClose(1000, f.Tree.Tier1.balances["cash"], "0.5/s over 1000 seconds of doubled dt");
        }

        // Any is one membership and membership is one implicit application, so
        // both legs holding at once is still x2 rather than x4.
        [Test]
        public void The_Pass_and_a_live_record_together_are_still_one_membership()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now;
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            f.Tree.Root.entitlements.Add(BackstagePass.EntitlementId);
            f.Tree.Root.timedBuffs.Add(new TimedBuff
                { buffId = "encore", expiresAtUtc = f.Tree.Now.AddSeconds(3600) });

            f.Session.Tick(1000, f.Tree.Now.AddSeconds(1000));

            AssertClose(1000, f.Tree.Tier1.balances["cash"], "0.5/s over 1000 seconds of doubled dt");
        }

        // ---- the knobs ----

        // A malformed Pass cap must fail at boot rather than shrink a paid
        // claim, and a store that has already reported success must never meet
        // a grant that throws.
        [Test]
        public void An_invalid_pass_cap_or_bundle_count_refuses_construction()
        {
            var tree = new TestTree();

            Assert.Throws<InvalidOperationException>(
                () => new GameSession(tree.Root, Config(passCap: 3600)));
            Assert.Throws<InvalidOperationException>(
                () => new GameSession(tree.Root, Config(passCap: double.NaN)));

            var zeroBundle = Config();
            zeroBundle.roadieBundleMedium = 0;
            Assert.Throws<InvalidOperationException>(() => new GameSession(tree.Root, zeroBundle));

            var negativeBundle = Config();
            negativeBundle.roadieBundleLarge = -1;
            Assert.Throws<InvalidOperationException>(() => new GameSession(tree.Root, negativeBundle));
        }
    }
}
