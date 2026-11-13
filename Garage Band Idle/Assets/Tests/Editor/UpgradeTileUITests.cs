using System;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Editor;
using RidiculousGaming.GarageBandIdle.Monetization;
using RidiculousGaming.GarageBandIdle.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The strip layout, the upgrade tile and its details window (design doc
    // 12.11), driven through the shipping Screen.uxml and a real host. Two
    // fixtures: the test tree with two authored sections - one without a title
    // and one strip of two tiles - and chapter 2's imported Office. Every
    // assertion reads a shown value, a class or an element's place - never a
    // label's text.
    public class UpgradeTileUITests
    {
        // ---- over the test tree ----

        private class TreeFixture
        {
            public readonly TestTree Tree = new();
            public readonly SectionDefinition Untitled;
            public readonly SectionDefinition Office;
            public readonly UpgradeDefinition Tuning;
            public readonly GameSession Session;
            public readonly ScreenHost Host;

            // Section one carries no title and lists the Cash line and a row for
            // a levelled upgrade; section two is a strip of the two tier1
            // upgrades. Both always show.
            public TreeFixture()
            {
                Tuning = TestTree.MakeLevelled("amp_tuning", Tree.Cash, "practice_amp", Stat.Rate, (100, 2), (400, 5));
                Tree.Tier1Def.upgrades.Add(Tuning);
                // The chrome the host binds: Screen.uxml's pill names timer
                // 'encore_timer', and the host requires root to declare it.
                TestTree.DeclareEncore(Tree.RootDef);
                Untitled = new SectionDefinition
                {
                    visibleWhen = new Always(),
                    scope = Tree.Tier1Def,
                    modules =
                    {
                        new ModuleDefinition { prefabId = "currency_line", content = Tree.Cash, scope = Tree.Tier1Def },
                        new ModuleDefinition { prefabId = "upgrade_row", content = Tuning, scope = Tree.Tier1Def },
                    },
                };
                Office = new SectionDefinition
                {
                    title = "The Office",
                    visibleWhen = new Always(),
                    scope = Tree.Tier1Def,
                    layout = SectionLayout.Strip,
                    modules = { TileModule(Tree.StagePresence), TileModule(Tree.AmpStrings) },
                };
                Tree.Ch1Def.sections.Add(Untitled);
                Tree.Ch1Def.sections.Add(Office);
                // The link pass runs in Build, so the sections and their
                // modules are wired only once the tree is built again.
                Tree.Rebuild();

                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.maxGameSpeed = 4;
                Session = new GameSession(Tree.Root, config);
                var clock = new GameClock(Tree.Now);
                var screen = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Screen.uxml");
                Assert.IsNotNull(screen, "Assets/UI/Screen.uxml is missing");
                var registry = AssetDatabase.LoadAssetAtPath<ModuleRegistry>("Assets/Settings/ModuleRegistry.asset");
                Assert.IsNotNull(registry, "Assets/Settings/ModuleRegistry.asset is missing.");
                Host = new ScreenHost(screen.Instantiate(), registry, Session, clock,
                    new AdManager(Session, new FakeAdService(), () => { }),
                    new IAPManager(Session, new FakeStoreService(), config, () => { }));

                // The stamp is now, so no idle window exists and the phase lands
                // Live; the switch's own refresh is the first render.
                Tree.Ch1.lastActiveUtc = Tree.Now;
                Session.SwitchChapter(Tree.Ch1, Tree.Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
                Session.Drain();
            }

            private ModuleDefinition TileModule(UpgradeDefinition upgrade) =>
                new() { prefabId = "upgrade_tile", content = upgrade, scope = Tree.Tier1Def };

            public GameContext Ctx => new GameContext(Tree.Tier1, Tree.Now);

            public ScreenHost.SectionView View(SectionDefinition section) =>
                Host.Sections.Single(view => view.Definition == section);

            public UpgradeTileUI Tile(UpgradeDefinition upgrade) =>
                (UpgradeTileUI)View(Office).Modules.Single(module => module.Definition.content == upgrade).Widget;

            public UpgradeRowUI Row(UpgradeDefinition upgrade) =>
                (UpgradeRowUI)View(Untitled).Modules.Single(module => module.Definition.content == upgrade).Widget;

            // Earned and held: the upgrades' gates read the earned total.
            public void Earn(BigNumber cash)
            {
                Ctx.Deposit(Tree.Cash.Id, cash);
                Session.Refresh();
            }

            public BigNumber Cash => Ctx.GetBalance(Tree.Cash.Id);
        }

        [Test]
        public void A_section_without_a_title_draws_no_title_element()
        {
            var fx = new TreeFixture();

            Assert.IsTrue(fx.View(fx.Untitled).Visible);
            Assert.IsNull(fx.View(fx.Untitled).Root.Q<Label>(className: "section-title"),
                "no title is authored, so no header is drawn");
            Assert.IsNotNull(fx.View(fx.Office).Root.Q<Label>(className: "section-title"),
                "an authored title is drawn");
        }

        [Test]
        public void The_strip_layout_puts_its_modules_in_one_sideways_scroll_view()
        {
            var fx = new TreeFixture();
            var office = fx.View(fx.Office);

            Assert.IsNotNull(office.Strip);
            Assert.AreEqual(ScrollViewMode.Horizontal, office.Strip.mode);
            Assert.AreEqual(ScrollerVisibility.AlwaysVisible, office.Strip.horizontalScrollerVisibility,
                "the thin bar beneath is always drawn");
            Assert.AreEqual(ScrollerVisibility.Hidden, office.Strip.verticalScrollerVisibility);
            Assert.AreSame(office.Root, office.Strip.parent);

            var content = office.Strip.contentContainer;
            var first = fx.Tile(fx.Tree.StagePresence).Root;
            var second = fx.Tile(fx.Tree.AmpStrings).Root;
            Assert.AreSame(content, first.parent);
            Assert.AreSame(content, second.parent);
            Assert.Less(content.IndexOf(first), content.IndexOf(second), "authored order");

            // The list layout keeps its plain container.
            var list = fx.View(fx.Untitled);
            Assert.IsNull(list.Strip);
            Assert.IsNotInstanceOf<ScrollView>(list.Modules[0].Widget.Root.parent);
            Assert.IsNull(list.Root.Q<ScrollView>());
        }

        // A module's own gate works in a strip exactly as in a list: hidden
        // until it holds, then shown in its authored place.
        [Test]
        public void A_strip_module_follows_its_own_visibleWhen()
        {
            var gated = new TestTree();
            TestTree.DeclareEncore(gated.RootDef);
            gated.Tier1Def.declaredFlags.Add("tiles_revealed");
            var strip = new SectionDefinition
            {
                title = "The Office",
                visibleWhen = new Always(),
                scope = gated.Tier1Def,
                layout = SectionLayout.Strip,
                modules =
                {
                    new ModuleDefinition
                    {
                        prefabId = "upgrade_tile", content = gated.StagePresence, scope = gated.Tier1Def,
                        visibleWhen = new FlagSet { flagId = "tiles_revealed" },
                    },
                    new ModuleDefinition { prefabId = "upgrade_tile", content = gated.AmpStrings, scope = gated.Tier1Def },
                },
            };
            gated.Ch1Def.sections.Add(strip);
            gated.Rebuild();
            var config = ScriptableObject.CreateInstance<GameConfig>();
            var session = new GameSession(gated.Root, config);
            var host = new ScreenHost(
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Screen.uxml").Instantiate(),
                AssetDatabase.LoadAssetAtPath<ModuleRegistry>("Assets/Settings/ModuleRegistry.asset"),
                session, new GameClock(gated.Now),
                new AdManager(session, new FakeAdService(), () => { }),
                new IAPManager(session, new FakeStoreService(), config, () => { }));
            gated.Ch1.lastActiveUtc = gated.Now;
            session.SwitchChapter(gated.Ch1, gated.Now);
            session.Drain();

            var view = host.Sections.Single();
            Assert.IsFalse(view.Modules[0].Visible, "the flag is not set");
            Assert.IsTrue(view.Modules[1].Visible);

            gated.Tier1.flags.Add("tiles_revealed");
            session.Refresh();

            Assert.IsTrue(view.Modules[0].Visible);
            var content = view.Strip.contentContainer;
            Assert.AreEqual(0, content.IndexOf(view.Modules[0].Widget.Root), "a late tile lands in its authored place");
            Assert.AreEqual(1, content.IndexOf(view.Modules[1].Widget.Root));
        }

        [Test]
        public void A_tile_shows_its_price_until_bought_and_owned_after()
        {
            var fx = new TreeFixture();
            var tile = fx.Tile(fx.Tree.StagePresence);

            Assert.AreSame(fx.Tree.StagePresence, tile.Upgrade);
            Assert.IsFalse(tile.ShownOwned);
            Assert.IsFalse(tile.ShownBuyable, "nothing earned yet");
            Assert.AreEqual(Purchasing.CostOf(fx.Tree.StagePresence, fx.Ctx), tile.ShownCost);
            Assert.IsTrue(tile.Root.ClassListContains("tile-unbuyable"));
            Assert.IsTrue(tile.Root.Q<Button>("tile").enabledSelf,
                "dimmed by a class, never disabled, so the hold still reaches it");

            fx.Earn(250);

            Assert.IsTrue(tile.ShownBuyable);
            Assert.IsFalse(tile.Root.ClassListContains("tile-unbuyable"));
            Assert.AreEqual(Purchasing.CostOf(fx.Tree.StagePresence, fx.Ctx), tile.ShownCost);

            tile.Buy();

            Assert.IsTrue(tile.ShownOwned);
            Assert.IsFalse(tile.ShownBuyable);
            Assert.AreEqual(BigNumber.Zero, tile.ShownCost);
            Assert.IsTrue(tile.Root.ClassListContains("tile-owned"));
            Assert.IsFalse(tile.Root.ClassListContains("tile-unbuyable"), "owned is not dimmed");
        }

        [Test]
        public void A_tap_buys_once_and_a_second_tap_does_nothing()
        {
            var fx = new TreeFixture();
            var tile = fx.Tile(fx.Tree.StagePresence);
            var cost = Purchasing.CostOf(fx.Tree.StagePresence, fx.Ctx);
            fx.Earn(cost * 3);

            tile.Buy();

            Assert.IsTrue(fx.Ctx.IsUpgradePurchased(fx.Tree.StagePresence.Id));
            Assert.AreEqual(cost * 2, fx.Cash, "one purchase spent");

            tile.Buy();

            Assert.AreEqual(cost * 2, fx.Cash, "the second tap spent nothing");
        }

        [Test]
        public void A_tap_on_a_tile_that_cannot_be_bought_does_nothing()
        {
            var fx = new TreeFixture();
            var tile = fx.Tile(fx.Tree.AmpStrings);
            Assert.IsFalse(tile.ShownBuyable);
            var before = fx.Cash;

            tile.Buy();

            Assert.IsFalse(fx.Ctx.IsUpgradePurchased(fx.Tree.AmpStrings.Id));
            Assert.AreEqual(before, fx.Cash);
        }

        [Test]
        public void The_hold_opens_the_details_window_and_buys_nothing()
        {
            var fx = new TreeFixture();
            var tile = fx.Tile(fx.Tree.StagePresence);
            fx.Earn(250);
            var window = fx.Host.UpgradeDetails;
            Assert.IsFalse(window.IsOpen);

            tile.OpenDetails();

            Assert.IsTrue(window.IsOpen);
            Assert.AreSame(fx.Tree.StagePresence, window.ShownUpgrade);
            Assert.AreSame(fx.Tree.Tier1, window.HeldScope, "the declaring scope the tile resolved");
            Assert.IsFalse(fx.Ctx.IsUpgradePurchased(fx.Tree.StagePresence.Id), "a hold is never a buy");
            Assert.AreEqual((BigNumber)250, fx.Cash);
        }

        [Test]
        public void The_details_window_reads_the_owned_state_and_Close_closes_it()
        {
            var fx = new TreeFixture();
            var tile = fx.Tile(fx.Tree.StagePresence);
            fx.Earn(250);
            tile.OpenDetails();
            var window = fx.Host.UpgradeDetails;

            Assert.IsFalse(window.ShownOwned, "the paragraph says what a purchase would do");

            tile.Buy();

            Assert.IsTrue(window.IsOpen, "a purchase beneath leaves the window up");
            Assert.IsTrue(window.ShownOwned, "the paragraph says what the upgrade is doing");

            window.Close();

            Assert.IsFalse(window.IsOpen);
            Assert.IsNull(window.ShownUpgrade);
            fx.Session.Refresh();
            Assert.IsFalse(window.IsOpen, "a refresh does not bring a closed window back");
        }

        // A levelled upgrade's row (12.11): the owned level beside the name,
        // the next level's price until the last, then Bought.
        [Test]
        public void A_levelled_rows_level_and_price_follow_each_level_and_read_Bought_at_the_last()
        {
            var fx = new TreeFixture();
            var row = fx.Row(fx.Tuning);
            fx.Earn(1000);

            Assert.AreEqual(0, row.ShownLevel, "nothing owned, no level shown");
            Assert.AreEqual(2, row.ShownMaxLevel);
            Assert.IsFalse(row.ShownBought);
            Assert.AreEqual(Purchasing.CostOf(fx.Tuning, fx.Ctx), row.ShownCost);
            Assert.AreEqual((BigNumber)100, row.ShownCost, "the first level's price");

            fx.Session.TryBuy(fx.Ctx, fx.Tuning);

            Assert.AreEqual(1, row.ShownLevel);
            Assert.IsFalse(row.ShownBought, "a level is left to buy");
            Assert.AreEqual((BigNumber)400, row.ShownCost, "the next level's price");

            fx.Session.TryBuy(fx.Ctx, fx.Tuning);

            Assert.AreEqual(2, row.ShownLevel);
            Assert.IsTrue(row.ShownBought, "the last level owns it");
            Assert.AreEqual(BigNumber.Zero, row.ShownCost);
            Assert.IsTrue(row.Root.ClassListContains("row-bought"));
        }

        // One press moves one view: the axis that travelled further owns the
        // drag, a tie going to the page.
        [Test]
        public void A_drag_starts_on_the_axis_that_travelled_further()
        {
            var down = new Vector2(0f, 40f);
            var across = new Vector2(40f, 0f);
            var diagonalDown = new Vector2(30f, 40f);
            var diagonalAcross = new Vector2(40f, 30f);
            var tie = new Vector2(30f, 30f);
            var short_ = new Vector2(23f, 0f);

            Assert.IsTrue(DragScrollManipulator.Starts(down, ScrollViewMode.Vertical));
            Assert.IsFalse(DragScrollManipulator.Starts(down, ScrollViewMode.Horizontal));
            Assert.IsTrue(DragScrollManipulator.Starts(across, ScrollViewMode.Horizontal));
            Assert.IsFalse(DragScrollManipulator.Starts(across, ScrollViewMode.Vertical));
            Assert.IsTrue(DragScrollManipulator.Starts(diagonalDown, ScrollViewMode.Vertical));
            Assert.IsFalse(DragScrollManipulator.Starts(diagonalDown, ScrollViewMode.Horizontal));
            Assert.IsTrue(DragScrollManipulator.Starts(diagonalAcross, ScrollViewMode.Horizontal));
            Assert.IsFalse(DragScrollManipulator.Starts(diagonalAcross, ScrollViewMode.Vertical));
            Assert.IsTrue(DragScrollManipulator.Starts(tie, ScrollViewMode.Vertical), "a tie is the page's");
            Assert.IsFalse(DragScrollManipulator.Starts(tie, ScrollViewMode.Horizontal));
            Assert.IsFalse(DragScrollManipulator.Starts(short_, ScrollViewMode.Horizontal), "short of the start");
            Assert.IsFalse(DragScrollManipulator.Starts(-short_, ScrollViewMode.Horizontal));
            Assert.IsTrue(DragScrollManipulator.Starts(-across, ScrollViewMode.Horizontal), "leftward too");
        }

        // ---- chapter 2's Office ----

        private class OfficeFixture
        {
            public static readonly DateTime Start = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

            public readonly TierDefinition Tier2Def;
            public readonly CurrencyDefinition Cash;
            public readonly UpgradeDefinition CrowdWork, BulkBooking;
            public readonly ChapterScopeState Ch2;
            public readonly TierScopeState Tier2;
            public readonly GameSession Session;
            public readonly ScreenHost Host;
            public readonly DateTime Now = Start;

            // The roster boot composes, entered as a live player enters it,
            // with the shipping screen over it.
            public OfficeFixture()
            {
                var rootDef = AssetDatabase.LoadAssetAtPath<RootDefinition>(
                    ChapterJsonImporter.AssetRootPath + "/root/root.asset");
                var ch1Def = AssetDatabase.LoadAssetAtPath<ChapterDefinition>(
                    ChapterJsonImporter.AssetRootPath + "/ch1/ch1.asset");
                var ch2Def = AssetDatabase.LoadAssetAtPath<ChapterDefinition>(
                    ChapterJsonImporter.AssetRootPath + "/ch2/ch2.asset");
                Assert.IsNotNull(rootDef, "root.json has not been imported - run Garage Band Idle/Import Content.");
                Assert.IsNotNull(ch1Def, "chapter-01.json has not been imported - run Garage Band Idle/Import Content.");
                Assert.IsNotNull(ch2Def, "chapter-02.json has not been imported - run Garage Band Idle/Import Content.");
                Tier2Def = (TierDefinition)ch2Def.children.Single();
                Cash = Tier2Def.declaredCurrencies.Single(c => c.Id == "cash");
                CrowdWork = Tier2Def.upgrades.Single(u => u.Id == "crowd_work");
                BulkBooking = Tier2Def.upgrades.Single(u => u.Id == "bulk_booking");

                var root = ScopeState.Build(ComposedContent.Compose(rootDef, new[] { ch1Def, ch2Def }));
                Ch2 = (ChapterScopeState)TestNavigation.Node(root, ch2Def);
                Tier2 = (TierScopeState)TestNavigation.Node(root, Tier2Def);
                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.maxGameSpeed = 4;
                config.minimumAwaySeconds = 180;
                config.idleCapSeconds = 14400;
                Session = new GameSession(root, config);
                var screen = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Screen.uxml");
                var registry = AssetDatabase.LoadAssetAtPath<ModuleRegistry>("Assets/Settings/ModuleRegistry.asset");
                Host = new ScreenHost(screen.Instantiate(), registry, Session, new GameClock(Now),
                    new AdManager(Session, new FakeAdService(), () => { }),
                    new IAPManager(Session, new FakeStoreService(), config, () => { }));

                root.flags.Add("ch1_complete");
                Ch2.lastActiveUtc = Now;
                Session.SwitchChapter(Ch2, Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
                Session.Drain();
            }

            public GameContext Ctx => new GameContext(Tier2, Now);

            // The Office is the chapter's one strip.
            public ScreenHost.SectionView Office =>
                Host.Sections.Single(view => view.Definition.layout == SectionLayout.Strip);

            public ScreenHost.ModuleView Module(UpgradeDefinition upgrade) =>
                Office.Modules.Single(module => module.Definition.content == upgrade);

            public void Reveal()
            {
                Ch2.flags.Add("office_revealed");
                Session.Refresh();
            }
        }

        [Test]
        public void Crowd_Work_shows_first_whenever_the_Office_does()
        {
            var fx = new OfficeFixture();
            Assert.IsFalse(fx.Office.Visible, "the Office waits for its reveal");

            fx.Reveal();

            Assert.IsTrue(fx.Office.Visible);
            Assert.IsTrue(fx.Module(fx.CrowdWork).Visible);
            Assert.AreSame(fx.Module(fx.CrowdWork), fx.Office.Modules[0], "the strip's first tile");
            Assert.IsInstanceOf<UpgradeTileUI>(fx.Module(fx.CrowdWork).Widget);
        }

        // One tap buys one level at that level's price: the tile shows the
        // owned level beside the name and the next level's price, then Owned at
        // the last; the details window says what buying does, then the current
        // level beside the next, then the current level alone.
        [Test]
        public void Crowd_Work_buys_one_level_per_tap_and_its_tile_and_window_follow_the_level()
        {
            var fx = new OfficeFixture();
            fx.Reveal();
            var tile = (UpgradeTileUI)fx.Module(fx.CrowdWork).Widget;
            var window = fx.Host.UpgradeDetails;
            tile.OpenDetails();

            Assert.AreEqual(0, tile.ShownLevel, "nothing owned, no level shown");
            Assert.AreEqual(2, tile.ShownMaxLevel);
            Assert.IsFalse(tile.ShownOwned);
            Assert.IsFalse(tile.ShownBuyable, "no cash yet");
            Assert.AreEqual(Purchasing.CostOf(fx.CrowdWork, fx.Ctx), tile.ShownCost);
            Assert.AreEqual((BigNumber)300, tile.ShownCost, "the first level's price");
            Assert.AreEqual(0, window.ShownLevel);
            Assert.IsFalse(window.ShownCurrent, "nothing is doing anything yet");
            Assert.IsTrue(window.ShownNext, "what buying does");
            Assert.AreEqual(1, window.ShownNextLevel);

            fx.Ctx.Deposit(fx.Cash.Id, tile.ShownCost);
            fx.Session.Refresh();
            Assert.IsTrue(tile.ShownBuyable);
            tile.Buy();

            Assert.AreEqual(1, fx.CrowdWork.LevelAt(fx.Tier2));
            Assert.IsTrue(fx.Ctx.IsUpgradePurchased(fx.CrowdWork.Id), "the latch stands from the first level");
            Assert.AreEqual(1, tile.ShownLevel);
            Assert.IsFalse(tile.ShownOwned, "a level is left to buy");
            Assert.AreEqual(Purchasing.CostOf(fx.CrowdWork, fx.Ctx), tile.ShownCost);
            Assert.AreEqual((BigNumber)25000, tile.ShownCost, "the second level's price");
            Assert.IsTrue(window.IsOpen, "a purchase beneath leaves the window up");
            Assert.AreEqual(1, window.ShownLevel);
            Assert.IsTrue(window.ShownCurrent);
            Assert.AreEqual(1, window.ShownCurrentLevel);
            Assert.IsTrue(window.ShownNext);
            Assert.AreEqual(2, window.ShownNextLevel);
            Assert.IsFalse(window.ShownOwned);

            fx.Ctx.Deposit(fx.Cash.Id, tile.ShownCost);
            fx.Session.Refresh();
            tile.Buy();

            Assert.AreEqual(2, fx.CrowdWork.LevelAt(fx.Tier2));
            Assert.AreEqual(2, tile.ShownLevel);
            Assert.IsTrue(tile.ShownOwned, "the last level owns it");
            Assert.IsFalse(tile.ShownBuyable);
            Assert.AreEqual(BigNumber.Zero, tile.ShownCost);
            Assert.IsTrue(tile.Root.ClassListContains("tile-owned"));
            Assert.AreEqual(2, window.ShownLevel);
            Assert.IsTrue(window.ShownOwned);
            Assert.IsTrue(window.ShownCurrent);
            Assert.AreEqual(2, window.ShownCurrentLevel);
            Assert.IsFalse(window.ShownNext, "nothing left to buy");
            Assert.AreEqual(0, window.ShownNextLevel);

            // Funded, a tap at the last level buys nothing.
            fx.Ctx.Deposit(fx.Cash.Id, 100000);
            var cash = fx.Ctx.GetBalance(fx.Cash.Id);
            tile.Buy();
            Assert.AreEqual(cash, fx.Ctx.GetBalance(fx.Cash.Id), "a tap spends nothing");
            Assert.AreEqual(2, fx.CrowdWork.LevelAt(fx.Tier2));
            Assert.IsTrue(fx.Module(fx.CrowdWork).Visible, "the owned tile stays");
        }

        [Test]
        public void Bulk_Booking_stays_on_the_strip_once_bought()
        {
            var fx = new OfficeFixture();
            fx.Reveal();
            var chapter = new GameContext(fx.Ch2, fx.Now);
            chapter.Deposit("eps", 8);
            fx.Ctx.Deposit(fx.Cash.Id, Purchasing.CostOf(fx.BulkBooking, fx.Ctx));
            fx.Session.Refresh();
            var module = fx.Module(fx.BulkBooking);
            Assert.IsTrue(module.Visible);
            var tile = (UpgradeTileUI)module.Widget;
            Assert.IsTrue(tile.ShownBuyable);

            tile.Buy();

            Assert.IsTrue(tile.ShownOwned);
            Assert.IsTrue(module.Visible, "bought, and still on the strip");

            // The purchase alone carries it: with the EP clause false the
            // owned tile still shows.
            chapter.SetBalance("eps", BigNumber.Zero);
            fx.Session.Refresh();
            Assert.IsTrue(module.Visible);
            Assert.IsTrue(tile.ShownOwned);
        }

        // Both switches are priced in the run's Cash, so their latch clears on
        // an EP; each declares ownedWhen on the chapter flag its payload sets,
        // so after the release the tile still reads owned, a tap spends
        // nothing, the tile stays on the strip, and what the flag switches on
        // still applies.
        [Test]
        public void Bulk_Booking_and_Favors_read_owned_after_the_EP_and_keep_applying()
        {
            var fx = new OfficeFixture();
            fx.Reveal();
            var favors = fx.Tier2Def.upgrades.Single(u => u.Id == "favors");
            var draw = fx.Tier2Def.generators.Single(g => g.Id == "open_mic_draw");
            var fans = fx.Tier2Def.declaredCurrencies.Single(c => c.Id == "fans");
            var chapter = new GameContext(fx.Ch2, fx.Now);
            chapter.Deposit("eps", 25);
            var undiscounted = Purchasing.CostOf(draw, fx.Ctx, 1);
            fx.Ctx.Deposit(fx.Cash.Id,
                Purchasing.CostOf(fx.BulkBooking, fx.Ctx) + Purchasing.CostOf(favors, fx.Ctx));
            fx.Session.Refresh();

            var bulkModule = fx.Module(fx.BulkBooking);
            var favorsModule = fx.Module(favors);
            var bulkTile = (UpgradeTileUI)bulkModule.Widget;
            var favorsTile = (UpgradeTileUI)favorsModule.Widget;
            bulkTile.Buy();
            favorsTile.Buy();
            Assert.IsTrue(fx.Tier2.purchasedUpgrades.Contains(fx.BulkBooking.Id));
            Assert.IsTrue(fx.Tier2.purchasedUpgrades.Contains(favors.Id));

            // The release: 300 fans earned this run, and the EP resets the tier.
            fx.Ctx.Deposit(fans.Id, 300);
            Assert.IsTrue(fx.Tier2Def.rung.IsOffered(fx.Ctx));
            fx.Session.TryRung(fx.Ctx);
            Assert.IsFalse(fx.Tier2.purchasedUpgrades.Contains(fx.BulkBooking.Id), "the latch went with the run");
            Assert.IsFalse(fx.Tier2.purchasedUpgrades.Contains(favors.Id));
            Assert.IsTrue(chapter.GetBalance("eps") >= (BigNumber)25, "both gates are met again");
            fx.Session.Refresh();

            foreach (var (module, upgrade) in new[] { (bulkModule, fx.BulkBooking), (favorsModule, favors) })
            {
                var tile = (UpgradeTileUI)module.Widget;
                Assert.IsTrue(tile.ShownOwned, upgrade.Id);
                Assert.IsFalse(tile.ShownBuyable, upgrade.Id);
                Assert.IsTrue(Purchasing.IsOwned(fx.Ctx, upgrade), upgrade.Id);
                Assert.IsFalse(Purchasing.CanBuy(fx.Ctx, upgrade), upgrade.Id);

                // Funded and gate met, the tap still buys nothing.
                fx.Ctx.Deposit(fx.Cash.Id, Purchasing.CostOf(upgrade, fx.Ctx));
                var cash = fx.Ctx.GetBalance(fx.Cash.Id);
                tile.Buy();
                Assert.AreEqual(cash, fx.Ctx.GetBalance(fx.Cash.Id), $"{upgrade.Id}: a tap spends nothing");
                Assert.IsFalse(fx.Tier2.purchasedUpgrades.Contains(upgrade.Id), $"{upgrade.Id}: and writes no latch");
            }

            // Visible on ownership alone: with the EP clause false both tiles stay.
            chapter.SetBalance("eps", BigNumber.Zero);
            fx.Session.Refresh();
            Assert.IsTrue(bulkModule.Visible, "Bulk Booking stays on the strip");
            Assert.IsTrue(favorsModule.Visible, "Favors Owed stays on the strip");
            Assert.IsTrue(((UpgradeTileUI)bulkModule.Widget).ShownOwned);
            Assert.IsTrue(((UpgradeTileUI)favorsModule.Widget).ShownOwned);

            // The discount: a draw at count zero costs half what it did before Favors.
            Assert.IsEmpty(fx.Tier2.generatorCounts, "the draws went with the run");
            Assert.AreEqual(undiscounted.ToDouble() * 0.5, Purchasing.CostOf(draw, fx.Ctx, 1).ToDouble(),
                undiscounted.ToDouble() * 1e-12, "Favors' discount outlives the run");

            // The autobuy: the next tick buys draws with no re-purchase.
            fx.Session.Tick(1, fx.Now.AddSeconds(1));
            Assert.IsTrue(fx.Tier2.generatorCounts.TryGetValue(draw.Id, out var owned) && owned > 0,
                "Bulk Booking's autobuy outlives the run");
        }
    }
}
