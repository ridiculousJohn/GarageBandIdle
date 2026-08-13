using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The snapshot/seed contract (design doc section 12, rules 6 and 12). The
    // load-bearing claims:
    //
    // - both halves of a currency's state round-trip, because cumulative Records IS
    //   the earned total and a balance-only restore is a silently re-locked chapter
    // - restore REPLACES rather than merges, and is idempotent
    // - it is atomic: silent primitives, a forced drain, one settle, and a replay
    //   nothing can observe halfway - modifiers included
    // - a payout cannot be paid twice by any path
    // - an isolated economy cannot reach the player's permanent pool
    public class EconomySnapshotTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private const string RecordsId = GameManager.RecordsCurrencyId;
        private const string RoadiesId = GameManager.RoadiesCurrencyId;

        private static readonly ModifierSubject CashYield = TestContent.YieldOf("cash");
        private static readonly ModifierSelector CashYieldSel = TestContent.Sel("cash_yield");

        // the two-pool content set the running game has: a chapter-placed run group
        // holding cash/fans, and a global permanent group holding Records + Roadies
        private static ContentDatabase MakeDatabase(ChapterDefinition chapter,
            List<UpgradeDefinition> upgrades = null, List<GeneratorDefinition> generators = null)
            => new(
                chapters: new[] { chapter },
                upgrades: upgrades,
                generators: generators,
                currencies: new[]
                {
                    TestContent.MakeCurrency("cash", "run"),
                    TestContent.MakeCurrency("fans", "run"),
                    TestContent.MakeCurrency(RecordsId, "permanent"),
                    TestContent.MakeCurrency(RoadiesId, "permanent"),
                },
                currencyGroups: new[]
                {
                    TestContent.MakeGroup("run", true, CurrencyPlacement.Chapter),
                    TestContent.MakeGroup("permanent", false, CurrencyPlacement.Global),
                });

        private static ChapterDefinition MakeChapter(List<string> upgradeIds = null,
            List<string> generatorIds = null, List<FlagDeclaration> flags = null)
            => TestContent.MakeChapter("garage", new List<string> { "fans" },
                currencyIds: new List<string> { "cash", "fans" },
                upgradeIds: upgradeIds, generatorIds: generatorIds, flags: flags,
                recordBuffAffects: new List<string> { "cash" });

        private static EconomyContext Build(ChapterDefinition chapter, ContentDatabase database,
            CurrencyManager permanent, EconomyRecipe recipe = null, EconomyLocalSnapshot seed = null)
            => EconomyContextFactory.Build(chapter, database, permanent,
                recipe ?? EconomyRecipe.FrontierChapter, seed);

        // ---- the earned total is a fact of its own ---------------------------

        // The gap this whole contract was built around. Records are never spent, so
        // the cumulative total the capstone gate and the income buff both read IS
        // CurrencyManager's earned total - and Set only ever moved the balance, so
        // there was no way to put it back. A load would have shown the right number
        // with the chapter re-locked and the multiplier at 1.0.
        [Test]
        public void Restore_RoundTripsBalanceAndEarnedTotalIndependently()
        {
            var pool = new CurrencyManager(
                new[] { TestContent.MakeGroup("permanent", false, CurrencyPlacement.Global) },
                new[] { TestContent.MakeCurrency(RecordsId, "permanent") });

            pool.Add(RecordsId, 30);
            pool.Add(RecordsId, -12); // spent, which never lowers the earned total
            Assert.AreEqual(18.0, pool.Get(RecordsId).ToDouble(), 1e-9);
            Assert.AreEqual(30.0, pool.GetEarned(RecordsId).ToDouble(), 1e-9);

            var fresh = new CurrencyManager(
                new[] { TestContent.MakeGroup("permanent", false, CurrencyPlacement.Global) },
                new[] { TestContent.MakeCurrency(RecordsId, "permanent") });
            fresh.Restore(RecordsId, pool.Get(RecordsId), pool.GetEarned(RecordsId));

            Assert.AreEqual(18.0, fresh.Get(RecordsId).ToDouble(), 1e-9, "the balance");
            Assert.AreEqual(30.0, fresh.GetEarned(RecordsId).ToDouble(), 1e-9,
                "and the lifetime total, which no amount of balance-setting could reconstruct");
        }

        // The consequence, end to end: restoring the total is what brings the
        // capstone gate and the permanent income multiplier back.
        [Test]
        public void RestoredRecordsTotal_ReactivatesTheCapstoneGateAndTheIncomeModifier()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);
            using var context = Build(chapter, database, permanent);
            var gate = new RecordsCumulativeCondition(30);
            var cashProduction = TestContent.RateOf("cash");

            Assert.IsFalse(ConditionEvaluator.IsMet(gate, context.Conditions), "no Records yet");
            Assert.AreEqual(1.0, context.Modifiers.For(cashProduction).Multiply.ToDouble(), 1e-9);

            // exactly what a load does to the permanent block: both halves, absolutely
            permanent.Restore(RecordsId, 30, 30);

            Assert.IsTrue(ConditionEvaluator.IsMet(gate, context.Conditions),
                "cumulative Records is the earned total, so the gate holds again");
            Assert.AreEqual(1.6, context.Modifiers.For(cashProduction).Multiply.ToDouble(), 1e-9,
                "1 + 0.02 x 30 - the derived modifier reads the restored total, nothing was saved");
        }

        // ---- replacement, not merge -----------------------------------------

        [Test]
        public void Restore_ClearsFlagsAndLatchesTheSnapshotOmits()
        {
            var chapter = MakeChapter(
                upgradeIds: new List<string> { "gated_a", "gated_b" },
                flags: new List<FlagDeclaration> { new("fans"), new("covers") });
            var database = MakeDatabase(chapter, upgrades: new List<UpgradeDefinition>
            {
                // gated so construction latches neither: the snapshot is the only
                // thing that decides what is applied
                TestContent.MakeUpgrade("gated_a", UpgradeType.ContentUnlock, ContentScope.PermanentInChapter,
                    new CurrencyBalanceCondition("cash", 1_000_000), new SetFlagEffect("fans")),
                TestContent.MakeUpgrade("gated_b", UpgradeType.ContentUnlock, ContentScope.PermanentInChapter,
                    new CurrencyBalanceCondition("cash", 1_000_000), new SetFlagEffect("covers")),
            });
            using var context = Build(chapter, database, EconomyContextFactory.BuildPermanentPool(database));

            context.Restore(new EconomyLocalSnapshot(
                appliedUpgradeIds: new List<string> { "gated_a", "gated_b" },
                setFlagIds: new List<string> { "fans", "covers" }));
            Assert.IsTrue(context.Upgrades.Get("gated_a").Applied);
            Assert.IsTrue(context.Flags.IsSet("covers"));

            // a DIFFERENT snapshot: what it omits goes away
            context.Restore(new EconomyLocalSnapshot(
                appliedUpgradeIds: new List<string> { "gated_a" },
                setFlagIds: new List<string> { "fans" }));

            Assert.IsTrue(context.Upgrades.Get("gated_a").Applied, "still named");
            Assert.IsFalse(context.Upgrades.Get("gated_b").Applied, "omitted, so cleared");
            Assert.IsTrue(context.Flags.IsSet("fans"));
            Assert.IsFalse(context.Flags.IsSet("covers"), "omitted, so cleared");
        }

        // The runtime half of the payout rule, structural since the effect/action
        // split. A content unlock is applied automatically whenever its latch is
        // absent and its gate holds, and restore clears any latch its snapshot
        // omits - so an award on one would be paid again by every such restore.
        // Awards are GameActions now, and the auto-apply path executes none: the
        // unlock latches, its payload applies, and nothing pays, because there is
        // no code on that path to pay - not a guard refusing, an absence.
        [Test]
        public void ContentUnlockCarryingActions_LatchesWithoutPaying()
        {
            var chapter = MakeChapter(upgradeIds: new List<string> { "payday" });
            var database = MakeDatabase(chapter, upgrades: new List<UpgradeDefinition>
            {
                // ungated, so a single settle applies it immediately
                TestContent.MakeUpgrade("payday", UpgradeType.ContentUnlock, ContentScope.PermanentInChapter,
                    null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 4),
                    actions: new List<GameAction> { new GrantCurrencyAction(RoadiesId, 1) }),
            });
            var permanent = EconomyContextFactory.BuildPermanentPool(database);

            using var context = Build(chapter, database, permanent);

            Assert.IsTrue(context.Upgrades.Get("payday").Applied,
                "the unlock latched at the construction settle");
            Assert.AreEqual(0.0, permanent.Get(RoadiesId).ToDouble(), 1e-9,
                "and banked nothing: the auto-apply path executes no actions");
        }

        // Restoring the same snapshot repeatedly changes nothing - the property the
        // whole contract rests on, over the facts that ARE legal.
        [Test]
        public void Restore_IsIdempotent()
        {
            var chapter = MakeChapter(
                upgradeIds: new List<string> { "gated" },
                flags: new List<FlagDeclaration> { new("fans") });
            var database = MakeDatabase(chapter, upgrades: new List<UpgradeDefinition>
            {
                TestContent.MakeUpgrade("gated", UpgradeType.ContentUnlock, ContentScope.PermanentInChapter,
                    new CurrencyBalanceCondition("cash", 1_000_000),
                    new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 4)),
            });
            using var context = Build(chapter, database, EconomyContextFactory.BuildPermanentPool(database));

            var snapshot = new EconomyLocalSnapshot(
                currencies: new Dictionary<string, CurrencyState> { ["cash"] = new CurrencyState(25, 40) },
                appliedUpgradeIds: new List<string> { "gated" },
                setFlagIds: new List<string> { "fans" });

            context.Restore(snapshot);
            context.Restore(snapshot);
            context.Restore(snapshot);

            Assert.AreEqual(25.0, context.Currencies.Get("cash").ToDouble(), 1e-9);
            Assert.AreEqual(40.0, context.Pool.GetEarned("cash").ToDouble(), 1e-9);
            Assert.IsTrue(context.Upgrades.Get("gated").Applied);
            Assert.IsTrue(context.Flags.IsSet("fans"));
            Assert.AreEqual(4.0, context.Modifiers.For(CashYield).Multiply.ToDouble(), 1e-9,
                "the store is rebuilt each time, never accumulated");
        }

        // ---- atomicity -------------------------------------------------------

        // MarkDirty is not decorative. The restore primitives are silent so nothing
        // observes partial state, and the condition context learns about state
        // THROUGH those same events - so without forcing the flag, a restore into an
        // already-settled context drains nothing. The fresh-context default cannot
        // cover it, because the second restore is the case that matters.
        [Test]
        public void Restore_ForcesConditionEvaluation_EvenWhenAlreadySettled()
        {
            var chapter = MakeChapter(
                upgradeIds: new List<string> { "reveal" },
                flags: new List<FlagDeclaration> { new("fans") });
            var database = MakeDatabase(chapter, upgrades: new List<UpgradeDefinition>
            {
                TestContent.MakeUpgrade("reveal", UpgradeType.ContentUnlock, ContentScope.PermanentInChapter,
                    new CurrencyBalanceCondition("cash", 50), new SetFlagEffect("fans")),
            });
            using var context = Build(chapter, database, EconomyContextFactory.BuildPermanentPool(database));

            // settle it dry first, so nothing is pending when the restore arrives
            context.Settle();
            var settled = 0;
            context.Conditions.Settled += () => settled++;

            // a snapshot whose balance MEETS the gate, with the latch absent: only an
            // evaluation can notice
            context.Restore(new EconomyLocalSnapshot(
                currencies: new Dictionary<string, CurrencyState>
                {
                    ["cash"] = new CurrencyState(50, 50),
                }));

            // ONE public Settled, however many drain passes it took. The fixpoint
            // needs two here - the first latches `reveal`, whose setFlag dirties the
            // context again (Drain clears its flag BEFORE evaluating), and the second
            // confirms nothing follows - but publishing each pass would hand
            // subscribers the state BETWEEN them, which is the half-derived read
            // atomic restore exists to prevent. The passes are internal; the signal
            // describes finished state.
            Assert.AreEqual(1, settled, "one terminal Settled, not one per fixpoint pass");
            Assert.IsTrue(context.Upgrades.Get("reveal").Applied,
                "the forced drain evaluated unlocks against the restored balance");
            Assert.IsTrue(context.Flags.IsSet("fans"));
            Assert.IsFalse(context.Conditions.IsDirty, "and it returned settled, not merely drained once");
        }

        // Nothing may observe a half-restored economy, and the modifier channel is
        // the one that can leak: a projection clears the store and re-grants, so
        // an undeferred rebuild fires Changed once per cleared target and once per
        // re-grant, each read against a store missing everything not yet re-applied.
        [Test]
        public void Restore_PublishesNothingUntilTheStateIsComplete()
        {
            var chapter = MakeChapter(upgradeIds: new List<string> { "buff" });
            var database = MakeDatabase(chapter, upgrades: new List<UpgradeDefinition>
            {
                TestContent.MakeUpgrade("buff", UpgradeType.ContentUnlock, ContentScope.PermanentInChapter,
                    new CurrencyBalanceCondition("cash", 1_000_000),
                    new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 4)),
            });
            using var context = Build(chapter, database, EconomyContextFactory.BuildPermanentPool(database));

            // every observation any subscriber makes during the restore, in order
            var observedYields = new List<double>();
            var observedCash = new List<double>();
            context.Modifiers.Changed += _ => observedYields.Add(context.Modifiers.For(CashYield).Multiply.ToDouble());
            context.Currencies.BalanceChanged += (id, _) =>
            {
                if (id == "cash")
                    observedCash.Add(context.Modifiers.For(CashYield).Multiply.ToDouble());
            };

            context.Restore(new EconomyLocalSnapshot(
                currencies: new Dictionary<string, CurrencyState> { ["cash"] = new CurrencyState(25, 25) },
                appliedUpgradeIds: new List<string> { "buff" }));

            Assert.AreEqual(4.0, context.Modifiers.For(CashYield).Multiply.ToDouble(), 1e-9,
                "the latch projected its buff");
            CollectionAssert.DoesNotContain(observedYields, 0.0,
                "no subscriber saw the store mid-rebuild - the projection's notifications are deferred");
            CollectionAssert.DoesNotContain(observedCash, 0.0,
                "and a balance notification never arrived before the buff was in place");
        }

        [Test]
        public void Restore_LeavesTheConditionContextClean()
        {
            var chapter = MakeChapter(flags: new List<FlagDeclaration> { new("fans") });
            var database = MakeDatabase(chapter);
            using var context = Build(chapter, database, EconomyContextFactory.BuildPermanentPool(database));

            context.Restore(new EconomyLocalSnapshot(
                currencies: new Dictionary<string, CurrencyState> { ["cash"] = new CurrencyState(25, 25) },
                setFlagIds: new List<string> { "fans" }));

            // a clean context does not drain again: the replay is suppressed, so it
            // cannot re-dirty what the settle just consumed
            var settled = 0;
            context.Conditions.Settled += () => settled++;
            context.Settle();
            Assert.AreEqual(0, settled, "nothing was left pending by the restore");
        }

        // ---- what a capture owns --------------------------------------------

        // The invariant protecting permanent progress: a context routes to the shared
        // permanent pool but does not own it, so a capture that reached through the
        // router would make every economy a claimant on Records and Roadies.
        [Test]
        public void CaptureLocalState_HoldsNoPermanentCurrency()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);
            using var context = Build(chapter, database, permanent);

            context.Currencies.Add(RecordsId, 30);
            context.Currencies.Add("cash", 10);

            var snapshot = context.CaptureLocalState();

            Assert.IsTrue(snapshot.Currencies.ContainsKey("cash"), "its own pool");
            Assert.IsFalse(snapshot.Currencies.ContainsKey(RecordsId), "not the shared pool's Records");
            Assert.IsFalse(snapshot.Currencies.ContainsKey(RoadiesId), "nor its Roadies");
        }

        // The permanent block's own capture/restore, through the SAME pair the
        // chapter pool uses. Ownership is decided by who calls it, not by which
        // mechanism exists - so slice 9 has nothing to invent for the permanent save
        // block beyond deciding that GameManager is the one writer.
        [Test]
        public void PermanentPool_RoundTripsThroughTheSameCapturePair()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);

            permanent.Add(RecordsId, 30);
            permanent.Add(RecordsId, -12);
            permanent.Add(RoadiesId, 1);

            var saved = permanent.CaptureAll();

            var reloaded = EconomyContextFactory.BuildPermanentPool(database);
            reloaded.RestoreAll(saved);

            Assert.AreEqual(18.0, reloaded.Get(RecordsId).ToDouble(), 1e-9);
            Assert.AreEqual(30.0, reloaded.GetEarned(RecordsId).ToDouble(), 1e-9,
                "the total the capstone gate and income buff read");
            Assert.AreEqual(1.0, reloaded.Get(RoadiesId).ToDouble(), 1e-9);
            Assert.AreEqual(1.0, reloaded.GetEarned(RoadiesId).ToDouble(), 1e-9,
                "Roadies keep a lifetime total too, for a later 'ever earned' condition");
        }

        [Test]
        public void RestoreAll_ReturnsAnUnnamedCurrencyToItsStartingValue()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);
            permanent.Add(RecordsId, 30);
            permanent.Add(RoadiesId, 4);

            // a snapshot that knows only about Records - Roadies must not survive it
            permanent.RestoreAll(new Dictionary<string, CurrencyState>
            {
                [RecordsId] = new CurrencyState(5, 5),
            });

            Assert.AreEqual(5.0, permanent.Get(RecordsId).ToDouble(), 1e-9);
            Assert.AreEqual(0.0, permanent.Get(RoadiesId).ToDouble(), 1e-9, "omitted, so back to its start");
            Assert.AreEqual(0.0, permanent.GetEarned(RoadiesId).ToDouble(), 1e-9);
        }

        // RestoreAll's own atomicity. Passing notify straight into each currency's
        // Restore published after the first one while the rest still held pre-restore
        // state - a subscriber could read Records restored beside Roadies untouched,
        // which is the half-applied observation the method's contract denies.
        [Test]
        public void RestoreAll_PublishesOnlyAfterEveryCurrencyIsInPlace()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);

            // what a subscriber could see of the OTHER currency at each notification
            var observedRoadiesWhenRecordsMoved = new List<double>();
            permanent.BalanceChanged += (id, _) =>
            {
                if (id == RecordsId)
                    observedRoadiesWhenRecordsMoved.Add(permanent.Get(RoadiesId).ToDouble());
            };

            permanent.RestoreAll(new Dictionary<string, CurrencyState>
            {
                [RecordsId] = new CurrencyState(30, 30),
                [RoadiesId] = new CurrencyState(2, 2),
            });

            CollectionAssert.DoesNotContain(observedRoadiesWhenRecordsMoved, 0.0,
                "no notification arrived while another currency still held pre-restore state");
        }

        [Test]
        public void RestoreAll_ReportsStateForACurrencyThePoolDoesNotHold()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);

            // "cash" is the chapter pool's - a permanent block carrying it is stale
            // state or a snapshot restored into the wrong pool, and dropping it
            // silently would lose the balance and the evidence together
            LogAssert.Expect(LogType.Error,
                "CurrencyManager: RestoreAll was given state for currency 'cash', which this pool does not hold. Ignoring it - stale saved state, or a snapshot restored into the wrong pool.");
            permanent.RestoreAll(new Dictionary<string, CurrencyState>
            {
                [RecordsId] = new CurrencyState(1, 1),
                ["cash"] = new CurrencyState(99, 99),
            });

            Assert.AreEqual(1.0, permanent.Get(RecordsId).ToDouble(), 1e-9, "the owned key still restored");
        }

        // ---- recipes ---------------------------------------------------------

        // Withholding the Records income modifier makes a sandbox's baseline fixed;
        // it does nothing to stop a sandbox WRITING Records. Only the pool decides
        // that, which is why routing is a second axis on the recipe.
        [Test]
        public void EventSandbox_CannotReachThePlayersPermanentPool()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);
            permanent.Add(RecordsId, 30);

            using var sandbox = Build(chapter, database, permanent, EconomyRecipe.EventSandbox);

            Assert.AreEqual(0.0, sandbox.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "the sandbox's own permanent pool starts empty - that IS the fixed baseline");

            sandbox.Currencies.Add(RecordsId, 500);
            sandbox.Currencies.Add(RoadiesId, 9);

            Assert.AreEqual(30.0, permanent.Get(RecordsId).ToDouble(), 1e-9,
                "the player's Records are untouched by anything the sandbox banked");
            Assert.AreEqual(0.0, permanent.Get(RoadiesId).ToDouble(), 1e-9);
        }

        // The seed an event sandbox is built from: the chapter's permanent facts and
        // none of the run's, filtered by what each declaration says rather than by a
        // name list - and the derived modifiers rebuild from the facts that survive.
        [Test]
        public void PermanentInChapterSeed_ExcludesRunFacts_AndRebuildsDerivedModifiers()
        {
            var chapter = MakeChapter(
                upgradeIds: new List<string> { "permanent_buff", "run_buff" },
                generatorIds: new List<string> { "drummer" },
                flags: new List<FlagDeclaration> { new("fans"), new("covers", ContentScope.Run) });
            var database = MakeDatabase(chapter,
                upgrades: new List<UpgradeDefinition>
                {
                    TestContent.MakeUpgrade("permanent_buff", UpgradeType.ContentUnlock,
                        ContentScope.PermanentInChapter, new CurrencyBalanceCondition("cash", 1_000_000),
                        new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 4)),
                    TestContent.MakeUpgrade("run_buff", UpgradeType.ContentUnlock, ContentScope.Run,
                        new CurrencyBalanceCondition("cash", 1_000_000),
                        new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 7)),
                },
                generators: new List<GeneratorDefinition>
                {
                    TestContent.MakeGenerator("drummer", "cash", 10, 1.15, 3, isBandmate: true),
                });
            var permanent = EconomyContextFactory.BuildPermanentPool(database);
            using var frontier = Build(chapter, database, permanent);

            // a run in progress: both buffs latched, both flags set, a band bought,
            // and a run currency balance
            frontier.Restore(new EconomyLocalSnapshot(
                currencies: new Dictionary<string, CurrencyState> { ["cash"] = new CurrencyState(500, 500) },
                generatorsOwned: new Dictionary<string, int> { ["drummer"] = 2 },
                appliedUpgradeIds: new List<string> { "permanent_buff", "run_buff" },
                setFlagIds: new List<string> { "fans", "covers" }));
            Assert.AreEqual(28.0, frontier.Modifiers.For(CashYield).Multiply.ToDouble(), 1e-9, "4 x 7 while running");

            var seed = frontier.CaptureSeedFor(EconomyRecipe.EventSandbox);
            using var sandbox = Build(chapter, database, permanent, EconomyRecipe.EventSandbox, seed);

            Assert.IsTrue(sandbox.Upgrades.Get("permanent_buff").Applied, "a chapter-permanent latch carries in");
            Assert.IsFalse(sandbox.Upgrades.Get("run_buff").Applied, "a run latch does not");
            Assert.IsTrue(sandbox.Flags.IsSet("fans"), "a permanent flag carries in");
            Assert.IsFalse(sandbox.Flags.IsSet("covers"), "a run-scoped flag does not");
            Assert.AreEqual(0, sandbox.Generators.Get("drummer").Owned,
                "the fleet is re-bought every run, so there is no permanent owned count");
            Assert.AreEqual(0.0, sandbox.Currencies.Get("cash").ToDouble(), 1e-9,
                "a run currency's balance is a run fact");

            Assert.AreEqual(4.0, sandbox.Modifiers.For(CashYield).Multiply.ToDouble(), 1e-9,
                "the granted store rebuilt from exactly the latches that carried in");

            // The per-bandmate bonus is each bandmate's own fans CONTRIBUTION now
            // (rule 13), so it is read off the producer rather than the modifier
            // stack - and it scales with the owned count because a generator's lines
            // always do. The sandbox's band is empty, so its fans rate is zero: the
            // frontier's two drummers leak in through production no more than they
            // do through the fleet.
            Assert.AreEqual(0.0, sandbox.Production.RateOf("fans").ToDouble(),
                1e-9, "the sandbox's own band is empty, so nothing contributes");
            Assert.AreEqual(0.04, frontier.Production.RateOf("fans").ToDouble(),
                1e-9, "while the frontier's two bandmates contribute 0.02 x 2 - two economies, two answers");
        }

        // ---- effect replay ---------------------------------------------------

        // An award pays when its operation executes it, and there is nothing else
        // to exercise: no rebuild path holds a GameAction, so "paid once ever" is
        // the shape of the data rather than a filter with cases to test.
        [Test]
        public void GrantCurrencyAction_PaysOnExecute()
        {
            var currencies = TestContent.MakeEconomy();
            var effects = new EffectContext(currencies, new FlagSystem(), new ModifierSystem());

            new GrantCurrencyAction("cash", 100).Execute(effects);

            Assert.AreEqual(100.0, currencies.Get("cash").ToDouble(), 1e-9);
        }

        // A compound re-applies safely at every boundary because everything in it
        // is re-applicable by construction: a payout is a GameAction, unauthorable
        // inside a payload, so nothing here has to filter for one.
        [Test]
        public void Compound_ReappliesExactly_AtTheRebuildBoundary()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem(new[] { "chapter_2_unlocked" });
            var modifiers = new ModifierSystem();
            var effects = new EffectContext(currencies, flags, modifiers);
            var payload = new CompoundEffect(new List<GameEffect>
            {
                new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 4),
                new SetFlagEffect("chapter_2_unlocked"),
            });

            payload.Apply(effects, ContentScope.PermanentInChapter);
            Assert.AreEqual(4.0, modifiers.For(TestContent.YieldOf("cash")).Multiply.ToDouble(), 1e-9);
            Assert.IsTrue(flags.IsSet("chapter_2_unlocked"));

            // the rebuild pattern (rule 6): clear the store, re-run the payload
            modifiers.ResetGranted();
            payload.Apply(effects, ContentScope.PermanentInChapter);

            Assert.AreEqual(4.0, modifiers.For(TestContent.YieldOf("cash")).Multiply.ToDouble(), 1e-9,
                "re-running rebuilds exactly, never compounds");
            Assert.IsTrue(flags.IsSet("chapter_2_unlocked"), "and the latch re-asserts idempotently");
        }

        // A flag re-asserting on re-Apply is load-bearing rather than convenient:
        // the release clears run-scoped flags and asks for the rebuild, which
        // re-sets every flag whose SETTER's latch survived. If setFlag could not
        // re-run, permanently unlocked content would go dark after the first demo.
        [Test]
        public void SetFlagEffect_ReassertsAcrossAnAlbumRelease()
        {
            var chapter = MakeChapter(
                upgradeIds: new List<string> { "cut_demo" },
                flags: new List<FlagDeclaration> { new("album") });
            var database = MakeDatabase(chapter, upgrades: new List<UpgradeDefinition>
            {
                // permanent-in-chapter, like the real cut_demo: its latch survives
                // the release, so the projection is what puts the flag back
                TestContent.MakeUpgrade("cut_demo", UpgradeType.ContentUnlock,
                    ContentScope.PermanentInChapter, null, new SetFlagEffect("album")),
            });
            using var context = Build(chapter, database, EconomyContextFactory.BuildPermanentPool(database));

            Assert.IsTrue(context.Flags.IsSet("album"), "latched at construction");

            context.ReleaseAlbum();

            Assert.IsTrue(context.Flags.IsSet("album"),
                "the release cleared run-scoped flags and re-projected; album came back from its surviving latch");
        }

        // The completed capstone is a fact source like any latch, and the
        // declared completion flag IS the latch: whenever it is set, projection
        // re-applies OnComplete with permanent scope. Ch1 authors no OnComplete,
        // so this is the wiring that keeps a later chapter's capstone-authored
        // state from vanishing at its first release - and the load half rides
        // the snapshot's SetFlagIds, so nothing about the capstone is saved.
        [Test]
        public void CapstoneOnComplete_ReprojectsFromTheCompletionFlagLatch()
        {
            var capstone = new CapstoneConfig("backyard_party", "Play the Backyard Party",
                new RecordsCumulativeCondition(1), "chapter_2_unlocked",
                new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 4),
                new List<GameAction>());
            var chapter = TestContent.MakeChapter("garage", null,
                currencyIds: new List<string> { "cash", "fans" },
                flags: new List<FlagDeclaration> { new("chapter_2_unlocked") },
                capstone: capstone,
                recordBuffAffects: new List<string> { "cash" });
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);
            using var context = Build(chapter, database, permanent);

            context.Currencies.Add(RecordsId, 1);
            Assert.IsTrue(context.CompleteCapstone());
            Assert.AreEqual(4.0, context.Modifiers.For(CashYield).Multiply.ToDouble(), 1e-9,
                "the operation applied OnComplete once");

            context.ReleaseAlbum();
            Assert.AreEqual(4.0, context.Modifiers.For(CashYield).Multiply.ToDouble(), 1e-9,
                "the release rebuilt the store, and the flag latch re-applied OnComplete exactly once");

            var seed = context.CaptureLocalState();
            using var loaded = Build(chapter, database, permanent, seed: seed);
            Assert.IsTrue(loaded.Flags.IsSet("chapter_2_unlocked"), "the flag rode the snapshot");
            Assert.AreEqual(4.0, loaded.Modifiers.For(CashYield).Multiply.ToDouble(), 1e-9,
                "and a load rebuilds the capstone's state from the flag alone");
        }
    }
}
