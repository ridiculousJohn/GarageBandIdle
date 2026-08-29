using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Editor;
using RidiculousGaming.GarageBandIdle.Events;
using UnityEditor;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The four walkthroughs of content doc section 13, played through
    // GameSession over the IMPORTED assets - loaded by path and assembled
    // through the same Compose seam boot uses, so what these drive is the
    // shipping content rather than a fixture shaped like it.
    //
    // A tap costs half a second of game time, the doc's own sustained 2
    // presses/sec, so production keeps running while the player taps and the
    // trace stays time coherent. Thresholds are reached by tapping or ticking
    // UNTIL a fact holds rather than by a hand-counted number of presses: the
    // count is an artifact of the tuning, and re-deriving it here would be a
    // second copy of the content doc that goes stale the first time a number
    // moves.
    public class Chapter1WalkthroughTests
    {
        // Computed amounts within tolerance, never bit-exact: BigDouble's
        // base-10 mantissa is binary-inexact for most values, so an exact
        // compare would pass or fail on the luck of the inputs. Scaled for the
        // idle claim's six-figure lines, where an absolute 1e-9 sits below the
        // representation itself.
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(),
                Math.Max(1e-9, Math.Abs(expected) * 1e-12), what ?? string.Empty);

        // ---- the fixture ----

        private class Chapter1
        {
            public static readonly DateTime Start = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

            public readonly RootDefinition RootDef;
            public readonly ChapterDefinition Ch1Def;
            public readonly TierDefinition Tier1Def;

            public readonly CurrencyDefinition Records, Roadies, Ch1Records, Cash, Fans, Rehearsal;
            public readonly ProducerDefinition TapProducer;
            public readonly GeneratorDefinition PracticeAmp, Drummer, Bassist;
            public readonly UpgradeDefinition StagePresence, PlayForCrowd, UnlockCovers, CutDemo;
            public readonly ModifierDefinition CoverBonus1, CoverBonus2, GjTap1;
            public readonly BarGroupDefinition LearnCovers;
            public readonly BarDefinition Cover1;
            public readonly EventDefinition GarageJam1;

            public readonly RootScopeState Root;
            public readonly ChapterScopeState Ch1;
            public readonly TierScopeState Tier1;
            public readonly GameSession Session;

            public DateTime Now = Start;

            // The real asset's numbers (section 9); the asset itself is
            // settings rather than content, and is hand-made with slice D.
            private static GameConfig Config()
            {
                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.maxGameSpeed = 4;
                config.minimumAwaySeconds = 180;
                config.idleCapSeconds = 14400;
                return config;
            }

            public Chapter1()
            {
                RootDef = AssetDatabase.LoadAssetAtPath<RootDefinition>(
                    ChapterJsonImporter.AssetRootPath + "/root/root.asset");
                Ch1Def = AssetDatabase.LoadAssetAtPath<ChapterDefinition>(
                    ChapterJsonImporter.AssetRootPath + "/ch1/ch1.asset");
                Assert.IsNotNull(RootDef, "root.json has not been imported - run Garage Band Idle/Import Content.");
                Assert.IsNotNull(Ch1Def, "chapter-01.json has not been imported - run Garage Band Idle/Import Content.");
                Tier1Def = (TierDefinition)Ch1Def.children.Single();

                Records = Find(RootDef.declaredCurrencies, "records");
                Roadies = Find(RootDef.declaredCurrencies, "roadies");
                Ch1Records = Find(Ch1Def.declaredCurrencies, "ch1_records");
                GjTap1 = Find(Ch1Def.modifiers, "gj_tap_1");
                Cash = Find(Tier1Def.declaredCurrencies, "cash");
                Fans = Find(Tier1Def.declaredCurrencies, "fans");
                Rehearsal = Find(Tier1Def.declaredCurrencies, "rehearsal");
                TapProducer = Find(Tier1Def.producers, "tap_producer");
                PracticeAmp = Find(Tier1Def.generators, "practice_amp");
                Drummer = Find(Tier1Def.generators, "drummer");
                Bassist = Find(Tier1Def.generators, "bassist");
                StagePresence = Find(Tier1Def.upgrades, "stage_presence");
                PlayForCrowd = Find(Tier1Def.upgrades, "play_for_crowd");
                UnlockCovers = Find(Tier1Def.upgrades, "unlock_covers");
                CutDemo = Find(Tier1Def.upgrades, "cut_demo");
                CoverBonus1 = Find(Tier1Def.modifiers, "cover_bonus_1");
                CoverBonus2 = Find(Tier1Def.modifiers, "cover_bonus_2");
                LearnCovers = Find(Tier1Def.barGroups, "learn_covers");
                Cover1 = Find(LearnCovers.bars, "cover_1");
                GarageJam1 = Find(Tier1Def.events, "garage_jam_1");

                Root = ScopeState.Build(ComposedContent.Compose(RootDef, new[] { Ch1Def }));
                Ch1 = (ChapterScopeState)Root.FindInSubtree(Ch1Def);
                Tier1 = (TierScopeState)Root.FindInSubtree(Tier1Def);
                Session = new GameSession(Root, Config());
            }

            private static T Find<T>(IEnumerable<T> definitions, string id) where T : Definition =>
                definitions.Single(d => d.Id == id);

            // The switch a live player makes: the stamp is now, so no idle
            // window exists to claim. Walkthrough 4 sets its own stamp first.
            public void Enter()
            {
                Ch1.lastActiveUtc = Now;
                Session.SwitchChapter(Ch1, Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
            }

            public GameContext Ctx(ScopeState scope) => new GameContext(scope, Now);

            public void Tick(double seconds)
            {
                Now = Now.AddSeconds(seconds);
                Session.Tick(seconds, Now);
            }

            // A press and the half second it takes at the doc's sustained rate.
            public void Tap(int times = 1)
            {
                for (var i = 0; i < times; i++)
                {
                    Assert.IsTrue(Session.FireProducer(Ctx(Tier1), TapProducer), "the tap fired");
                    Tick(0.5);
                }
            }

            public void TapUntil(Func<bool> held, string what, int maxTaps = 4000)
            {
                for (var i = 0; i < maxTaps && !held(); i++)
                    Tap();
                Assert.IsTrue(held(), $"tapping never reached: {what}");
            }

            public void TickUntil(Func<bool> held, string what, int maxSeconds = 4000)
            {
                for (var i = 0; i < maxSeconds && !held(); i++)
                    Tick(1);
                Assert.IsTrue(held(), $"waiting never reached: {what}");
            }

            public BigNumber Balance(ScopeState home, CurrencyDefinition currency) => home.balances[currency.Id];

            public BigNumber Earned(ScopeState home, CurrencyDefinition currency) => home.earnedTotals[currency.Id];

            public BigNumber Rate(CurrencyDefinition currency) => Producer.GetRate(Ctx(Ch1), currency);

            public BigNumber Progress(BarDefinition bar) =>
                Tier1.barProgress.TryGetValue(bar.Id, out var stored) ? stored : BigNumber.Zero;

            public double Elapsed => (Now - Start).TotalSeconds;

            public void Buy(GeneratorDefinition generator)
            {
                TapUntil(() => Purchasing.CanBuy(Ctx(Tier1), generator), $"{generator.Id} affordable");
                Assert.IsTrue(Session.TryBuy(Ctx(Tier1), generator), generator.Id);
            }

            public void Buy(UpgradeDefinition upgrade)
            {
                TapUntil(() => Purchasing.CanBuy(Ctx(Tier1), upgrade), $"{upgrade.Id} affordable");
                Assert.IsTrue(Session.TryBuy(Ctx(Tier1), upgrade), upgrade.Id);
            }

            // The doc's mid-chapter run (13.2 and 13.3): a banked Records count
            // with a live run at some fan total and one cover learned.
            public void SeedRun(double records, double fans)
            {
                Root.balances[Records.Id] = records;
                // The two counters track together through a first playthrough:
                // one AddCurrency evaluation pays both.
                Ch1.balances[Ch1Records.Id] = records;
                Tier1.balances[Fans.Id] = fans;
                Tier1.barProgress[Cover1.Id] = Cover1.fillAmount;
                Tier1.flags.Add("fans_revealed");
            }

            // The doc's 13.4 state: ten amps, five drummers, two bassists,
            // covers 1 and 2 learned, both reveals set, twenty records, no
            // roadies.
            public void SeedIdleState()
            {
                Tier1.generatorCounts[PracticeAmp.Id] = 10;
                Tier1.generatorCounts[Drummer.Id] = 5;
                Tier1.generatorCounts[Bassist.Id] = 2;
                Tier1.modifierStacks[CoverBonus1.Id] = 1;
                Tier1.modifierStacks[CoverBonus2.Id] = 1;
                Tier1.flags.Add("fans_revealed");
                Tier1.flags.Add("rehearsal_revealed");
                Root.balances[Records.Id] = 20;
            }

            public void AssertTierIsFresh()
            {
                Assert.AreEqual((BigNumber)0, Balance(Tier1, Cash), "cash");
                Assert.AreEqual((BigNumber)0, Balance(Tier1, Fans), "fans");
                Assert.AreEqual((BigNumber)0, Balance(Tier1, Rehearsal), "rehearsal");
                Assert.IsEmpty(Tier1.generatorCounts, "generators");
                Assert.IsEmpty(Tier1.purchasedUpgrades, "upgrade latches");
                Assert.IsEmpty(Tier1.flags, "reveal flags");
                Assert.IsEmpty(Tier1.barProgress, "bar progress");
                Assert.IsEmpty(Tier1.modifierStacks, "granted stacks");
            }
        }

        // ---- 13.1 normal release ----

        [Test]
        public void Walkthrough_1_the_first_demo_pays_three_records_and_resets_the_run()
        {
            var f = new Chapter1();
            f.Enter();

            // Nothing is revealed at t=0: the band region gates on a lifetime
            // earned total, and only the tap pays anything at all.
            Assert.IsFalse(f.PracticeAmp.IsAvailable(f.Ctx(f.Tier1)), "the band region is closed on a fresh run");
            AssertClose(0, f.Rate(f.Cash), "nothing pays a rate yet");
            Assert.IsFalse(f.Tier1Def.rung.IsOffered(f.Ctx(f.Tier1)), "the release wants fans and a cover");

            f.TapUntil(() => f.Earned(f.Tier1, f.Cash) >= 100, "100 cash earned");
            Assert.IsTrue(f.PracticeAmp.IsAvailable(f.Ctx(f.Tier1)), "EarnedTotalAtLeast(cash, 100) opened the band region");
            Assert.IsFalse(f.StagePresence.IsOffered(f.Ctx(f.Tier1)), "the gear region is still closed");

            f.TapUntil(() => f.Earned(f.Tier1, f.Cash) >= 250, "250 cash earned");
            Assert.IsTrue(f.StagePresence.IsOffered(f.Ctx(f.Tier1)),
                "the gear region gates on the earned total, with no flag of its own");

            // The upgrade is a pure latch; the flat bonus is tap_producer's
            // conditioned entry reading it, which is why the press doubles.
            f.Buy(f.StagePresence);
            var before = f.Balance(f.Tier1, f.Cash);
            f.Tap();
            AssertClose(2, f.Balance(f.Tier1, f.Cash) - before, "the press pays 2 after stage_presence");

            Assert.IsFalse(f.Drummer.IsAvailable(f.Ctx(f.Tier1)), "the drummer wants three amps");
            f.Buy(f.PracticeAmp);
            f.Buy(f.PracticeAmp);
            Assert.IsFalse(f.Drummer.IsAvailable(f.Ctx(f.Tier1)), "two amps is not three");
            f.Buy(f.PracticeAmp);
            Assert.IsTrue(f.Drummer.IsAvailable(f.Ctx(f.Tier1)), "OwnedCountAtLeast(practice_amp, 3)");
            f.Buy(f.Drummer);

            // The BAND's base trickle is the gated line (content doc section
            // 4); a bandmate's own 0.02 carries no condition (section 5), and
            // the drummer necessarily precedes the reveal because
            // play_for_crowd gates on owning one. So the reveal starts the
            // accrual that matters rather than uncovering a total already run
            // up - it just is not a hard zero before it.
            Assert.AreEqual((BigNumber)0, f.Balance(f.Tier1, f.Fans), "no fans banked at the moment the drummer lands");
            AssertClose(0.02, f.Rate(f.Fans), "the bandmate line alone, before the reveal");
            f.Buy(f.PlayForCrowd);
            Assert.IsTrue(f.Tier1.flags.Contains("fans_revealed"));
            // 0.35 base plus the drummer's own 0.02: band size IS the fan rate,
            // with no per-bandmate constant anywhere.
            AssertClose(0.37, f.Rate(f.Fans), "band trickle plus one bandmate");

            Assert.AreEqual((BigNumber)0, f.Balance(f.Tier1, f.Rehearsal), "rehearsal before its reveal");
            f.TickUntil(() => f.Balance(f.Tier1, f.Fans) >= 25, "25 fans");
            f.Buy(f.UnlockCovers);
            Assert.IsTrue(f.Tier1.flags.Contains("rehearsal_revealed"));
            AssertClose(0.5, f.Rate(f.Rehearsal), "the passive trickle joins with the reveal");

            // The cover drinks the pool the taps bank, at its OWN 2/s rather
            // than the pool's - a press pays one Rehearsal every half second,
            // which is exactly what the bar takes.
            Assert.IsTrue(f.Session.SetActiveBars(f.Ctx(f.Tier1), f.LearnCovers, new[] { f.Cover1 }));
            f.TapUntil(() => f.Progress(f.Cover1) >= f.Cover1.fillAmount, "cover_1 filled");
            Assert.AreEqual(1, f.Tier1.modifierStacks[f.CoverBonus1.Id],
                "a non-repeating completion leaves no derivable fact, so its reward is a grant");
            AssertClose(0.37 * 1.15, f.Rate(f.Fans), "the cover bonus lifts the fan rate");

            // Both legs are required, and here the cover is the one that holds:
            // the release stays shut on the fan count alone.
            Assert.Less(f.Balance(f.Tier1, f.Fans).ToDouble(), 50);
            Assert.IsFalse(f.Tier1Def.rung.IsOffered(f.Ctx(f.Tier1)), "50 fans is the other leg");

            f.TickUntil(() => f.Balance(f.Tier1, f.Fans) >= 50, "50 fans");
            var fans = f.Balance(f.Tier1, f.Fans).ToDouble();
            Assert.Less(fans, 80, $"released at {fans:F1} fans, inside the bracket paying 3 - floor(sqrt(f/5)) is 3 for f in [45, 80)");

            // The album flag is the chapter's, so the release region it reveals
            // outlives the run that unlocked it.
            f.Buy(f.CutDemo);
            Assert.IsTrue(f.Ch1.flags.Contains("album"));

            Assert.IsTrue(f.Tier1Def.rung.IsOffered(f.Ctx(f.Tier1)));
            Assert.IsTrue(f.Session.TryRung(f.Ctx(f.Tier1)), "the release fires");

            // One evaluation, two targets.
            AssertClose(3, f.Balance(f.Root, f.Records), "records");
            AssertClose(3, f.Balance(f.Ch1, f.Ch1Records), "ch1_records");
            f.AssertTierIsFresh();
            Assert.IsTrue(f.Ch1.flags.Contains("album"), "the chapter flag survives the tier reset");

            // Section 1's pacing target is a first demo around 5-6 minutes at
            // this tap rate. The band is wide because the doc's 352s is one
            // purchase interleaving and this script is another; what it catches
            // is a retune that puts the first demo seconds or hours away.
            Assert.That(f.Elapsed, Is.InRange(240, 480), $"first demo at {f.Elapsed:F0}s");
        }

        // ---- 13.2 event entry ----

        [Test]
        public void Walkthrough_2_the_garage_jam_banks_the_run_pauses_the_gear_and_pays_on_dismissal()
        {
            var f = new Chapter1();
            f.Enter();
            f.SeedRun(records: 17, fans: 60);
            f.Tier1.generatorCounts[f.PracticeAmp.Id] = 3;

            // What those three amps pay before the sprint, so the handicap
            // below is measured against a live number rather than an assumed
            // one: 3 x 0.5, lifted by 17 banked Records.
            AssertClose(1.5 * 1.34, f.Rate(f.Cash), "the gear pays before the jam");

            Assert.IsTrue(f.GarageJam1.IsAvailable(f.Ctx(f.Tier1)), "CurrencyAtLeast(records, 1) holds at 17");
            Assert.IsTrue(f.Session.TryStartEvent(f.Ctx(f.Tier1), f.GarageJam1));

            // onEntry's RestartScope banks through the release's OWN gate - 60
            // fans with a cover done meets it - and then clears the run, so
            // entry costs the player nothing already earned.
            AssertClose(20, f.Balance(f.Root, f.Records), "17 + floor(sqrt(60/5)) = 20");
            AssertClose(20, f.Balance(f.Ch1, f.Ch1Records));
            f.AssertTierIsFresh();

            var record = f.Tier1.activeEvent;
            Assert.IsNotNull(record, "the record lands in the FRESH payload the entry list made");
            Assert.AreEqual(f.GarageJam1.Id, record.eventId);
            Assert.AreEqual(60d, record.remainingSeconds);
            Assert.IsFalse(record.goalReached);

            // The same three amps, in the fresh run, now pay nothing: the gear
            // handicap zeroes every generator line by derivation, and the tap
            // is all that is left - at 1 x (1 + 0.02 x 20).
            f.Tier1.generatorCounts[f.PracticeAmp.Id] = 3;
            AssertClose(0, f.Rate(f.Cash), "the gear x0 handicap");
            f.Tap();
            AssertClose(1.4, f.Balance(f.Tier1, f.Cash), "the tap yield rides the Records multiplier");

            f.TapUntil(() => f.Balance(f.Tier1, f.Cash) >= 150, "the 150 cash goal");
            Assert.Greater(f.Tier1.activeEvent.remainingSeconds, 0,
                "the sprint fits inside the 60s timer at 2 presses/sec");
            Assert.IsTrue(f.Tier1.activeEvent.goalReached, "the sweep latched the goal");

            // An armed reward disarms the release. The other two legs are made
            // to hold here, so the guard is the only one that can refuse.
            f.Tier1.balances[f.Fans.Id] = 60;
            f.Tier1.barProgress[f.Cover1.Id] = f.Cover1.fillAmount;
            Assert.IsFalse(f.Tier1Def.rung.IsOffered(f.Ctx(f.Tier1)),
                "no reset may destroy an armed, unclaimed reward");

            // Spending back below the goal un-secures nothing - the latch is
            // the fact, not the balance.
            Assert.IsTrue(f.Session.TryBuy(f.Ctx(f.Tier1), f.PracticeAmp));
            Assert.Less(f.Balance(f.Tier1, f.Cash).ToDouble(), 150);
            Assert.IsTrue(f.Tier1.activeEvent.goalReached);

            Assert.IsTrue(f.Session.TryDismissEvent(f.Ctx(f.Tier1), f.GarageJam1));
            Assert.IsNull(f.Tier1.activeEvent, "the record is removed first, which is what reopens the guard");
            Assert.AreEqual(1, f.Ch1.modifierStacks[f.GjTap1.Id], "the bonus is granted at the chapter");
            Assert.IsTrue(f.Ch1.flags.Contains("gj1_done"));
            f.AssertTierIsFresh();

            // A fresh run starts with the bonus live: 1 x 1.25 x (1 + 0.02 x 20).
            f.Tap();
            AssertClose(1.75, f.Balance(f.Tier1, f.Cash), "+25% tap for the rest of the chapter");
        }

        [Test]
        public void Walkthrough_2_an_expired_jam_holds_the_host_and_dismisses_with_no_reward()
        {
            var f = new Chapter1();
            f.Enter();
            f.SeedRun(records: 17, fans: 60);

            Assert.IsTrue(f.Session.TryStartEvent(f.Ctx(f.Tier1), f.GarageJam1));
            f.TickUntil(() => f.Tier1.activeEvent.remainingSeconds <= 0, "the timer expiring", 120);

            // The record persists past expiry: the handicap keeps applying and
            // the host stays occupied until somebody dismisses it.
            Assert.IsNotNull(f.Tier1.activeEvent);
            Assert.IsFalse(f.Tier1.activeEvent.goalReached, "the goal was never met");
            Assert.IsFalse(f.Session.TryStartEvent(f.Ctx(f.Tier1), f.GarageJam1), "an occupied host refuses entry");

            Assert.IsTrue(f.Session.TryDismissEvent(f.Ctx(f.Tier1), f.GarageJam1));
            Assert.IsNull(f.Tier1.activeEvent);
            Assert.IsFalse(f.Ch1.modifierStacks.ContainsKey(f.GjTap1.Id), "no bonus without the goal");
            Assert.IsFalse(f.Ch1.flags.Contains("gj1_done"));
            // onEnd runs either way: the sprint clears, and nothing is lost
            // that the attempt did not itself create.
            f.AssertTierIsFresh();
        }

        // ---- 13.3 replay clear ----

        [Test]
        public void Walkthrough_3_the_capstone_banks_the_run_pays_a_roadie_and_resets_the_chapter()
        {
            var f = new Chapter1();
            f.Enter();
            f.SeedRun(records: 32, fans: 70);
            f.Ch1.flags.Add("album");

            Assert.IsTrue(f.Ch1Def.rung.IsOffered(f.Ctx(f.Ch1)), "32 banked records clears the gate of 30");
            Assert.IsTrue(f.Session.TryRung(f.Ctx(f.Ch1)));

            // ExecuteRung banks the live run through the release's own gate -
            // floor(sqrt(70/5)) = 3 - before the wipe reaches it.
            AssertClose(35, f.Balance(f.Root, f.Records), "the live run banked");
            AssertClose(1, f.Balance(f.Root, f.Roadies), "chapter 1's reward formula is the constant 1");
            Assert.IsTrue(f.Root.flags.Contains("ch1_complete"));

            // ResetScope(ch1) is downward closed: the chapter's own facts go
            // with the tier's, and the chapter sits immediately replayable.
            AssertClose(0, f.Balance(f.Ch1, f.Ch1Records), "the gate counter zeroes");
            Assert.IsEmpty(f.Ch1.flags, "album and the gj latches go with it");
            Assert.IsEmpty(f.Ch1.modifierStacks);
            f.AssertTierIsFresh();

            // The replay, with the new roadie stationed here: records x
            // roadie_total x roadie_active = 1.7 x 1.05 x 1.05, about 1.87.
            f.Root.roadieAllocation[f.Ch1.ScopeId] = 1;
            f.Tier1.generatorCounts[f.PracticeAmp.Id] = 1;
            AssertClose(0.5 * 1.7 * 1.05 * 1.05, f.Rate(f.Cash), "the income multiplier comes back at ~1.87x");

            // The fan floor does not move: fans carry no roadie-reachable tag,
            // which is the farm throttle.
            f.Tier1.generatorCounts[f.Drummer.Id] = 1;
            f.Tier1.flags.Add("fans_revealed");
            var boosted = f.Rate(f.Fans);
            f.Root.roadieAllocation.Clear();
            Assert.AreEqual(f.Rate(f.Fans), boosted, "the roadie reaches cash and never fans");
            AssertClose(0.37, boosted);
        }

        // ---- 13.4 four-hour idle claim ----

        [Test]
        public void Walkthrough_4_four_hours_away_offers_the_authored_arithmetic_and_doubles_on_claim()
        {
            var f = new Chapter1();
            f.SeedIdleState();

            f.Ch1.lastActiveUtc = f.Now.AddSeconds(-14400);
            f.Session.SwitchChapter(f.Ch1, f.Now);

            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            Assert.AreEqual(3, f.Session.CurrentOffer.lines.Count, "cash, fans and rehearsal");

            // Live rates are 84/s cash, 0.648025/s fans and 0.5/s rehearsal;
            // the authored idle base halves each, and the window is the 4h cap.
            AssertClose(604800, Line(f, f.Cash).amount, "cash");
            AssertClose(4665.78, Line(f, f.Fans).amount, "fans - the doc's 4,666 is display rounding");
            AssertClose(3600, Line(f, f.Rehearsal).amount, "rehearsal");
            Assert.AreEqual((BigNumber)0, f.Balance(f.Tier1, f.Cash), "an offer deposits nothing until it settles");

            // The ad callback's write; settlement doubles and stamps in one
            // transaction, which is the whole exactly-once mechanism.
            f.Session.CurrentOffer.doubled = true;
            var windowEnd = f.Session.CurrentOffer.windowEndUtc;
            Assert.IsTrue(f.Session.ClaimIdle(f.Now));

            AssertClose(1209600, f.Balance(f.Tier1, f.Cash));
            AssertClose(9331.56, f.Balance(f.Tier1, f.Fans));
            AssertClose(7200, f.Balance(f.Tier1, f.Rehearsal));
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            Assert.IsNull(f.Session.CurrentOffer);
            Assert.AreEqual(windowEnd, f.Ch1.lastActiveUtc, "the stamp advances to the window actually paid");

            // Bar progress moved zero - the pool banked instead, so the
            // returning player pours covers at their own rate.
            Assert.IsEmpty(f.Tier1.barProgress);
        }

        [Test]
        public void Walkthrough_4_a_running_timed_gig_offers_nothing()
        {
            var f = new Chapter1();
            f.SeedIdleState();
            f.Tier1.activeEvent = new ActiveEvent { eventId = f.GarageJam1.Id, remainingSeconds = 60 };

            f.Ch1.lastActiveUtc = f.Now.AddSeconds(-14400);
            f.Session.SwitchChapter(f.Ch1, f.Now);

            Assert.AreEqual(SessionPhase.Live, f.Session.Phase, "a timed record blocks the idle path outright");
            Assert.IsNull(f.Session.CurrentOffer);
            Assert.AreEqual((BigNumber)0, f.Balance(f.Tier1, f.Cash));
        }

        private static IdleOfferLine Line(Chapter1 f, CurrencyDefinition currency) =>
            f.Session.CurrentOffer.lines.Find(l => l.currency == currency);
    }
}
