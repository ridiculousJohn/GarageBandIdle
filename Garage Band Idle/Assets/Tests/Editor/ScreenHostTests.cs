using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Editor;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.Monetization;
using RidiculousGaming.GarageBandIdle.Story;
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
        private const int OpenStoryRow = 5;
        private const int EndStoryRow = 6;

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
            public readonly StoryBeatDefinition Opener;
            public readonly StoryBeatDefinition Capstone;

            public readonly RootScopeState Root;
            public readonly ChapterScopeState Ch1;
            public readonly TierScopeState Tier1;
            public readonly GameSession Session;
            public readonly GameClock Clock;
            public readonly VisualElement Screen;
            public readonly VisualElement Container;
            public readonly ScreenHost Host;

            // The seams the dialog's two request buttons call into. Fakes, so a
            // press is a recorded request and the payout stays the callback's.
            public readonly FakeAdService Ads = new();
            public readonly FakeStoreService Store = new();
            public readonly AdManager AdManager;
            public readonly IAPManager IAPManager;
            public int Saves;

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

            public Fixture(Action<GameConfig> configure = null)
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
                Opener = Find(Ch1Def.storyBeats, "story_ch1_open");
                Capstone = Find(Ch1Def.storyBeats, "story_ch1_end");

                Root = ScopeState.Build(ComposedContent.Compose(rootDef, new[] { Ch1Def }));
                Ch1 = (ChapterScopeState)TestNavigation.Node(Root, Ch1Def);
                Tier1 = (TierScopeState)TestNavigation.Node(Root, Tier1Def);
                var config = Config();
                configure?.Invoke(config);
                Session = new GameSession(Root, config);
                AdManager = new AdManager(Session, Ads, config, () => Saves++);
                IAPManager = new IAPManager(Session, Store, config, () => Saves++);

                var registry = AssetDatabase.LoadAssetAtPath<ModuleRegistry>("Assets/Settings/ModuleRegistry.asset");
                Assert.IsNotNull(registry,
                    "Assets/Settings/ModuleRegistry.asset is missing - it is hand-made settings.");

                Clock = new GameClock(Now);
                // The shipping screen itself, so the rows drive the real
                // Screen.uxml: the host requires its three named elements from
                // this tree, and the select and the dialog are authored in it.
                var screen = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Screen.uxml");
                Assert.IsNotNull(screen, "Assets/UI/Screen.uxml is missing");
                Screen = screen.Instantiate();
                Container = Screen.Q<VisualElement>("sections");
                Host = new ScreenHost(Screen, registry, Session, Clock, AdManager, IAPManager);
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

            // Which of the three screens is up: the host toggles a whole screen
            // by display, so the question has one answer per element.
            public static bool Shown(VisualElement e) => e.style.display.value == DisplayStyle.Flex;
        }

        // The roster is root's children in composition order, named from
        // content, and only a fresh game ever reaches this screen: boot enters a
        // recorded chapter, and a save with no record owes no idle (12.9).
        [Test]
        public void NoChapterRendersTheSelectOverRootsRoster()
        {
            var fx = new Fixture();
            fx.Host.Render();

            Assert.AreEqual(SessionPhase.NoChapter, fx.Session.Phase);
            Assert.IsTrue(Fixture.Shown(fx.Screen.Q<VisualElement>("select")), "the select is the screen");
            Assert.IsFalse(Fixture.Shown(fx.Screen.Q<VisualElement>("collect")), "nothing is owed, so no dialog");
            Assert.AreEqual(0, fx.Host.Sections.Count, "a chapterless session has no sections");
            Assert.AreEqual(0, fx.Container.childCount, "nothing was added to the container");

            var buttons = fx.Screen.Q<VisualElement>("chapters")
                .Query<Button>(className: "select-chapter").ToList();
            Assert.AreEqual(1, buttons.Count, "one button per chapter in root's roster");
            Assert.AreEqual("The Garage", buttons[0].text, "ch1's authored displayName");
        }

        // The pick is the switch the button makes, and the switch's own refresh
        // is what repaints: the select goes down and the chapter comes up in one
        // pass, with no render of the test's own (12.9).
        [Test]
        public void ThePickHidesTheSelectAndBuildsTheChapter()
        {
            var fx = new Fixture();
            fx.Host.Render();
            fx.Ch1.lastActiveUtc = fx.Now;
            // The generated button and this headless call share Select, so the
            // presentation-before-command order is under test too.
            fx.Host.SelectChapter(fx.Ch1);

            Assert.IsFalse(Fixture.Shown(fx.Screen.Q<VisualElement>("select")),
                "a chapter is entered, so the select is down");
            Assert.IsFalse(Fixture.Shown(fx.Screen.Q<VisualElement>("collect")),
                "the entry stamps at now, so no window is owed");
            Assert.AreEqual(7, fx.Host.Sections.Count, "the authored section count");
            Assert.IsTrue(fx.Host.Sections[GarageFloor].Visible, "the garage floor is gated Always");
        }

        // ---- chapter chrome and its requested overlays (section 10) ----

        [Test]
        public void LiveChapterShowsTheTwoPillsAndOnlyOneRequestedOverlay()
        {
            var fx = new Fixture();
            fx.Enter();

            var top = fx.Screen.Q<VisualElement>("top-bar");
            Assert.IsTrue(Fixture.Shown(top));
            Assert.AreEqual("\u23F1  00:00:00", top.Q<Button>("encore").text);
            Assert.IsFalse(top.Q<Button>("story-log").enabledSelf, "the story log belongs to slice E");

            fx.Host.OpenStory(fx.Opener, fx.Ch1);
            Assert.IsTrue(Fixture.Shown(StoryCard(fx.Screen)));
            fx.Host.OpenSettings();
            Assert.IsFalse(Fixture.Shown(StoryCard(fx.Screen)), "a requested overlay replaces the card");
            Assert.IsTrue(Fixture.Shown(fx.Screen.Q<VisualElement>("settings")));
            fx.Host.OpenEncore();
            Assert.IsFalse(Fixture.Shown(fx.Screen.Q<VisualElement>("settings")));
            Assert.IsTrue(Fixture.Shown(fx.Screen.Q<VisualElement>("encore-window")));
            Assert.AreEqual(7, fx.Host.Sections.Count, "live sections remain under every overlay");
        }

        [Test]
        public void EncorePillAndWindowCountDownFromTheSharedClock()
        {
            var fx = new Fixture(config => config.encoreAdSeconds = 7200);
            fx.Root.timedBuffs.Add(new TimedBuff
            {
                buffId = "encore",
                expiresAtUtc = fx.Now.AddSeconds(3661),
            });
            fx.Enter();
            fx.Host.OpenEncore();

            var window = fx.Screen.Q<VisualElement>("encore-window");
            Assert.AreEqual("Time remaining 01:01:01", window.Q<Label>("encore-remaining").text);
            Assert.AreEqual("While active, game speed is 2.00x.", window.Q<Label>("encore-description").text);
            Assert.AreEqual("Boost for 2 hours", window.Q<Button>("encore-ad").text,
                "the promise comes from the manager's configured grant");

            fx.Clock.Frame(fx.Now.AddSeconds(2), 2);
            fx.Host.Interpolate();
            Assert.AreEqual("Time remaining 01:00:59", window.Q<Label>("encore-remaining").text);
            Assert.AreEqual("\u23F1  01:00:59", fx.Screen.Q<Button>("encore").text);
        }

        [Test]
        public void APassOwnerSeesPermanentEncoreAndNoPurchaseChoices()
        {
            var fx = new Fixture();
            fx.Root.entitlements.Add("backstage_pass");
            fx.Enter();
            fx.Host.OpenEncore();

            var window = fx.Screen.Q<VisualElement>("encore-window");
            Assert.AreEqual("Time remaining \u221E", window.Q<Label>("encore-remaining").text);
            Assert.IsFalse(Fixture.Shown(window.Q<Button>("encore-ad")));
            Assert.IsFalse(Fixture.Shown(window.Q<Button>("encore-pass")));
            Assert.AreEqual("\u23F1  \u221E", fx.Screen.Q<Button>("encore").text);
        }

        [Test]
        public void PendingEncoreRewardRepaintsWithoutReplacingTheOpenOverlay()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.AdManager.RequestEncoreExtension();
            fx.Host.OpenSettings();

            fx.AdManager.Update(fx.Now);

            Assert.IsTrue(Fixture.Shown(fx.Screen.Q<VisualElement>("settings")),
                "the callback's refresh preserves the requested overlay");
            Assert.IsFalse(Fixture.Shown(fx.Screen.Q<VisualElement>("encore-window")));
            Assert.AreEqual("\u23F1  04:00:00", fx.Screen.Q<Button>("encore").text);
            Assert.AreEqual(1, fx.Saves, "the delivered reward was saved");
        }

        [Test]
        public void LiveChapterSelectClosesBeforeSameChapterNoOp()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Host.OpenChapterSelect();
            var select = fx.Screen.Q<VisualElement>("select");
            Assert.IsTrue(Fixture.Shown(select));
            Assert.IsTrue(Fixture.Shown(select.Q<Button>("select-close")));

            fx.Host.SelectChapter(fx.Ch1);

            Assert.IsFalse(Fixture.Shown(select), "same-chapter SwitchChapter emits no refresh");
            Assert.AreEqual(SessionPhase.Live, fx.Session.Phase);
            Assert.AreEqual(7, fx.Host.Sections.Count);
        }

        [Test]
        public void RoadieDraftSurvivesRefreshAndDoneCommitsItsOneMap()
        {
            var fx = new Fixture();
            fx.Ctx(fx.Root).Deposit("roadies", 2);
            fx.Enter();
            fx.Host.OpenRoadies();
            var overlay = fx.Screen.Q<VisualElement>("roadie-allocation");

            fx.Host.RoadieAllocation.Increase("ch1");
            Assert.AreEqual("1", overlay.Q<Label>("roadie-count-ch1").text);
            Assert.AreEqual("Unallocated  1.00", overlay.Q<Label>("roadie-unallocated").text);
            Assert.IsTrue(overlay.Q<Button>("roadie-minus-ch1").enabledSelf);

            fx.Session.GrantRoadies(1, fx.Now);
            Assert.AreEqual("1", overlay.Q<Label>("roadie-count-ch1").text,
                "a state refresh keeps the local draft");
            Assert.AreEqual("Unallocated  2.00", overlay.Q<Label>("roadie-unallocated").text);

            fx.Host.RoadieAllocation.Done();

            Assert.AreEqual(1, fx.Root.roadieAllocation["ch1"]);
            Assert.IsFalse(Fixture.Shown(overlay), "the accepted command's completion closes it");
        }

        [Test]
        public void LeavingLiveDropsAnUnsubmittedRoadieDraft()
        {
            var fx = new Fixture();
            fx.Ctx(fx.Root).Deposit("roadies", 1);
            fx.Enter();
            fx.Host.OpenRoadies();
            fx.Host.RoadieAllocation.Increase("ch1");

            fx.Session.SwitchChapter(null, fx.Now);

            Assert.IsFalse(Fixture.Shown(fx.Screen.Q<VisualElement>("roadie-allocation")));
            Assert.IsEmpty(fx.Root.roadieAllocation, "leaving without Done submits no command");
            fx.Host.SelectChapter(fx.Ch1);
            fx.Host.OpenRoadies();
            Assert.AreEqual("0", fx.Screen.Q<Label>("roadie-count-ch1").text,
                "the next overlay starts from simulation state, not the dead draft");
        }

        // A return with an unpaid window (12.9): the dialog is the whole screen
        // and the sections stay down beneath it, because a phase that never
        // ticks must not interpolate a display on a report measured before the
        // switch. The dialog shows what the session holds and computes nothing.
        [Test]
        public void AReturnWithAnUnpaidWindowShowsTheOfferAndHidesTheSections()
        {
            var fx = new Fixture();
            fx.Tier1.generatorCounts["practice_amp"] = 1;
            fx.Ch1.lastActiveUtc = fx.Now.AddSeconds(-1000);
            fx.Session.SwitchChapter(fx.Ch1, fx.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, fx.Session.Phase);

            Assert.IsTrue(Fixture.Shown(fx.Screen.Q<VisualElement>("collect")), "the unpaid window is the screen");
            Assert.IsFalse(Fixture.Shown(fx.Screen.Q<VisualElement>("select")), "a chapter is entered");
            Assert.AreEqual(0, fx.Host.Sections.Count, "the sections stay down under the dialog");

            var lines = fx.Screen.Q<VisualElement>("lines");
            Assert.AreEqual(fx.Session.CurrentOffer.lines.Count, lines.childCount,
                "one row per line the offer holds");
            // Fans and rehearsal sit behind reveal flags a fresh state lacks, so
            // an inactive currency takes nothing from any source and the amp's
            // cash is the whole offer.
            Assert.AreEqual(1, lines.childCount, "the amp's cash is the only line");

            var labels = lines.Children().Single().Query<Label>().ToList();
            Assert.AreEqual("Cash", labels[0].text, "the currency's authored name");
            Assert.AreEqual("+250.00", labels[1].text,
                "one amp pays 0.5 cash/s, root's authored idle base halves it, over 1000s");
        }

        // The dialog's three actions (12.11): OK settles, and the two request
        // buttons only ask - the payout is the callback's own transaction. A
        // free player is offered both.
        [Test]
        public void TheDialogOffersBothRequestsToAFreePlayer()
        {
            var fx = new Fixture();
            fx.Tier1.generatorCounts["practice_amp"] = 1;
            fx.Ch1.lastActiveUtc = fx.Now.AddSeconds(-400);
            fx.Session.SwitchChapter(fx.Ch1, fx.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, fx.Session.Phase);

            var collect = fx.Screen.Q<VisualElement>("collect");
            Assert.IsTrue(Fixture.Shown(collect.Q<Button>("double")), "the rewarded ad is on offer");
            Assert.IsTrue(Fixture.Shown(collect.Q<Button>("pass")), "and so is the Pass");
            Assert.IsTrue(Fixture.Shown(collect.Q<Button>("ok")));

            var labels = fx.Screen.Q<VisualElement>("lines").Children().Single().Query<Label>().ToList();
            Assert.AreEqual("+100.00", labels[1].text,
                "one amp pays 0.5 cash/s, root's authored idle base halves it, over 400s");
        }

        // A Pass owner reaches the same 4x with no ad and nothing to buy, so the
        // dialog is OK alone: permanent Encore doubles the window's speed and
        // the entitlement doubled the lines at computation, and what is shown is
        // what OK pays (section 9).
        [Test]
        public void APassOwnersDialogIsOkAloneAndReadsTheDoubledOffer()
        {
            var fx = new Fixture();
            fx.Root.entitlements.Add("backstage_pass");
            fx.Tier1.generatorCounts["practice_amp"] = 1;
            fx.Ch1.lastActiveUtc = fx.Now.AddSeconds(-400);
            fx.Session.SwitchChapter(fx.Ch1, fx.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, fx.Session.Phase);

            var collect = fx.Screen.Q<VisualElement>("collect");
            Assert.IsFalse(Fixture.Shown(collect.Q<Button>("double")), "the Pass already gives the ad's reward");
            Assert.IsFalse(Fixture.Shown(collect.Q<Button>("pass")), "and there is nothing left to buy");
            Assert.IsTrue(Fixture.Shown(collect.Q<Button>("ok")));

            var labels = fx.Screen.Q<VisualElement>("lines").Children().Single().Query<Label>().ToList();
            Assert.AreEqual("+400.00", labels[1].text, "the free player's 100 at 4x");
        }

        // OK settles (12.9): the claim pays the stored lines and advances the
        // stamp in one transaction, and its own refresh is what takes the dialog
        // down and brings the chapter up already holding what was offered.
        [Test]
        public void OkSettlesTheOfferAndTheChapterComesUpPaid()
        {
            var fx = new Fixture();
            fx.Tier1.generatorCounts["practice_amp"] = 1;
            fx.Ch1.lastActiveUtc = fx.Now.AddSeconds(-1000);
            fx.Session.SwitchChapter(fx.Ch1, fx.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, fx.Session.Phase);

            // What OK calls; a headless test has no pointer to press with.
            fx.Session.ClaimIdle(fx.Now);

            Assert.IsFalse(Fixture.Shown(fx.Screen.Q<VisualElement>("collect")),
                "the offer is paid, so the dialog is down");
            Assert.AreEqual(7, fx.Host.Sections.Count, "the chapter came up in the claim's own refresh");
            Assert.AreEqual("250.00", Fixture.Text(fx.Host.Sections[GarageFloor].Modules[CashLine], "value"),
                "what the dialog offered is what the balance holds");
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
            Assert.AreEqual(7, section.Modules.Count, "the authored module count");
            Assert.IsTrue(section.Modules[CashLine].Visible, "cash is ungated");
            Assert.IsFalse(section.Modules[FansLine].Visible, "fans sits behind its reveal");
            Assert.IsFalse(section.Modules[RehearsalLine].Visible, "rehearsal sits behind its reveal");
            Assert.IsFalse(section.Modules[RecordsLine].Visible, "no records are held yet");
            Assert.IsTrue(section.Modules[JamButton].Visible, "the jam is ungated");
            Assert.IsTrue(section.Modules[OpenStoryRow].Visible, "the opener's row is ungated");
            Assert.IsFalse(section.Modules[EndStoryRow].Visible, "the capstone's row waits on the chapter flag");

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

            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);
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
            fx.Session.TryBuy(fx.Ctx(fx.Tier1), fx.PlayForCrowd);
            Assert.IsTrue(fx.Tier1.purchasedUpgrades.Contains("play_for_crowd"), "play_for_crowd was bought");

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
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);

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
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);

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
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);
            fx.Session.SetActiveBars(fx.Ctx(fx.Tier1), fx.LearnCovers, new[] { fx.Cover1 });
            Assert.IsTrue(fx.Tier1.activeBars[fx.LearnCovers.Id].Contains(fx.Cover1.Id), "cover_1 was selected");

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
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);

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
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);

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
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);

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
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);
            fx.Session.TryStartEvent(fx.Ctx(fx.Tier1), fx.GarageJam1);
            Assert.IsNotNull(fx.Tier1.activeEvent, "the jam started");

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

        // ---- the story rows and the card (section 10, 12.11) ----

        // A story row's one button, named by the UXML.
        private static Button StoryButton(ScreenHost.ModuleView module) =>
            module.Widget.Root.Q<Button>("open");

        // The card: the app-owned overlay Screen.uxml names.
        private static VisualElement StoryCard(VisualElement screen) =>
            screen.Q<VisualElement>("story");

        [Test]
        public void TheOpenersRowReadsItsBeatAndIsLiveOnAFreshChapter()
        {
            var fx = new Fixture();
            fx.Enter();

            var row = fx.Host.Sections[GarageFloor].Modules[OpenStoryRow];
            var open = StoryButton(row);
            Assert.AreEqual("Make Some Noise", open.text, "the beat's authored displayName is the button");
            Assert.IsTrue(open.enabledSelf, "the opener's gate is Always, so it is live from the first press");
            Assert.IsEmpty(Fixture.VisibleLegs(row), "an available beat has nothing left to explain");
            Assert.IsFalse(open.ClassListContains("seen"), "and nothing has read it yet");
            Assert.IsNull(fx.Host.ShownStory, "an unmarked beat never opens its own card");
        }

        // A beat is read the moment its card opens from the button: the command
        // writes the latch and its own refresh is what raises the card and
        // repaints the row, so an app killed with the card up leaves it read.
        [Test]
        public void OpeningTheOpenersCardMarksItReadInTheSameRefresh()
        {
            var fx = new Fixture();
            fx.Enter();

            // What the row's button calls; a headless test has no pointer.
            fx.Host.OpenStory(fx.Opener, fx.Ch1);

            var card = StoryCard(fx.Screen);
            Assert.IsTrue(Fixture.Shown(card), "the card is up over the chapter");
            Assert.AreEqual("Make Some Noise", card.Q<Label>("title").text);
            StringAssert.StartsWith("It starts in the garage.", card.Q<Label>("text").text);
            Assert.AreSame(fx.Opener, fx.Host.ShownStory);
            // The chapter is live and ticking, so the interpolation reason that
            // keeps the sections down under the idle dialog does not apply.
            Assert.AreEqual(7, fx.Host.Sections.Count, "the sections stay up beneath the card");

            Assert.IsTrue(fx.Root.flags.Contains("story_ch1_open_seen"),
                "the latch is written at root, where the chapter declares it");
            var open = StoryButton(fx.Host.Sections[GarageFloor].Modules[OpenStoryRow]);
            Assert.IsTrue(open.ClassListContains("seen"), "the row read the flag in the same pass");
            Assert.IsTrue(open.enabledSelf, "a seen beat stays rereadable");
        }

        [Test]
        public void TheCardsCloseTakesItDownAndHoldsNoBeat()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Host.OpenStory(fx.Opener, fx.Ch1);

            // What the close button calls; a headless test has no pointer.
            fx.Host.CloseStory();

            Assert.IsFalse(Fixture.Shown(StoryCard(fx.Screen)));
            Assert.IsNull(fx.Host.ShownStory, "the request is cleared, so nothing is showing");
            Assert.AreEqual(7, fx.Host.Sections.Count, "the chapter was never taken down");
        }

        // The capstone's row arrives with the flag its gate reads, so the one
        // transaction that completes the chapter both reveals the button and
        // makes it live.
        [Test]
        public void TheCapstonesRowArrivesLiveWithTheCompletionFlag()
        {
            var fx = new Fixture();
            fx.Enter();
            Assert.IsFalse(fx.Host.Sections[GarageFloor].Modules[EndStoryRow].Visible);

            fx.Root.flags.Add("ch1_complete");
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);

            var row = fx.Host.Sections[GarageFloor].Modules[EndStoryRow];
            Assert.IsTrue(row.Visible, "the flag opened the row's own gate");
            var open = StoryButton(row);
            Assert.AreEqual("Your First Roadie", open.text);
            Assert.IsTrue(open.enabledSelf, "the same flag is the beat's gate");
            Assert.IsEmpty(Fixture.VisibleLegs(row));
            Assert.IsNull(fx.Host.ShownStory, "chapter 1 marks neither beat, so neither pops");
        }

        // Leaving Live takes the card down with the sections and clears the
        // request, so a re-entry starts from no card rather than from the one
        // the player was reading a phase ago.
        [Test]
        public void LeavingLiveTakesTheCardDownAndClearsTheRequest()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Host.OpenStory(fx.Opener, fx.Ch1);
            Assert.IsTrue(Fixture.Shown(StoryCard(fx.Screen)));

            fx.Session.SwitchChapter(null, fx.Now);

            Assert.AreEqual(SessionPhase.NoChapter, fx.Session.Phase);
            Assert.IsFalse(Fixture.Shown(StoryCard(fx.Screen)));
            Assert.IsNull(fx.Host.ShownStory);

            fx.Enter();
            Assert.IsFalse(Fixture.Shown(StoryCard(fx.Screen)),
                "the click's request did not survive the phase change");
            Assert.IsNull(fx.Host.ShownStory);
        }

        // ---- the marked beat pops by itself (decision 7) ----

        // A second host over the standing test tree: the pop rows need a MARKED
        // beat, and no test mutates the imported assets. One section, two rows -
        // a marked beat and an unmarked one behind the SAME gate, which is what
        // makes "unmarked beats never pop" a fact about the mark rather than
        // about the gate - plus the trigger whose transaction opens that gate.
        private class StoryFixture
        {
            public readonly TestTree Tree = new();
            public readonly StoryBeatDefinition Marked;
            public readonly StoryBeatDefinition Unmarked;
            public readonly TriggerDefinition Release;
            public readonly GameSession Session;
            public readonly VisualElement Screen;
            public readonly ScreenHost Host;

            public StoryFixture(bool markedUnlocksOnLiveTick = false)
            {
                Marked = TestTree.MakeDefinition<StoryBeatDefinition>("story_marked");
                Marked.displayName = "The First Cut";
                Marked.text = "The tape is done.";
                Marked.availableWhen = markedUnlocksOnLiveTick
                    ? new EarnedTotalAtLeast { currency = Tree.Cash, threshold = 1, uiText = "Earn 1 cash" }
                    : new FlagSet { flagId = "album", uiText = "Cut a demo" };
                Marked.seenFlag = "story_marked_seen";
                Marked.opensWhenAvailable = true;
                if (markedUnlocksOnLiveTick)
                {
                    // The story evaluates at the chapter. Re-home Cash there
                    // so its tier producer deposits outward and the chapter's
                    // earned-total gate reads the same reachable fact.
                    Tree.Tier1Def.declaredCurrencies.Remove(Tree.Cash);
                    Tree.Ch1Def.declaredCurrencies.Add(Tree.Cash);
                }

                Unmarked = TestTree.MakeDefinition<StoryBeatDefinition>("story_unmarked");
                Unmarked.displayName = "The Second Cut";
                Unmarked.text = "And so is this one.";
                Unmarked.availableWhen = new FlagSet { flagId = "album", uiText = "Cut a demo" };
                Unmarked.seenFlag = "story_unmarked_seen";

                Tree.Ch1Def.storyBeats.AddRange(new[] { Marked, Unmarked });
                Tree.RootDef.declaredFlags.AddRange(new[] { "story_marked_seen", "story_unmarked_seen" });
                var encore = TestTree.MakeDefinition<ModifierDefinition>("encore");
                encore.effects.Add(new Effect { stat = Stat.GameSpeed, multiplier = 2 });
                Tree.RootDef.modifiers.Add(encore);
                // A beat's home is the chapter, so both rows evaluate there.
                Tree.Ch1Def.sections.Add(new SectionDefinition
                {
                    title = "The Release",
                    visibleWhen = new Always(),
                    scope = Tree.Ch1Def,
                    modules =
                    {
                        new ModuleDefinition { prefabId = "story_row", content = Marked, scope = Tree.Ch1Def },
                        new ModuleDefinition { prefabId = "story_row", content = Unmarked, scope = Tree.Ch1Def },
                    }
                });

                // Closed at the entry sweep and armed by the test between
                // transactions, so the sweep that fires it belongs to the one
                // command the pop row is about.
                Release = TestTree.MakeDefinition<TriggerDefinition>("release_trigger");
                Release.condition = new Not { condition = new Always() };
                Release.actions.Add(new SetFlag { flagId = "album" });
                Tree.Ch1Def.triggers.Add(Release);

                // The link pass runs in Build, so the section, its modules and
                // the trigger are wired only once the tree is built again.
                Tree.Rebuild();
                if (markedUnlocksOnLiveTick)
                    Tree.Tier1.generatorCounts[Tree.PracticeAmp.Id] = 1;

                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.maxGameSpeed = 4;
                Session = new GameSession(Tree.Root, config);
                var clock = new GameClock(Tree.Now);
                var screen = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Screen.uxml");
                Assert.IsNotNull(screen, "Assets/UI/Screen.uxml is missing");
                Screen = screen.Instantiate();
                var registry = AssetDatabase.LoadAssetAtPath<ModuleRegistry>("Assets/Settings/ModuleRegistry.asset");
                Assert.IsNotNull(registry,
                    "Assets/Settings/ModuleRegistry.asset is missing - it is hand-made settings.");
                Host = new ScreenHost(Screen, registry, Session, clock,
                    new AdManager(Session, new FakeAdService(), config, () => { }),
                    new IAPManager(Session, new FakeStoreService(), config, () => { }));

                // The stamp is now, so no idle window exists and the phase lands
                // Live; the switch's own refresh is the first render.
                Tree.Ch1.lastActiveUtc = Tree.Now;
                Session.SwitchChapter(Tree.Ch1, Tree.Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
            }

            public ScreenHost.ModuleView Row(int index) => Host.Sections.Single().Modules[index];

            // Any command sweeps, so this one's sweep is what fires the armed
            // trigger and its refresh is what repaints.
            public void CutTheDemo()
            {
                Release.condition = new Always();
                Session.FireProducer(Tree.Ctx(Tree.Tier1), Tree.TapProducer);
                Assert.IsTrue(Tree.Ch1.flags.Contains("album"), "the sweep set the flag the gates read");
            }
        }

        // Unseen and unavailable: the button is closed and the unmet legs are
        // the goal readout, which is the row's whole state (12.11).
        [Test]
        public void AnUnavailableStoryRowIsClosedAndNamesItsGate()
        {
            var fx = new StoryFixture();

            var open = StoryButton(fx.Row(0));
            Assert.IsFalse(open.enabledSelf, "the gate does not hold and the beat is unread");
            CollectionAssert.AreEqual(new[] { "Cut a demo" }, Fixture.VisibleLegs(fx.Row(0)));
            Assert.IsNull(fx.Host.ShownStory, "a marked beat that is not available yet has nothing to pop");
        }

        // State, never a transition: the pair "available and unseen" holds from
        // the transaction that made it available, so the card comes up in that
        // transaction's own refresh with no render of the test's own. A beat is
        // read when its card OPENS (section 10), and this open happened inside
        // a refresh, so the mark is submitted and runs at the drain - the
        // request holds the card up in between. An unmarked beat behind the
        // same gate stays a button.
        [Test]
        public void AMarkedBeatOpensItsCardInTheTransactionThatMakesItAvailable()
        {
            var fx = new StoryFixture();

            fx.CutTheDemo();

            Assert.IsTrue(Fixture.Shown(StoryCard(fx.Screen)));
            Assert.AreSame(fx.Marked, fx.Host.ShownStory, "the first marked, available, unseen beat");
            Assert.AreEqual("The First Cut", StoryCard(fx.Screen).Q<Label>("title").text);
            Assert.IsFalse(fx.Tree.Root.flags.Contains("story_marked_seen"),
                "the open's mark waits behind the transaction it was issued from");
            Assert.IsTrue(StoryButton(fx.Row(1)).enabledSelf, "the unmarked beat's own gate holds too");

            fx.Session.Drain();

            Assert.IsTrue(fx.Tree.Root.flags.Contains("story_marked_seen"), "the drain runs the mark");
            Assert.IsTrue(Fixture.Shown(StoryCard(fx.Screen)),
                "and the request is what holds the card up, so a read beat keeps reading");
            Assert.IsTrue(StoryButton(fx.Row(0)).ClassListContains("seen"), "the row read the flag");
        }

        [TestCase("settings")]
        [TestCase("select")]
        [TestCase("roadie-allocation")]
        [TestCase("encore-window")]
        public void ARequestedOverlayDefersAnAutomaticStoryCardUntilItCloses(string overlayName)
        {
            var fx = new StoryFixture(markedUnlocksOnLiveTick: true);
            fx.Tree.Root.balances["roadies"] = 1;
            switch (overlayName)
            {
                case "settings": fx.Host.OpenSettings(); break;
                case "select": fx.Host.OpenChapterSelect(); break;
                case "roadie-allocation":
                    fx.Host.OpenRoadies();
                    fx.Host.RoadieAllocation.Increase("ch1");
                    break;
                case "encore-window": fx.Host.OpenEncore(); break;
            }
            Assert.IsTrue(Fixture.Shown(fx.Screen.Q<VisualElement>(overlayName)));
            Assert.IsNull(fx.Host.ShownStory, "the passive income threshold has not been reached yet");

            // Live production reaches the threshold beneath the modal.
            fx.Session.Tick(2, fx.Tree.Now.AddSeconds(2));

            Assert.IsTrue(Fixture.Shown(fx.Screen.Q<VisualElement>(overlayName)),
                "the automatic story must not replace the player's modal");
            Assert.IsFalse(Fixture.Shown(StoryCard(fx.Screen)));
            Assert.IsNull(fx.Host.ShownStory);
            Assert.IsFalse(fx.Tree.Root.flags.Contains("story_marked_seen"));
            if (overlayName == "roadie-allocation")
                Assert.AreEqual("1", fx.Screen.Q<Label>("roadie-count-ch1").text,
                    "the unsubmitted allocation draft survives while the story waits");

            fx.Host.CloseOverlay();
            fx.Session.Refresh();

            Assert.IsTrue(Fixture.Shown(StoryCard(fx.Screen)));
            Assert.AreSame(fx.Marked, fx.Host.ShownStory);
            Assert.IsFalse(fx.Tree.Root.flags.Contains("story_marked_seen"),
                "the open's mark is queued behind the first refresh after close");

            fx.Session.Drain();

            Assert.IsTrue(fx.Tree.Root.flags.Contains("story_marked_seen"), "the drain runs the mark");
            Assert.IsTrue(Fixture.Shown(StoryCard(fx.Screen)),
                "the story request keeps the now-seen beat open for the player");
        }

        // The close writes nothing: the beat was read when the card opened, so
        // closing clears the request and takes the card down, and the state
        // that raised it - available and unseen - is already broken by the
        // mark. The unmarked beat behind the same gate never takes its place.
        [Test]
        public void ClosingAPoppedCardTakesItDownAndNoUnmarkedBeatTakesItsPlace()
        {
            var fx = new StoryFixture();
            fx.CutTheDemo();
            // The mark was issued from inside the pop's own refresh.
            fx.Session.Drain();

            fx.Host.CloseStory();

            Assert.IsTrue(fx.Tree.Root.flags.Contains("story_marked_seen"), "the open is the read");
            Assert.IsFalse(Fixture.Shown(StoryCard(fx.Screen)));
            Assert.IsNull(fx.Host.ShownStory);
            Assert.IsTrue(StoryButton(fx.Row(0)).ClassListContains("seen"));

            fx.Session.FireProducer(fx.Tree.Ctx(fx.Tree.Tier1), fx.Tree.TapProducer);
            Assert.IsNull(fx.Host.ShownStory, "nothing pops a second time, and no unmarked beat ever pops");
            Assert.IsFalse(fx.Tree.Root.flags.Contains("story_unmarked_seen"));
        }

        // ---- layout scopes are linked at the chapter (12.11) ----

        // Build reads chapter.Link(section) and chapter.Link(module): the node
        // each one evaluates in was resolved when the tree was built, so the
        // build is a dictionary read per view and never a subtree search.
        [Test]
        public void EverySectionAndModuleEvaluatesInANodeInsideTheChapter()
        {
            var fx = new Fixture();
            fx.Enter();

            Assert.AreEqual(7, fx.Host.Sections.Count);
            foreach (var section in fx.Host.Sections)
            {
                Assert.AreSame(section.Definition.scope, section.Scope.Definition,
                    $"section '{section.Definition.title}' evaluates in its authored scope");
                Assert.IsNotNull(TestNavigation.Node(fx.Ch1, section.Definition.scope),
                    "which is the chapter or one of its descendants");
                foreach (var module in section.Modules)
                {
                    Assert.AreSame(module.Definition.scope, module.Scope.Definition,
                        $"module '{module.Definition.prefabId}' evaluates in its authored scope");
                    Assert.IsNotNull(TestNavigation.Node(fx.Ch1, module.Definition.scope));
                }
            }
        }

        // The 12.11 reach rule moved from a runtime throw at each chapter build
        // to the link pass, so a screen naming a scope outside its chapter is a
        // content fault the tree refuses to build at all.
        [Test]
        public void AModuleScopeOutsideTheChapterFailsAtBuild()
        {
            var tree = new TestTree();
            var ch2Def = TestTree.MakeChapter("ch2");
            var tier2Def = TestTree.MakeTier("tier2");
            ch2Def.children.Add(tier2Def);
            tree.Chapters.Add(ch2Def);
            tree.Ch1Def.sections.Add(new SectionDefinition
            {
                title = "The Garage Floor",
                visibleWhen = new Always(),
                scope = tree.Tier1Def,
                modules = { new ModuleDefinition { prefabId = "currency_line", content = tree.Cash, scope = tier2Def } }
            });

            var thrown = Assert.Throws<InvalidOperationException>(() => tree.Rebuild());
            StringAssert.Contains("tier2", thrown.Message);
        }

        [Test]
        public void ASectionScopeOutsideTheChapterFailsAtBuild()
        {
            var tree = new TestTree();
            var ch2Def = TestTree.MakeChapter("ch2");
            tree.Chapters.Add(ch2Def);
            tree.Ch1Def.sections.Add(new SectionDefinition
            {
                title = "Elsewhere",
                visibleWhen = new Always(),
                scope = ch2Def,
            });

            Assert.Throws<InvalidOperationException>(() => tree.Rebuild());
        }
    }
}
