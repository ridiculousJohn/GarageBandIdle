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
        // means.
        private const int GarageFloor = 0;
        private const int Band = 1;
        private const int Gear = 2;
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
            public readonly GeneratorDefinition PracticeAmp;
            public readonly UpgradeDefinition StagePresence;
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
                PracticeAmp = Find(Tier1Def.generators, "practice_amp");
                StagePresence = Find(Tier1Def.upgrades, "stage_presence");
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
                Session = new GameSession(Root, config);
                AdManager = new AdManager(Session, Ads, () => Saves++);
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
            Assert.IsTrue(top.Q<Button>("story-log").enabledSelf, "the story log opens from the right pill");

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
            var fx = new Fixture();
            fx.Root.timedBuffs.Add(new TimedBuff
            {
                buffId = "encore_timer",
                expiresAtUtc = fx.Now.AddSeconds(3661),
            });
            fx.Enter();
            fx.Host.OpenEncore();

            var window = fx.Screen.Q<VisualElement>("encore-window");
            Assert.AreEqual("Time remaining 01:01:01", window.Q<Label>("encore-remaining").text);
            Assert.AreEqual("While active, game speed is 2.00x.", window.Q<Label>("encore-description").text);
            Assert.AreEqual("Boost for 4 hours", window.Q<Button>("encore-ad").text,
                "the promise is the authored grant");

            fx.Clock.Frame(fx.Now.AddSeconds(2), 2);
            fx.Host.Interpolate();
            Assert.AreEqual("Time remaining 01:00:59", window.Q<Label>("encore-remaining").text);
            Assert.AreEqual("\u23F1  01:00:59", fx.Screen.Q<Button>("encore").text);
        }

        // The chrome names its timer ONCE, in its own UXML: the pill is an
        // element declaring a `timer` attribute, and the pill's value is what
        // the top bar and the window both count down (12.11).
        // The window says what the timer buys right now: a rung with no band is
        // the ad's promise and always prints, and a banded rung prints while its
        // own appliesWhen holds - the membership the tick reads, whatever legs it
        // is authored with - so a ladder over the same timer reads 2.00x under the
        // threshold, 4.00x over it, and 4.00x for a Pass owner the tier's gate
        // admits with no timer at all.
        [Test]
        public void TheWindowPrintsTheLaddersCurrentRungNotItsTop()
        {
            var fx = new StoryFixture(author: tree =>
            {
                var tier = TestTree.MakeDefinition<ModifierDefinition>("encore_4x");
                tier.timer = "encore_timer";
                tier.activeAfterSeconds = 86400;
                tier.effects.Add(new Effect { stat = Stat.GameSpeed, multiplier = 2 });
                tier.appliesWhen = new Any
                {
                    conditions =
                    {
                        new HasEntitlement { entitlementId = "backstage_pass" },
                        new BuffActive { modifier = tier },
                    }
                };
                tree.RootDef.modifiers.Add(tier);
                tree.RootDef.permanentModifiers.Add(tier);
            });
            var description = fx.Screen.Q<Label>("encore-description");

            fx.Host.OpenEncore();
            Assert.AreEqual("While active, game speed is 2.00x.", description.text, "nothing banked: the promise");

            var record = new TimedBuff { buffId = "encore_timer", expiresAtUtc = fx.Tree.Now.AddSeconds(14400) };
            fx.Tree.Root.timedBuffs.Add(record);
            fx.Host.OpenEncore();
            Assert.AreEqual("While active, game speed is 2.00x.", description.text, "four hours: under the band");

            record.expiresAtUtc = fx.Tree.Now.AddSeconds(90000);
            fx.Host.OpenEncore();
            Assert.AreEqual("While active, game speed is 4.00x.", description.text, "twenty-five hours: both rungs");

            fx.Tree.Root.timedBuffs.Clear();
            fx.Tree.Root.entitlements.Add("backstage_pass");
            fx.Host.OpenEncore();
            Assert.AreEqual("While active, game speed is 4.00x.", description.text,
                "the Pass leg admits the tier with no timer, as the tick reads it");
        }

        [Test]
        public void TheEncorePillCarriesTheTimerItsUxmlNames()
        {
            var fx = new Fixture();

            Assert.AreEqual("encore_timer", fx.Screen.Q<TimerPill>("encore").Timer);
        }

        // The host requires that timer to be one ROOT declares, at construction,
        // the way it requires the named elements themselves (requirement 7) - so
        // a content set the pill's attribute does not match is refused where the
        // screen is bound rather than at the first countdown.
        [Test]
        public void AScreenWhosePillNamesATimerRootDoesNotDeclareIsRefused()
        {
            var tree = new TestTree();
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.maxGameSpeed = 4;
            var session = new GameSession(tree.Root, config);
            var screen = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Screen.uxml");
            Assert.IsNotNull(screen, "Assets/UI/Screen.uxml is missing");
            var registry = AssetDatabase.LoadAssetAtPath<ModuleRegistry>("Assets/Settings/ModuleRegistry.asset");
            Assert.IsNotNull(registry,
                "Assets/Settings/ModuleRegistry.asset is missing - it is hand-made settings.");

            Assert.Throws<InvalidOperationException>(() => new ScreenHost(
                screen.Instantiate(), registry, session, new GameClock(tree.Now),
                new AdManager(session, new FakeAdService(), () => { }),
                new IAPManager(session, new FakeStoreService(), config, () => { })));
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

        // The window's two buttons only REQUEST, and the close rides the grant's
        // completed callback - which runs after the save (12.9), so the boost is
        // on disk before the player is handed the screen back. A request still in
        // flight leaves the window standing (12.11).
        [Test]
        public void TheEncoreWindowClosesWhenTheAdsGrantLands()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Host.OpenEncore();
            var window = fx.Screen.Q<VisualElement>("encore-window");
            Assert.IsTrue(Fixture.Shown(window));

            // What the ad button calls; a headless test has no pointer to press.
            fx.Host.EncoreWindow.RequestAd();
            Assert.IsTrue(Fixture.Shown(window), "a request is not a grant");

            fx.AdManager.Update(fx.Now);

            Assert.IsFalse(Fixture.Shown(window), "the close rides the grant's completion, after the save");
            Assert.AreEqual("encore_timer", fx.Root.timedBuffs.Single().buffId, "the boost is what landed");
            Assert.AreEqual(1, fx.Saves, "and the save ran before the close");

            fx.Host.OpenEncore();
            Assert.IsTrue(Fixture.Shown(window), "the overlay reopens, so the close broke nothing");
        }

        // A failed result invokes no callback, so there is nothing for a close to
        // ride: the window stands with its buttons, which is the only state a
        // retry can come from (12.11).
        [Test]
        public void TheEncoreWindowStaysOpenWhenTheAdFails()
        {
            var fx = new Fixture();
            fx.Ads.NextResult = AdResult.Failed;
            fx.Enter();
            fx.Host.OpenEncore();

            fx.Host.EncoreWindow.RequestAd();
            fx.AdManager.Update(fx.Now);

            Assert.IsTrue(Fixture.Shown(fx.Screen.Q<VisualElement>("encore-window")),
                "a failed ad grants nothing, so nothing closed the window");
            Assert.IsEmpty(fx.Root.timedBuffs);
            Assert.AreEqual(0, fx.Saves, "and there is no write for a save to follow");
        }

        // The Pass is the window's other request and it lands the same way: the
        // close follows the grant, the save, and the acknowledge the store is
        // waiting on (12.9).
        [Test]
        public void TheEncoreWindowClosesWhenThePassPurchaseLands()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Host.OpenEncore();

            // What the pass button calls; a headless test has no pointer.
            fx.Host.EncoreWindow.RequestPass();
            fx.IAPManager.Update(fx.Now);

            Assert.IsFalse(Fixture.Shown(fx.Screen.Q<VisualElement>("encore-window")),
                "the close rides the grant's completion");
            Assert.IsTrue(fx.Root.entitlements.Contains(Meta.BackstagePass.EntitlementId));
            Assert.AreEqual(1, fx.Saves, "the grant was saved");
            Assert.AreEqual(1, fx.Store.Acknowledged.Count, "and the store was told after it");
        }

        // The grant may land after the player closed the window and opened
        // something else, and closing then would take down whatever stands - so
        // the window closes only itself and the boost lands either way (12.11).
        [Test]
        public void AGrantLandingAfterTheWindowClosedLeavesTheOpenOverlayAlone()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Host.OpenEncore();
            fx.Host.EncoreWindow.RequestAd();
            fx.Host.CloseOverlay();
            fx.Host.OpenSettings();

            fx.AdManager.Update(fx.Now);

            Assert.IsTrue(Fixture.Shown(fx.Screen.Q<VisualElement>("settings")),
                "the window closes only itself");
            Assert.IsFalse(Fixture.Shown(fx.Screen.Q<VisualElement>("encore-window")));
            Assert.AreEqual("encore_timer", fx.Root.timedBuffs.Single().buffId, "the reward was never in doubt");
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

        // The lines are a fact of the offer and the buttons a fact of the
        // entitlement: a repaint under the dialog judges the buttons again and
        // leaves the rows built when the offer was first shown (12.9).
        [Test]
        public void TheIdleDialogsLinesAreBuiltOncePerOffer()
        {
            var fx = new Fixture();
            fx.Tier1.generatorCounts["practice_amp"] = 1;
            fx.Ch1.lastActiveUtc = fx.Now.AddSeconds(-400);
            fx.Session.SwitchChapter(fx.Ch1, fx.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, fx.Session.Phase);

            var collect = fx.Screen.Q<VisualElement>("collect");
            var first = collect.Q<VisualElement>("lines").Children().First();

            // A root-owned write, legal in every phase: it grants the Pass and
            // refreshes, and it settles nothing.
            fx.Session.GrantEntitlement(Meta.BackstagePass.EntitlementId, fx.Now);

            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, fx.Session.Phase, "the offer still stands");
            Assert.AreSame(first, collect.Q<VisualElement>("lines").Children().First(),
                "the offer's row is the element it was");
            Assert.IsFalse(Fixture.Shown(collect.Q<Button>("double")), "the Pass already gives the ad's reward");
            Assert.IsFalse(Fixture.Shown(collect.Q<Button>("pass")), "and there is nothing left to buy");
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

        // The reference game's rate line (12.11): the tick report's realized
        // slope for the currency at its home, beside the balance - which is
        // exactly the rate the interpolated balance climbs by between ticks.
        [Test]
        public void HeaderShowsTheRealizedRateBesideTheBalance()
        {
            var fx = new Fixture();
            fx.Enter();

            var cash = fx.Host.Sections[GarageFloor].Modules[CashLine];
            var zero = "(" + NumberFormatter.Format(BigNumber.Zero) + "/s)";
            Assert.AreEqual(zero, Fixture.Text(cash, "rate"),
                "no tick has run, so there is no measured slope to show");

            // The amp's authored gate is 100 earned cash and its first unit
            // costs 60, so one deposit both opens the gate and pays for it.
            fx.Ctx(fx.Tier1).Deposit("cash", 100);
            fx.Session.TryBuy(fx.Ctx(fx.Tier1), fx.PracticeAmp, 1);
            Assert.AreEqual(1, fx.Tier1.generatorCounts["practice_amp"], "one amp is producing");

            // One tick of the Live cadence; its own refresh is what repaints.
            fx.Session.Tick(0.25, fx.Now.AddSeconds(0.25));

            // Cash is declared at tier1, so tier1 is the home the report keys
            // the net by (12.3).
            var slope = fx.Session.LastTick.CurrencySlope(fx.Tier1, "cash");
            Assert.AreEqual("(" + NumberFormatter.Format(slope) + "/s)", Fixture.Text(cash, "rate"));
            Assert.AreNotEqual(zero, Fixture.Text(cash, "rate"), "the amp paid, so the slope is not zero");
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
        public void ASectionCrossingIntoViewShowsTheRowsItsFlagsReveal()
        {
            var fx = new Fixture();
            fx.Enter();
            Assert.IsFalse(fx.Host.Sections[Band].Visible, "the band's flag is unset on a fresh chapter");

            // The deposit raises the earned total; the command's sweep is what
            // fires reveal_band, whose condition is the amp's own purchase gate.
            fx.Ctx(fx.Tier1).Deposit("cash", 100);
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);

            var section = fx.Host.Sections[Band];
            Assert.IsTrue(section.Visible, "the trigger set the flag the section reads");
            Assert.IsTrue(fx.Ch1.flags.Contains("band_revealed"), "the latch is homed at the chapter");
            Assert.AreEqual(4, section.Modules.Count, "one module per authored generator");
            Assert.IsTrue(section.Modules[0].Visible, "the amp's row carries no gate of its own");
            Assert.IsNotNull(section.Modules[0].Widget, "the row built its widget in the pass that showed the section");
            for (var i = 1; i < section.Modules.Count; i++)
                Assert.IsFalse(section.Modules[i].Visible, "their flags are unset");

            // The yield line is the reference game's row: the first amp's
            // authored 60 cash, then what one amp pays per second. The button
            // beside it is the single-unit buy and carries nothing else.
            var amp = section.Modules[0].Widget.Root;
            Assert.AreEqual("60.00 Cash => 0.50 Cash", amp.Q<Label>("yield").text);
            var buy = amp.Q<Button>("buy");
            Assert.AreEqual("+1", buy.text);
            Assert.IsTrue(buy.enabledSelf, "101 cash covers the 60");
            var buyMax = amp.Q<Button>("buy_max");
            Assert.AreEqual("+1", buyMax.text, "101 cash covers one amp and not the second's 69");
            Assert.IsTrue(buyMax.enabledSelf);
        }

        // Exposed stays exposed, and the purchase gate is the button's business
        // (12.11, content doc section 2).
        [Test]
        public void ARevealedRowStaysVisibleAndGreyedAfterTheRunResets()
        {
            var fx = new Fixture();
            fx.Enter();
            // The flags a save could hold after a reset: the run's earned total
            // went with the wipe and the chapter's latches did not.
            fx.Ch1.flags.Add("band_revealed");
            fx.Ch1.flags.Add("drummer_revealed");
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);

            var section = fx.Host.Sections[Band];
            Assert.IsTrue(section.Visible, "the chapter's flag outlived the run");
            Assert.IsTrue(section.Modules[0].Visible, "the amp's row carries no gate of its own");
            Assert.IsTrue(section.Modules[1].Visible, "the drummer's flag is set");
            Assert.IsFalse(section.Modules[2].Visible, "the bassist's is not");
            Assert.IsFalse(section.Modules[3].Visible, "and neither is the guitarist's");

            var drummer = section.Modules[1].Widget.Root;
            var drummerBuy = drummer.Q<Button>("buy");
            Assert.IsFalse(drummerBuy.enabledSelf,
                "no amps are owned, so the purchase gate is closed and the button says so");
            Assert.AreEqual("+1", drummerBuy.text, "the button is the single-unit buy");
            var drummerBuyMax = drummer.Q<Button>("buy_max");
            Assert.IsFalse(drummerBuyMax.enabledSelf, "a closed gate affords zero of them");
            Assert.AreEqual("+1", drummerBuyMax.text, "and at zero the max button reads the single unit");
            Assert.AreEqual("250.00 Cash => 3.00 Cash", drummer.Q<Label>("yield").text,
                "fans sits behind its own reveal, and an inactive currency takes nothing from any source");
            Assert.IsFalse(section.Modules[0].Widget.Root.Q<Button>("buy").enabledSelf,
                "1 cash after one press does not cover the amp's 60");
        }

        // The row's second button prints the largest count the balance affords
        // and buys that count (12.2, 12.11): the number on the screen and the
        // number the bank pays are the same number.
        [Test]
        public void TheMaxButtonPrintsTheLargestAffordableCountAndBuysIt()
        {
            var fx = new Fixture();
            fx.Enter();
            var row = AmpRow(fx);

            fx.Tier1.balances["cash"] = 1000;
            fx.Tier1.earnedTotals["cash"] = 1000;
            // The tap is a transaction, so its own refresh is what repaints the
            // row over the balance set beneath it.
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);

            var ctx = fx.Ctx(fx.Tier1);
            var max = Purchasing.MaxAffordable(ctx, fx.PracticeAmp);
            // 60 x (1.15^n - 1) / 0.15 against the 1001 cash the tap left:
            // eight units cost 823.61 and nine cost 1007.15.
            Assert.AreEqual(8, max);
            var buy = row.Root.Q<Button>("buy");
            var buyMax = row.Root.Q<Button>("buy_max");
            Assert.AreEqual("+1", buy.text);
            Assert.AreEqual("+8", buyMax.text);
            Assert.IsTrue(buy.enabledSelf);
            Assert.IsTrue(buyMax.enabledSelf);

            // What the button's click submits: the count it printed.
            fx.Session.TryBuy(ctx, fx.PracticeAmp, max);

            Assert.AreEqual("x8", row.Root.Q<Label>("count").text);
            // 177.39 left against the ninth unit's 183.54, so nothing is
            // affordable: both buttons read "+1" and neither presses.
            Assert.AreEqual("+1", buy.text);
            Assert.AreEqual("+1", buyMax.text);
            Assert.IsFalse(buy.enabledSelf);
            Assert.IsFalse(buyMax.enabledSelf);
        }

        // Exactly one unit affordable is the state both buttons read "+1" for,
        // and both do the same thing - no hiding rule for a state that lasts
        // seconds (12.11).
        [Test]
        public void BothButtonsReadPlusOneWhenExactlyOneUnitIsAffordable()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Ctx(fx.Tier1).Deposit("cash", 100);
            var row = AmpRow(fx);

            // The tap the reveal fires leaves 101 cash: the first amp's 60 is
            // covered and the second's 69 is not.
            Assert.AreEqual(1, Purchasing.MaxAffordable(fx.Ctx(fx.Tier1), fx.PracticeAmp));
            var buy = row.Root.Q<Button>("buy");
            var buyMax = row.Root.Q<Button>("buy_max");
            Assert.AreEqual("+1", buy.text);
            Assert.AreEqual("+1", buyMax.text);
            Assert.IsTrue(buy.enabledSelf);
            Assert.IsTrue(buyMax.enabledSelf);
        }

        // The description is content read off the definition, never a widget's
        // words (12.11): a band shows the section's own and an upgrade row the
        // one authored on the content it binds.
        [Test]
        public void RowsAndSectionsShowTheirAuthoredDescriptions()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Ch1.flags.Add("band_revealed");
            fx.Ch1.flags.Add("gear_revealed");
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);

            var floor = fx.Host.Sections[GarageFloor].Root.Q<Label>(className: "section-description");
            Assert.IsNotNull(floor, "the garage floor's band carries its authored line");
            Assert.AreEqual(fx.Ch1Def.sections[GarageFloor].description, floor.text);

            var upgrade = fx.Host.Sections[Gear].Modules[0].Widget.Root.Q<Label>("description");
            Assert.IsTrue(Fixture.Shown(upgrade), "the latch's row has a description, so it shows one");
            Assert.AreEqual(fx.StagePresence.description, upgrade.text, "the bound upgrade's own text");

            // A generator row holds no description label: its authored text is
            // read on the info screen instead (12.11).
            Assert.IsNull(fx.Host.Sections[Band].Modules[0].Widget.Root.Q<Label>("description"));
        }

        // A bought upgrade is a fact the row renders rather than a reason to
        // drop it: the latch reads Bought and the row keeps its place (12.11).
        [Test]
        public void ABoughtUpgradeRowReadsBoughtAndStaysListed()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Ch1.flags.Add("gear_revealed");
            fx.Ctx(fx.Tier1).Deposit("cash", 250);
            fx.Session.TryBuy(fx.Ctx(fx.Tier1), fx.StagePresence);
            Assert.IsTrue(fx.Tier1.purchasedUpgrades.Contains("stage_presence"), "stage_presence was bought");

            var section = fx.Host.Sections[Gear];
            Assert.IsTrue(section.Visible, "the chapter's flag opened the gear");
            Assert.AreEqual(6, section.Modules.Count, "one module per authored upgrade");
            Assert.IsTrue(section.Modules[0].Visible, "the latch's row carries no gate of its own");
            var buy = section.Modules[0].Widget.Root.Q<Button>("buy");
            Assert.AreEqual("Bought", buy.text);
            Assert.IsFalse(buy.enabledSelf, "a one-shot purchase is not pressable twice");
            for (var i = 1; i < section.Modules.Count; i++)
                Assert.IsFalse(section.Modules[i].Visible, "the rest wait on their own flags");
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
            Assert.AreEqual("Would bank: +0.00 Records, Demo Tapes", preview.text,
                "the payout at zero fans, over both tied currencies' authored names");
        }

        [Test]
        public void TheBackyardPartyShowsTheChaptersRungLegAndPreviewsNothing()
        {
            var fx = new Fixture();
            fx.Enter();

            fx.Ch1.flags.Add("album");
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);

            var module = fx.Host.Sections[BackyardParty].Modules.Single();
            var press = module.Widget.Root.Q<Button>("press");
            Assert.AreEqual("Play the Backyard Party", press.text);
            Assert.IsFalse(press.enabledSelf, "no demo tapes have been banked");

            // The threshold leg carries its own uiText, so it renders as that
            // text with the progress beside it - the capstone's whole readout.
            CollectionAssert.AreEqual(new[] { "Hand out 30 Demo Tapes (0.00/30.00)" }, Fixture.VisibleLegs(module));
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

        // ---- the generator info screen (12.11) ----

        // The band's latch and the amp's row widget. The long press that opens
        // the screen is a pointer gesture on panel time, so the row exposes
        // OpenInfo for the same reason the Encore window exposes RequestAd.
        private static GeneratorRowUI AmpRow(Fixture fx)
        {
            fx.Ch1.flags.Add("band_revealed");
            fx.Session.FireProducer(fx.Ctx(fx.Tier1), fx.TapProducer);
            return (GeneratorRowUI)fx.Host.Sections[Band].Modules[0].Widget;
        }

        private static string InfoText(Fixture fx, string element) =>
            fx.Host.GeneratorInfo.Root.Q<Label>(element).text;

        // The one figure nothing else on the screen shows: the owned count and
        // what those units produce, beside the flavor text the row gave up and
        // the same cost-and-yield line the row prints. Production is the count
        // times the per-unit rate, so it follows a purchase made beneath the
        // screen in that purchase's own refresh.
        [Test]
        public void TheInfoScreenReadsTheHeldGeneratorAndFollowsAPurchase()
        {
            var fx = new Fixture();
            fx.Enter();
            var row = AmpRow(fx);

            row.OpenInfo();

            Assert.IsTrue(Fixture.Shown(fx.Host.GeneratorInfo.Root), "the screen is up over the chapter");
            Assert.AreSame(fx.PracticeAmp, fx.Host.GeneratorInfo.Held, "the row's own generator");
            Assert.AreEqual(fx.PracticeAmp.displayName, InfoText(fx, "info-name"));
            var description = fx.Host.GeneratorInfo.Root.Q<Label>("info-description");
            Assert.IsTrue(Fixture.Shown(description), "the amp has a description, so the screen shows one");
            Assert.AreEqual(fx.PracticeAmp.description, description.text);
            Assert.AreEqual(row.Root.Q<Label>("yield").text, InfoText(fx, "info-cost"),
                "the row and the screen print one line, not two that agree until they do not");
            Assert.AreEqual("Owned: 0", InfoText(fx, "info-owned"));
            Assert.AreEqual("Producing: nothing", InfoText(fx, "info-production"),
                "no units are owned, so there is no product to name");

            // Bought beneath the standing screen: the gate is 100 earned cash
            // and the first unit costs 60.
            fx.Ctx(fx.Tier1).Deposit("cash", 100);
            fx.Session.TryBuy(fx.Ctx(fx.Tier1), fx.PracticeAmp, 1);
            Assert.AreEqual(1, fx.Tier1.generatorCounts["practice_amp"], "the amp was bought");

            Assert.IsTrue(Fixture.Shown(fx.Host.GeneratorInfo.Root), "a purchase is not a close");
            Assert.AreEqual("Owned: 1", InfoText(fx, "info-owned"));
            // One unit's rate at the declaring scope, which is what N units
            // produce: nothing in the effect vocabulary reads the owned count.
            var (currency, amount) = Producer.UnitRate(fx.Ctx(fx.Tier1), fx.PracticeAmp).Single();
            Assert.AreEqual("Producing: " + NumberFormatter.Format(amount) + " " + currency.displayName + "/s",
                InfoText(fx, "info-production"));
        }

        // A press that spans a refresh lands on the element it started on: the
        // screen's labels are the document's, so a tick's pass moves their text
        // and never the hierarchy (12.11).
        [Test]
        public void TheInfoScreenStandsAcrossATicksRefresh()
        {
            var fx = new Fixture();
            fx.Enter();
            AmpRow(fx).OpenInfo();

            fx.Session.Tick(0.25, fx.Now.AddSeconds(0.25));

            Assert.IsTrue(Fixture.Shown(fx.Host.GeneratorInfo.Root), "the screen is still the requested overlay");
            Assert.AreSame(fx.PracticeAmp, fx.Host.GeneratorInfo.Held, "and it holds the generator it held");
        }

        // A requested overlay like any other (12.11): the next one replaces it
        // and takes the held pair with it, and Back drops the pair too, so a
        // reopen starts from the row that asked rather than a dead reference.
        [Test]
        public void TheInfoScreenIsReplacedByAnotherOverlayAndDroppedOnClose()
        {
            var fx = new Fixture();
            fx.Enter();
            var row = AmpRow(fx);
            row.OpenInfo();

            fx.Host.OpenEncore();

            Assert.IsFalse(Fixture.Shown(fx.Host.GeneratorInfo.Root), "one requested overlay at a time");
            Assert.IsNull(fx.Host.GeneratorInfo.Held, "and the request goes down with it");
            Assert.IsTrue(Fixture.Shown(fx.Screen.Q<VisualElement>("encore-window")));

            row.OpenInfo();
            Assert.IsTrue(Fixture.Shown(fx.Host.GeneratorInfo.Root));

            fx.Host.CloseOverlay();

            Assert.IsFalse(Fixture.Shown(fx.Host.GeneratorInfo.Root));
            Assert.IsNull(fx.Host.GeneratorInfo.Held);
        }

        [Test]
        public void TheInfoScreenReplacesAnOpenStoryCard()
        {
            var fx = new Fixture();
            fx.Enter();
            var row = AmpRow(fx);
            fx.Host.OpenStory(fx.Opener, fx.Ch1);
            Assert.IsNotNull(fx.Host.ShownStory, "the card is the top of the overlay stack");

            row.OpenInfo();

            Assert.IsNull(fx.Host.ShownStory, "a requested overlay replaces the card");
            Assert.IsFalse(Fixture.Shown(StoryCard(fx.Screen)));
            Assert.IsTrue(Fixture.Shown(fx.Host.GeneratorInfo.Root));
        }

        // The overlay belongs to a live chapter, so a phase with no chapter has
        // nothing to open one over (12.11).
        [Test]
        public void TheInfoScreenIsRefusedOutsideLive()
        {
            var fx = new Fixture();
            fx.Host.Render();
            Assert.AreEqual(SessionPhase.NoChapter, fx.Session.Phase);

            fx.Host.OpenGeneratorInfo(fx.PracticeAmp, fx.Tier1);

            Assert.IsFalse(Fixture.Shown(fx.Host.GeneratorInfo.Root),
                "there is no chapter to open an overlay over");
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

        // ---- the story log (section 10, 12.11) ----

        // The log: the app-owned overlay Screen.uxml names.
        private static VisualElement StoryLogWindow(VisualElement screen) =>
            screen.Q<VisualElement>("story-log-window");

        // The listed beats, found by the class each entry gives its own button
        // and filtered to the shown ones: a button stands for every candidate
        // beat and the log shows the read ones, so the display IS the listing.
        private static List<Button> LogEntries(VisualElement screen) =>
            StoryLogWindow(screen).Query<Button>(className: "story-log-entry").ToList()
                .Where(entry => entry.style.display.value == DisplayStyle.Flex).ToList();

        // The log lists by the seen latch alone, read outward from each chapter
        // as the row reads it (section 10), so an unread beat is absent whatever
        // its gate says.
        [Test]
        public void TheStoryLogListsOnlyReadBeatsAcrossTheRoster()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Host.OpenStoryLog();

            Assert.IsTrue(Fixture.Shown(StoryLogWindow(fx.Screen)), "the log is up over the chapter");
            Assert.IsEmpty(LogEntries(fx.Screen), "a fresh game has read nothing");
            Assert.IsTrue(Fixture.Shown(StoryLogWindow(fx.Screen).Q<Label>("story-log-empty")),
                "so the empty line is the whole log");
            Assert.AreEqual(7, fx.Host.Sections.Count, "the sections stay up beneath every overlay");

            fx.Host.CloseOverlay();
            fx.Host.OpenStory(fx.Opener, fx.Ch1);
            fx.Host.CloseStory();
            fx.Host.OpenStoryLog();

            var entries = LogEntries(fx.Screen);
            Assert.AreEqual(1, entries.Count, "one entry per read beat");
            Assert.AreEqual("Make Some Noise", entries[0].text, "the beat's authored displayName");
            Assert.IsFalse(Fixture.Shown(StoryLogWindow(fx.Screen).Q<Label>("story-log-empty")));
            CollectionAssert.DoesNotContain(entries.Select(entry => entry.text).ToArray(), "Your First Roadie",
                "the capstone is unread, so no entry stands for it");
        }

        // A reopen from the log is presentation alone (section 10): the latch
        // was written when the card first opened, so the entry raises the card
        // and writes nothing, and the card's close returns to the log.
        [Test]
        public void ARewatchFromTheLogShowsTheCardAndWritesNothing()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Host.OpenStory(fx.Opener, fx.Ch1);
            fx.Host.CloseStory();
            fx.Host.OpenStoryLog();
            var flags = fx.Root.flags.Count;

            // What the entry's button calls; a headless test has no pointer.
            fx.Host.StoryLog.Open(fx.Opener, fx.Ch1);

            Assert.IsFalse(Fixture.Shown(StoryLogWindow(fx.Screen)), "the card takes the log's place");
            Assert.IsTrue(Fixture.Shown(StoryCard(fx.Screen)));
            Assert.AreEqual("Make Some Noise", StoryCard(fx.Screen).Q<Label>("title").text);
            Assert.AreSame(fx.Opener, fx.Host.ShownStory);
            Assert.AreEqual(flags, fx.Root.flags.Count, "a read beat's reopen writes nothing");

            fx.Host.CloseStory();

            Assert.IsFalse(Fixture.Shown(StoryCard(fx.Screen)));
            Assert.IsTrue(Fixture.Shown(StoryLogWindow(fx.Screen)), "the close returns to the log");
            Assert.IsNull(fx.Host.ShownStory, "the request is cleared, so no card is showing");
            Assert.AreEqual(7, fx.Host.Sections.Count);

            fx.Host.CloseOverlay();

            Assert.IsFalse(Fixture.Shown(StoryLogWindow(fx.Screen)), "the log's own Back returns to the chapter");
        }

        // The log is a requested overlay (12.11): it replaces an open card and
        // drops the request with it, exactly as settings does, and the next
        // requested overlay replaces the log in turn.
        [Test]
        public void OpeningTheStoryLogReplacesAnOpenCard()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Host.OpenStory(fx.Opener, fx.Ch1);
            Assert.IsTrue(Fixture.Shown(StoryCard(fx.Screen)));

            fx.Host.OpenStoryLog();

            Assert.IsFalse(Fixture.Shown(StoryCard(fx.Screen)), "a requested overlay replaces the card");
            Assert.IsTrue(Fixture.Shown(StoryLogWindow(fx.Screen)));
            Assert.IsNull(fx.Host.ShownStory, "and the request goes down with it");

            fx.Host.OpenSettings();

            Assert.IsFalse(Fixture.Shown(StoryLogWindow(fx.Screen)), "one requested overlay at a time");
            Assert.IsTrue(Fixture.Shown(fx.Screen.Q<VisualElement>("settings")));
        }

        // A press that spans a refresh lands on the element it started on: the
        // log holds a button per candidate beat for the whole process and a show
        // toggles each by its latch, so the tick's pass moves a display and
        // never the hierarchy (12.11).
        [Test]
        public void AStoryLogEntrySurvivesTheTicksRefresh()
        {
            var fx = new Fixture();
            fx.Enter();
            fx.Host.OpenStory(fx.Opener, fx.Ch1);
            fx.Host.CloseStory();
            fx.Host.CloseOverlay();
            fx.Host.OpenStoryLog();
            var entry = LogEntries(fx.Screen).Single();

            // The Live cadence is a tick every quarter second, and each one
            // refreshes the log that is standing over the chapter.
            fx.Session.Tick(0.25, fx.Now.AddSeconds(0.25));

            Assert.IsTrue(Fixture.Shown(StoryLogWindow(fx.Screen)), "the log is still the requested overlay");
            Assert.AreSame(entry, LogEntries(fx.Screen).Single(),
                "the listed entry is the element it was, not a replacement for it");
            Assert.IsNotNull(entry.parent, "and it is still in the tree, so a press on it is never cancelled");
            Assert.AreEqual(fx.Ch1Def.storyBeats.Count,
                StoryLogWindow(fx.Screen).Query<Button>(className: "story-log-entry").ToList().Count,
                "one button per candidate beat, whatever its latch says");
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

            // The dormant chapter and its one beat, off unless a row asks for
            // them: every other row is about the foreground chapter's own list.
            public readonly ChapterDefinition Ch2Def;
            public readonly StoryBeatDefinition SecondStage;
            public readonly ChapterScopeState Ch2;

            // `author` runs over the definitions before the build, for a row that
            // wants content the standing fixture does not carry.
            public StoryFixture(bool markedUnlocksOnLiveTick = false, bool withSecondChapter = false,
                                Action<TestTree> author = null)
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
                // The chrome the host binds: Screen.uxml's pill names timer
                // 'encore_timer', and the host requires root to declare it.
                TestTree.DeclareEncore(Tree.RootDef);
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

                if (withSecondChapter)
                {
                    // A second chapter in root's roster holding one beat and
                    // nothing else: its latch is homed at root, which is where
                    // the walk outward from ch2 lands (section 10).
                    Ch2Def = TestTree.MakeChapter("ch2");
                    SecondStage = TestTree.MakeDefinition<StoryBeatDefinition>("story_ch2");
                    SecondStage.displayName = "Second Stage";
                    SecondStage.text = "The second stage is yours.";
                    SecondStage.availableWhen = new Always();
                    SecondStage.seenFlag = "story_ch2_seen";
                    Ch2Def.storyBeats.Add(SecondStage);
                    Tree.RootDef.declaredFlags.Add("story_ch2_seen");
                    Tree.Chapters.Add(Ch2Def);
                }

                author?.Invoke(Tree);
                // The link pass runs in Build, so the section, its modules and
                // the trigger are wired only once the tree is built again.
                Tree.Rebuild();
                if (markedUnlocksOnLiveTick)
                    Tree.Tier1.generatorCounts[Tree.PracticeAmp.Id] = 1;
                if (withSecondChapter)
                    Ch2 = (ChapterScopeState)TestNavigation.Node(Tree.Root, Ch2Def);

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
                    new AdManager(Session, new FakeAdService(), () => { }),
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
        [TestCase("story-log-window")]
        [TestCase("generator-info")]
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
                case "story-log-window": fx.Host.OpenStoryLog(); break;
                // What the row's long press calls, at the scope the row
                // resolved; a headless test has no pointer to hold.
                case "generator-info":
                    fx.Host.OpenGeneratorInfo(fx.Tree.PracticeAmp, fx.Tree.Tier1);
                    break;
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

        // The log walks root's whole roster (section 10), so a beat read in a
        // chapter that is not in the foreground is listed and rewatchable -
        // reopening it shows the card alone and the foreground stays put.
        [Test]
        public void TheStoryLogListsADormantChaptersReadBeat()
        {
            var fx = new StoryFixture(withSecondChapter: true);
            // A latch the save could hold: ch2's beat was read before, and ch1
            // is the chapter the fixture switched into.
            fx.Tree.Root.flags.Add("story_ch2_seen");
            var flags = fx.Tree.Root.flags.Count;
            fx.Host.OpenStoryLog();

            var entries = LogEntries(fx.Screen);
            Assert.AreEqual(1, entries.Count, "ch1's own two beats are unread");
            Assert.AreEqual("Second Stage", entries[0].text, "the dormant chapter's beat");

            // What the entry's button calls; a headless test has no pointer.
            fx.Host.StoryLog.Open(fx.SecondStage, fx.Ch2);

            Assert.IsTrue(Fixture.Shown(StoryCard(fx.Screen)));
            Assert.AreEqual("Second Stage", StoryCard(fx.Screen).Q<Label>("title").text);
            Assert.AreSame(fx.Tree.Ch1, fx.Session.ForegroundChapter, "a rewatch is not a switch");
            Assert.AreEqual(flags, fx.Tree.Root.flags.Count, "the beat was already read");

            fx.Host.CloseStory();

            Assert.IsFalse(Fixture.Shown(StoryCard(fx.Screen)));
            Assert.IsTrue(Fixture.Shown(StoryLogWindow(fx.Screen)), "the close returns to the log");
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
