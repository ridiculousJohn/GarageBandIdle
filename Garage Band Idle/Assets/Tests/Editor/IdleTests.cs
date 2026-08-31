using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Save;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // SwitchChapter's idle half and ClaimIdle. THE STAMP IS THE PENDING CLAIM:
    // the offer is transient, computed once over [stamp, B], and settlement
    // pays the stored lines and advances the stamp to B. The arithmetic
    // everywhere: the amp pays cash at 0.5/s live, and the authored idle base
    // halves it to 0.25/s idle.
    public class IdleTests
    {
        private static GameConfig Config(double minimumAway = 180, double idleCap = 14400)
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.minimumAwaySeconds = minimumAway;
            config.idleCapSeconds = idleCap;
            return config;
        }

        // Computed amounts are asserted within tolerance, never bit-exact:
        // BigDouble's base-10 mantissa is binary-inexact for most values (the
        // default cap's 1.44e4 included), so an exact compare would pass or
        // fail on the luck of the inputs rather than on the arithmetic.
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly GameSession Session;

            public Fixture(GameConfig config = null)
            {
                Tree.Tier1.generatorCounts["practice_amp"] = 1;
                Session = new GameSession(Tree.Root, config != null ? config : Config());
            }
        }

        // A second chapter beside ch1 with its own currency and source, and
        // the states rebuilt to include it.
        private class TwoChapters
        {
            public readonly TestTree Tree = new();
            public readonly RootScopeState Root;
            public readonly ChapterScopeState Ch1;
            public readonly ScopeState Tier1;
            public readonly ChapterScopeState Ch2;
            public readonly GameSession Session;

            public TwoChapters()
            {
                var ch2Def = TestTree.MakeChapter("ch2");
                var merch = TestTree.DeclareCurrency(ch2Def, "merch");
                var merchPress = TestTree.MakeDefinition<ProducerDefinition>("merch_press");
                merchPress.produces.Add(TestTree.Entry(merch, Stat.Rate, 2));
                ch2Def.producers.Add(merchPress);
                Tree.Chapters.Add(ch2Def);

                Root = ScopeState.Build(Tree.Content);
                Ch1 = (ChapterScopeState)Root.FindInSubtree(Tree.Ch1Def);
                Tier1 = Root.FindInSubtree(Tree.Tier1Def);
                Ch2 = (ChapterScopeState)Root.FindInSubtree(ch2Def);
                Tier1.generatorCounts["practice_amp"] = 1;
                Session = new GameSession(Root, Config());
            }
        }

        // A tier source paying the ROOT-homed currency, so an offer carries a
        // line whose home sits above the chapter.
        private static void AddRecordsPress(TestTree tree, double rate)
        {
            var press = TestTree.MakeDefinition<ProducerDefinition>("records_press");
            press.produces.Add(TestTree.Entry(tree.Records, Stat.Rate, rate));
            tree.Tier1Def.producers.Add(press);
        }

        private static IdleOfferLine Line(GameSession session, CurrencyDefinition currency) =>
            session.CurrentOffer.lines.Find(l => l.currency == currency);

        // ---- the stamps ----

        // A chapter never LEFT owes no idle (12.3). Ch2 carries a starter rate
        // and has never been entered, so measured from the stamp's default a
        // first visit would hand over a capped offer for a chapter the player
        // has not played. This is the case no construction-time stamp reaches:
        // the switch lands nine hours into a session the player sat through, so
        // there is no boot and no load anywhere near it. The sibling test above
        // sets Ch2's stamp by hand to keep it quiet; that workaround is the
        // symptom this covers.
        [Test]
        public void A_first_switch_into_a_never_entered_chapter_owes_no_idle()
        {
            var w = new TwoChapters();
            w.Ch1.lastActiveUtc = w.Tree.Now;
            w.Session.SwitchChapter(w.Ch1, w.Tree.Now);

            var later = w.Tree.Now.AddHours(9);
            w.Session.SwitchChapter(w.Ch2, later);

            Assert.AreEqual(SessionPhase.Live, w.Session.Phase);
            Assert.IsNull(w.Session.CurrentOffer);
            Assert.AreEqual(later, w.Ch2.lastActiveUtc, "entry stamped what it found unstamped");
            Assert.AreEqual(BigNumber.Zero, w.Ch2.balances["merch"], "nothing accrued for a chapter never played");
        }

        [Test]
        public void Switch_away_settles_the_offer_undoubled_and_stamps_its_window()
        {
            var w = new TwoChapters();
            w.Ch1.lastActiveUtc = w.Tree.Now.AddSeconds(-1000);
            w.Session.SwitchChapter(w.Ch1, w.Tree.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, w.Session.Phase);
            // The ad callback's write - an exit path pays the undoubled value.
            w.Session.CurrentOffer.doubled = true;

            var later = w.Tree.Now.AddSeconds(60);
            w.Ch2.lastActiveUtc = later;   // the incoming side stays quiet
            w.Session.SwitchChapter(w.Ch2, later);

            AssertClose(250, w.Tier1.balances["cash"]);                       // 0.25/s x 1000, x1 not x2
            Assert.AreEqual(w.Tree.Now, w.Ch1.lastActiveUtc);                 // the window's end, not the switch moment
            Assert.AreEqual(SessionPhase.Live, w.Session.Phase);
            Assert.IsNull(w.Session.CurrentOffer);
        }

        // Settlement commits every line the offer promised, even when paying
        // one closes another's gate. The offer judged them together against one
        // snapshot; re-asking per line would refuse the second after the first
        // had banked, leaving the stamp unadvanced and the offer live to pay
        // the first one again.
        [Test]
        public void One_lines_deposit_never_refuses_a_later_line()
        {
            var f = new Fixture();
            f.Tree.Tier1.flags.Add("fans_revealed");            // band's line joins the offer
            f.Tree.Cash.activeWhen = new Not
            {
                condition = new CurrencyAtLeast { currency = f.Tree.Fans, threshold = 1 }
            };
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);

            Assert.IsTrue(f.Session.ClaimIdle(f.Tree.Now));

            // Fans settles first and shuts cash's gate; cash is paid anyway.
            AssertClose(175, f.Tree.Tier1.balances["fans"]);     // 0.175/s x 1000
            AssertClose(250, f.Tree.Tier1.balances["cash"]);     // 0.25/s x 1000
            Assert.AreEqual(f.Tree.Now, f.Tree.Ch1.lastActiveUtc, "the stamp advanced with the payment");
            Assert.IsNull(f.Session.CurrentOffer);
        }

        [Test]
        public void Backgrounding_drops_the_offer_and_leaves_the_stamp()
        {
            var f = new Fixture();
            var stamp = f.Tree.Now.AddSeconds(-1000);
            f.Tree.Ch1.lastActiveUtc = stamp;
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.IsNotNull(f.Session.CurrentOffer);

            f.Session.SwitchChapter(null, f.Tree.Now.AddSeconds(60));

            Assert.AreEqual(SessionPhase.NoChapter, f.Session.Phase);
            Assert.IsNull(f.Session.CurrentOffer);
            Assert.AreEqual(stamp, f.Tree.Ch1.lastActiveUtc);                 // the unpaid window stays open
            Assert.AreEqual(BigNumber.Zero, f.Tree.Tier1.balances["cash"]);
        }

        [Test]
        public void A_same_chapter_switch_neither_stamps_nor_recomputes()
        {
            var f = new Fixture();
            var stamp = f.Tree.Now.AddSeconds(-100);   // under the minimum: enters Live
            f.Tree.Ch1.lastActiveUtc = stamp;
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);

            // An hour of play later, switching to the current chapter must not
            // read the old stamp - that would mint an offer covering live play.
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now.AddSeconds(3600));

            Assert.AreEqual(stamp, f.Tree.Ch1.lastActiveUtc);
            Assert.IsNull(f.Session.CurrentOffer);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
        }

        [Test]
        public void A_rolled_back_clock_never_regresses_a_stamp_and_pays_no_phantom_idle()
        {
            // Backgrounding a LIVE chapter on a rolled-back clock keeps the
            // newer stamp.
            var f = new Fixture();
            var stamp = f.Tree.Now;
            f.Tree.Ch1.lastActiveUtc = stamp;
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);   // elapsed 0: Live
            f.Session.SwitchChapter(null, f.Tree.Now.AddSeconds(-500));
            Assert.AreEqual(stamp, f.Tree.Ch1.lastActiveUtc);

            // The reset re-stamp is monotonic too.
            f.Tree.Ch1.Clear(f.Tree.Now.AddSeconds(-500));
            Assert.AreEqual(stamp, f.Tree.Ch1.lastActiveUtc);
            f.Tree.Ch1.Clear(f.Tree.Now.AddSeconds(10));
            Assert.AreEqual(f.Tree.Now.AddSeconds(10), f.Tree.Ch1.lastActiveUtc);

            // The recovered clock pays only what passed since the stamp.
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now.AddSeconds(20));
            Assert.IsNull(f.Session.CurrentOffer);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);

            // Switch-away to another chapter on a rolled-back clock.
            var w = new TwoChapters();
            w.Ch1.lastActiveUtc = w.Tree.Now;
            w.Ch2.lastActiveUtc = w.Tree.Now;
            w.Session.SwitchChapter(w.Ch1, w.Tree.Now);
            w.Session.SwitchChapter(w.Ch2, w.Tree.Now.AddSeconds(-500));
            Assert.AreEqual(w.Tree.Now, w.Ch1.lastActiveUtc);
        }

        // ---- the offer computation ----

        [Test]
        public void Switch_in_computes_rate_under_the_idle_circumstance_per_paid_currency()
        {
            var f = new Fixture();
            AddRecordsPress(f.Tree, 1);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            // Two lines, a tier-homed one and a root-homed one, each holding
            // its home reference; the window's end is the computation moment.
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            var offer = f.Session.CurrentOffer;
            Assert.AreEqual(2, offer.lines.Count);
            Assert.AreEqual(f.Tree.Now, offer.windowEndUtc);
            var records = Line(f.Session, f.Tree.Records);
            var cash = Line(f.Session, f.Tree.Cash);
            AssertClose(500, records.amount);                  // 1/s halved by the base, x1000
            Assert.AreSame(f.Tree.Root, records.home);
            AssertClose(250, cash.amount);                     // 0.5/s halved by the base, x1000
            Assert.AreSame(f.Tree.Tier1, cash.home);
        }

        [Test]
        public void Elapsed_over_the_cap_pays_the_cap()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-100000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            AssertClose(3600, f.Session.CurrentOffer.lines[0].amount);   // 0.25/s x the default 14400s cap
        }

        [Test]
        public void A_live_only_modifier_contributes_nothing_to_the_offer()
        {
            var f = new Fixture();
            var liveOnly = TestTree.MakeDefinition<ModifierDefinition>("live_only");
            liveOnly.appliesWhen = new Not { condition = new IdleAccumulation() };
            liveOnly.effects.Add(new Effect { currencyId = "cash", stat = Stat.Rate, multiplier = 2 });
            f.Tree.RootDef.modifiers.Add(liveOnly);
            f.Tree.RootDef.permanentModifiers.Add(liveOnly);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            AssertClose(250, f.Session.CurrentOffer.lines[0].amount);
        }

        [Test]
        public void A_negative_clock_offers_nothing()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(1000);   // the stamp sits in the future

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            Assert.IsNull(f.Session.CurrentOffer);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
        }

        [Test]
        public void Away_time_under_the_minimum_skips_the_offer()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-100);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            Assert.IsNull(f.Session.CurrentOffer);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
        }

        [Test]
        public void A_blocking_record_in_the_subtree_skips_the_offer()
        {
            var f = new Fixture();
            f.Tree.Tier1.activeEvent = new ActiveEvent { eventId = "timed_gig", remainingSeconds = 100 };
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.IsNull(f.Session.CurrentOffer);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);

            // An untimed record blocks nothing - blocksIdle is derived from
            // the timer, and the idle path asks the event.
            var g = new Fixture();
            g.Tree.Tier1.activeEvent = new ActiveEvent { eventId = "open_mic", remainingSeconds = 0 };
            g.Tree.Ch1.lastActiveUtc = g.Tree.Now.AddSeconds(-1000);
            g.Session.SwitchChapter(g.Tree.Ch1, g.Tree.Now);
            Assert.IsNotNull(g.Session.CurrentOffer);
        }

        [Test]
        public void An_unclaimed_offer_recomputes_from_the_unmoved_stamp()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            AssertClose(250, f.Session.CurrentOffer.lines[0].amount);

            // Backgrounded unclaimed: the stamp never moved, so re-entry much
            // later recomputes over the grown window, capped.
            f.Session.SwitchChapter(null, f.Tree.Now.AddSeconds(10));
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now.AddSeconds(50000));
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            AssertClose(3600, f.Session.CurrentOffer.lines[0].amount);   // 0.25/s x the default 14400s cap

            // Settled, the advanced stamp offers nothing again.
            Assert.IsTrue(f.Session.ClaimIdle(f.Tree.Now.AddSeconds(50000)));
            AssertClose(3600, f.Tree.Tier1.balances["cash"]);
            f.Session.SwitchChapter(null, f.Tree.Now.AddSeconds(50010));
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now.AddSeconds(50020));
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            Assert.IsNull(f.Session.CurrentOffer);
            AssertClose(3600, f.Tree.Tier1.balances["cash"]);
        }

        // ---- the deferred sweep around the offer ----

        [Test]
        public void No_sweep_runs_during_the_switch_that_creates_an_offer()
        {
            var f = new Fixture();
            // The dangerous shape: a root trigger legally resetting the
            // descendant chapter - a sweep during the switch would re-stamp
            // the unpaid window away before the dialog presents it.
            var rootTrigger = TestTree.MakeDefinition<TriggerDefinition>("root_reset");
            rootTrigger.condition = new Always();
            rootTrigger.actions.Add(new ResetScope { scope = f.Tree.Ch1Def });
            f.Tree.RootDef.triggers.Add(rootTrigger);
            f.Tree.Tier1Trigger.condition = new Always();
            f.Tree.Tier1Trigger.actions.Add(new SetFlag { flagId = "fans_revealed" });
            AddRecordsPress(f.Tree, 1);   // a root-homed line survives the reset the claim's sweep runs
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            Assert.IsNotNull(f.Session.CurrentOffer);
            Assert.IsEmpty(f.Tree.Root.firedTriggers);
            Assert.IsEmpty(f.Tree.Tier1.firedTriggers);

            // The claim's own sweep is where the root trigger fires - after
            // the deposits, which the surviving root-homed line proves.
            var facts = f.Tree.Ch1.facts;
            Assert.IsTrue(f.Session.ClaimIdle(f.Tree.Now));
            Assert.IsTrue(f.Tree.Root.firedTriggers.Contains("root_reset"));
            AssertClose(500, f.Tree.Root.balances["records"]);
            Assert.AreNotSame(facts, f.Tree.Ch1.facts);   // the reset took the chapter's facts; the deposits were already banked
            Assert.IsNull(f.Session.CurrentOffer);
        }

        [Test]
        public void Both_triggers_fire_on_the_claims_own_sweep()
        {
            var f = new Fixture();
            var rootTrigger = TestTree.MakeDefinition<TriggerDefinition>("root_latch");
            rootTrigger.condition = new Always();
            rootTrigger.actions.Add(new SetFlag { flagId = "ch1_complete" });
            f.Tree.RootDef.triggers.Add(rootTrigger);
            f.Tree.Tier1Trigger.condition = new Always();
            f.Tree.Tier1Trigger.actions.Add(new SetFlag { flagId = "fans_revealed" });
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.IsEmpty(f.Tree.Root.flags);
            Assert.IsEmpty(f.Tree.Tier1.flags);

            f.Session.ClaimIdle(f.Tree.Now);
            Assert.IsTrue(f.Tree.Root.flags.Contains("ch1_complete"));
            Assert.IsTrue(f.Tree.Tier1.flags.Contains("fans_revealed"));
        }

        // ---- settlement ----

        [Test]
        public void ClaimIdle_pays_the_stored_lines_doubled_when_marked_and_stamps_the_window()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            f.Session.CurrentOffer.doubled = true;

            // Claimed well after the offer was computed: the deposit is the
            // stored amount, and the stamp advances to the window's end, not
            // the claim moment.
            Assert.IsTrue(f.Session.ClaimIdle(f.Tree.Now.AddSeconds(300)));

            AssertClose(500, f.Tree.Tier1.balances["cash"]);   // 250 x2
            Assert.AreEqual(f.Tree.Now, f.Tree.Ch1.lastActiveUtc);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            Assert.IsNull(f.Session.CurrentOffer);

            // Replay is refused by phase, and nothing re-deposits.
            Assert.IsFalse(f.Session.ClaimIdle(f.Tree.Now.AddSeconds(300)));
            AssertClose(500, f.Tree.Tier1.balances["cash"]);
        }

        [Test]
        public void A_relaunch_reoffers_from_the_saved_stamp()
        {
            var f = new Fixture();
            AddRecordsPress(f.Tree, 1);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.IsNotNull(f.Session.CurrentOffer);

            // Killed with the dialog up: the save carries the stamp and nothing
            // of the offer; the relaunch recomputes over the grown window.
            var json = SaveSystem.Serialize(f.Tree.Root);
            Assert.IsTrue(SaveSystem.TryDeserialize(json, f.Tree.Content, out var loaded));
            var loadedCh1 = (ChapterScopeState)loaded.FindInSubtree(f.Tree.Ch1Def);
            var loadedTier1 = loaded.FindInSubtree(f.Tree.Tier1Def);
            Assert.AreEqual(f.Tree.Now.AddSeconds(-1000), loadedCh1.lastActiveUtc);

            var session = new GameSession(loaded, Config());
            session.SwitchChapter(loadedCh1, f.Tree.Now.AddSeconds(1000));
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, session.Phase);
            AssertClose(500, session.CurrentOffer.lines.Find(l => l.currency == f.Tree.Cash).amount);      // 0.25/s x 2000
            AssertClose(1000, session.CurrentOffer.lines.Find(l => l.currency == f.Tree.Records).amount);  // 0.5/s x 2000

            Assert.IsTrue(session.ClaimIdle(f.Tree.Now.AddSeconds(1000)));
            AssertClose(500, loadedTier1.balances["cash"]);
            AssertClose(1000, loaded.balances["records"]);
            Assert.AreEqual(f.Tree.Now.AddSeconds(1000), loadedCh1.lastActiveUtc);
        }

        // ---- narrowing and placement ----

        [Test]
        public void A_currency_narrowed_idle_only_effect_scales_only_its_currencys_line()
        {
            var f = new Fixture();
            AddRecordsPress(f.Tree, 2);
            var cashIdle = TestTree.MakeDefinition<ModifierDefinition>("cash_idle");
            cashIdle.appliesWhen = new IdleAccumulation();
            cashIdle.effects.Add(new Effect { currencyId = "cash", stat = Stat.Rate, multiplier = 2 });
            f.Tree.RootDef.modifiers.Add(cashIdle);
            f.Tree.RootDef.permanentModifiers.Add(cashIdle);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            AssertClose(500, Line(f.Session, f.Tree.Cash).amount);      // 0.25/s doubled
            AssertClose(1000, Line(f.Session, f.Tree.Records).amount);  // 1/s, untouched
        }

        [Test]
        public void A_chapter_declared_idle_only_modifier_scales_only_its_own_chapters_offer()
        {
            var w = new TwoChapters();
            var ch1Idle = TestTree.MakeDefinition<ModifierDefinition>("ch1_idle");
            ch1Idle.appliesWhen = new IdleAccumulation();
            ch1Idle.effects.Add(new Effect { stat = Stat.Rate, multiplier = 2 });   // wildcard, chapter-placed
            w.Tree.Ch1Def.modifiers.Add(ch1Idle);
            w.Tree.Ch1Def.permanentModifiers.Add(ch1Idle);
            w.Ch1.lastActiveUtc = w.Tree.Now.AddSeconds(-1000);
            w.Ch2.lastActiveUtc = w.Tree.Now.AddSeconds(-1000);

            w.Session.SwitchChapter(w.Ch1, w.Tree.Now);
            AssertClose(500, w.Session.CurrentOffer.lines[0].amount);    // 0.25/s doubled by ch1's own tuning

            w.Session.SwitchChapter(w.Ch2, w.Tree.Now);   // settles ch1 out, enters ch2
            AssertClose(1000, w.Session.CurrentOffer.lines[0].amount);   // 2/s halved by the base alone
        }

        // ---- the current-chapter root fact ----

        [Test]
        public void Switching_in_records_the_chapter_at_root_and_backgrounding_leaves_it()
        {
            var f = new Fixture();
            Assert.IsNull(f.Tree.Root.currentChapterId);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now;

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual("ch1", f.Tree.Root.currentChapterId);

            f.Session.SwitchChapter(null, f.Tree.Now);
            Assert.AreEqual("ch1", f.Tree.Root.currentChapterId);
        }

        [Test]
        public void A_recorded_chapter_no_content_authors_is_dropped_at_load()
        {
            var f = new Fixture();
            f.Tree.Root.currentChapterId = "ch1";
            var kept = SaveSystem.Serialize(f.Tree.Root);
            Assert.IsTrue(SaveSystem.TryDeserialize(kept, f.Tree.Content, out var loaded));
            Assert.AreEqual("ch1", loaded.currentChapterId);

            f.Tree.Root.currentChapterId = "chapter_gone";
            var stale = SaveSystem.Serialize(f.Tree.Root);
            Assert.IsTrue(SaveSystem.TryDeserialize(stale, f.Tree.Content, out var cleared));
            Assert.IsNull(cleared.currentChapterId);
        }
    }
}
