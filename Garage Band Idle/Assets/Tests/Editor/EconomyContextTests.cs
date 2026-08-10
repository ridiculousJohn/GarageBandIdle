using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The economy-context boundary (design doc section 12, rule 12): which pool
    // owns a balance, what a context refuses to be built with, and the two
    // properties the bundle exists to guarantee - one settle seam per operation,
    // and a modifier store that is only ever rebuilt from facts.
    public class EconomyContextTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private const string RecordsId = GameManager.RecordsCurrencyId;

        private static readonly ModifierTargetKey TapValue = ModifierTargetKey.Global(ModifierTarget.TapValue);

        // the two-pool content set the running game has: a chapter-placed run
        // group holding cash/fans/rehearsal, and a global group holding Records
        private static ContentDatabase MakeDatabase(ChapterDefinition chapter,
            CurrencyPlacement globalPlacement = CurrencyPlacement.Global,
            bool globalResetsOnRelease = false,
            List<UpgradeDefinition> upgrades = null,
            List<ProducerDefinition> producers = null)
        {
            var run = TestContent.MakeGroup("run", true, CurrencyPlacement.Chapter);
            var permanent = TestContent.MakeGroup("permanent", globalResetsOnRelease, globalPlacement);

            return new ContentDatabase(
                chapters: new[] { chapter },
                currencies: new[]
                {
                    TestContent.MakeCurrency("cash", "run"),
                    TestContent.MakeCurrency("fans", "run"),
                    TestContent.MakeCurrency("rehearsal", "run"),
                    TestContent.MakeCurrency(RecordsId, "permanent"),
                },
                currencyGroups: new[] { run, permanent },
                upgrades: upgrades,
                producers: producers);
        }

        private static ChapterDefinition MakeChapter(List<string> currencyIds = null,
            List<string> upgradeIds = null, List<string> producerIds = null)
            => TestContent.MakeChapter("garage", new List<string> { "fans" },
                currencyIds: currencyIds ?? new List<string> { "cash", "fans", "rehearsal" },
                upgradeIds: upgradeIds, producerIds: producerIds);

        // ---- placement decides the pool --------------------------------------

        // The startup pool holds exactly the global-placed currencies, and its
        // lifetime comes from being built out here rather than from a flag inside
        // it: nothing a run does reaches this call site.
        [Test]
        public void BuildPermanentPool_HoldsOnlyGloballyPlacedCurrencies()
        {
            var database = MakeDatabase(MakeChapter());

            var permanent = EconomyContextFactory.BuildPermanentPool(database);

            Assert.IsTrue(permanent.Contains(RecordsId), "Records is placed Global");
            Assert.IsFalse(permanent.Contains("cash"), "a chapter-placed currency stays out of the startup pool");
            Assert.IsFalse(permanent.Contains("fans"));
            Assert.IsFalse(permanent.Contains("rehearsal"));
        }

        // A group whose placement was never set (an un-migrated asset, or one
        // created by hand) would put its currencies in NO pool - not the roster,
        // not the startup pool - and every balance in it would silently read
        // zero. Boot validation is the only thing that can catch this: group
        // assets are hand-authored, so there is no import step to refuse it at.
        [Test]
        public void BuildPermanentPool_LeavesOutAGroupWithNoPlacement()
        {
            var database = MakeDatabase(MakeChapter(), globalPlacement: CurrencyPlacement.None);

            var permanent = EconomyContextFactory.BuildPermanentPool(database);

            Assert.IsFalse(permanent.Contains(RecordsId),
                "None is not Global: the currency lands nowhere, which is what validation reports");
        }

        // ---- what a context refuses to be built with -------------------------

        // Placement says which pool owns the balance, so a roster naming a global
        // currency asks for a second copy of it. Honored, the chapter would accrue
        // into its own Records while the permanent pool's - the total the income
        // buff and the capstone gate read - stayed at zero.
        [Test]
        public void Build_RefusesARosterIdInAGlobalGroup()
        {
            var chapter = MakeChapter(new List<string> { "cash", "fans", "rehearsal", RecordsId });
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);

            LogAssert.Expect(LogType.Error, new Regex(
                $"roster names currency '{RecordsId}', whose group 'permanent' is placed Global"));

            var context = EconomyContextFactory.Build(chapter, database, permanent, EconomyRecipe.FrontierChapter);

            Assert.IsFalse(context.Pool.Contains(RecordsId), "the chapter pool never gets a second Records balance");
            Assert.AreSame(permanent, ((CurrencyRouter)context.Currencies).OwnerOf(RecordsId),
                "reads still resolve to the one Records balance, in the startup pool");
        }

        // An id in both pools has two balances, and every read would pick one by
        // code order: a spend could charge one while the UI reads the other. The
        // collision is reported and the chapter's own pool wins, so the failure is
        // loud and the chapter still plays.
        [Test]
        public void Build_RefusesAnIdThePermanentPoolAlreadyHolds()
        {
            // 'shared' is filed in the chapter's group AND handed to the startup
            // pool directly, which is the only way to construct the collision
            var chapter = MakeChapter(new List<string> { "cash", "fans", "shared" });
            var run = TestContent.MakeGroup("run", true, CurrencyPlacement.Chapter);
            var database = new ContentDatabase(
                chapters: new[] { chapter },
                currencies: new[]
                {
                    TestContent.MakeCurrency("cash", "run"),
                    TestContent.MakeCurrency("fans", "run"),
                    TestContent.MakeCurrency("shared", "run"),
                },
                currencyGroups: new[] { run });
            var permanent = new CurrencyManager(new[] { run },
                new[] { TestContent.MakeCurrency("shared", "run") });

            LogAssert.Expect(LogType.Error, new Regex(
                "roster names currency 'shared', which the permanent pool already holds"));

            var context = EconomyContextFactory.Build(chapter, database, permanent, EconomyRecipe.FrontierChapter);

            Assert.IsFalse(context.Pool.Contains("shared"), "the shadowing entry is refused, not resolved");
        }

        [Test]
        public void Build_ReportsARosterIdThatResolvesToNoCurrency()
        {
            var chapter = MakeChapter(new List<string> { "cash", "fans", "merch" });
            var database = MakeDatabase(chapter);

            LogAssert.Expect(LogType.Error, new Regex("roster names unknown currency id 'merch'"));

            var context = EconomyContextFactory.Build(chapter, database,
                EconomyContextFactory.BuildPermanentPool(database), EconomyRecipe.FrontierChapter);

            Assert.IsFalse(context.Pool.Contains("merch"));
        }

        // ---- the router ------------------------------------------------------

        // One surface over both pools, so no system chooses: a consumer asks for
        // 'cash' and for Records identically, and which instance answers was
        // decided by placement data at construction.
        [Test]
        public void Currencies_ResolveAcrossBothPools_ThroughOneSurface()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);
            var context = EconomyContextFactory.Build(chapter, database, permanent, EconomyRecipe.FrontierChapter);

            context.Currencies.Add("cash", 5);
            context.Currencies.Add(RecordsId, 3);

            Assert.AreEqual(5.0, context.Pool.Get("cash").ToDouble(), 1e-9, "the chapter pool took the cash");
            Assert.AreEqual(3.0, permanent.Get(RecordsId).ToDouble(), 1e-9, "the startup pool took the Records");
            Assert.AreEqual(3.0, context.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "and the same surface reads it back");
        }

        // One aggregated subscription: a consumer holds one handler no matter how
        // many pools back the surface, which is what keeps the condition context
        // from having a list of sources to keep in step with.
        [Test]
        public void Currencies_AggregateBalanceChangedFromEveryPool()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);
            var context = EconomyContextFactory.Build(chapter, database, permanent, EconomyRecipe.FrontierChapter);

            var seen = new List<string>();
            context.Currencies.BalanceChanged += (id, _) => seen.Add(id);

            context.Currencies.Add("cash", 1);
            context.Currencies.Add(RecordsId, 1);

            CollectionAssert.AreEquivalent(new[] { "cash", RecordsId }, seen,
                "both pools reach the one subscription");
        }

        // A discarded context must stop listening to the startup pool, which
        // outlives it. Invisible with one economy; with two, a dead chapter's
        // subscribers keep receiving balance changes.
        [Test]
        public void Dispose_StopsListeningToThePoolThatOutlivesTheContext()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var permanent = EconomyContextFactory.BuildPermanentPool(database);
            var context = EconomyContextFactory.Build(chapter, database, permanent, EconomyRecipe.FrontierChapter);

            var changes = 0;
            context.Currencies.BalanceChanged += (id, balance) => changes++;

            context.Dispose();
            permanent.Add(RecordsId, 1);

            Assert.AreEqual(0, changes, "a disposed context's surface is deaf to the pool it shared");
        }

        // ---- the recipe ------------------------------------------------------

        // The frontier recipe registers the Records income derivations; an event
        // sandbox's recipe does not, and that ABSENCE is what makes its baseline
        // fixed (slice 8). Declared rather than branched, so it can be read.
        [Test]
        public void Recipe_DecidesWhetherTheRecordsIncomeDerivationsRegister()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var cashProduction = ModifierTargetKey.Of(ModifierTarget.CurrencyProduction, "cash");

            var frontier = EconomyContextFactory.Build(chapter, database,
                EconomyContextFactory.BuildPermanentPool(database), EconomyRecipe.FrontierChapter);
            frontier.Currencies.Add(RecordsId, 10);
            Assert.AreEqual(1.2, frontier.Modifiers.For(cashProduction).Multiply.ToDouble(), 1e-9,
                "0.02 per record x 10, from the chapter's recordBuff");

            var sandbox = EconomyContextFactory.Build(chapter, database,
                EconomyContextFactory.BuildPermanentPool(database),
                new EconomyRecipe(EconomyRecipeKind.EventSandbox));
            sandbox.Currencies.Add(RecordsId, 10);
            Assert.AreEqual(1.0, sandbox.Modifiers.For(cashProduction).Multiply.ToDouble(), 1e-9,
                "the sandbox never registered the derivation, so Records do not reach it");
        }

        // ---- the projection --------------------------------------------------

        // Construction re-projects (design doc section 12, rule 6): the store is
        // built from the FACTS that exist, and every boundary that resets facts
        // asks for the same rebuild. Here the fact is a latched content unlock,
        // and the rebuild is what puts its buff back.
        //
        // The gate is deliberately UNMET at construction, which is how "reads
        // facts, not definitions" is still provable now that construction settles
        // (6.5: Build seeds, projects and settles as one operation, so an unlock
        // whose gate already holds latches before Build returns). An unlatched
        // upgrade is a definition the context can see and a fact it does not have.
        [Test]
        public void ProjectModifiers_RebuildsGrantsFromTheFactsThatExist()
        {
            var upgrade = TestContent.MakeUpgrade("permanent_tap", UpgradeType.ContentUnlock,
                ContentScope.PermanentInChapter, new CurrencyBalanceCondition("cash", 10),
                new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 4));
            var chapter = MakeChapter(upgradeIds: new List<string> { "permanent_tap" });
            var database = MakeDatabase(chapter, upgrades: new List<UpgradeDefinition> { upgrade });
            var context = EconomyContextFactory.Build(chapter, database,
                EconomyContextFactory.BuildPermanentPool(database), EconomyRecipe.FrontierChapter);

            Assert.AreEqual(0.0, context.Modifiers.For(TapValue).Add.ToDouble(), 1e-9,
                "the gate does not hold, so there is no latch to project from");

            context.Currencies.Add("cash", 10);
            context.Settle();
            Assert.AreEqual(4.0, context.Modifiers.For(TapValue).Add.ToDouble(), 1e-9,
                "the unlock's gate held, so it latched and granted");

            // the store is emptied and rebuilt, which is the only mechanism -
            // nothing filters it, so the add returning is proof the LATCH was read
            context.ProjectModifiers();
            Assert.AreEqual(4.0, context.Modifiers.For(TapValue).Add.ToDouble(), 1e-9,
                "re-projecting from the surviving latch grants exactly once again");
        }

        // The construction sequence 6.5 established: a context comes back seeded,
        // projected AND settled, so a caller never has to know to settle it. The
        // ungated unlock below is the shape that used to need an external Settle -
        // it is latched and granted before Build returns.
        [Test]
        public void Build_ReturnsAContextThatHasAlreadySettled()
        {
            var upgrade = TestContent.MakeUpgrade("open_now", UpgradeType.ContentUnlock,
                ContentScope.PermanentInChapter, null,
                new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 4));
            var chapter = MakeChapter(upgradeIds: new List<string> { "open_now" });
            var database = MakeDatabase(chapter, upgrades: new List<UpgradeDefinition> { upgrade });

            var context = EconomyContextFactory.Build(chapter, database,
                EconomyContextFactory.BuildPermanentPool(database), EconomyRecipe.FrontierChapter);

            Assert.IsTrue(context.Upgrades.Get("open_now").Applied,
                "an ungated content unlock latched during construction");
            Assert.AreEqual(4.0, context.Modifiers.For(TapValue).Add.ToDouble(), 1e-9,
                "and its payload is in the store, with no external Settle");
        }

        // ---- SelectBar -------------------------------------------------------

        // One cover bar filling from rehearsal, and a content unlock gated on
        // completing it: the smallest content where a selection has something
        // to settle.
        private static EconomyContext BuildBarEconomy()
        {
            var chapter = TestContent.MakeChapter("garage", new List<string> { "gigs" },
                currencyIds: new List<string> { "cash", "fans", "rehearsal" },
                upgradeIds: new List<string> { "play_gigs" },
                barGroupIds: new List<string> { "learn_covers" });
            var database = TestContent.MakeDatabase(
                chapters: new[] { chapter },
                upgrades: new List<UpgradeDefinition>
                {
                    TestContent.MakeUpgrade("play_gigs", UpgradeType.ContentUnlock,
                        ContentScope.PermanentInChapter,
                        new BarsCompletedCondition("learn_covers", 1), new SetFlagEffect("gigs")),
                },
                bars: new List<BarDefinition> { TestContent.MakeBar("cover_1", "rehearsal", 120) },
                barGroups: new List<BarGroupDefinition>
                {
                    TestContent.MakeBarGroup("learn_covers", null, new List<string> { "cover_1" }),
                },
                currencies: new[]
                {
                    TestContent.MakeCurrency("cash", "run"),
                    TestContent.MakeCurrency("fans", "run"),
                    TestContent.MakeCurrency("rehearsal", "run"),
                    TestContent.MakeCurrency(RecordsId, "permanent"),
                });
            return EconomyContextFactory.Build(chapter, database,
                EconomyContextFactory.BuildPermanentPool(database), EconomyRecipe.FrontierChapter);
        }

        // Selection is a top-level operation, not a UI detail: retargeting pours
        // the pool that accumulated while nothing was selected, the pour can
        // complete the bar, and a completion is a condition input. No tick runs
        // here, so the unlock latching is proof the operation settled itself.
        [Test]
        public void SelectBar_PoursTheAccumulatedPool_AndSettles()
        {
            var context = BuildBarEconomy();

            context.Currencies.Add("rehearsal", 120);
            Assert.IsFalse(context.Flags.IsSet("gigs"), "nothing has completed yet");

            context.SelectBar("learn_covers", "cover_1");

            Assert.AreEqual(0.0, context.Currencies.Get("rehearsal").ToDouble(), 1e-9,
                "the accumulated pool poured on selection");
            Assert.IsTrue(context.Upgrades.Get("play_gigs").Applied,
                "the completion satisfied the barsCompleted gate with no tick: the operation settled");
            Assert.IsTrue(context.Flags.IsSet("gigs"), "and the latched unlock's payload applied");
        }

        // the toggle's other half (BarRowUI re-selecting the active bar): a null
        // bar id clears the target through the same operation
        [Test]
        public void SelectBar_WithANullBarId_ClearsTheTarget()
        {
            var context = BuildBarEconomy();
            var covers = (PerBarContinuousRuntime)context.Bars.GetRuntime("learn_covers");

            // a partial pour, so the selection is still standing to be cleared
            context.Currencies.Add("rehearsal", 50);
            context.SelectBar("learn_covers", "cover_1");
            Assert.IsNotNull(covers.ActiveBar, "the partial pour left the bar selected");

            context.SelectBar("learn_covers", null);

            Assert.IsNull(covers.ActiveBar);
        }

        // an unknown group id is reported (by the bar system, which owns the
        // roster) and the operation is refused rather than thrown out of
        [Test]
        public void SelectBar_ReportsAnUnknownGroupId()
        {
            var context = BuildBarEconomy();

            LogAssert.Expect(LogType.Error, new Regex("unknown bar group id 'setlist'"));

            context.SelectBar("setlist", "cover_1");
        }

        // ---- CompleteCapstone --------------------------------------------------

        private const string RoadiesId = GameManager.RoadiesCurrencyId;

        // the real capstone's shape: gated on cumulative Records, declaring its
        // completion flag, awarding one Roadie as a one-shot action
        private static CapstoneConfig MakeCapstone(List<GameAction> actions = null, GameEffect onComplete = null)
            => new("backyard_party", "Play the Backyard Party",
                new RecordsCumulativeCondition(30), "chapter_2", onComplete,
                actions ?? new List<GameAction> { new GrantCurrencyAction(RoadiesId, 1) });

        // Roadies joins Records in the global group, like the running game's:
        // the action's CanExecute probe needs a pool that owns the id to answer
        // from, and the award has to land somewhere a release never resets.
        private static EconomyContext BuildCapstoneEconomy(CapstoneConfig capstone,
            List<UpgradeDefinition> upgrades = null, List<string> upgradeIds = null)
        {
            var chapter = TestContent.MakeChapter("garage", null,
                currencyIds: new List<string> { "cash", "fans" },
                upgradeIds: upgradeIds,
                flags: new List<FlagDeclaration> { new("chapter_2") },
                capstone: capstone);
            var database = new ContentDatabase(
                chapters: new[] { chapter },
                upgrades: upgrades,
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
            return EconomyContextFactory.Build(chapter, database,
                EconomyContextFactory.BuildPermanentPool(database), EconomyRecipe.FrontierChapter);
        }

        // The completion is one operation ending at one settle: the run banks
        // through the release's own path (the payout formula has exactly one
        // home), the declared flag latches, and an unlock gated on that flag is
        // applied by the time the call returns - no tick, no second settle. The
        // latched unlock is the proof the settle ran once, after ALL the facts:
        // had the mid-operation release settled on its own, the unlock would
        // have evaluated against a banked run whose chapter had not finished.
        [Test]
        public void CompleteCapstone_BanksTheRunAndLatchesTheFlag_InOneSettle()
        {
            var epilogue = TestContent.MakeUpgrade("epilogue", UpgradeType.ContentUnlock,
                ContentScope.PermanentInChapter, new FlagSetCondition("chapter_2"),
                new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 4));
            var context = BuildCapstoneEconomy(MakeCapstone(),
                upgrades: new List<UpgradeDefinition> { epilogue },
                upgradeIds: new List<string> { "epilogue" });

            context.Currencies.Add(RecordsId, 30); // the gate reads the earned total
            context.Currencies.Add("fans", 500);   // floor((500/5)^0.5) = 10 Records

            Assert.IsTrue(context.CompleteCapstone());

            Assert.AreEqual(0.0, context.Currencies.Get("fans").ToDouble(), 1e-9,
                "the capstone implicitly cut an album: the run reset");
            Assert.AreEqual(40.0, context.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "and the fans banked through the release's own formula");
            Assert.IsTrue(context.Flags.IsSet("chapter_2"),
                "the operation set its declared flag - no payload carried a copy");
            Assert.IsTrue(context.Upgrades.Get("epilogue").Applied,
                "an unlock gated on the flag latched with no tick: the operation settled itself");
        }

        // One-shot means once EVER, not once per path: the refusal, the release
        // and the load all leave the payout alone, because none of those paths
        // holds a GameAction to run - the operation that earned it is the only
        // line in the system that executes it.
        [Test]
        public void CompleteCapstone_PaysActionsOnce_AndNoRebuildPaysThemAgain()
        {
            var context = BuildCapstoneEconomy(MakeCapstone());
            context.Currencies.Add(RecordsId, 30);
            context.Currencies.Add("fans", 125);

            Assert.IsTrue(context.CompleteCapstone());
            Assert.AreEqual(1.0, context.Currencies.Get(RoadiesId).ToDouble(), 1e-9, "the one-shot paid");

            Assert.IsFalse(context.CompleteCapstone(), "a finished chapter does not complete twice");
            context.ReleaseAlbum();
            context.Restore(context.CaptureLocalState());

            Assert.AreEqual(1.0, context.Currencies.Get(RoadiesId).ToDouble(), 1e-9,
                "a second attempt, a release and a load all left the Roadie count alone");
            Assert.IsTrue(context.Flags.IsSet("chapter_2"),
                "and the permanent completion flag survived every boundary");
        }

        // The gate is asked by the operation, not only by the button that offered
        // it (fail-closed like TryBuy, unlike the deliberately ungated release):
        // a completion latches a permanent flag, so a UI bug must not be able to
        // finish a chapter early.
        [Test]
        public void CompleteCapstone_RefusesWhileTheGateIsUnmet()
        {
            var context = BuildCapstoneEconomy(MakeCapstone());
            context.Currencies.Add(RecordsId, 29);
            context.Currencies.Add("fans", 500);

            Assert.IsFalse(context.CompleteCapstone());

            Assert.AreEqual(500.0, context.Currencies.Get("fans").ToDouble(), 1e-9,
                "a refused completion releases nothing");
            Assert.IsFalse(context.Flags.IsSet("chapter_2"));
            Assert.AreEqual(0.0, context.Currencies.Get(RoadiesId).ToDouble(), 1e-9);
        }

        // The preflight runs BEFORE the irreversible release - the same
        // charged-for-nothing rule TryBuy applies. A completion that banked the
        // run and then failed to award would strand it: the run's fans are gone
        // and the chapter can never pay what it promised.
        [Test]
        public void CompleteCapstone_RefusesWhenAnActionCannotExecute_BeforeTheRelease()
        {
            var context = BuildCapstoneEconomy(MakeCapstone(
                actions: new List<GameAction> { new GrantCurrencyAction("merch", 1) }));
            context.Currencies.Add(RecordsId, 30);
            context.Currencies.Add("fans", 500);

            LogAssert.Expect(LogType.Error, new Regex(
                "capstone 'backyard_party' has an action that cannot execute"));

            Assert.IsFalse(context.CompleteCapstone());

            Assert.AreEqual(500.0, context.Currencies.Get("fans").ToDouble(), 1e-9,
                "the refusal came before the release: the run was not banked for nothing");
            Assert.IsFalse(context.Flags.IsSet("chapter_2"));
        }

        // a chapter with no capstone has nothing to complete, and a caller
        // asking anyway is a broken caller - reported, not thrown out of
        [Test]
        public void CompleteCapstone_OnAChapterAuthoringNone_IsRefusedLoudly()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var context = EconomyContextFactory.Build(chapter, database,
                EconomyContextFactory.BuildPermanentPool(database), EconomyRecipe.FrontierChapter);

            LogAssert.Expect(LogType.Error, new Regex("authors no capstone"));

            Assert.IsFalse(context.CompleteCapstone());
        }

        // ---- focus lifecycle -------------------------------------------------

        // Only a focused economy accrues (rule 7). The context enforces it from
        // its own side, so a stray reference cannot tick a background economy
        // even if the router hands it a tick.
        [Test]
        public void Tick_DoesNothingWhileUnfocused()
        {
            // a producer that trickles cash on the tick, so "nothing accrued" is
            // an actual claim about the tick rather than about an empty chapter
            var trickle = TestContent.MakeProducer("jam", new List<ProductionConfig>
            {
                new("cash", 10, ProductionTrigger.Tick, null, ModifierTarget.None),
            });
            var chapter = MakeChapter(producerIds: new List<string> { "jam" });
            var database = MakeDatabase(chapter, producers: new List<ProducerDefinition> { trickle });
            var context = EconomyContextFactory.Build(chapter, database,
                EconomyContextFactory.BuildPermanentPool(database), EconomyRecipe.FrontierChapter);

            Assert.IsFalse(context.IsFocused, "a context starts constructed, not focused");

            context.Tick(1.0);
            Assert.AreEqual(0.0, context.Currencies.Get("cash").ToDouble(), 1e-9,
                "an unfocused economy accrues nothing, even with a live producer");

            context.Focus();
            Assert.IsTrue(context.IsFocused);

            context.Tick(1.0);
            Assert.AreEqual(10.0, context.Currencies.Get("cash").ToDouble(), 1e-9,
                "and the same tick pays once it has focus");
        }

        // The value slice 9's idle earnings will read: how long this economy has
        // been away. Stamped by the context on focus loss, because GameManager
        // routing the tick elsewhere IS the event.
        [Test]
        public void Unfocus_StampsTheLastInteractionTime()
        {
            var chapter = MakeChapter();
            var database = MakeDatabase(chapter);
            var context = EconomyContextFactory.Build(chapter, database,
                EconomyContextFactory.BuildPermanentPool(database), EconomyRecipe.FrontierChapter);

            Assert.IsNull(context.LastInteractionUtc,
                "null until the first time focus is lost: an economy that has never been away has no away-time");

            context.Focus();
            context.Unfocus();

            Assert.IsNotNull(context.LastInteractionUtc);
            Assert.IsFalse(context.IsFocused);
        }
    }
}
