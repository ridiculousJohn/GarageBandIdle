using System.Linq;
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

            // `author` runs against the DEFINITIONS before the tree is built,
            // so a test whose content names a scope gets the link pass over it
            // - and the session holds the tree that pass ran on.
            public Fixture(GameConfig config = null, System.Action<TestTree> author = null)
            {
                author?.Invoke(Tree);
                Tree.Rebuild();
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

            // `author` runs against the DEFINITIONS before the tree is built,
            // for the same reason Fixture takes one: the gather is compiled at
            // build, so a modifier authored afterward sits on no plan.
            public TwoChapters(System.Action<TestTree> author = null)
            {
                var ch2Def = TestTree.MakeChapter("ch2");
                var merch = TestTree.DeclareCurrency(ch2Def, "merch");
                var merchPress = TestTree.MakeDefinition<ProducerDefinition>("merch_press");
                merchPress.produces.Add(TestTree.Entry(merch, Stat.Rate, 2));
                ch2Def.producers.Add(merchPress);
                Tree.Chapters.Add(ch2Def);
                author?.Invoke(Tree);

                Root = ScopeState.Build(Tree.Content);
                Ch1 = (ChapterScopeState)TestNavigation.Node(Root, Tree.Ch1Def);
                Tier1 = TestNavigation.Node(Root, Tree.Tier1Def);
                Ch2 = (ChapterScopeState)TestNavigation.Node(Root, ch2Def);
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
            session.CurrentOffer.lines.Find(l => l.target == currency);

        // What the window's bars drew from one currency, held beside the lines:
        // a line is what the window PAID and a draw is what it drank, and the
        // claim deposits the one and spends the other (section 9).
        private static IdleOfferLine Draw(GameSession session, CurrencyDefinition currency) =>
            session.CurrentOffer.draws.Find(d => d.target == currency);

        // A bar declared at tier1 with a group of its own, so a test selects it
        // without touching the covers' group (12.7). Authored against the
        // definitions, so it runs inside the fixture's `author` step.
        private static BarDefinition AddBar(TestTree tree, string id, double fillAmount, double fillRate,
                                            Condition repeatWhen = null)
        {
            var bar = TestTree.MakeDefinition<BarDefinition>(id);
            bar.fillAmount = fillAmount;
            bar.fillRate = fillRate;
            bar.repeatWhen = repeatWhen;
            tree.Tier1Def.bars.Add(bar);
            var group = TestTree.MakeDefinition<GroupDefinition>(id + "_group");
            group.maxActive = 1;
            group.members.Add(bar);
            tree.Tier1Def.groups.Add(group);
            return bar;
        }

        // The generator a completion fires: one yield entry into fans, which
        // nothing else pays while the reveal is shut, so the line the offer
        // carries is the payout alone.
        private static GeneratorDefinition AddCrew(TestTree tree, double perUnit)
        {
            var crew = TestTree.MakeDefinition<GeneratorDefinition>("road_crew");
            crew.availableWhen = new Always();
            crew.costCurrency = tree.Cash;
            crew.baseCost = 10;
            crew.growth = 1.15;
            crew.produces.Add(TestTree.Entry(tree.Fans, Stat.Yield, perUnit));
            tree.Tier1Def.generators.Add(crew);
            return crew;
        }

        // Selection as a FACT, the way the covers are selected elsewhere: an
        // offer test is not a SetActiveMembers test.
        private static void Select(TestTree tree, params BarDefinition[] bars)
        {
            foreach (var bar in bars)
                tree.Tier1.activeMembers[bar.Id + "_group"] =
                    new System.Collections.Generic.HashSet<string> { bar.Id };
        }

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

        // Settlement pays the lines as they stand (section 9): a Pass owner's
        // offer is computed doubled at entry, so an exit pays what the dialog
        // was showing - shown and paid never differ. The ad callback's doubling
        // is never met here, because it settles in the same transaction.
        [Test]
        public void Switch_away_settles_a_Pass_owners_doubled_offer_and_stamps_its_window()
        {
            var w = new TwoChapters();
            w.Root.entitlements.Add("backstage_pass");
            w.Ch1.lastActiveUtc = w.Tree.Now.AddSeconds(-1000);
            w.Session.SwitchChapter(w.Ch1, w.Tree.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, w.Session.Phase);
            AssertClose(500, w.Session.CurrentOffer.lines[0].amount, "0.25/s x 1000, computed doubled");

            var later = w.Tree.Now.AddSeconds(60);
            w.Ch2.lastActiveUtc = later;   // the incoming side stays quiet
            w.Session.SwitchChapter(w.Ch2, later);

            AssertClose(500, w.Tier1.balances["cash"]);                       // 0.25/s x 1000, doubled
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

            f.Session.ClaimIdle(f.Tree.Now);

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
            var f = new Fixture(author: tree => AddRecordsPress(tree, 1));
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

        // Idle pays every rate target the tick would, one line per target
        // (12.9), and the claim lands each through the write its kind takes.
        [Test]
        public void An_offer_carries_a_generator_target_line_and_the_claim_grants_it()
        {
            var f = new Fixture(author: tree =>
            {
                var crew = TestTree.MakeDefinition<ProducerDefinition>("road_crew");
                crew.produces.Add(TestTree.Entry(tree.PracticeAmp, Stat.Rate, 1));
                tree.Tier1Def.producers.Add(crew);
            });
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            var line = f.Session.CurrentOffer.lines.Find(l => l.target == f.Tree.PracticeAmp);
            Assert.IsNotNull(line, "the amp is a rate target like any other");
            Assert.AreSame(f.Tree.Tier1, line.home, "the generator's declaring scope");
            // Away pays half of all of it (section 9): the idle base's count
            // wildcard halves the grant as its rate wildcard halves a currency.
            AssertClose(500, line.amount, "1/s over the window, halved while away");

            f.Session.ClaimIdle(f.Tree.Now);

            AssertClose(500, f.Tree.Tier1.grantedCounts["practice_amp"]);
        }

        // The purchase phase is the TICK's (12.9), and no offline simulation of
        // the buys a window would have made is built - so a switch left on
        // through a long away window pays the window's rate and nothing else.
        [Test]
        public void An_idle_window_pays_its_rate_and_buys_nothing()
        {
            var f = new Fixture(author: tree =>
            {
                var carrier = TestTree.MakeDefinition<ModifierDefinition>("hands_free");
                carrier.effects.Add(new Effect { target = "practice_amp", stat = Stat.AutoBuy, multiplier = 1 });
                tree.Tier1Def.modifiers.Add(carrier);
            });
            f.Tree.Tier1.modifierStacks["hands_free"] = 1;
            f.Tree.Tier1.balances["cash"] = 1000;
            f.Tree.Tier1.earnedTotals["cash"] = 1000;
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            f.Session.ClaimIdle(f.Tree.Now);

            AssertClose(1250, f.Tree.Tier1.balances["cash"], "the one amp's 0.25/s idle over the window");
            Assert.AreEqual(1, f.Tree.Tier1.generatorCounts["practice_amp"], "the count the window began with");
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
            var f = new Fixture(author: tree =>
            {
                var liveOnly = TestTree.MakeDefinition<ModifierDefinition>("live_only");
                liveOnly.appliesWhen = new Not { condition = new IdleAccumulation() };
                liveOnly.effects.Add(new Effect { currencyId = "cash", stat = Stat.Rate, multiplier = 2 });
                tree.RootDef.modifiers.Add(liveOnly);
                tree.RootDef.permanentModifiers.Add(liveOnly);
            });
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
            f.Session.ClaimIdle(f.Tree.Now.AddSeconds(50000));
            AssertClose(3600, f.Tree.Tier1.balances["cash"]);
            f.Session.SwitchChapter(null, f.Tree.Now.AddSeconds(50010));
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now.AddSeconds(50020));
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            Assert.IsNull(f.Session.CurrentOffer);
            // No frame ran between the claim and the backgrounding, so nothing
            // is banked for the switch to settle: the balance is the claim's.
            AssertClose(3600, f.Tree.Tier1.balances["cash"]);
        }

        // ---- the deferred sweep around the offer ----

        [Test]
        public void No_sweep_runs_during_the_switch_that_creates_an_offer()
        {
            // The dangerous shape: a root trigger legally resetting the
            // descendant chapter - a sweep during the switch would re-stamp
            // the unpaid window away before the dialog presents it.
            var f = new Fixture(author: tree =>
            {
                var rootTrigger = TestTree.MakeDefinition<TriggerDefinition>("root_reset");
                rootTrigger.condition = new Always();
                rootTrigger.actions.Add(new ResetScope { scope = tree.Ch1Def });
                tree.RootDef.triggers.Add(rootTrigger);
                tree.Tier1Trigger.condition = new Always();
                tree.Tier1Trigger.actions.Add(new SetFlag { flagId = "fans_revealed" });
                // A root-homed line survives the reset the claim's sweep runs.
                AddRecordsPress(tree, 1);
            });
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            Assert.IsNotNull(f.Session.CurrentOffer);
            Assert.IsEmpty(f.Tree.Root.firedTriggers);
            Assert.IsEmpty(f.Tree.Tier1.firedTriggers);

            // The claim's own sweep is where the root trigger fires - after
            // the deposits, which the surviving root-homed line proves.
            var facts = f.Tree.Ch1.facts;
            f.Session.ClaimIdle(f.Tree.Now);
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
        public void ClaimIdle_pays_the_stored_lines_and_stamps_the_window()
        {
            var f = new Fixture();
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            // Claimed well after the offer was computed: the deposit is the
            // stored amount, and the stamp advances to the window's end, not
            // the claim moment.
            f.Session.ClaimIdle(f.Tree.Now.AddSeconds(300));

            AssertClose(250, f.Tree.Tier1.balances["cash"]);   // 0.25/s x 1000
            Assert.AreEqual(f.Tree.Now, f.Tree.Ch1.lastActiveUtc);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            Assert.IsNull(f.Session.CurrentOffer);

            // Replay is refused by phase, and nothing re-deposits.
            bool? settled = null;
            f.Session.ClaimIdle(f.Tree.Now.AddSeconds(300), ran => settled = ran);
            Assert.AreEqual(false, settled, "a settled offer is claimed once");
            AssertClose(250, f.Tree.Tier1.balances["cash"]);
        }

        [Test]
        public void A_relaunch_reoffers_from_the_saved_stamp()
        {
            var f = new Fixture(author: tree => AddRecordsPress(tree, 1));
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.IsNotNull(f.Session.CurrentOffer);

            // Killed with the dialog up: the save carries the stamp and nothing
            // of the offer; the relaunch recomputes over the grown window.
            var json = SaveSystem.Serialize(f.Tree.Root);
            Assert.IsTrue(SaveSystem.TryDeserialize(json, f.Tree.Content, out var loaded));
            var loadedCh1 = (ChapterScopeState)TestNavigation.Node(loaded, f.Tree.Ch1Def);
            var loadedTier1 = TestNavigation.Node(loaded, f.Tree.Tier1Def);
            Assert.AreEqual(f.Tree.Now.AddSeconds(-1000), loadedCh1.lastActiveUtc);

            var session = new GameSession(loaded, Config());
            session.SwitchChapter(loadedCh1, f.Tree.Now.AddSeconds(1000));
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, session.Phase);
            AssertClose(500, session.CurrentOffer.lines.Find(l => l.target == f.Tree.Cash).amount);      // 0.25/s x 2000
            AssertClose(1000, session.CurrentOffer.lines.Find(l => l.target == f.Tree.Records).amount);  // 0.5/s x 2000

            session.ClaimIdle(f.Tree.Now.AddSeconds(1000));
            AssertClose(500, loadedTier1.balances["cash"]);
            AssertClose(1000, loaded.balances["records"]);
            Assert.AreEqual(f.Tree.Now.AddSeconds(1000), loadedCh1.lastActiveUtc);
        }

        // ---- narrowing and placement ----

        [Test]
        public void A_currency_narrowed_idle_only_effect_scales_only_its_currencys_line()
        {
            var f = new Fixture(author: tree =>
            {
                AddRecordsPress(tree, 2);
                var cashIdle = TestTree.MakeDefinition<ModifierDefinition>("cash_idle");
                cashIdle.appliesWhen = new IdleAccumulation();
                cashIdle.effects.Add(new Effect { currencyId = "cash", stat = Stat.Rate, multiplier = 2 });
                tree.RootDef.modifiers.Add(cashIdle);
                tree.RootDef.permanentModifiers.Add(cashIdle);
            });
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            AssertClose(500, Line(f.Session, f.Tree.Cash).amount);      // 0.25/s doubled
            AssertClose(1000, Line(f.Session, f.Tree.Records).amount);  // 1/s, untouched
        }

        [Test]
        public void A_chapter_declared_idle_only_modifier_scales_only_its_own_chapters_offer()
        {
            var w = new TwoChapters(author: tree =>
            {
                var ch1Idle = TestTree.MakeDefinition<ModifierDefinition>("ch1_idle");
                ch1Idle.appliesWhen = new IdleAccumulation();
                ch1Idle.effects.Add(new Effect { stat = Stat.Rate, multiplier = 2 });   // wildcard, chapter-placed
                tree.Ch1Def.modifiers.Add(ch1Idle);
                tree.Ch1Def.permanentModifiers.Add(ch1Idle);
            });
            w.Ch1.lastActiveUtc = w.Tree.Now.AddSeconds(-1000);
            w.Ch2.lastActiveUtc = w.Tree.Now.AddSeconds(-1000);

            w.Session.SwitchChapter(w.Ch1, w.Tree.Now);
            AssertClose(500, w.Session.CurrentOffer.lines[0].amount);    // 0.25/s doubled by ch1's own tuning

            w.Session.SwitchChapter(w.Ch2, w.Tree.Now);   // settles ch1 out, enters ch2
            AssertClose(1000, w.Session.CurrentOffer.lines[0].amount);   // 2/s halved by the base alone
        }

        // ---- bars over the window (section 9) ----

        // A bar has everything the computation needs: a fill rate over time,
        // and a dependency the same computation resolves first. Nothing runs
        // the tick over the window - the value is computed.
        [Test]
        public void A_time_fed_repeating_bar_pays_its_yield_once_per_completion()
        {
            BarDefinition drill = null;
            var f = new Fixture(author: tree =>
            {
                var crew = AddCrew(tree, 5);
                drill = AddBar(tree, "drill", 10, 1, repeatWhen: new Always());
                drill.onComplete.Add(new FireGeneratorYield { generator = crew });
            });
            f.Tree.Tier1.generatorCounts["road_crew"] = 1;
            Select(f.Tree, drill);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            // The idle base halves the FILL, so the bar completes half as often
            // and each lump is whole: half the income, one rule.
            var bar = f.Session.CurrentOffer.bars.Single();
            Assert.AreSame(drill, bar.bar);
            Assert.AreSame(f.Tree.Tier1, bar.home);
            Assert.AreEqual(50, bar.completions, "0.5/s of fill over 1000s, at 10 a completion");
            AssertClose(0, bar.progress, "nothing left over");
            AssertClose(250, Line(f.Session, f.Tree.Fans).amount, "5 fans a completion, whole");
        }

        // What a bar consumes is a dependency, not a payment: the currency can
        // give what it holds at the stamp plus what the window pays into it,
        // less what earlier bars already drew. The window's inflow stays the
        // line and the take is its own draw, so the claim deposits one and
        // spends the other - and a draw is a spend, so the earned total moves
        // by the inflow alone.
        [Test]
        public void A_consuming_bar_is_capped_by_the_balance_plus_the_inflow_and_draws_what_it_took()
        {
            BarDefinition drill = null;
            var f = new Fixture(author: tree =>
            {
                drill = AddBar(tree, "drill", 1000000, 1);
                drill.consumes.Add(new ConsumesEntry { currency = tree.Rehearsal, amount = 1 });
            });
            f.Tree.Tier1.flags.Add("rehearsal_revealed");       // the Jam's 0.5/s rate entry joins
            f.Tree.Tier1.balances["rehearsal"] = 100;
            Select(f.Tree, drill);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            // It wants 500 over the window; 100 banked plus the 250 the window
            // pays in is the whole of what it can have.
            AssertClose(350, f.Session.CurrentOffer.bars.Single().progress, "capped by what it drinks");
            AssertClose(250, Line(f.Session, f.Tree.Rehearsal).amount, "the window's own inflow");
            AssertClose(350, Draw(f.Session, f.Tree.Rehearsal).amount, "and what the bar drank of it");

            f.Session.ClaimIdle(f.Tree.Now);

            AssertClose(0, f.Tree.Tier1.balances["rehearsal"], "100 banked plus 250 in, less the 350 drunk");
            AssertClose(250, f.Tree.Tier1.earnedTotals["rehearsal"], "a draw is a spend, so only the inflow was earned");
            AssertClose(350, f.Tree.Tier1.barProgress["drill"]);
        }

        // The tick's settlement order, over the window: the first bar drinks and
        // the second sees what is left, exactly as a live segment gives them.
        [Test]
        public void Two_bars_on_one_consumed_currency_take_in_settlement_order()
        {
            BarDefinition first = null;
            BarDefinition second = null;
            var f = new Fixture(author: tree =>
            {
                first = AddBar(tree, "first", 1000000, 1);
                first.consumes.Add(new ConsumesEntry { currency = tree.Rehearsal, amount = 1 });
                second = AddBar(tree, "second", 1000000, 1);
                second.consumes.Add(new ConsumesEntry { currency = tree.Rehearsal, amount = 1 });
            });
            f.Tree.Tier1.flags.Add("rehearsal_revealed");
            Select(f.Tree, first, second);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            var bars = f.Session.CurrentOffer.bars;
            Assert.AreSame(first, bars[0].bar, "declaration order is the settlement order");
            Assert.AreSame(second, bars[1].bar);
            AssertClose(250, bars[0].progress, "the whole window's inflow");
            AssertClose(0, bars[1].progress, "and the one behind it stalls");
            AssertClose(250, Line(f.Session, f.Tree.Rehearsal).amount, "the inflow is the line either way");
            AssertClose(250, Draw(f.Session, f.Tree.Rehearsal).amount, "the first bar drew all of it");
        }

        // Some bars pay VERY slowly, and time away goes toward their progress:
        // a window that crosses nothing still moves the bar.
        [Test]
        public void A_slow_bar_carries_its_progress_with_no_completion()
        {
            BarDefinition slow = null;
            var f = new Fixture(author: tree => slow = AddBar(tree, "slow", 1000000, 1));
            Select(f.Tree, slow);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            var bar = f.Session.CurrentOffer.bars.Single();
            AssertClose(500, bar.progress, "0.5/s of fill over the window");
            Assert.AreEqual(0, bar.completions);

            f.Session.ClaimIdle(f.Tree.Now);

            AssertClose(500, f.Tree.Tier1.barProgress["slow"]);
            Assert.IsFalse(f.Tree.Tier1.fillCounts.ContainsKey("slow"), "a bar that fills once counts nothing");
        }

        // The dialog exists for a balance the player is owed, and bar progress
        // is a write, never a row (section 9). A window in which nothing paid
        // and nothing was drunk - the amp unowned, a time-fed bar part-way
        // through its cycle - has nothing to show, so the claim runs on entry:
        // the progress and the stamp are written and no dialog is raised.
        [Test]
        public void A_window_that_changes_no_balance_settles_on_entry_with_no_dialog()
        {
            BarDefinition slow = null;
            var f = new Fixture(author: tree => slow = AddBar(tree, "slow", 1000000, 1));
            f.Tree.Tier1.generatorCounts["practice_amp"] = 0;
            Select(f.Tree, slow);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            Assert.AreEqual(SessionPhase.Live, f.Session.Phase, "nothing to show, so nothing to claim");
            Assert.IsNull(f.Session.CurrentOffer);
            AssertClose(500, f.Tree.Tier1.barProgress["slow"], "the fill the window earned is written on entry");
            Assert.AreEqual(f.Tree.Now, f.Tree.Ch1.lastActiveUtc, "and the window is settled");
        }

        // Progress is monotonic for a bar that fills once (12.7), so the fill
        // past the threshold is never drawn - and never paid for.
        [Test]
        public void A_fill_once_bar_completes_once_and_stops_drinking()
        {
            BarDefinition once = null;
            var f = new Fixture(author: tree =>
            {
                once = AddBar(tree, "once", 100, 1);
                once.consumes.Add(new ConsumesEntry { currency = tree.Rehearsal, amount = 1 });
            });
            f.Tree.Tier1.balances["rehearsal"] = 10000;
            Select(f.Tree, once);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            var bar = f.Session.CurrentOffer.bars.Single();
            AssertClose(100, bar.progress, "full, not the 500 the window would have filled");
            Assert.AreEqual(1, bar.completions);
            Assert.IsNull(Line(f.Session, f.Tree.Rehearsal), "nothing paid rehearsal over the window");
            AssertClose(100, Draw(f.Session, f.Tree.Rehearsal).amount, "and it drew only the fill it took");
            Assert.IsTrue(bar.off, "complete, so it leaves its groups at the claim");

            f.Session.ClaimIdle(f.Tree.Now);
            AssertClose(9900, f.Tree.Tier1.balances["rehearsal"]);
            Assert.IsFalse(f.Tree.Tier1.activeMembers["once_group"].Contains("once"), "the slot is free for the next choice");
        }

        // The manual team ran one cycle while away, so it comes back off: the
        // claim removes it from every group listing it, and selecting it again
        // is how the player runs it again (12.7).
        [Test]
        public void The_manual_team_completes_once_and_is_off_after_the_claim()
        {
            BarDefinition team = null;
            var f = new Fixture(author: tree =>
                team = AddBar(tree, "team", 10, 1, repeatWhen: new Not { condition = new Always() }));
            Select(f.Tree, team);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            var bar = f.Session.CurrentOffer.bars.Single();
            Assert.AreEqual(1, bar.completions, "one cycle, whatever the window would have filled");
            AssertClose(0, bar.progress, "back to zero, the excess discarded");
            Assert.IsTrue(bar.off);
            Assert.IsTrue(f.Tree.Tier1.activeMembers["team_group"].Contains("team"), "nothing is written yet");

            f.Session.ClaimIdle(f.Tree.Now);

            Assert.IsFalse(f.Tree.Tier1.activeMembers["team_group"].Contains("team"));
        }

        // The claim writes what the computation carried and runs the completion
        // list once per completion, with the payment actions skipped - those
        // were the lines.
        [Test]
        public void The_claim_writes_the_counts_and_runs_a_non_payment_completion_once_per_crossing()
        {
            BarDefinition loop = null;
            var f = new Fixture(author: tree =>
            {
                loop = AddBar(tree, "loop", 10, 1, repeatWhen: new Always());
                loop.onComplete.Add(new AddCurrency { currencies = { tree.Fans }, amount = 1 });
            });
            Select(f.Tree, loop);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.IsEmpty(f.Tree.Tier1.fillCounts, "an offer writes nothing");

            f.Session.ClaimIdle(f.Tree.Now);

            Assert.AreEqual(50, f.Tree.Tier1.fillCounts["loop"], "one per crossing");
            AssertClose(0, f.Tree.Tier1.barProgress["loop"], "the residual the computation carried");
            AssertClose(50, f.Tree.Tier1.balances["fans"], "the completion action ran once per crossing");
        }

        // Twice the Cash, never twice the cover: the ad doubles what the window
        // PAID, and a bar's progress and the drink that bought it are neither.
        [Test]
        public void Double_it_doubles_the_lines_and_leaves_the_draws_and_the_bars_alone()
        {
            BarDefinition once = null;
            var f = new Fixture(author: tree =>
            {
                once = AddBar(tree, "once", 100, 1);
                once.consumes.Add(new ConsumesEntry { currency = tree.Rehearsal, amount = 1 });
            });
            f.Tree.Tier1.balances["rehearsal"] = 10000;
            Select(f.Tree, once);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            f.Session.DoubleAndClaimIdle(f.Tree.Now);

            AssertClose(500, f.Tree.Tier1.balances["cash"], "the amp's 250, doubled");
            AssertClose(9900, f.Tree.Tier1.balances["rehearsal"], "the take is not doubled");
            AssertClose(100, f.Tree.Tier1.barProgress["once"], "and neither is the progress");
        }

        // A Pass owner's offer is computed as if the ad had been watched, and
        // the same rule holds at computation: the lines, and nothing else.
        [Test]
        public void A_Pass_owners_offer_doubles_its_lines_and_leaves_the_draws_alone()
        {
            BarDefinition once = null;
            var f = new Fixture(author: tree =>
            {
                once = AddBar(tree, "once", 100, 1);
                once.consumes.Add(new ConsumesEntry { currency = tree.Rehearsal, amount = 1 });
            });
            f.Tree.Root.entitlements.Add("backstage_pass");
            f.Tree.Tier1.balances["rehearsal"] = 10000;
            Select(f.Tree, once);
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            AssertClose(500, Line(f.Session, f.Tree.Cash).amount, "0.25/s over the window, doubled");
            AssertClose(100, Draw(f.Session, f.Tree.Rehearsal).amount, "the take stands as computed");
            AssertClose(100, f.Session.CurrentOffer.bars.Single().progress);
        }

        // Nothing is written at computation, bars included: the unpaid window
        // simply stays open, and re-entry recomputes it over the grown one.
        [Test]
        public void An_unclaimed_offer_recomputes_its_bars_with_no_progress_written()
        {
            BarDefinition slow = null;
            var f = new Fixture(author: tree => slow = AddBar(tree, "slow", 1000000, 1));
            Select(f.Tree, slow);
            var stamp = f.Tree.Now.AddSeconds(-1000);
            f.Tree.Ch1.lastActiveUtc = stamp;

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            AssertClose(500, f.Session.CurrentOffer.bars.Single().progress);
            Assert.IsEmpty(f.Tree.Tier1.barProgress, "the offer wrote nothing");

            f.Session.SwitchChapter(null, f.Tree.Now);
            Assert.AreEqual(stamp, f.Tree.Ch1.lastActiveUtc, "the unpaid window stays open");
            Assert.IsEmpty(f.Tree.Tier1.barProgress);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now.AddSeconds(1000));
            AssertClose(1000, f.Session.CurrentOffer.bars.Single().progress, "the grown window, from the same stamp");
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
