using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Meta;
using RidiculousGaming.GarageBandIdle.Monetization;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The two managers over their fakes: what a callback does when it lands, and
    // WHEN it lands - only from Update, never at request time, because both
    // buttons only REQUEST and the payout is the callback's own transaction. The
    // arithmetic is IdleTests': the amp pays cash at 0.5/s live, and the
    // authored idle base halves it to 0.25/s idle.
    public class MonetizationTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly ChapterDefinition Ch2Def;
            public readonly ModifierDefinition Encore;

            public readonly RootScopeState Root;
            public readonly ChapterScopeState Ch1;
            public readonly ChapterScopeState Ch2;
            public readonly TierScopeState Tier1;

            public readonly GameConfig ConfigAsset;
            public readonly GameSession Session;
            public readonly FakeAdService Ads = new();
            public readonly FakeStoreService Store = new();
            public readonly AdManager AdManager;
            public readonly IAPManager IAPManager;

            // What the store had acknowledged each time the save site ran. The
            // delivery order is grant, save, acknowledge (12.9), so a save that
            // ran with nothing acknowledged is the order holding.
            public readonly List<int> Saves = new();

            // The kill between the grant and the acknowledge: a real store
            // re-delivers a transaction the app never acknowledged.
            public bool SaveThrows;

            public Fixture()
            {
                // root.json's Encore shape, because the ad callback resolves the
                // modifier off root's own list and a miss throws.
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

                // A second chapter, so a callback can land under a foreground
                // that is no longer the one its request was made for.
                Ch2Def = TestTree.MakeChapter("ch2");
                var merch = TestTree.DeclareCurrency(Ch2Def, "merch");
                var press = TestTree.MakeDefinition<ProducerDefinition>("merch_press");
                press.produces.Add(TestTree.Entry(merch, Stat.Rate, 2));
                Ch2Def.producers.Add(press);
                Tree.Chapters.Add(Ch2Def);
                Tree.Rebuild();

                Root = Tree.Root;
                Ch1 = Tree.Ch1;
                Tier1 = Tree.Tier1;
                Ch2 = (ChapterScopeState)TestNavigation.Node(Root, Ch2Def);
                Tier1.generatorCounts["practice_amp"] = 1;

                // Section 9's own numbers are the asset's defaults, and the rows
                // read the knobs back rather than repeating them.
                ConfigAsset = ScriptableObject.CreateInstance<GameConfig>();
                Session = new GameSession(Root, ConfigAsset);
                AdManager = new AdManager(Session, Ads, ConfigAsset, Save);
                IAPManager = new IAPManager(Session, Store, ConfigAsset, Save);
            }

            public DateTime Now => Tree.Now;

            private void Save()
            {
                Saves.Add(Store.Acknowledged.Count);
                if (SaveThrows)
                    throw new InvalidOperationException("the save site died before the acknowledge.");
            }

            // The switch a live player makes: the stamp is now, so no window is
            // owed and the phase lands Live.
            public void Enter()
            {
                Ch1.lastActiveUtc = Now;
                Session.SwitchChapter(Ch1, Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
            }

            // The return that owes a window: 1000 idle seconds of the amp's cash.
            public void EnterOwing()
            {
                Ch1.lastActiveUtc = Now.AddSeconds(-1000);
                Session.SwitchChapter(Ch1, Now);
                Assert.AreEqual(SessionPhase.AwaitingIdleClaim, Session.Phase);
            }

            // The tap's yield at its home, which is how a deferred row reads
            // whether the queued command has run yet.
            public BigNumber Cash =>
                Tier1.balances.TryGetValue("cash", out var held) ? held : BigNumber.Zero;

            // The queue made non-empty the way a frame makes it (12.9): a
            // refresh handler submits a command, which lands behind the
            // transaction that refreshed and waits for the drain. Anything a
            // manager submits afterwards waits behind it too, which is the
            // circumstance the rows at the bottom of the file are about.
            public void QueueATap()
            {
                var armed = true;
                Session.Refreshed += () =>
                {
                    if (!armed)
                        return;
                    armed = false;
                    Session.FireProducer(Tree.Ctx(Tier1), Tree.TapProducer);
                };
                Session.Refresh();
                Assert.AreEqual(BigNumber.Zero, Cash, "the handler's tap is on the queue, not run");
            }
        }

        // ---- the ad seam ----

        // An EncoreExtension result has no offer to lose and its command is
        // legal in every phase, so a watched ad is never discarded - after a
        // backgrounding and after a chapter change alike.
        [Test]
        public void An_encore_extension_always_reaches_ExtendBuff_and_saves()
        {
            var live = new Fixture();
            live.Enter();
            live.AdManager.RequestEncoreExtension();
            Assert.IsEmpty(live.Root.timedBuffs, "a request grants nothing");
            Assert.IsEmpty(live.Saves);

            live.AdManager.Update(live.Now);

            Assert.AreEqual(new[] { AdPlacement.EncoreExtension }, live.Ads.Shown.ToArray());
            Assert.AreEqual(live.Now.AddSeconds(live.ConfigAsset.encoreAdSeconds),
                live.Root.timedBuffs.Single().expiresAtUtc);
            Assert.AreEqual(1, live.Saves.Count);

            // Backgrounded between the request and the result.
            var backgrounded = new Fixture();
            backgrounded.Enter();
            backgrounded.AdManager.RequestEncoreExtension();
            backgrounded.Session.SwitchChapter(null, backgrounded.Now);
            Assert.AreEqual(SessionPhase.NoChapter, backgrounded.Session.Phase);

            backgrounded.AdManager.Update(backgrounded.Now);

            Assert.AreEqual(backgrounded.Now.AddSeconds(backgrounded.ConfigAsset.encoreAdSeconds),
                backgrounded.Root.timedBuffs.Single().expiresAtUtc);
            Assert.AreEqual(1, backgrounded.Saves.Count);

            // Switched to another chapter between the request and the result.
            var switched = new Fixture();
            switched.Enter();
            switched.AdManager.RequestEncoreExtension();
            switched.Ch2.lastActiveUtc = switched.Now;
            switched.Session.SwitchChapter(switched.Ch2, switched.Now);

            switched.AdManager.Update(switched.Now);

            Assert.AreEqual(switched.Now.AddSeconds(switched.ConfigAsset.encoreAdSeconds),
                switched.Root.timedBuffs.Single().expiresAtUtc);
            Assert.AreEqual(1, switched.Saves.Count);
        }

        // The double is ATOMIC with its settlement (12.9): one transaction ends
        // Live, and the save follows the grant so a crash cannot take the reward
        // off disk.
        [Test]
        public void An_idle_double_for_the_foreground_chapter_doubles_settles_and_saves_once()
        {
            var f = new Fixture();
            f.EnterOwing();
            var windowEnd = f.Session.CurrentOffer.windowEndUtc;

            f.AdManager.RequestIdleDouble();
            Assert.AreEqual(BigNumber.Zero, f.Tier1.balances["cash"], "a request pays nothing");
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);

            f.AdManager.Update(f.Now);

            AssertClose(500, f.Tier1.balances["cash"], "0.25/s x 1000, doubled");
            Assert.AreEqual(windowEnd, f.Ch1.lastActiveUtc, "the stamp advanced with the payment");
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            Assert.IsNull(f.Session.CurrentOffer);
            Assert.AreEqual(1, f.Saves.Count);
        }

        // Backgrounding is not a switch: it drops the offer and keeps the stamp
        // (12.9), and Unity runs one more frame after the pause hook, so the
        // result waits with no chapter in front and lands on the offer the
        // resume recomputes.
        [Test]
        public void An_idle_double_waits_through_a_backgrounding_and_doubles_the_recomputed_offer()
        {
            var f = new Fixture();
            f.EnterOwing();
            f.AdManager.RequestIdleDouble();

            f.Session.SwitchChapter(null, f.Now);
            Assert.AreEqual(SessionPhase.NoChapter, f.Session.Phase);
            f.AdManager.Update(f.Now);
            Assert.AreEqual(BigNumber.Zero, f.Tier1.balances["cash"], "nothing is paid with no chapter in front");
            Assert.IsEmpty(f.Saves);

            // The resume: the recorded chapter re-enters and the unmoved stamp
            // recomputes the window, a minute longer.
            var resume = f.Now.AddSeconds(60);
            f.Session.SwitchChapter(f.Ch1, resume);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            f.AdManager.Update(resume);

            AssertClose(530, f.Tier1.balances["cash"], "0.25/s x 1060, doubled");
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            Assert.AreEqual(1, f.Saves.Count);
        }

        // The drop rule is IdleDouble's alone: the player switched under the
        // dialog, and that switch already settled the offer on the way out.
        [Test]
        public void An_idle_double_whose_chapter_changed_is_dropped()
        {
            var f = new Fixture();
            f.EnterOwing();
            f.AdManager.RequestIdleDouble();

            f.Ch2.lastActiveUtc = f.Now;   // the incoming side stays quiet
            f.Session.SwitchChapter(f.Ch2, f.Now);
            AssertClose(250, f.Tier1.balances["cash"], "the exit settled the offer as computed");

            f.AdManager.Update(f.Now);

            AssertClose(250, f.Tier1.balances["cash"], "nothing more was paid");
            Assert.IsEmpty(f.Saves, "a dropped result saves nothing");
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
        }

        [Test]
        public void An_aborted_or_failed_ad_leaves_the_offer_standing_and_saves_nothing()
        {
            foreach (var result in new[] { AdResult.Aborted, AdResult.Failed })
            {
                var f = new Fixture();
                f.Ads.NextResult = result;
                f.EnterOwing();

                f.AdManager.RequestIdleDouble();
                f.AdManager.Update(f.Now);

                Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase, result.ToString());
                AssertClose(250, f.Session.CurrentOffer.lines[0].amount, result.ToString());
                Assert.AreEqual(BigNumber.Zero, f.Tier1.balances["cash"], result.ToString());
                Assert.IsEmpty(f.Saves, result.ToString());
            }
        }

        // ---- the store seam ----

        // A real store re-delivers any transaction the app never acknowledged,
        // which is why the acknowledge comes last: grant, save, acknowledge.
        [Test]
        public void A_pass_purchase_grants_then_saves_then_acknowledges()
        {
            var f = new Fixture();
            f.Enter();

            f.IAPManager.RequestPurchase(ProductId.BackstagePass);
            Assert.IsEmpty(f.Root.entitlements, "a request grants nothing");
            Assert.IsEmpty(f.Store.Acknowledged);

            f.IAPManager.Update(f.Now);

            Assert.IsTrue(f.Root.entitlements.Contains(BackstagePass.EntitlementId));
            Assert.AreEqual(new[] { 0 }, f.Saves.ToArray(),
                "the save ran with nothing acknowledged yet, so the grant was on disk first");
            Assert.AreEqual(f.Store.Purchases.Single().transactionId, f.Store.Acknowledged.Single());
        }

        [Test]
        public void A_roadie_bundle_grants_the_configured_count()
        {
            var f = new Fixture();
            f.Enter();

            f.IAPManager.RequestPurchase(ProductId.RoadieBundleMedium);
            f.IAPManager.Update(f.Now);

            Assert.AreEqual((BigNumber)f.ConfigAsset.roadieBundleMedium, f.Root.balances["roadies"]);
            Assert.AreEqual((BigNumber)f.ConfigAsset.roadieBundleMedium, f.Root.earnedTotals["roadies"]);
            Assert.AreEqual(new[] { 0 }, f.Saves.ToArray());
            Assert.AreEqual(1, f.Store.Acknowledged.Count);
        }

        // A Tip Jar tier gates no content, so its grant is a no-op - and it
        // still takes the delivery order, because the store is waiting on the
        // acknowledge either way.
        [Test]
        public void A_tip_jar_purchase_writes_nothing_and_is_still_saved_and_acknowledged()
        {
            var f = new Fixture();
            f.Enter();

            f.IAPManager.RequestPurchase(ProductId.TipJarSmall);
            f.IAPManager.Update(f.Now);

            Assert.IsEmpty(f.Root.entitlements);
            Assert.AreEqual(BigNumber.Zero, f.Root.balances["roadies"]);
            Assert.AreEqual(new[] { 0 }, f.Saves.ToArray());
            Assert.AreEqual(1, f.Store.Acknowledged.Count);
        }

        [Test]
        public void A_failed_or_cancelled_purchase_writes_nothing_and_acknowledges_nothing()
        {
            foreach (var outcome in new[] { PurchaseOutcome.Failed, PurchaseOutcome.Cancelled })
            {
                var f = new Fixture();
                f.Store.NextOutcome = outcome;
                f.Enter();

                f.IAPManager.RequestPurchase(ProductId.BackstagePass);
                f.IAPManager.Update(f.Now);

                Assert.IsEmpty(f.Root.entitlements, outcome.ToString());
                Assert.IsEmpty(f.Saves, outcome.ToString());
                Assert.IsEmpty(f.Store.Acknowledged, outcome.ToString());
            }
        }

        // The kill between the grant and the acknowledge. The throw is the save
        // site dying, and whether the manager lets it out is its own business -
        // what the row is about is the transaction staying unacknowledged.
        [Test]
        public void A_save_that_throws_leaves_the_transaction_unacknowledged()
        {
            var f = new Fixture();
            f.Enter();
            f.SaveThrows = true;

            f.IAPManager.RequestPurchase(ProductId.RoadieBundleSmall);
            try
            {
                f.IAPManager.Update(f.Now);
            }
            catch (InvalidOperationException)
            {
            }

            Assert.AreEqual((BigNumber)f.ConfigAsset.roadieBundleSmall, f.Root.balances["roadies"],
                "the grant ran first");
            Assert.AreEqual(1, f.Saves.Count, "and the save was attempted");
            Assert.IsEmpty(f.Store.Acknowledged, "so a real store re-delivers the transaction");
        }

        // Restoration is idempotent by construction - the entitlement is set or
        // it is not - so it needs no ledger, and it lands on a frame after boot.
        [Test]
        public void RequestRestore_grants_the_owned_entitlement_and_saves_once()
        {
            var f = new Fixture();
            f.Enter();
            f.Store.Owned.Add(ProductId.BackstagePass);

            f.IAPManager.RequestRestore();
            Assert.IsEmpty(f.Root.entitlements, "a request grants nothing");

            f.IAPManager.Update(f.Now);

            Assert.IsTrue(f.Root.entitlements.Contains(BackstagePass.EntitlementId));
            Assert.AreEqual(1, f.Saves.Count);
        }

        [Test]
        public void A_restore_listing_a_consumable_grants_nothing()
        {
            var f = new Fixture();
            f.Enter();
            f.Store.Owned.Add(ProductId.RoadieBundleLarge);

            f.IAPManager.RequestRestore();
            f.IAPManager.Update(f.Now);

            Assert.AreEqual(BigNumber.Zero, f.Root.balances["roadies"], "a consumable is never restored");
            Assert.IsEmpty(f.Root.entitlements);
            Assert.IsEmpty(f.Saves, "nothing was written, so nothing was saved");
        }

        // ---- delivered while the queue is non-empty ----

        // Every grant is a SUBMITTED command (12.9), so a delivery that lands
        // with the queue non-empty writes nothing at the call: the command runs
        // at the frame's drain, and the completed callback is what carries the
        // save - and, for the store, the acknowledge after it. A save at the
        // call would write a tree the grant is not on, and an acknowledge would
        // tell the store a grant is banked that is not. The rows above deliver
        // with an EMPTY queue, where the command runs at the call and the
        // callback fires inside it - same order, one frame earlier.

        [Test]
        public void A_deferred_encore_grant_saves_when_the_command_runs()
        {
            var f = new Fixture();
            f.Enter();
            f.AdManager.RequestEncoreExtension();
            f.QueueATap();

            f.AdManager.Update(f.Now);

            Assert.IsEmpty(f.Root.timedBuffs, "the grant is behind the queued tap");
            Assert.IsEmpty(f.Saves, "so there is nothing on the tree for a save to write");

            f.Session.Drain();

            Assert.AreEqual((BigNumber)1, f.Cash, "the tap ran first, as it was queued first");
            Assert.AreEqual(f.Now.AddSeconds(f.ConfigAsset.encoreAdSeconds),
                f.Root.timedBuffs.Single().expiresAtUtc, "the grant stamps the moment it was delivered at");
            Assert.AreEqual(1, f.Saves.Count, "and the save followed the write it is for");
        }

        [Test]
        public void A_deferred_roadie_bundle_grants_saves_and_acknowledges_at_the_drain()
        {
            var f = new Fixture();
            f.Enter();
            f.IAPManager.RequestPurchase(ProductId.RoadieBundleMedium);
            f.QueueATap();

            f.IAPManager.Update(f.Now);

            Assert.AreEqual(BigNumber.Zero, f.Root.balances["roadies"], "the grant is behind the queued tap");
            Assert.IsEmpty(f.Saves, "a save here would write a tree the grant is not on");
            Assert.IsEmpty(f.Store.Acknowledged, "and the store would be told a grant is banked that is not");

            f.Session.Drain();

            Assert.AreEqual((BigNumber)f.ConfigAsset.roadieBundleMedium, f.Root.balances["roadies"]);
            Assert.AreEqual(new[] { 0 }, f.Saves.ToArray(),
                "the save ran with nothing acknowledged yet, so the order held across the wait");
            Assert.AreEqual(f.Store.Purchases.Single().transactionId, f.Store.Acknowledged.Single());
        }

        // Bought FROM the claim dialog, which is the command's other half: the
        // entitlement, the doubled offer and the settlement are one transaction,
        // so with the queue non-empty the dialog closes at the drain and not at
        // the delivery.
        [Test]
        public void A_deferred_pass_purchase_settles_the_dialog_saves_and_acknowledges_at_the_drain()
        {
            var f = new Fixture();
            f.EnterOwing();
            f.IAPManager.RequestPurchase(ProductId.BackstagePass);
            f.QueueATap();

            f.IAPManager.Update(f.Now);

            Assert.IsEmpty(f.Root.entitlements, "the grant is behind the queued tap");
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase, "so the offer still stands");
            Assert.IsEmpty(f.Saves);
            Assert.IsEmpty(f.Store.Acknowledged);

            f.Session.Drain();

            Assert.IsTrue(f.Root.entitlements.Contains(BackstagePass.EntitlementId));
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase, "the dialog closed with the phase");
            AssertClose(501, f.Cash, "the queued tap's 1, then the doubled window's 500");
            Assert.AreEqual(new[] { 0 }, f.Saves.ToArray(), "grant, then save, then acknowledge");
            Assert.AreEqual(f.Store.Purchases.Single().transactionId, f.Store.Acknowledged.Single());
        }

        [Test]
        public void A_deferred_restore_writes_the_entitlement_and_saves_at_the_drain()
        {
            var f = new Fixture();
            f.Enter();
            f.Store.Owned.Add(ProductId.BackstagePass);
            f.IAPManager.RequestRestore();
            f.QueueATap();

            f.IAPManager.Update(f.Now);

            Assert.IsEmpty(f.Root.entitlements, "the write is behind the queued tap");
            Assert.IsEmpty(f.Saves, "and a restore saves only what is already on the tree");

            f.Session.Drain();

            Assert.IsTrue(f.Root.entitlements.Contains(BackstagePass.EntitlementId));
            Assert.AreEqual(1, f.Saves.Count, "one save, for the one entitlement that was written");
        }

        // The Tip Jar is the exception that shows the rule: it issues no
        // command, so there is nothing to wait for and nothing a wait could
        // protect - the save and the acknowledge run at the delivery with the
        // queue non-empty exactly as they do with it empty.
        [Test]
        public void A_tip_jar_saves_and_acknowledges_at_the_call_with_the_queue_non_empty()
        {
            var f = new Fixture();
            f.Enter();
            f.IAPManager.RequestPurchase(ProductId.TipJarSmall);
            f.QueueATap();

            f.IAPManager.Update(f.Now);

            Assert.AreEqual(new[] { 0 }, f.Saves.ToArray(), "saved at the delivery, with nothing acknowledged yet");
            Assert.AreEqual(f.Store.Purchases.Single().transactionId, f.Store.Acknowledged.Single());
            Assert.AreEqual(BigNumber.Zero, f.Cash, "and the queued tap is still waiting");

            f.Session.Drain();

            Assert.AreEqual((BigNumber)1, f.Cash, "which the drain runs");
            Assert.AreEqual(1, f.Saves.Count, "nothing was owed a second save");
        }
    }
}
