using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The pattern SetCurrency and InverseOfBalance exist for: a run's clock,
    // fed at one per second, is recorded into a chapter currency when the run
    // completes, and an away-only source pays an amount per run over that
    // recorded time - so the away rate is the amount per second of the best
    // recorded pace. Two tours, each recording its own time and zeroing the
    // other's, so only the tour completed last pays while away.
    public class RecordedPaceTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // The idle fraction is the root's authored idle_base, the same
        // wildcard rate x0.5 under IdleAccumulation the standing TestTree
        // authors (IdleTests' "the authored idle base halves it"). No game
        // speed effect is authored anywhere, so the window pays at speed one.
        private const double IdleFraction = 0.5;

        // The window IdleTests uses, under the cap and over the minimum it
        // sets explicitly, so the whole absence is paid.
        private const double MinimumAway = 180;
        private const double IdleCap = 14400;
        private const double Away = 1000;

        private class Fixture
        {
            public readonly DateTime Now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

            public readonly RootDefinition RootDef;
            public readonly ChapterDefinition ChDef;
            public readonly TierDefinition TourADef;
            public readonly TierDefinition TourBDef;
            public readonly CurrencyDefinition Take;
            public readonly CurrencyDefinition TimeA;
            public readonly CurrencyDefinition TimeB;
            public readonly CurrencyDefinition ClockA;
            public readonly CurrencyDefinition ClockB;
            public readonly ComposedContent Content;

            public readonly RootScopeState Root;
            public readonly ChapterScopeState Ch;
            public readonly ScopeState TourA;
            public readonly ScopeState TourB;
            public readonly GameSession Session;

            public Fixture()
            {
                RootDef = TestTree.MakeRoot("root");
                TestTree.DeclareCodeReferences(RootDef);
                var idleBase = TestTree.MakeDefinition<ModifierDefinition>("idle_base");
                idleBase.appliesWhen = new IdleAccumulation();
                idleBase.effects.Add(new Effect { stat = Stat.Rate, multiplier = IdleFraction });
                RootDef.modifiers.Add(idleBase);
                RootDef.permanentModifiers.Add(idleBase);

                ChDef = TestTree.MakeChapter("ch");
                Take = TestTree.DeclareCurrency(ChDef, "take");
                TimeA = TestTree.DeclareCurrency(ChDef, "time_a");
                TimeB = TestTree.DeclareCurrency(ChDef, "time_b");

                ChDef.producers.Add(AwaySource("away_a", 120));
                ChDef.producers.Add(AwaySource("away_b", 300));
                Pace(ChDef, "pace_a", "away_a", TimeA);
                Pace(ChDef, "pace_b", "away_b", TimeB);

                TourADef = TestTree.MakeTier("tour_a");
                ClockA = TestTree.DeclareCurrency(TourADef, "clock_a");
                TourBDef = TestTree.MakeTier("tour_b");
                ClockB = TestTree.DeclareCurrency(TourBDef, "clock_b");
                Tour(TourADef, "clock_a_runner", ClockA, TimeA, TimeB);
                Tour(TourBDef, "clock_b_runner", ClockB, TimeB, TimeA);
                ChDef.children.Add(TourADef);
                ChDef.children.Add(TourBDef);

                Content = ComposedContent.Compose(RootDef, new[] { ChDef });
                Root = ScopeState.Build(Content);
                Ch = (ChapterScopeState)TestNavigation.Node(Root, ChDef);
                TourA = TestNavigation.Node(Root, TourADef);
                TourB = TestNavigation.Node(Root, TourBDef);

                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.minimumAwaySeconds = MinimumAway;
                config.idleCapSeconds = IdleCap;
                Session = new GameSession(Root, config);
                Ch.lastActiveUtc = Now;
                Session.SwitchChapter(Ch, Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
            }

            // An amount of take per run, paid only under the idle claim.
            private ProducerDefinition AwaySource(string id, double perRun)
            {
                var source = TestTree.MakeDefinition<ProducerDefinition>(id);
                source.produces.Add(TestTree.Entry(Take, Stat.Rate, perRun, new IdleAccumulation()));
                return source;
            }

            // The source's rate over the recorded time, declared and applied
            // at the chapter.
            private static void Pace(ChapterDefinition ch, string id, string source, CurrencyDefinition time)
            {
                var pace = TestTree.MakeDefinition<ModifierDefinition>(id);
                pace.effects.Add(new Effect { target = source, stat = Stat.Rate, formula = new InverseOfBalance { currency = time } });
                ch.modifiers.Add(pace);
                ch.permanentModifiers.Add(pace);
            }

            // A tour: its clock fed at one per second, and a rung that records
            // the clock into its own time, zeroes the other tour's time, and
            // resets the tour.
            private static void Tour(TierDefinition tour, string runnerId, CurrencyDefinition clock,
                                     CurrencyDefinition own, CurrencyDefinition other)
            {
                var runner = TestTree.MakeDefinition<ProducerDefinition>(runnerId);
                runner.produces.Add(TestTree.Entry(clock, Stat.Rate, 1));
                tour.producers.Add(runner);
                tour.rung = new Rung
                {
                    label = "Wrap " + tour.Id,
                    offerCondition = new CurrencyAtLeast { currency = clock, threshold = 1 },
                    actions =
                    {
                        new SetCurrency
                        {
                            currency = own,
                            formula = new RootCurveFormula { currency = clock, reads = PayoutTotal.Balance, divisor = 1, exponent = 1 },
                        },
                        new SetCurrency { currency = other, amount = 0 },
                        new ResetScope { scope = tour },
                    }
                };
            }

            public BigNumber Balance(ScopeState scope, CurrencyDefinition currency) =>
                new GameContext(scope, Now).GetBalance(currency.Id);

            public void Press(ScopeState tour, DateTime at)
            {
                bool? ran = null;
                Session.TryRung(new GameContext(tour, at), r => ran = r);
                Assert.AreEqual(true, ran, $"the rung at '{tour.ScopeId}' ran");
            }

            // Backgrounded at `from` and back at `from + Away`: the live exit
            // stamps the chapter and the return computes the offer over the
            // window, exactly as IdleTests' stamped absences do.
            public void BeAway(DateTime from)
            {
                Session.SwitchChapter(null, from);
                Session.SwitchChapter(Ch, from.AddSeconds(Away));
            }

            public IdleOfferLine TakeLine() =>
                Session.CurrentOffer?.lines.Find(l => l.target == Take);
        }

        [Test]
        public void The_recorded_pace_content_validates_without_errors()
        {
            var f = new Fixture();

            var report = ContentValidator.Validate(f.Content);

            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Findings));
        }

        [Test]
        public void With_no_record_neither_live_play_nor_an_absence_pays_take()
        {
            var f = new Fixture();

            Assert.AreEqual(BigNumber.Zero, Producer.GetRate(new GameContext(f.Ch, f.Now), f.Take),
                "the away entries are closed live");
            Assert.AreEqual(BigNumber.Zero, Producer.GetRate(new GameContext(f.Ch, f.Now, idleAccumulation: true), f.Take),
                "and under the claim both recorded times are zero");

            Assert.DoesNotThrow(() => f.BeAway(f.Now));
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase, "the clocks ran while away");
            Assert.IsNull(f.TakeLine(), "the window paid no take");

            Assert.DoesNotThrow(() => f.Session.ClaimIdle(f.Now.AddSeconds(Away)));
            Assert.AreEqual(BigNumber.Zero, f.Balance(f.Ch, f.Take));
        }

        // Sixty real seconds through the session's own tick, so the clock is
        // what its runner paid.
        [Test]
        public void Completing_a_tour_records_its_clock_and_zeroes_the_other_time()
        {
            var f = new Fixture();
            f.Session.Tick(60, f.Now.AddSeconds(60));
            AssertClose(60, f.Balance(f.TourA, f.ClockA), "one per second for sixty seconds");
            Assert.AreEqual(BigNumber.Zero, f.Balance(f.Ch, f.Take), "nothing paid take live");

            f.Press(f.TourA, f.Now.AddSeconds(60));

            Assert.AreEqual((BigNumber)60, f.Balance(f.Ch, f.TimeA), "floor(60)");
            Assert.AreEqual(BigNumber.Zero, f.Balance(f.Ch, f.TimeB));
            Assert.AreEqual(BigNumber.Zero, f.Balance(f.TourA, f.ClockA), "the tour reset itself");
        }

        // 120 take per run over a 60 second run is 2 take per second, halved
        // by idle_base, over the whole 1000 second window: 1000 take. Then the
        // other tour records 100 seconds: 300 / 100 = 3 per second, halved, over
        // another 1000 seconds, and away_a pays nothing beside it.
        [Test]
        public void The_last_recorded_tour_pays_its_amount_per_recorded_second_while_away()
        {
            var f = new Fixture();
            var t = f.Now.AddSeconds(60);
            f.Session.Tick(60, t);
            f.Press(f.TourA, t);

            f.BeAway(t);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            Assert.IsNotNull(f.TakeLine(), "the offer carries take");
            AssertClose(120.0 / 60 * IdleFraction * Away, f.TakeLine().amount, "2/s halved over the window");
            t = t.AddSeconds(Away);
            f.Session.ClaimIdle(t);
            AssertClose(1000, f.Balance(f.Ch, f.Take));
            Assert.AreEqual((BigNumber)60, f.Balance(f.Ch, f.TimeA), "the claim leaves the record alone");

            // The second tour's clock recorded by hand: its runner has been
            // paying since the first tick, and the claim paid it the window.
            f.TourB.balances[f.ClockB.Id] = 100;
            f.Press(f.TourB, t);
            Assert.AreEqual((BigNumber)100, f.Balance(f.Ch, f.TimeB), "floor(100)");
            Assert.AreEqual(BigNumber.Zero, f.Balance(f.Ch, f.TimeA), "tour_b zeroed tour_a's time");
            Assert.AreEqual(BigNumber.Zero, f.Balance(f.TourB, f.ClockB), "the tour reset itself");

            f.BeAway(t);
            Assert.IsNotNull(f.TakeLine(), "the offer carries take");
            AssertClose(300.0 / 100 * IdleFraction * Away, f.TakeLine().amount, "3/s halved over the window, nothing from away_a");
            f.Session.ClaimIdle(t.AddSeconds(Away));
            AssertClose(1000 + 1500, f.Balance(f.Ch, f.Take));
        }
    }
}
