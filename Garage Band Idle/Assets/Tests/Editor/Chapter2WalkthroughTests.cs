using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Editor;
using RidiculousGaming.GarageBandIdle.Story;
using UnityEditor;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Chapter 2 played through GameSession over the IMPORTED assets - loaded by
    // path and assembled through the same Compose seam boot uses, so what these
    // drive is the shipping content rather than a fixture shaped like it.
    //
    // The chapter's whole income is gig completions, so the unit of play here is
    // a CYCLE and not a press: the calendar takes the gig, the bar fills over
    // its own seconds, and the completion fires the draw's lump. A gig is manual
    // until its booked flag is set, so each cycle selects the bar again.
    // Thresholds are reached by cycling or ticking UNTIL a fact holds rather
    // than by a hand-counted number of cycles: the count is an artifact of the
    // tuning, and re-deriving it here would be a second copy of the content doc
    // that goes stale the first time a number moves.
    public class Chapter2WalkthroughTests
    {
        // Computed amounts within tolerance, never bit-exact: BigDouble's
        // base-10 mantissa is binary-inexact for most values, so an exact
        // compare would pass or fail on the luck of the inputs.
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(),
                Math.Max(1e-9, Math.Abs(expected) * 1e-12), what ?? string.Empty);

        // ---- the fixture ----

        private class Chapter2
        {
            public static readonly DateTime Start = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

            public readonly RootDefinition RootDef;
            public readonly ChapterDefinition Ch1Def;
            public readonly ChapterDefinition Ch2Def;
            public readonly TierDefinition Tier2Def;

            public readonly CurrencyDefinition Records, Roadies, Ch2Records, Cash, Fans;
            public readonly ProducerDefinition CrowdOpenMic;
            public readonly GeneratorDefinition OpenMicDraw;
            public readonly UpgradeDefinition StandingOpenMic, CrowdWork1, BulkBooking;
            public readonly GroupDefinition TheCalendar;
            public readonly BarDefinition OpenMic;
            public readonly StoryBeatDefinition StoryCh2End;

            public readonly RootScopeState Root;
            public readonly ChapterScopeState Ch2;
            public readonly TierScopeState Tier2;
            public readonly GameSession Session;

            // The knobs the session was built on, held so a later reader can
            // hand the same asset to anything built over this session.
            public readonly GameConfig ConfigAsset;

            public DateTime Now = Start;

            // The real asset's numbers (section 9); the asset itself is settings
            // rather than content, and is hand-made with slice D.
            private static GameConfig Config()
            {
                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.maxGameSpeed = 4;
                config.minimumAwaySeconds = 180;
                config.idleCapSeconds = 14400;
                return config;
            }

            public Chapter2()
            {
                RootDef = AssetDatabase.LoadAssetAtPath<RootDefinition>(
                    ChapterJsonImporter.AssetRootPath + "/root/root.asset");
                Ch1Def = AssetDatabase.LoadAssetAtPath<ChapterDefinition>(
                    ChapterJsonImporter.AssetRootPath + "/ch1/ch1.asset");
                Ch2Def = AssetDatabase.LoadAssetAtPath<ChapterDefinition>(
                    ChapterJsonImporter.AssetRootPath + "/ch2/ch2.asset");
                Assert.IsNotNull(RootDef, "root.json has not been imported - run Garage Band Idle/Import Content.");
                Assert.IsNotNull(Ch1Def, "chapter-01.json has not been imported - run Garage Band Idle/Import Content.");
                Assert.IsNotNull(Ch2Def, "chapter-02.json has not been imported - run Garage Band Idle/Import Content.");
                Tier2Def = (TierDefinition)Ch2Def.children.Single();

                Records = Find(RootDef.declaredCurrencies, "records");
                Roadies = Find(RootDef.declaredCurrencies, "roadies");
                Ch2Records = Find(Ch2Def.declaredCurrencies, "ch2_records");
                StoryCh2End = Find(Ch2Def.storyBeats, "story_ch2_end");
                Cash = Find(Tier2Def.declaredCurrencies, "cash");
                Fans = Find(Tier2Def.declaredCurrencies, "fans");
                CrowdOpenMic = Find(Tier2Def.producers, "crowd_open_mic");
                OpenMicDraw = Find(Tier2Def.generators, "open_mic_draw");
                StandingOpenMic = Find(Tier2Def.upgrades, "standing_open_mic");
                CrowdWork1 = Find(Tier2Def.upgrades, "crowd_work_1");
                BulkBooking = Find(Tier2Def.upgrades, "bulk_booking");
                TheCalendar = Find(Tier2Def.groups, "the_calendar");
                OpenMic = Find(Tier2Def.bars, "open_mic");

                // The roster boot composes: chapter 2 is a sibling of chapter 1,
                // and the unlock below is what keeps it shut until 1 is done.
                Root = ScopeState.Build(ComposedContent.Compose(RootDef, new[] { Ch1Def, Ch2Def }));
                Ch2 = (ChapterScopeState)TestNavigation.Node(Root, Ch2Def);
                Tier2 = (TierScopeState)TestNavigation.Node(Root, Tier2Def);
                ConfigAsset = Config();
                Session = new GameSession(Root, ConfigAsset);
            }

            private static T Find<T>(IEnumerable<T> definitions, string id) where T : Definition =>
                definitions.Single(d => d.Id == id);

            // The switch a live player makes, with the root flag a finished
            // chapter 1 leaves: the stamp is now, so no idle window exists to
            // claim. Walkthrough 5 moves its own stamp back first.
            public void Enter()
            {
                Root.flags.Add("ch1_complete");
                Ch2.lastActiveUtc = Now;
                Session.SwitchChapter(Ch2, Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
            }

            // Backgrounding and coming back, which is the path 13.4 drives: the
            // session drops the chapter, the stamp moves back to when play
            // stopped, and the switch in computes the window from it.
            public void GoAway(double seconds)
            {
                Session.SwitchChapter(null, Now);
                Ch2.lastActiveUtc = Now.AddSeconds(-seconds);
                Session.SwitchChapter(Ch2, Now);
            }

            public GameContext Ctx(ScopeState scope) => new GameContext(scope, Now);

            public void Tick(double seconds)
            {
                Now = Now.AddSeconds(seconds);
                Session.Tick(seconds, Now);
            }

            // A press of Work the Crowd, and nothing else: the bar is the clock
            // in this chapter, so a press that also advanced time would mix the
            // tap's own yield into the fill the same second pays for.
            public void Tap(int times = 1)
            {
                for (var i = 0; i < times; i++)
                    Session.FireProducer(Ctx(Tier2), CrowdOpenMic);
            }

            // The screen's toggle: the calendar takes the one gig this step
            // runs, which sits inside maxActive 6 and leaves the rest off.
            public void Select(BarDefinition bar) =>
                Session.SetActiveMembers(Ctx(Tier2), TheCalendar, new Definition[] { bar });

            // One manual cycle of the open mic: select it, then let it fill. The
            // gig is time-fed at 1 unit per second, so its fillAmount IS its
            // length in seconds and the length comes from the content.
            public void PlayGig()
            {
                Select(OpenMic);
                Tick(OpenMic.fillAmount.ToDouble() / OpenMic.fillRate.ToDouble());
            }

            public void PlayUntil(Func<bool> held, string what, int maxGigs = 400)
            {
                for (var i = 0; i < maxGigs && !held(); i++)
                    PlayGig();
                Assert.IsTrue(held(), $"playing the open mic never reached: {what}");
            }

            public BigNumber Balance(ScopeState home, CurrencyDefinition currency) => home.balances[currency.Id];

            public BigNumber Earned(ScopeState home, CurrencyDefinition currency) => home.earnedTotals[currency.Id];

            public BigNumber Progress(BarDefinition bar) =>
                Tier2.barProgress.TryGetValue(bar.Id, out var stored) ? stored : BigNumber.Zero;

            public bool Selected(BarDefinition bar) =>
                Tier2.activeMembers.TryGetValue(TheCalendar.Id, out var active) && active.Contains(bar.Id);

            public int Owned(GeneratorDefinition generator) =>
                Tier2.generatorCounts.TryGetValue(generator.Id, out var count) ? count : 0;

            public void Buy(GeneratorDefinition generator)
            {
                PlayUntil(() => Purchasing.CanBuy(Ctx(Tier2), generator, 1), $"{generator.Id} affordable");
                var owned = Owned(generator);
                Session.TryBuy(Ctx(Tier2), generator, 1);
                Assert.AreEqual(owned + 1, Tier2.generatorCounts[generator.Id], generator.Id);
            }

            public void Buy(UpgradeDefinition upgrade)
            {
                PlayUntil(() => Purchasing.CanBuy(Ctx(Tier2), upgrade), $"{upgrade.Id} affordable");
                Session.TryBuy(Ctx(Tier2), upgrade);
                Assert.IsTrue(Tier2.purchasedUpgrades.Contains(upgrade.Id), upgrade.Id);
            }

            // The run state the standing booking makes: three Open Mic Draws,
            // the booking bought, and the gig selected and repeating. Its last
            // cycle completed, so the bar sits at zero progress.
            public void BookTheOpenMic()
            {
                Enter();
                Buy(OpenMicDraw);
                Buy(OpenMicDraw);
                Buy(OpenMicDraw);
                Buy(StandingOpenMic);
                Select(OpenMic);
            }
        }

        // ---- 1. the first minute ----

        [Test]
        public void Entering_ch2_pays_the_opening_cash_and_the_first_draw_opens_the_gig()
        {
            var f = new Chapter2();
            f.Enter();

            // opening_cash is the only trigger the switch-in sweep collects:
            // collection precedes execution, so the reveals reading what it
            // deposits wait for the next pass (12.5).
            AssertClose(5, f.Balance(f.Tier2, f.Cash), "the chapter opens with 5 Cash");
            Assert.AreEqual((BigNumber)0, f.Balance(f.Tier2, f.Fans), "fans");
            Assert.IsEmpty(f.Tier2.generatorCounts, "nothing is hired yet");
            Assert.IsFalse(f.Ch2.flags.Contains("bookings_revealed"),
                "the earned total the deposit wrote is read by the NEXT sweep");
            Assert.IsFalse(f.OpenMic.availableWhen.Evaluate(f.Ctx(f.Tier2)),
                "the gig wants a draw before it can be booked");

            // The draw is priced at exactly the opening lump, which is the
            // authored point of the 5 Cash.
            f.Session.TryBuy(f.Ctx(f.Tier2), f.OpenMicDraw, 1);
            Assert.AreEqual((BigNumber)0, f.Balance(f.Tier2, f.Cash), "the first draw costs the whole lump");
            Assert.IsTrue(f.OpenMic.availableWhen.Evaluate(f.Ctx(f.Tier2)),
                "OwnedCountAtLeast(open_mic_draw, 1) opened the gig");
            Assert.IsTrue(f.Ch2.flags.Contains("bookings_revealed"),
                "and the buy's own sweep read the earned total");
        }

        // ---- 2. the first lump ----

        [Test]
        public void The_first_open_mic_pays_its_lump_and_leaves_the_manual_team_off()
        {
            var f = new Chapter2();
            f.Enter();
            f.Session.TryBuy(f.Ctx(f.Tier2), f.OpenMicDraw, 1);

            f.Select(f.OpenMic);
            Assert.IsTrue(f.Selected(f.OpenMic), "the gig is the one the calendar is running");
            f.Tick(5);

            // One draw owned, no Records and no Buzz banked and no Roadies
            // stationed, so every factor on the lump is 1: the completion pays
            // the authored 3 Cash and 0.5 Fans times the owned count of 1.
            AssertClose(3, f.Balance(f.Tier2, f.Cash), "the gig's cash lump");
            AssertClose(0.5, f.Balance(f.Tier2, f.Fans), "and its fans");
            Assert.AreEqual((BigNumber)0, f.Progress(f.OpenMic), "the manual team returns to zero");
            Assert.IsFalse(f.Selected(f.OpenMic), "and leaves the set until the player books it again");
            Assert.IsTrue(f.Ch2.flags.Contains("bookings_revealed"), "the reveal is a ch2 flag a tier trigger set");
        }

        // ---- 3. working the crowd ----

        [Test]
        public void Working_the_crowd_adds_a_second_per_tap_and_doubles_with_stage_banter()
        {
            var f = new Chapter2();
            f.Enter();
            f.Session.TryBuy(f.Ctx(f.Tier2), f.OpenMicDraw, 1);
            f.Select(f.OpenMic);

            // The producer pays the BAR one unit of progress, and the gig fills
            // at 1 unit per second, so a press is worth one second of the five.
            f.Tap();
            AssertClose(1, f.Progress(f.OpenMic), "one press, one second of the gig");
            AssertClose(5, f.OpenMic.fillAmount.ToDouble(), "of five");

            // Stage Banter is a x2 on the work_the_crowd tag the producer
            // carries, so the same press moves the bar twice as far.
            f.PlayUntil(() => f.Balance(f.Tier2, f.Cash) >= 300, "300 cash for Stage Banter");
            f.Buy(f.CrowdWork1);
            f.Select(f.OpenMic);
            Assert.AreEqual((BigNumber)0, f.Progress(f.OpenMic), "the last cycle settled and zeroed the bar");
            f.Tap();
            AssertClose(2, f.Progress(f.OpenMic), "the press pays 2 after crowd_work_1");
        }

        // ---- 4. the standing booking ----

        [Test]
        public void The_standing_booking_makes_the_gig_repeat_and_keeps_it_selected()
        {
            var f = new Chapter2();
            f.BookTheOpenMic();

            Assert.AreEqual(3, f.Owned(f.OpenMicDraw), "three draws is the booking's gate");
            Assert.IsTrue(f.Ch2.flags.Contains("booked_open_mic"), "the payload flag is a ch2 fact");
            Assert.IsTrue(f.Ch2.flags.Contains("standing_open_mic_revealed"),
                "and the reveal its own trigger set at three draws");

            var cash = f.Balance(f.Tier2, f.Cash);
            var fans = f.Balance(f.Tier2, f.Fans);
            f.Tick(10);

            // 1 unit per second over 10 seconds is two crossings of the gig's 5,
            // and each completion fires the draw at its owned count of 3: 2 x 3
            // x 3 Cash and 2 x 0.5 x 3 Fans.
            AssertClose(18, f.Balance(f.Tier2, f.Cash) - cash, "two completions of the three-draw lump");
            AssertClose(3, f.Balance(f.Tier2, f.Fans) - fans, "and their fans");
            Assert.IsTrue(f.Selected(f.OpenMic), "a booked gig stays on the calendar");
            Assert.AreEqual((BigNumber)0, f.Progress(f.OpenMic), "10 seconds less the two 5s it paid out");

            // The residual is retained rather than discarded, which is what
            // separates the repeating settlement from the manual one.
            f.Tick(2);
            AssertClose(2, f.Progress(f.OpenMic), "two seconds carried toward the next completion");
            Assert.IsTrue(f.Selected(f.OpenMic));
        }

        // ---- 5. an hour away ----

        [Test]
        public void An_hour_away_pays_the_standing_booking_and_deposits_it_on_the_claim()
        {
            var f = new Chapter2();
            f.BookTheOpenMic();
            Assert.AreEqual((BigNumber)0, f.Progress(f.OpenMic), "the window starts on an empty bar");
            Assert.IsTrue(f.Selected(f.OpenMic), "and on a gig left running");

            var cash = f.Balance(f.Tier2, f.Cash);
            var fans = f.Balance(f.Tier2, f.Fans);
            f.GoAway(3600);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);

            // The fill is a rate, so idle_base's wildcard x0.5 halves it: 0.5
            // units per second over 3600 seconds is 1800, and the gig's 5 makes
            // that 360 completions. Each fires the draw at its owned count of 3,
            // with no Records, Buzz or Roadies on the lump.
            var bar = f.Session.CurrentOffer.bars.Single();
            Assert.AreSame(f.OpenMic, bar.bar);
            Assert.AreEqual(360, bar.completions, "3600 s at 0.5/s over a 5 unit gig");
            AssertClose(360 * 3 * 3, Line(f, f.Cash).amount, "cash");
            AssertClose(360 * 0.5 * 3, Line(f, f.Fans).amount, "fans");
            Assert.IsEmpty(f.Session.CurrentOffer.draws, "a time-fed gig drinks nothing");
            Assert.AreEqual(cash, f.Balance(f.Tier2, f.Cash), "an offer deposits nothing until it settles");

            f.Session.ClaimIdle(f.Now);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            AssertClose(3240, f.Balance(f.Tier2, f.Cash) - cash, "the claim deposits the cash line");
            AssertClose(540, f.Balance(f.Tier2, f.Fans) - fans, "and the fans line");
            Assert.AreEqual((BigNumber)0, f.Progress(f.OpenMic), "1800 of fill is a whole number of gigs");
        }

        // ---- 6. the EP ----

        [Test]
        public void The_ep_pays_five_records_and_resets_the_run_leaving_the_booking()
        {
            var f = new Chapter2();
            f.BookTheOpenMic();

            // The run's own fans plus the remainder, so the release reads
            // exactly 300 earned this round and pays floor(sqrt(300 / 10)) = 5.
            f.Ctx(f.Tier2).Deposit(f.Fans.Id, 300 - f.Earned(f.Tier2, f.Fans));
            AssertClose(300, f.Earned(f.Tier2, f.Fans), "300 fans earned this round");

            // A direct deposit is not a transaction, so the reveal that reads it
            // fires on the next tick's sweep.
            f.Tick(1);
            Assert.IsTrue(f.Ch2.flags.Contains("album"), "reveal_release opened The Release at 300 fans");

            Assert.IsTrue(f.Tier2Def.rung.IsOffered(f.Ctx(f.Tier2)));
            f.Session.TryRung(f.Ctx(f.Tier2));

            // One evaluation, two targets.
            AssertClose(5, f.Balance(f.Root, f.Records), "records");
            AssertClose(5, f.Balance(f.Ch2, f.Ch2Records), "ch2_records");

            // The reset re-arms opening_cash, and the transaction's own closing
            // sweep is what re-fires it - so the new run opens funded.
            AssertClose(5, f.Balance(f.Tier2, f.Cash), "the next run's opening lump");
            Assert.AreEqual((BigNumber)0, f.Balance(f.Tier2, f.Fans), "fans go with the run");
            Assert.IsEmpty(f.Tier2.generatorCounts, "the draws are hired again from scratch");
            Assert.IsEmpty(f.Tier2.barProgress, "and the calendar is clear");
            Assert.IsTrue(f.Ch2.flags.Contains("booked_open_mic"),
                "the booking is a ch2 fact, so it survives the EP");
        }

        // ---- 7. bulk booking ----

        [Test]
        public void Bulk_booking_autobuys_the_draw_every_tick_and_survives_the_ep()
        {
            var f = new Chapter2();
            f.Enter();
            f.Ctx(f.Ch2).Deposit(f.Ch2Records.Id, 8);
            f.Ctx(f.Tier2).Deposit(f.Cash.Id, 250000);
            f.Session.TryBuy(f.Ctx(f.Tier2), f.BulkBooking);
            Assert.IsTrue(f.Ch2.flags.Contains("bulk_booked"), "the payload wrote the chapter-lifetime flag");

            // The switch is an effect naming gig_draw, read as liveness rather
            // than as a factor, so the tick's purchase phase buys the largest
            // count the bank allows - the same number the row's button would.
            f.Ctx(f.Tier2).Deposit(f.Cash.Id, 1000);
            var affordable = Purchasing.MaxAffordable(f.Ctx(f.Tier2), f.OpenMicDraw);
            Assert.Greater(affordable, 1, "1000 cash covers several draws at base 5 and growth 1.3");
            f.Tick(1);
            Assert.AreEqual(affordable, f.Owned(f.OpenMicDraw), "the tick bought the whole affordable count");
            Assert.IsFalse(Purchasing.CanBuy(f.Ctx(f.Tier2), f.OpenMicDraw, 1),
                "and stopped at the first count the bank could not cover");

            f.Ctx(f.Tier2).Deposit(f.Fans.Id, 300);
            Assert.IsTrue(f.Tier2Def.rung.IsOffered(f.Ctx(f.Tier2)));
            f.Session.TryRung(f.Ctx(f.Tier2));
            Assert.IsTrue(f.Ch2.flags.Contains("bulk_booked"), "the flag outlives the run that bought it");
            Assert.IsEmpty(f.Tier2.generatorCounts, "the draws went with the run");

            // The fresh run's 5 Cash buys exactly one draw at its base 5, with
            // no second one affordable - the switch needs no re-purchase.
            f.Tick(1);
            Assert.AreEqual(1, f.Owned(f.OpenMicDraw), "the reopened trigger's lump, spent by the switch");
        }

        // ---- 8. the talent show ----

        [Test]
        public void The_talent_show_pays_a_roadie_and_resets_the_chapter()
        {
            var f = new Chapter2();
            f.BookTheOpenMic();
            f.Ctx(f.Ch2).Deposit(f.Ch2Records.Id, 120);

            // The capstone's ExecuteRung banks the live run through the EP's own
            // gate before the wipe reaches it, so the run's fans are made to
            // meet that gate: floor(sqrt(300 / 10)) = 5.
            f.Ctx(f.Tier2).Deposit(f.Fans.Id, 300 - f.Earned(f.Tier2, f.Fans));
            Assert.IsTrue(f.Ch2Def.rung.IsOffered(f.Ctx(f.Ch2)), "120 EPs clears the capstone's gate");
            f.Session.TryRung(f.Ctx(f.Ch2));

            AssertClose(5, f.Balance(f.Root, f.Records), "the live run banked");
            AssertClose(1, f.Balance(f.Root, f.Roadies), "chapter 2 pays one Roadie");
            Assert.IsTrue(f.Root.flags.Contains("ch2_complete"));

            // ResetScope(ch2) is downward closed: the chapter's own facts go
            // with the tier's, and the chapter sits immediately replayable.
            AssertClose(0, f.Balance(f.Ch2, f.Ch2Records), "the gate counter zeroes");
            Assert.IsFalse(f.Ch2.flags.Contains("booked_open_mic"), "the booking goes with the chapter");
            Assert.IsEmpty(f.Ch2.flags, "every ch2 flag with it - the reveals and the battle latches");
            Assert.IsEmpty(f.Tier2.generatorCounts);
            Assert.IsEmpty(f.Tier2.barProgress);

            // The closing beat reads the root flag the capstone set, which is
            // why it is homed where no chapter reset can reach it.
            Assert.IsTrue(f.StoryCh2End.IsAvailable(f.Ctx(f.Ch2)), "the end card is open once the show is won");
        }

        // ---- the draw row ----

        // A draw has yield entries only, so the row's line and the info screen
        // read UnitYield beside UnitRate: the price, then what one completion
        // pays, and the screen names the unit the row implies (12.11).
        [Test]
        public void The_draw_row_prints_the_lump_one_unit_pays_and_the_screen_names_the_unit()
        {
            var f = new Chapter2();
            f.Enter();

            Assert.AreEqual("5.00 Cash => 3.00 Cash, 0.50 Fans",
                UI.GeneratorRowUI.CostAndYieldText(f.Ctx(f.Tier2), f.OpenMicDraw));
            Assert.AreEqual("Producing: nothing",
                UI.GeneratorInfoUI.ProductionText(f.Ctx(f.Tier2), f.OpenMicDraw, 0));

            f.Ctx(f.Tier2).Deposit(f.Cash.Id, 100);
            f.Session.TryBuy(f.Ctx(f.Tier2), f.OpenMicDraw, 3);
            Assert.AreEqual("Pays: 9.00 Cash, 1.50 Fans per gig",
                UI.GeneratorInfoUI.ProductionText(f.Ctx(f.Tier2), f.OpenMicDraw, 3),
                "three draws, one completion");
        }

        private static IdleOfferLine Line(Chapter2 f, CurrencyDefinition currency) =>
            f.Session.CurrentOffer.lines.Find(l => l.target == currency);
    }
}
