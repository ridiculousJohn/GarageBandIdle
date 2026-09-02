using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Editor;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The screen host over the IMPORTED chapter 1, like the walkthroughs: what
    // these drive is the shipping content and the shipping registry, so a gate
    // moved in the JSON moves the screen here too. The host is plain C# and UI
    // Toolkit elements need no panel, so this is an EditMode suite (12.11).
    //
    // Nothing calls Render except the row that mirrors UIRoot.Bind: the host is
    // the one Refreshed subscriber, and a test that repainted by hand would
    // prove the widget draws without proving the screen ever hears.
    public class ScreenHostTests
    {
        // The authored section order, read by index so a row says which band it
        // means. The Gear has no row of its own, so it gets no constant.
        private const int GarageFloor = 0;
        private const int Band = 1;
        private const int RehearsalSpace = 3;
        private const int Release = 4;
        private const int GarageJam = 5;
        private const int BackyardParty = 6;

        // The garage floor's authored module order.
        private const int CashLine = 0;
        private const int FansLine = 1;
        private const int RehearsalLine = 2;
        private const int RecordsLine = 3;
        private const int JamButton = 4;

        private class Fixture
        {
            public static readonly DateTime Start = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

            public readonly ChapterDefinition Ch1Def;
            public readonly TierDefinition Tier1Def;
            public readonly ProducerDefinition TapProducer;
            public readonly UpgradeDefinition PlayForCrowd;
            public readonly BarGroupDefinition LearnCovers;
            public readonly BarDefinition Cover1;
            public readonly EventDefinition GarageJam1;

            public readonly ChapterScopeState Ch1;
            public readonly TierScopeState Tier1;
            public readonly GameSession Session;
            public readonly GameClock Clock;
            public readonly VisualElement Container;
            public readonly ScreenHost Host;

            public DateTime Now = Start;

            // The real asset's numbers (section 9); the asset is settings rather
            // than content, so an inline instance keeps the suite off it.
            private static GameConfig Config()
            {
                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.maxGameSpeed = 4;
                config.minimumAwaySeconds = 180;
                config.idleCapSeconds = 14400;
                return config;
            }

            public Fixture()
            {
                var rootDef = AssetDatabase.LoadAssetAtPath<RootDefinition>(
                    ChapterJsonImporter.AssetRootPath + "/root/root.asset");
                Ch1Def = AssetDatabase.LoadAssetAtPath<ChapterDefinition>(
                    ChapterJsonImporter.AssetRootPath + "/ch1/ch1.asset");
                Assert.IsNotNull(rootDef, "root.json has not been imported - run Garage Band Idle/Import Content.");
                Assert.IsNotNull(Ch1Def, "chapter-01.json has not been imported - run Garage Band Idle/Import Content.");
                Tier1Def = (TierDefinition)Ch1Def.children.Single();

                TapProducer = Find(Tier1Def.producers, "tap_producer");
                PlayForCrowd = Find(Tier1Def.upgrades, "play_for_crowd");
                LearnCovers = Find(Tier1Def.barGroups, "learn_covers");
                Cover1 = Find(LearnCovers.bars, "cover_1");
                GarageJam1 = Find(Tier1Def.events, "garage_jam_1");

                var root = ScopeState.Build(ComposedContent.Compose(rootDef, new[] { Ch1Def }));
                Ch1 = (ChapterScopeState)root.FindInSubtree(Ch1Def);
                Tier1 = (TierScopeState)root.FindInSubtree(Tier1Def);
                Session = new GameSession(root, Config());

                var registry = AssetDatabase.LoadAssetAtPath<ModuleRegistry>("Assets/Settings/ModuleRegistry.asset");
                Assert.IsNotNull(registry,
                    "Assets/Settings/ModuleRegistry.asset is missing - it is hand-made settings.");

                Clock = new GameClock(Now);
                Container = new VisualElement();
                Host = new ScreenHost(Container, registry, Session, Clock);
            }

            private static T Find<T>(IEnumerable<T> definitions, string id) where T : Definition =>
                definitions.Single(d => d.Id == id);

            // The switch a live player makes: the stamp is now, so no idle
            // window exists and the phase lands Live. The host subscribed at
            // construction, so the switch's own refresh is the first render.
            public void Enter()
            {
                Ch1.lastActiveUtc = Now;
                Session.SwitchChapter(Ch1, Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
            }

            public GameContext Ctx(ScopeState scope) => new GameContext(scope, Now);

            public static string Text(ScreenHost.ModuleView module, string element) =>
                module.Widget.Root.Q<Label>(element).text;

            // The bar rows, found by the class the row gives its own root: a
            // row is built per authored bar, so no UXML names one.
            public static List<VisualElement> BarRows(ScreenHost.ModuleView module) =>
                module.Widget.Root.Query<VisualElement>(className: "bar-row").ToList();

            // What a gate is actually explaining right now: the leg labels are
            // built once and toggled, so the visible ones are the unmet set.
            public static string[] VisibleLegs(ScreenHost.ModuleView module) =>
                module.Widget.Root.Q<VisualElement>("legs").Children()
                    .Where(leg => leg.style.display.value == DisplayStyle.Flex)
                    .Select(leg => ((Label)leg).text)
                    .ToArray();
        }

        [Test]
        public void NoChapterRendersNothing()
        {
            var fx = new Fixture();
            fx.Host.Render();

            Assert.AreEqual(SessionPhase.NoChapter, fx.Session.Phase);
            Assert.AreEqual(0, fx.Host.Sections.Count, "a chapterless session has no sections");
            Assert.AreEqual(0, fx.Container.childCount, "nothing was added to the container");
        }

        [Test]
        public void TheSwitchBuildsTheAuthoredSectionsWithOnlyTheGarageFloorOpen()
        {
            var fx = new Fixture();
            fx.Enter();
            // The unconditional first render UIRoot.Bind runs, on a screen the
            // switch already rendered: it repeats the pass, it does not rebuild.
            fx.Host.Render();

            var titles = fx.Host.Sections.Select(s => s.Definition.title).ToArray();
            CollectionAssert.AreEqual(
                new[]
                {
                    "The Garage Floor", "The Band", "The Gear", "The Rehearsal Space",
                    "The Release", "Garage Jam", "The Backyard Party"
                },
                titles, "the authored section order");
            Assert.AreEqual(titles.Length, fx.Container.childCount, "every section root is in the container");

            Assert.IsTrue(fx.Host.Sections[GarageFloor].Visible, "the garage floor is gated Always");
            for (var i = 1; i < fx.Host.Sections.Count; i++)
                Assert.IsFalse(fx.Host.Sections[i].Visible, $"'{titles[i]}' is open on a fresh game");
        }

        [Test]
        public void TheFreshGarageFloorShowsCashAndTheJamAndBuildsNothingElse()
        {
            var fx = new Fixture();
            fx.Enter();

            var section = fx.Host.Sections[GarageFloor];
            Assert.AreEqual(5, section.Modules.Count, "the authored module count");
            Assert.IsTrue(section.Modules[CashLine].Visible, "cash is ungated");
            Assert.IsFalse(section.Modules[FansLine].Visible, "fans sits behind its reveal");
            Assert.IsFalse(section.Modules[RehearsalLine].Visible, "rehearsal sits behind its reveal");
            Assert.IsFalse(section.Modules[RecordsLine].Visible, "no records are held yet");
            Assert.IsTrue(section.Modules[JamButton].Visible, "the jam is ungated");

            // Instantiation is lazy, so a hidden module costs no element tree.
            foreach (var module in section.Modules)
                Assert.AreEqual(module.Visible, module.Widget != null,
                    $"module '{module.Definition.prefabId}' holds a widget it is not showing, or shows one it never built");
        }

        [Test]
        public void TheCashLineRendersTheAuthoredNameAndTheBalance()
        {
            var fx = new Fixture();
            fx.Enter();

            var cash = fx.Host.Sections[GarageFloor].Modules[CashLine];
            Assert.AreEqual("Cash", Fixture.Text(cash, "name"));
            Assert.AreEqual("0.00", Fixture.Text(cash, "value"));
        }

        [Test]
        public void ATapRepaintsTheCashLineThroughTheSessionsOwnRefresh()
        {
            var fx = new Fixture();
            fx.Enter();

            Assert.IsTrue(fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer), "the tap fired");
            // No Render call here: the transaction's refresh is what repaints.
            Assert.AreEqual("1.00", Fixture.Text(fx.Host.Sections[GarageFloor].Modules[CashLine], "value"));
        }

        [Test]
        public void RevealingFansBuildsItsLineAndPopulatesItInTheSamePass()
        {
            var fx = new Fixture();
            fx.Enter();

            // The authored route to the reveal: play_for_crowd's gate is one
            // drummer and its cost is 100 cash, and its own action sets the flag.
            fx.Tier1.generatorCounts["drummer"] = 1;
            fx.Ctx(fx.Tier1).Deposit("cash", 100);
            Assert.IsTrue(fx.Session.TryBuy(fx.Ctx(fx.Tier1), fx.PlayForCrowd), "play_for_crowd was bought");

            var fans = fx.Host.Sections[GarageFloor].Modules[FansLine];
            Assert.IsTrue(fans.Visible, "the purchase revealed the fans line");
            Assert.IsNotNull(fans.Widget, "the revealed module built its widget");
            // The regression: a widget created mid-pass is refreshed by the pass
            // that created it, so the label is never blank for one transaction.
            Assert.AreEqual("Fans", Fixture.Text(fans, "name"));
            Assert.AreEqual("0.00", Fixture.Text(fans, "value"));
        }

        [Test]
        public void ASectionCrossingIntoViewBuildsItsListAndShowsOnlyAvailableRows()
        {
            var fx = new Fixture();
            fx.Enter();
            Assert.IsFalse(fx.Host.Sections[Band].Visible, "the band opens at 100 earned cash");

            fx.Ctx(fx.Tier1).Deposit("cash", 100);
            Assert.IsTrue(fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer), "the tap fired");

            var section = fx.Host.Sections[Band];
            Assert.IsTrue(section.Visible, "the earned total crossed the section's gate");
            var list = section.Modules.Single();
            Assert.IsTrue(list.Visible, "the list module carries no gate of its own");
            Assert.IsNotNull(list.Widget, "the list built its widget in the pass that showed the section");

            var rows = list.Widget.Root.Q<VisualElement>("rows");
            Assert.AreEqual(4, rows.childCount, "one row per authored generator");
            var shown = rows.Children()
                .Where(row => row.style.display.value == DisplayStyle.Flex)
                .Select(row => row.Q<Label>(className: "row-name").text)
                .ToArray();
            CollectionAssert.AreEqual(new[] { "Practice Amp" }, shown,
                "only practice_amp is available at 100 earned cash - the drummer wants three amps");

            // The button is the reference game's line: the first amp's authored
            // 60 cash, then what one amp pays per second.
            var buy = rows.Children().First(row => row.style.display.value == DisplayStyle.Flex)
                .Q<Button>(className: "row-buy");
            Assert.AreEqual("60.00 Cash => 0.50 Cash", buy.text);
        }

        [Test]
        public void TheRehearsalSpaceBuildsTheAuthoredGroupWithARowPerCover()
        {
            var fx = new Fixture();
            fx.Enter();

            fx.Tier1.flags.Add("rehearsal_revealed");
            Assert.IsTrue(fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer), "the tap fired");

            var section = fx.Host.Sections[RehearsalSpace];
            Assert.IsTrue(section.Visible, "the reveal opened the rehearsal space");
            var module = section.Modules.Single();
            var block = module.Widget.Root.Q<VisualElement>("groups").Children().Single();

            // One readout per DISTINCT pool: the three covers all drink
            // Rehearsal, and the name is the currency's authored one.
            var readouts = block.Query<VisualElement>(className: "pool-readout").ToList();
            CollectionAssert.AreEqual(new[] { "Rehearsal" }, readouts.Select(line => line.Q<Label>().text).ToArray());

            var rows = Fixture.BarRows(module);
            Assert.AreEqual(3, rows.Count, "one row per authored bar");
            CollectionAssert.AreEqual(
                new[] { "Three-Chord Anthem", "Parking-Lot Standard", "The Crowd-Pleaser" },
                rows.Select(row => row.Q<Label>(className: "bar-name").text).ToArray(),
                "the authored bar order and names");
            CollectionAssert.AreEqual(
                new[] { "0.00 / 100.00", "0.00 / 300.00", "0.00 / 600.00" },
                rows.Select(row => row.Q<Label>(className: "bar-progress").text).ToArray(),
                "the authored fill amounts, none of them started");
            foreach (var row in rows)
            {
                var select = row.Q<Button>(className: "bar-select");
                Assert.AreEqual("Select", select.text);
                Assert.IsTrue(select.enabledSelf, "nothing is selected yet, so every cover is choosable");
            }
        }

        [Test]
        public void SelectingACoverRepaintsItsRowAndLeavesTheRestChoosable()
        {
            var fx = new Fixture();
            fx.Enter();

            fx.Tier1.flags.Add("rehearsal_revealed");
            Assert.IsTrue(fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer), "the tap fired");
            Assert.IsTrue(fx.Session.SetActiveBars(fx.Ctx(fx.Tier1), fx.LearnCovers, new[] { fx.Cover1 }),
                "cover_1 was selected");

            // No Render call: the command's own refresh is what repaints.
            var buttons = Fixture.BarRows(fx.Host.Sections[RehearsalSpace].Modules.Single())
                .Select(row => row.Q<Button>(className: "bar-select"))
                .ToArray();
            Assert.AreEqual("Selected", buttons[0].text);
            Assert.IsFalse(buttons[0].enabledSelf, "the running cover is not re-selectable");
            Assert.AreEqual("Select", buttons[1].text);
            Assert.AreEqual("Select", buttons[2].text);
            Assert.IsTrue(buttons[1].enabledSelf, "a sibling stays pressable - pressing one replaces the choice");
        }

        [Test]
        public void TheReleaseShowsTheTiersRungWithItsUnmetLegsAndItsPreview()
        {
            var fx = new Fixture();
            fx.Enter();

            fx.Ch1.flags.Add("album");
            Assert.IsTrue(fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer), "the tap fired");

            var module = fx.Host.Sections[Release].Modules.Single();
            Assert.IsTrue(module.Visible, "the flag opened the release");
            var press = module.Widget.Root.Q<Button>("press");
            Assert.AreEqual("Cut a Demo", press.text, "the rung's authored label");
            Assert.IsFalse(press.enabledSelf, "a fresh run meets neither threshold");

            Assert.AreEqual(3, module.Widget.Root.Q<VisualElement>("legs").childCount,
                "one label per authored leg, built once and toggled");
            CollectionAssert.AreEqual(
                new[] { "50 fans (0.00/50.00)", "Learn a cover (0.00/1.00)" }, Fixture.VisibleLegs(module),
                "the two unmet legs; nothing is pending, so the reward leg holds and stays hidden");

            var preview = module.Widget.Root.Q<Label>("preview");
            Assert.AreEqual(DisplayStyle.Flex, preview.style.display.value, "the rung opens with an AddCurrency");
            Assert.AreEqual("Would bank: +0.00 Records, Garage Records", preview.text,
                "the payout at zero fans, over both tied currencies' authored names");
        }

        [Test]
        public void TheBackyardPartyShowsTheChaptersRungAsProgressAloneAndPreviewsNothing()
        {
            var fx = new Fixture();
            fx.Enter();

            fx.Ch1.flags.Add("album");
            Assert.IsTrue(fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer), "the tap fired");

            var module = fx.Host.Sections[BackyardParty].Modules.Single();
            var press = module.Widget.Root.Q<Button>("press");
            Assert.AreEqual("Play the Backyard Party", press.text);
            Assert.IsFalse(press.enabledSelf, "no garage records have been banked");

            // The threshold leg carries no uiText, so it renders as its
            // progress alone - the capstone's whole readout.
            CollectionAssert.AreEqual(new[] { "0.00/30.00" }, Fixture.VisibleLegs(module));
            Assert.AreEqual(DisplayStyle.None,
                module.Widget.Root.Q<Label>("preview").style.display.value,
                "the capstone opens with ExecuteRung, which previews no number rather than a wrong one");
        }

        [Test]
        public void TheGarageJamSectionBuildsARowPerEventEachExplainingItsOwnGate()
        {
            var fx = new Fixture();
            fx.Enter();

            // Records are root's, and the section's gate reads them by the
            // outward walk from tier1 - so the deposit lands at root.
            fx.Ctx(fx.Tier1).Deposit("records", 1);
            Assert.IsTrue(fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer), "the tap fired");

            var section = fx.Host.Sections[GarageJam];
            Assert.IsTrue(section.Visible, "the first record opened the section");
            CollectionAssert.AreEqual(
                new[] { "Garage Jam I", "Garage Jam II", "Garage Jam III" },
                section.Modules.Select(module => Fixture.Text(module, "name")).ToArray(),
                "one row per authored event, named from content");

            var first = section.Modules[0];
            Assert.IsTrue(first.Widget.Root.Q<Button>("start").enabledSelf, "one record is jam I's whole gate");
            Assert.IsEmpty(Fixture.VisibleLegs(first), "a startable event has nothing left to explain");

            var second = section.Modules[1];
            Assert.IsFalse(second.Widget.Root.Q<Button>("start").enabledSelf);
            CollectionAssert.AreEqual(
                new[] { "Clear Garage Jam I first", "15 Records (1.00/15.00)" }, Fixture.VisibleLegs(second),
                "the unmet legs in authored order; jam II is uncleared, so its own already-cleared leg holds");
        }

        [Test]
        public void StartingAJamRepaintsItsRowActiveAndShutsTheSiblingsOut()
        {
            var fx = new Fixture();
            fx.Enter();

            fx.Ctx(fx.Tier1).Deposit("records", 1);
            Assert.IsTrue(fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer), "the tap fired");
            Assert.IsTrue(fx.Session.TryStartEvent(fx.Ctx(fx.Tier1), fx.GarageJam1), "the jam started");

            var section = fx.Host.Sections[GarageJam];
            var first = section.Modules[0];
            Assert.AreEqual(DisplayStyle.None, first.Widget.Root.Q<Button>("start").style.display.value,
                "a running attempt has nothing left to start");
            var dismiss = first.Widget.Root.Q<Button>("dismiss");
            Assert.AreEqual(DisplayStyle.Flex, dismiss.style.display.value);
            Assert.AreEqual("Dismiss", dismiss.text, "the goal has not latched, so the ending banks nothing");
            Assert.IsEmpty(Fixture.VisibleLegs(first), "a running attempt has no gate to explain");
            // The onEntry restarted tier1, so the cash the tap earned is gone
            // and the goal reads from zero.
            Assert.AreEqual("60s left - Goal 0.00/150.00", Fixture.Text(first, "status"));

            for (var i = 1; i < section.Modules.Count; i++)
                Assert.IsFalse(section.Modules[i].Widget.Root.Q<Button>("start").enabledSelf,
                    $"the host is occupied, so '{section.Modules[i].Definition.content.Id}' cannot start");
        }

        [Test]
        public void InterpolateRunsOverTheLiveWidgets()
        {
            var fx = new Fixture();
            fx.Enter();

            Assert.IsNotNull(fx.Host.Sections[GarageFloor].Modules[CashLine].Widget, "a widget is present to interpolate");
            Assert.DoesNotThrow(() => fx.Host.Interpolate());
        }
    }
}
