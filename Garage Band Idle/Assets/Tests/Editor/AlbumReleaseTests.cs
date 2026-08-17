using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The album release (design doc section 5, slice 6): fans bank as Records
    // in the permanent pool, the run's facts reset by scope and group flags -
    // never a name list - and the modifier store is REBUILT from whatever
    // facts survived (rule 6). The release is a press of the chapter's album
    // RUNG now (rule 14), so what these tests exercise is the authored curve
    // and the reset behind one CompleteRung call. They run it through a real
    // factory-built context because the release is the first production
    // boundary to re-project a non-empty store: the guarantee
    // ScopeTests could only assert in isolation is exercised here as
    // the operation that needed it.
    public class AlbumReleaseTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private const string RecordsId = GameManager.RecordsCurrencyId;

        private static readonly ModifierSubject CashYield = TestContent.YieldOf("cash");
        private static readonly ModifierSelector CashYieldSel = TestContent.Sel("cash_yield");
        private static readonly ModifierSubject FanRate = TestContent.RateOf("fans");
        private static readonly ModifierSelector FanRateSel = TestContent.Sel("fans_rate");
        private static readonly ModifierSubject CashProduction = TestContent.RateOf("cash");
        private static readonly ModifierSelector CashProductionSel = TestContent.Sel("cash_rate");

        // The Chapter 1 shape in miniature: a run-scoped buff (re-bought each
        // demo), a permanent content unlock that latches the album flag on the
        // cut_demo gate, a permanent unlock granting a modifier (what "keeps
        // upgrades.contentUnlock" means for the store), gear, one cover bar
        // paying a run-lifetime fan-rate reward - and the album RUNG itself,
        // without which there is no press to make.
        private static Scope BuildChapterEconomy(out CurrencyManager permanent,
            Condition albumOffer = null)
        {
            var chapter = TestContent.MakeChapter("garage", new List<string> { "album" },
                currencyIds: new List<string> { "cash", "fans", "rehearsal" },
                generatorIds: new List<string> { "practice_amp", "drummer" },
                upgradeIds: new List<string> { "stage_presence", "backstage_pass", "cut_demo" },
                barGroupIds: new List<string> { "learn_covers" },
                rungs: new List<PrestigeTierDefinition>
                {
                    TestContent.MakeAlbumRung(offer: albumOffer),
                });

            var database = TestContent.MakeDatabase(
                chapters: new[] { chapter },
                generators: new List<GeneratorDefinition>
                {
                    // gated on an earned total, like the real practice amp: the
                    // reveal a release has to be able to take back
                    TestContent.MakeGenerator("practice_amp", "cash", 10, 1.15, 1,
                        unlock: new CurrencyEarnedTotalCondition("cash", 100)),
                    TestContent.MakeGenerator("drummer", "cash", 10, 1.15, 3),
                },
                upgrades: new List<UpgradeDefinition>
                {
                    TestContent.MakeUpgrade("stage_presence", UpgradeType.Buff, ContentScope.Run,
                        null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 2),
                        costAmount: 10),
                    TestContent.MakeUpgrade("backstage_pass", UpgradeType.ContentUnlock,
                        ContentScope.PermanentInChapter, null,
                        new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 4)),
                    TestContent.MakeUpgrade("cut_demo", UpgradeType.ContentUnlock,
                        ContentScope.PermanentInChapter,
                        new CurrencyBalanceCondition("fans", 50), new SetFlagEffect("album")),
                },
                bars: new List<BarDefinition>
                {
                    TestContent.MakeBar("cover_1", "rehearsal", 120, "fan_rate_x1_15"),
                },
                barGroups: new List<BarGroupDefinition>
                {
                    TestContent.MakeBarGroup("learn_covers", null, new List<string> { "cover_1" }),
                },
                rewards: new List<RewardDefinition>
                {
                    TestContent.MakeFanRateReward("fan_rate_x1_15", 1.15),
                },
                currencies: new[]
                {
                    TestContent.MakeCurrency("cash", "run"),
                    TestContent.MakeCurrency("fans", "run"),
                    TestContent.MakeCurrency("rehearsal", "run"),
                    TestContent.MakeCurrency(RecordsId, "permanent"),
                });

            permanent = ScopeFactory.BuildPermanentPool(database);
            return ScopeFactory.Build(chapter, database, permanent, EconomyRecipe.FrontierChapter);
        }

        // a run with everything the release walks: bought gear, a bought run
        // buff, a latched permanent unlock, a completed cover, banked fans
        private static void PlayARun(Scope context, double fans)
        {
            TestContent.BuyTimes(context.Generators.Get("drummer"), context.Pool, 2);

            context.Currencies.Add("cash", 10);
            Assert.IsTrue(context.BuyUpgrade(context.Upgrades.Get("stage_presence")),
                "the run buff purchase is part of the fixture");

            var covers = (PerBarContinuousRuntime)context.Bars.GetRuntime("learn_covers");
            context.Currencies.Add("rehearsal", 120);
            covers.SetActiveBar("cover_1");

            context.Currencies.Add("fans", fans);
            // the settle latches backstage_pass (no gate) and cut_demo (50 fans)
            context.Settle();
        }

        [Test]
        public void PressingTheAlbumRung_BanksFansAsRecords_InThePermanentPool()
        {
            var context = BuildChapterEconomy(out var permanent);
            context.Currencies.Add("fans", 125);

            Assert.AreEqual(5.0, context.PendingRungGrant("cut_demo", RecordsId).ToDouble(), 1e-9,
                "the preview reads the same formula the press banks");

            Assert.IsTrue(context.CompleteRung("cut_demo"));

            Assert.AreEqual(5.0, permanent.Get(RecordsId).ToDouble(), 1e-9,
                "floor((125 / 5) ^ 0.5), landed in the pool no press resets");
            Assert.AreEqual(5.0, permanent.GetEarned(RecordsId).ToDouble(), 1e-9,
                "banked as earned: the capstone gate and the income buff read this total");
            Assert.AreEqual(0.0, context.PendingRungGrant("cut_demo", RecordsId).ToDouble(), 1e-9,
                "the fans reset, so the next demo starts from nothing");
        }

        [Test]
        public void PressingTheAlbumRung_ResetsTheRunFacts_AndKeepsThePermanentOnes()
        {
            var context = BuildChapterEconomy(out var permanent);
            PlayARun(context, fans: 125);
            Assert.IsTrue(context.Flags.IsSet("album"), "cut_demo latched at 50 fans");
            Assert.AreEqual(1, context.Bars.CompletedCount("learn_covers"));

            Assert.IsTrue(context.CompleteRung("cut_demo"));

            // the run block: group-flagged balances, owned counts, run-scoped
            // latches and bar progress all return to their starting state
            Assert.AreEqual(0.0, context.Pool.Get("cash").ToDouble(), 1e-9);
            Assert.AreEqual(0.0, context.Pool.Get("fans").ToDouble(), 1e-9);
            Assert.AreEqual(0.0, context.Pool.Get("rehearsal").ToDouble(), 1e-9);
            Assert.AreEqual(0, context.Generators.Get("drummer").Owned);
            Assert.IsFalse(context.Upgrades.Get("stage_presence").Applied,
                "a run-scoped buff is re-bought each demo");
            Assert.AreEqual(0, context.Bars.CompletedCount("learn_covers"),
                "run-scoped bars forget their completions");

            // the permanent block: Records, content-unlock latches, and flags
            // survive, so content stays revealed across demos
            Assert.AreEqual(5.0, permanent.Get(RecordsId).ToDouble(), 1e-9);
            Assert.IsTrue(context.Upgrades.Get("backstage_pass").Applied);
            Assert.IsTrue(context.Upgrades.Get("cut_demo").Applied);
            Assert.IsTrue(context.Flags.IsSet("album"),
                "the Release button stays through every demo after the first");
        }

        // The store is rebuilt, never filtered: the run buff's multiplier is gone
        // because its latch is gone, the permanent unlock's is back because its
        // latch survived, and the bar reward followed its bar. This is
        // re-projection over a NON-EMPTY store running as the production operation
        // it was built for.
        [Test]
        public void PressingTheAlbumRung_RebuildsTheModifierStore_FromTheSurvivingFacts()
        {
            var context = BuildChapterEconomy(out _);
            PlayARun(context, fans: 125);

            Assert.AreEqual(8.0, context.Modifiers.For(CashYield).Multiply.ToDouble(), 1e-9,
                "the permanent unlock's x4 and the run buff's x2, composed");
            Assert.AreEqual(1.15, context.Modifiers.For(FanRate).Multiply.ToDouble(), 1e-9, "cover_1's reward");

            Assert.IsTrue(context.CompleteRung("cut_demo"));

            Assert.AreEqual(4.0, context.Modifiers.For(CashYield).Multiply.ToDouble(), 1e-9,
                "re-projected from the latch that survived - the run buff's fact is gone, "
                + "so nothing re-granted its effect");
            Assert.AreEqual(1.0, context.Modifiers.For(FanRate).Multiply.ToDouble(), 1e-9,
                "the reward's lifetime is its bar's");
        }

        // each Record adds +2% to the declared currencies' production through
        // the derived income buff - nothing re-applies it, the banked total is
        // simply higher, which is the whole prestige payoff
        [Test]
        public void PressingTheAlbumRung_MakesTheNextRunFaster_ThroughTheRecordsBuff()
        {
            var context = BuildChapterEconomy(out _);
            Assert.AreEqual(1.0, context.Modifiers.For(CashProduction).Multiply.ToDouble(), 1e-9);

            context.Currencies.Add("fans", 125);
            Assert.IsTrue(context.CompleteRung("cut_demo"));

            Assert.AreEqual(1.1, context.Modifiers.For(CashProduction).Multiply.ToDouble(), 1e-9,
                "1 + 0.02 x 5 banked Records");
        }

        // below the formula's floor a press banks nothing - and "nothing"
        // must mean no Add at all, so the earned total the capstone gate reads
        // never moves on an empty payout
        [Test]
        public void PressingTheAlbumRung_BanksNothingBelowTheFormulaFloor_ButStillResets()
        {
            var context = BuildChapterEconomy(out var permanent);
            context.Currencies.Add("fans", 4);

            Assert.AreEqual(0.0, context.PendingRungGrant("cut_demo", RecordsId).ToDouble(), 1e-9);

            Assert.IsTrue(context.CompleteRung("cut_demo"));

            Assert.AreEqual(0.0, permanent.GetEarned(RecordsId).ToDouble(), 1e-9,
                "no zero-amount award ever accrues");
            Assert.AreEqual(0.0, context.Pool.Get("fans").ToDouble(), 1e-9,
                "the reset is unconditional: releasing early is a legal, bad trade");
        }

        // What the earned total means is the currency GROUP's call, the same
        // call that decides the balance. A run-reset group's total is the run's
        // earnings; a permanent group's is a real lifetime. Leaving the run
        // one standing is what kept every earned-total gate met forever after
        // the first demo.
        [Test]
        public void PressingTheAlbumRung_ResetsEarnedTotals_OfRunScopedCurrenciesOnly()
        {
            var context = BuildChapterEconomy(out var permanent);
            context.Currencies.Add("cash", 250);
            context.Currencies.Add("fans", 125);

            Assert.AreEqual(250.0, context.Currencies.GetEarned("cash").ToDouble(), 1e-9,
                "the run earned it");

            Assert.IsTrue(context.CompleteRung("cut_demo"));

            Assert.AreEqual(0.0, context.Currencies.GetEarned("cash").ToDouble(), 1e-9,
                "cash resets on release, so what the run earned resets with it");
            Assert.AreEqual(5.0, permanent.GetEarned(RecordsId).ToDouble(), 1e-9,
                "Records sit in a permanent group: their total survives every demo");
        }

        // The reveal a release has to be able to take back. This was a one-way
        // latch on the Generator, so a row the player had ever seen stayed on
        // screen after the fleet behind it was zeroed; unlock is now a live
        // read of the condition, and the buy refuses through the same read.
        [Test]
        public void PressingTheAlbumRung_RelocksAGenerator_WhoseEarnedGateReset()
        {
            var context = BuildChapterEconomy(out _);
            var amp = context.Generators.Get("practice_amp");

            context.Currencies.Add("cash", 100);
            Assert.IsTrue(amp.IsUnlocked(context.Conditions),
                "100 earned cash puts the amp on offer");

            Assert.IsTrue(context.CompleteRung("cut_demo"));

            Assert.IsFalse(amp.IsUnlocked(context.Conditions),
                "and the demo takes the offer back - nothing latched it");

            // affordable, but nowhere near the 100 earned the gate wants: the
            // refusal has to be the gate, not the price
            context.Currencies.Add("cash", 20);
            Assert.IsFalse(context.BuyGenerator(amp),
                "a re-locked generator cannot be bought through a stale row");
        }

        // The album rung's OFFER re-arms each run: its inputs are run facts the
        // press itself resets. This is the Chapter 1 authoring - hidden until
        // first met via the flag, then greyed each demo until 50 fans + a
        // re-learned cover. Read off the filed rung rather than off the local
        // condition, because what is claimed is that the chapter's own
        // declaration re-arms, not that a Condition evaluates.
        [Test]
        public void TheAlbumRungsOffer_HoldsBeforeAPress_AndRearmsOnlyOnTheReclimb()
        {
            var gate = new CompoundCondition(new List<Condition>
            {
                new CurrencyBalanceCondition("fans", 50),
                new BarsCompletedCondition("learn_covers", 1),
            }, null);
            var context = BuildChapterEconomy(out _, albumOffer: gate);
            Assert.IsTrue(context.Prestige.TryGet("cut_demo", out var album),
                "the chapter files the album rung the offer belongs to");

            bool Offered() => ConditionEvaluator.IsMet(album.Offer, context.Conditions);

            Assert.IsFalse(Offered(), "nothing earned yet, no offer");

            PlayARun(context, fans: 125);
            Assert.IsTrue(Offered());

            Assert.IsTrue(context.CompleteRung("cut_demo"));
            Assert.IsFalse(Offered(),
                "fans and cover completions reset, so the offer disarms");

            // the re-climb: fans back over the bar AND the cover re-learned -
            // bars are run-scoped, so the barsCompleted leg really re-earns
            context.Currencies.Add("fans", 50);
            Assert.IsFalse(Offered(), "fans alone are not the gate");
            var covers = (PerBarContinuousRuntime)context.Bars.GetRuntime("learn_covers");
            context.Currencies.Add("rehearsal", 120);
            covers.SetActiveBar("cover_1");
            Assert.IsTrue(Offered(), "both legs re-met, the offer re-arms");
        }

        // The second-run flow as one cascade (design doc section 2): a
        // run-scoped flag set by a run-scoped unlock. The release clears both
        // facts, so the section AND the accrual gated on the flag go dark
        // together with nothing walked by name - and the whole system re-opens
        // as one moment when the run re-earns the unlock's own gate. One
        // condition, one place, four consumers. Section visibility here is the
        // live evaluation the screen performs - a pure read over the flag, so
        // it derives its reset from the flag's rather than owning one.
        [Test]
        public void PressingTheAlbumRung_ARunScopedFlag_TakesItsWholeSystemDarkAndRearmsOnTheReclimb()
        {
            var section = TestContent.MakeSection("rehearsal_space", new FlagSetCondition("covers"));
            var unlock = TestContent.MakeUpgrade("learn_covers", UpgradeType.ContentUnlock,
                ContentScope.Run, new CurrencyBalanceCondition("fans", 25), new SetFlagEffect("covers"));
            var producer = TestContent.MakeProducer("jam",
                ("rehearsal", 1, ProductionFeed.Rate, new FlagSetCondition("covers")));

            var chapter = TestContent.MakeChapter("garage",
                flagIds: null,
                flags: new List<FlagDeclaration> { new("covers", ContentScope.Run) },
                currencyIds: new List<string> { "cash", "fans", "rehearsal" },
                sectionIds: new List<string> { "rehearsal_space" },
                upgradeIds: new List<string> { "learn_covers" },
                producerIds: new List<string> { "jam" },
                rungs: new List<PrestigeTierDefinition> { TestContent.MakeAlbumRung() });
            var database = TestContent.MakeDatabase(
                chapters: new[] { chapter },
                sections: new List<SectionDefinition> { section },
                upgrades: new List<UpgradeDefinition> { unlock },
                producers: new List<ProducerDefinition> { producer },
                currencies: new[]
                {
                    TestContent.MakeCurrency("cash", "run"),
                    TestContent.MakeCurrency("fans", "run"),
                    TestContent.MakeCurrency("rehearsal", "run"),
                    TestContent.MakeCurrency(RecordsId, "permanent"),
                });
            var context = ScopeFactory.Build(chapter, database,
                ScopeFactory.BuildPermanentPool(database), EconomyRecipe.FrontierChapter);
            context.Focus();

            // the read ChapterScreen performs each settle: visibility is a pure
            // function of the section's condition over this economy's state
            bool SectionVisible() => ConditionEvaluator.IsMet(section.VisibleWhen, context.Conditions);

            // dark until taught: no flag, no section, no accrual
            context.Tick(10);
            Assert.IsFalse(context.Flags.IsSet("covers"));
            Assert.IsFalse(SectionVisible());
            Assert.AreEqual(0.0, context.Pool.Get("rehearsal").ToDouble(), 1e-9);

            // the run earns the unlock's gate: the whole system opens together
            context.Currencies.Add("fans", 30);
            context.Settle();
            Assert.IsTrue(context.Flags.IsSet("covers"));
            Assert.IsTrue(SectionVisible());
            context.Tick(10);
            Assert.AreEqual(10.0, context.Pool.Get("rehearsal").ToDouble(), 1e-9, "accrual on once taught");

            Assert.IsTrue(context.CompleteRung("cut_demo"));

            // one release, everything gated on the flag goes dark together -
            // and the release's own settle could not re-fire the unlock,
            // because its gate reads the fans balance the release reset
            Assert.IsFalse(context.Flags.IsSet("covers"), "the run flag cleared");
            Assert.IsFalse(SectionVisible(), "the section's visibility derives from the flag");
            Assert.IsFalse(context.Upgrades.Get("learn_covers").Applied, "the unlock re-arms");
            context.Tick(10);
            Assert.AreEqual(0.0, context.Pool.Get("rehearsal").ToDouble(), 1e-9,
                "no accrual before the run re-earns the system");

            // the re-climb: the unlock re-fires on its own gate and the same
            // flag re-opens everything at once
            context.Currencies.Add("fans", 25);
            context.Settle();
            Assert.IsTrue(context.Flags.IsSet("covers"), "re-earned, re-set");
            Assert.IsTrue(SectionVisible(), "the section re-shows with its flag");
            context.Tick(10);
            Assert.AreEqual(10.0, context.Pool.Get("rehearsal").ToDouble(), 1e-9, "accrual re-armed");
        }
    }
}
