using System;
using System.Collections.Generic;
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
    // The levelled group (design doc 12.11) over chapter 2's IMPORTED calendar,
    // built from the shipping UXML and bound by hand at tier2 the way the host
    // binds the Calendar's module. No host is present, so each test repaints
    // with Refresh after the commands it issues, and every assertion reads the
    // widget's shown values - never a label.
    public class LeveledGroupUITests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(),
                Math.Max(1e-9, Math.Abs(expected) * 1e-12), what ?? string.Empty);

        // The one thing a row asks of the host, recorded: the hold hands over
        // the level generator and its declaring scope.
        private sealed class RecordingInfos : IGeneratorInfoOpener
        {
            public GeneratorDefinition Generator;
            public ScopeState Scope;

            public void OpenGeneratorInfo(GeneratorDefinition generator, ScopeState scope)
            {
                Generator = generator;
                Scope = scope;
            }
        }

        // The one thing the card's Auto asks of the host, recorded: the hold
        // hands over the group and its declaring scope.
        private sealed class RecordingAutomations : IAutomationOpener
        {
            public GroupDefinition Group;
            public ScopeState Scope;

            public void OpenAutomation(GroupDefinition group, ScopeState scope)
            {
                Group = group;
                Scope = scope;
            }
        }

        private sealed class NoDetails : IUpgradeDetailsOpener
        {
            public void OpenUpgradeDetails(UpgradeDefinition upgrade, ScopeState scope) { }
        }

        private sealed class NoStories : IStoryOpener
        {
            public void OpenStory(Story.StoryBeatDefinition beat, ScopeState scope) { }
        }

        private sealed class NoRungs : IRungOpener
        {
            public void OpenRungInfo(Rung rung) { }

            public void OpenRungConfirm(Rung rung, ScopeState scope) { }
        }

        // The one thing a row asks of the host on a crossing, recorded in the
        // order the row raised them.
        private sealed class RecordingToasts : IToastOpener
        {
            public readonly List<ThresholdEffect> Raised = new();

            public void OpenToast(ThresholdEffect effect) => Raised.Add(effect);
        }

        private class Fixture
        {
            public static readonly DateTime Start = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

            public readonly TierDefinition Tier2Def;
            public readonly CurrencyDefinition Cash, Fans, Buzz;
            public readonly GroupDefinition TheCalendar;
            public readonly BarDefinition OpenMic, HouseParty;
            public readonly GeneratorDefinition OpenMicDraw, HousePartyDraw;

            public readonly RootScopeState Root;
            public readonly ChapterScopeState Ch2;
            public readonly TierScopeState Tier2;
            public readonly GameSession Session;
            public readonly GameClock Clock;
            public readonly ModuleRegistry Registry;
            public readonly RecordingInfos Infos = new();
            public readonly RecordingAutomations Automations = new();
            public readonly RecordingToasts Toasts = new();

            public DateTime Now = Start;

            private static GameConfig Config()
            {
                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.maxGameSpeed = 4;
                config.minimumAwaySeconds = 180;
                config.idleCapSeconds = 14400;
                return config;
            }

            // The roster boot composes, entered as a live player enters it:
            // the root flag a finished chapter 1 leaves, the stamp at now so
            // no idle window is owed. opening_cash pays its 5 Cash on entry.
            public Fixture()
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

                Cash = Find(Tier2Def.declaredCurrencies, "cash");
                Fans = Find(Tier2Def.declaredCurrencies, "fans");
                Buzz = Find(Tier2Def.declaredCurrencies, "buzz");
                TheCalendar = Find(Tier2Def.groups, "the_calendar");
                OpenMic = Find(Tier2Def.bars, "open_mic");
                HouseParty = Find(Tier2Def.bars, "house_party");
                OpenMicDraw = Find(Tier2Def.generators, "open_mic_draw");
                HousePartyDraw = Find(Tier2Def.generators, "house_party_draw");

                Root = ScopeState.Build(ComposedContent.Compose(rootDef, new[] { ch1Def, ch2Def }));
                Ch2 = (ChapterScopeState)TestNavigation.Node(Root, ch2Def);
                Tier2 = (TierScopeState)TestNavigation.Node(Root, Tier2Def);
                Session = new GameSession(Root, Config());
                Clock = new GameClock(Now);
                Registry = AssetDatabase.LoadAssetAtPath<ModuleRegistry>("Assets/Settings/ModuleRegistry.asset");
                Assert.IsNotNull(Registry, "Assets/Settings/ModuleRegistry.asset is missing.");

                Root.flags.Add("ch1_complete");
                Ch2.lastActiveUtc = Now;
                Session.SwitchChapter(Ch2, Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
            }

            private static T Find<T>(IEnumerable<T> definitions, string id) where T : Definition =>
                definitions.Single(d => d.Id == id);

            public GameContext Ctx => new GameContext(Tier2, Now);

            // A widget as the host builds one: the shipping UXML instantiated,
            // the factory's controller over it, bound at tier2.
            // `configure` runs between construction and the bind, where the
            // host hands a levelled card its module's buy control.
            public ModuleWidget Widget(string prefabId, string uxml, Definition content,
                                       Action<ModuleWidget> configure = null)
            {
                var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Widgets/" + uxml);
                Assert.IsNotNull(asset, "Assets/UI/Widgets/" + uxml + " is missing");
                var widget = ModuleWidgetFactory.Create(prefabId, asset.Instantiate(), Registry, new NoStories(),
                    Infos, Automations, new NoDetails(), new NoRungs(), Toasts);
                configure?.Invoke(widget);
                widget.Bind(Session, Tier2, content, Clock);
                return widget;
            }

            public LeveledGroupUI Calendar(BuyControlKind control = BuyControlKind.Pair)
            {
                var widget = (LeveledGroupUI)Widget("leveled_group", "LeveledGroup.uxml", TheCalendar,
                    w => ((LeveledGroupUI)w).BuyControl = control);
                widget.Refresh();
                return widget;
            }

            // The shipping screen and its host over this session, rendered once:
            // the card's hold reaches the host's window, and every command's
            // refresh repaints both, as in the game. The chapter's opening
            // story is read by the drain, so no card stands over the window.
            public ScreenHost Host()
            {
                var screen = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Screen.uxml");
                Assert.IsNotNull(screen, "Assets/UI/Screen.uxml is missing");
                var host = new ScreenHost(screen.Instantiate(), Registry, Session, Clock,
                    new AdManager(Session, new FakeAdService(), () => { }),
                    new IAPManager(Session, new FakeStoreService(), Session.Config, () => { }));
                Session.Refresh();
                Session.Drain();
                return host;
            }

            // The calendar's card as the host built it.
            public static LeveledGroupUI Calendar(ScreenHost host) =>
                host.Sections.SelectMany(section => section.Modules)
                    .Select(module => module.Widget).OfType<LeveledGroupUI>().Single();

            // Real and game time move together, so a command the widget issues
            // samples the same moment the test's own reads do.
            public void Tick(double seconds)
            {
                Now = Now.AddSeconds(seconds);
                Session.Tick(seconds, Now);
                Clock.Frame(Now, seconds);
            }

            public void Give(BigNumber cash) => Ctx.Deposit(Cash.Id, cash);

            public int Owned(GeneratorDefinition generator) =>
                Tier2.generatorCounts.TryGetValue(generator.Id, out var count) ? count : 0;

            public bool Active(BarDefinition bar) =>
                Tier2.activeMembers.TryGetValue(TheCalendar.Id, out var active) && active.Contains(bar.Id);

            public BigNumber Progress(BarDefinition bar) =>
                Tier2.barProgress.TryGetValue(bar.Id, out var stored) ? stored : BigNumber.Zero;

            public double GameSpeed => TickSystem.GameSpeed(new GameContext(Ch2, Now), Ch2, Session.Config);
        }

        // ---- the rows ----

        [Test]
        public void A_row_shows_only_while_its_bar_is_available()
        {
            var fx = new Fixture();
            var calendar = fx.Calendar();
            foreach (var bar in fx.Tier2Def.bars)
                Assert.IsFalse(calendar.Row(bar).Visible, $"{bar.Id}: no draw bought, so no gig is booked");

            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            calendar.Refresh();

            Assert.IsTrue(calendar.Row(fx.OpenMic).Visible, "one draw opens the open mic");
            foreach (var bar in fx.Tier2Def.bars.Where(b => b != fx.OpenMic))
                Assert.IsFalse(calendar.Row(bar).Visible, $"{bar.Id} still wants its own draw");
        }

        [Test]
        public void The_row_shows_its_level_as_the_level_generators_owned_count()
        {
            var fx = new Fixture();
            fx.Give(100);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 3);
            var calendar = fx.Calendar();
            var row = calendar.Row(fx.OpenMic);

            Assert.AreEqual(3, row.ShownPurchased);
            Assert.AreEqual(fx.Owned(fx.OpenMicDraw), row.ShownPurchased);
            Assert.AreEqual(BigNumber.Zero, row.ShownGranted, "nothing pays the draw a granted count");
        }

        [Test]
        public void The_rates_inside_the_bar_are_the_bars_completion_rate()
        {
            var fx = new Fixture();
            fx.Give(100);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 3);
            var row = fx.Calendar().Row(fx.OpenMic);

            var expected = BarSystem.CompletionRate(fx.Ctx, fx.OpenMic, fx.GameSpeed);
            Assert.AreEqual(expected.Count, row.ShownRates.Count, "one line per currency the gig pays");
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.AreSame(expected[i].currency, row.ShownRates[i].currency);
                Assert.AreEqual(expected[i].perSecond, row.ShownRates[i].perSecond, expected[i].currency.Id);
            }
            Assert.AreSame(fx.Cash, row.ShownRates[0].currency, "the draw pays Cash first");
            Assert.Greater(row.ShownRates[0].perSecond.ToDouble(), 0, "three draws pay something");
        }

        [Test]
        public void Tapping_an_idle_row_starts_the_gig_and_pays_nothing_into_it()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var calendar = fx.Calendar();
            var row = calendar.Row(fx.OpenMic);
            Assert.IsTrue(row.Idle, "available and not running");

            row.Tap();
            calendar.Refresh();

            Assert.IsTrue(fx.Active(fx.OpenMic), "the tap put the gig on the calendar");
            Assert.AreEqual(BigNumber.Zero, fx.Progress(fx.OpenMic), "and fired nothing into its bar");
            Assert.IsFalse(row.Idle);
        }

        [Test]
        public void Tapping_a_running_row_works_the_crowd_and_leaves_it_running()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var calendar = fx.Calendar();
            var row = calendar.Row(fx.OpenMic);
            row.Tap();
            calendar.Refresh();

            row.Tap();
            calendar.Refresh();

            // crowd_open_mic pays the bar 1 unit per press.
            AssertClose(1, fx.Progress(fx.OpenMic), "one firing of the tap producer");
            Assert.IsTrue(fx.Active(fx.OpenMic), "a tap never takes a gig off the calendar");
            AssertClose(1, row.ShownProgress, "the fill draws it");
        }

        [Test]
        public void The_buy_buttons_buy_the_level_generator()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Give(100);
            var calendar = fx.Calendar();
            var row = calendar.Row(fx.OpenMic);

            AssertClose(Purchasing.CostOf(fx.OpenMicDraw, fx.Ctx, 1).ToDouble(), row.ShownCost, "the next level's price");
            var max = Purchasing.MaxAffordable(fx.Ctx, fx.OpenMicDraw);
            Assert.Greater(max, 1, "100 Cash covers several draws");
            Assert.AreEqual(max, row.ShownMax);

            row.BuyMax();
            Assert.AreEqual(1 + max, fx.Owned(fx.OpenMicDraw), "+max bought the count it printed");

            fx.Give(1000);
            calendar.Refresh();
            row.BuyOne();
            Assert.AreEqual(2 + max, fx.Owned(fx.OpenMicDraw), "+1 bought one");
        }

        [Test]
        public void Holding_the_title_opens_the_level_generators_info_at_its_home()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var row = fx.Calendar().Row(fx.OpenMic);

            row.OpenInfo();

            Assert.AreSame(fx.OpenMicDraw, fx.Infos.Generator);
            Assert.AreSame(fx.Tier2, fx.Infos.Scope);
        }

        // ---- Book ----

        [Test]
        public void Book_offers_the_first_unbooked_gig_whose_draw_is_offered()
        {
            var fx = new Fixture();
            var calendar = fx.Calendar();

            Assert.AreSame(fx.OpenMic, calendar.BookTarget, "open_mic_draw is offered from the start");
            AssertClose(Purchasing.CostOf(fx.OpenMicDraw, fx.Ctx, 1).ToDouble(), calendar.ShownBookCost);
            Assert.IsTrue(calendar.BookEnabled, "the opening 5 Cash buys the first draw");

            fx.Tier2.balances[fx.Cash.Id] = BigNumber.Zero;
            calendar.Refresh();
            Assert.AreSame(fx.OpenMic, calendar.BookTarget, "an unaffordable booking is still offered");
            Assert.IsFalse(calendar.BookEnabled, "and disarmed");
        }

        [Test]
        public void Book_buys_the_draw_and_starts_the_gig_then_waits_for_the_next_draw()
        {
            var fx = new Fixture();
            var calendar = fx.Calendar();

            calendar.Book();
            calendar.Refresh();

            Assert.AreEqual(1, fx.Owned(fx.OpenMicDraw), "one level bought");
            Assert.IsTrue(fx.Active(fx.OpenMic), "and the gig started in the same press");
            Assert.IsTrue(calendar.Row(fx.OpenMic).Visible);
            Assert.IsNull(calendar.BookTarget, "house_party_draw is not offered before 150 Cash earned");

            fx.Give(200);
            calendar.Refresh();
            Assert.AreSame(fx.HouseParty, calendar.BookTarget, "the next gig whose draw opened");
            AssertClose(Purchasing.CostOf(fx.HousePartyDraw, fx.Ctx, 1).ToDouble(), calendar.ShownBookCost);
            Assert.IsTrue(calendar.BookEnabled);

            calendar.Book();
            calendar.Refresh();
            Assert.AreEqual(1, fx.Owned(fx.HousePartyDraw));
            Assert.IsTrue(fx.Active(fx.HouseParty));
            Assert.IsTrue(fx.Active(fx.OpenMic), "booking adds to the calendar and takes nothing off it");
        }

        // ---- Auto ----

        [Test]
        public void Auto_offers_the_first_visible_gig_that_does_not_go_again_and_moves_on_when_bought()
        {
            var fx = new Fixture();
            fx.Give(200);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Session.TryBuy(fx.Ctx, fx.HousePartyDraw, 1);
            fx.Tier2.balances[fx.Cash.Id] = BigNumber.Zero;
            var calendar = fx.Calendar();

            Assert.AreSame(fx.OpenMic, calendar.AutoTarget, "group order: the open mic first");
            Assert.IsFalse(calendar.AutoEnabled, "no Cash, so the booking is unaffordable");

            fx.Give(Purchasing.CostOf(fx.OpenMic.automation, fx.Ctx));
            calendar.Refresh();
            Assert.IsTrue(calendar.AutoEnabled, "exactly the price");

            calendar.Auto();
            calendar.Refresh();
            Assert.IsTrue(fx.OpenMic.repeatWhen.Evaluate(fx.Ctx), "the purchase made the open mic go again");
            Assert.AreSame(fx.HouseParty, calendar.AutoTarget, "and the button moved to the next gig");
        }

        [Test]
        public void Auto_leaves_when_every_visible_gig_goes_again_and_returns_when_another_is_booked()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Give(Purchasing.CostOf(fx.OpenMic.automation, fx.Ctx));
            var calendar = fx.Calendar();
            calendar.Auto();
            calendar.Refresh();

            Assert.IsNull(calendar.AutoTarget, "the one visible gig repeats");
            Assert.IsFalse(calendar.AutoEnabled);

            fx.Give(200);
            calendar.Refresh();
            Assert.AreSame(fx.HouseParty, calendar.BookTarget);
            calendar.Book();
            calendar.Refresh();
            Assert.AreSame(fx.HouseParty, calendar.AutoTarget, "a newly booked gig brings Auto back");
        }

        // A booking flag outlives the run while the upgrade's latch does not
        // (chapter 2's EP), so "automated" is the bar's repeatWhen and never
        // the purchase: a gig whose flag stands is skipped with no upgrade
        // bought this run.
        [Test]
        public void Auto_skips_a_gig_whose_repeat_condition_holds_without_a_purchase()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Ch2.flags.Add(((FlagSet)fx.OpenMic.repeatWhen).flagId);
            var calendar = fx.Calendar();

            Assert.IsFalse(fx.Tier2.purchasedUpgrades.Contains(fx.OpenMic.automation.Id));
            Assert.IsNull(calendar.AutoTarget, "the open mic already goes again");
        }

        // ---- the automation window ----

        // What the hold on Auto calls: the group and the scope the card
        // resolved, handed to the host.
        [Test]
        public void Holding_Auto_hands_the_host_the_group_at_its_declaring_scope()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var calendar = fx.Calendar();

            calendar.OpenAutomation();

            Assert.AreSame(fx.TheCalendar, fx.Automations.Group);
            Assert.AreSame(fx.Tier2, fx.Automations.Scope);
        }

        [Test]
        public void The_hold_opens_the_window_on_the_cards_group()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var host = fx.Host();
            var window = host.AutomationWindow;
            Assert.IsFalse(window.IsOpen);

            Fixture.Calendar(host).OpenAutomation();

            Assert.IsTrue(window.IsOpen);
            Assert.AreSame(fx.TheCalendar, window.ShownGroup);
            Assert.AreSame(fx.Tier2, window.HeldScope);
        }

        // A tap buys and nothing else: the window is the hold's alone.
        [Test]
        public void A_tap_on_Auto_buys_and_does_not_open_the_window()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Give(Purchasing.CostOf(fx.OpenMic.automation, fx.Ctx));
            var host = fx.Host();
            var calendar = Fixture.Calendar(host);
            Assert.AreSame(fx.OpenMic, calendar.AutoTarget);

            calendar.Auto();

            Assert.IsTrue(fx.OpenMic.repeatWhen.Evaluate(fx.Ctx), "the tap bought the open mic's automation");
            Assert.IsFalse(host.AutomationWindow.IsOpen);
            Assert.IsNull(host.AutomationWindow.ShownGroup);
        }

        [Test]
        public void The_rows_are_the_available_bars_naming_an_automation_in_group_order()
        {
            var fx = new Fixture();
            fx.Give(200);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Session.TryBuy(fx.Ctx, fx.HousePartyDraw, 1);
            var host = fx.Host();
            var calendar = Fixture.Calendar(host);

            calendar.OpenAutomation();

            var expected = fx.TheCalendar.members.Cast<BarDefinition>()
                .Where(bar => bar.automation != null && calendar.Row(bar).Visible).ToArray();
            CollectionAssert.AreEqual(new[] { fx.OpenMic, fx.HouseParty }, expected, "two gigs booked");
            CollectionAssert.AreEqual(expected, host.AutomationWindow.Rows.Select(row => row.Bar).ToArray());
        }

        // Price: neither repeating nor bought, and the purchase would be
        // refused. Buy: the purchase would succeed. Purchased: the bar goes
        // again, whether by the upgrade or by its flag alone.
        [Test]
        public void Each_row_reads_Price_Buy_or_Purchased_under_its_conditions()
        {
            var fx = new Fixture();
            fx.Give(200);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Session.TryBuy(fx.Ctx, fx.HousePartyDraw, 1);
            fx.Tier2.balances[fx.Cash.Id] = BigNumber.Zero;
            var host = fx.Host();
            Fixture.Calendar(host).OpenAutomation();
            var window = host.AutomationWindow;

            Assert.AreEqual(AutomationState.Price, Row(window, fx.OpenMic).State, "no Cash");
            Assert.AreEqual(AutomationState.Price, Row(window, fx.HouseParty).State);
            AssertClose(Purchasing.CostOf(fx.OpenMic.automation, fx.Ctx).ToDouble(), Row(window, fx.OpenMic).ShownCost);
            AssertClose(Purchasing.CostOf(fx.HouseParty.automation, fx.Ctx).ToDouble(),
                Row(window, fx.HouseParty).ShownCost);

            // Cash arriving while the window stands flips the row it covers on
            // the next refresh, with no reopen.
            fx.Give(Purchasing.CostOf(fx.OpenMic.automation, fx.Ctx));
            fx.Session.Refresh();
            Assert.AreEqual(AutomationState.Buy, Row(window, fx.OpenMic).State, "exactly the open mic's price");
            Assert.AreEqual(AutomationState.Price, Row(window, fx.HouseParty).State, "short of the house party's");

            fx.Ch2.flags.Add(((FlagSet)fx.HouseParty.repeatWhen).flagId);
            fx.Session.Refresh();
            Assert.AreEqual(AutomationState.Purchased, Row(window, fx.HouseParty).State,
                "the flag alone makes the house party go again");
            Assert.IsFalse(fx.Tier2.purchasedUpgrades.Contains(fx.HouseParty.automation.Id));
        }

        [Test]
        public void Buy_purchases_that_bars_automation_and_the_window_stays_open()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Give(Purchasing.CostOf(fx.OpenMic.automation, fx.Ctx));
            var host = fx.Host();
            Fixture.Calendar(host).OpenAutomation();
            var window = host.AutomationWindow;
            Assert.AreEqual(AutomationState.Buy, Row(window, fx.OpenMic).State);

            window.Buy(fx.OpenMic);

            Assert.IsTrue(fx.OpenMic.repeatWhen.Evaluate(fx.Ctx), "the purchase made the open mic go again");
            Assert.IsTrue(window.IsOpen, "a purchase is not a close");
            Assert.AreSame(fx.TheCalendar, window.ShownGroup);
            Assert.AreEqual(AutomationState.Purchased, Row(window, fx.OpenMic).State,
                "the command's own refresh repainted the row");
        }

        // Out of order: the second gig's automation bought from the window
        // while the first's is not, and the card's button stays on the first,
        // then leaves once the first is bought too.
        [Test]
        public void Buying_out_of_order_moves_the_cards_target_accordingly()
        {
            var fx = new Fixture();
            fx.Give(200);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Session.TryBuy(fx.Ctx, fx.HousePartyDraw, 1);
            fx.Give(Purchasing.CostOf(fx.HouseParty.automation, fx.Ctx));
            var host = fx.Host();
            var calendar = Fixture.Calendar(host);
            Assert.AreSame(fx.OpenMic, calendar.AutoTarget);
            calendar.OpenAutomation();
            var window = host.AutomationWindow;

            window.Buy(fx.HouseParty);

            Assert.IsTrue(fx.HouseParty.repeatWhen.Evaluate(fx.Ctx));
            Assert.IsFalse(fx.OpenMic.repeatWhen.Evaluate(fx.Ctx));
            Assert.AreEqual(AutomationState.Purchased, Row(window, fx.HouseParty).State);
            Assert.AreNotEqual(AutomationState.Purchased, Row(window, fx.OpenMic).State);
            Assert.AreSame(fx.OpenMic, calendar.AutoTarget, "the tap still buys the first in order");

            fx.Give(Purchasing.CostOf(fx.OpenMic.automation, fx.Ctx));
            fx.Session.Refresh();
            window.Buy(fx.OpenMic);

            Assert.AreEqual(AutomationState.Purchased, Row(window, fx.OpenMic).State);
            Assert.IsNull(calendar.AutoTarget, "both visible gigs go again");
        }

        [Test]
        public void A_bar_that_becomes_available_while_the_window_is_open_appears_on_the_next_refresh()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var host = fx.Host();
            Fixture.Calendar(host).OpenAutomation();
            var window = host.AutomationWindow;
            CollectionAssert.AreEqual(new[] { fx.OpenMic }, window.Rows.Select(row => row.Bar).ToArray());

            fx.Give(200);
            fx.Session.TryBuy(fx.Ctx, fx.HousePartyDraw, 1);

            Assert.IsTrue(window.IsOpen);
            CollectionAssert.AreEqual(new[] { fx.OpenMic, fx.HouseParty },
                window.Rows.Select(row => row.Bar).ToArray());
        }

        [Test]
        public void Dismiss_closes_the_window_and_drops_the_group()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var host = fx.Host();
            Fixture.Calendar(host).OpenAutomation();
            var window = host.AutomationWindow;
            Assert.IsTrue(window.IsOpen);

            window.Dismiss();

            Assert.IsFalse(window.IsOpen);
            Assert.IsNull(window.ShownGroup);
            Assert.IsEmpty(window.Rows);

            fx.Session.Refresh();
            Assert.IsFalse(window.IsOpen, "a refresh does not bring a dismissed window back");
        }

        private static AutomationWindowUI.Row Row(AutomationWindowUI window, BarDefinition bar) =>
            window.Rows.Single(row => row.Bar == bar);

        // ---- binding ----

        [Test]
        public void Binding_a_group_with_a_member_that_is_not_a_bar_throws()
        {
            var fx = new Fixture();
            var loose = TestTree.MakeDefinition<GroupDefinition>("loose_group");
            try
            {
                loose.members.Add(fx.OpenMicDraw);
                Assert.Throws<InvalidOperationException>(() =>
                    fx.Widget("leveled_group", "LeveledGroup.uxml", loose));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(loose);
            }
        }

        [Test]
        public void Binding_a_group_with_a_bar_that_names_no_level_throws()
        {
            var fx = new Fixture();
            var loose = TestTree.MakeDefinition<GroupDefinition>("loose_group");
            var bare = TestTree.MakeDefinition<BarDefinition>("bare_bar");
            try
            {
                loose.members.Add(bare);
                Assert.Throws<InvalidOperationException>(() =>
                    fx.Widget("leveled_group", "LeveledGroup.uxml", loose));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bare);
                UnityEngine.Object.DestroyImmediate(loose);
            }
        }

        // ---- the header rate ----

        // A running gig's completions are income the deposit ledger never sees
        // (they land in lumps), so the header adds the completion slope to the
        // deposit slope; the balance itself still rides the deposit slope.
        [Test]
        public void The_header_rate_adds_a_running_gigs_completion_slope_to_the_deposit_slope()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Session.SetActiveMembers(fx.Ctx, fx.TheCalendar, new Definition[] { fx.OpenMic });
            fx.Tick(1);
            var report = fx.Session.LastTick;
            Assert.Greater(report.CompletionSlope(fx.Tier2, fx.Cash.Id).ToDouble(), 0, "the gig filled for a second");

            var header = (CurrencyHeaderUI)fx.Widget("currency_line", "CurrencyLine.uxml", fx.Cash);
            header.Refresh();

            Assert.AreEqual(report.DepositSlope(fx.Tier2, fx.Cash.Id) + report.CompletionSlope(fx.Tier2, fx.Cash.Id),
                header.ShownRate);
        }

        [Test]
        public void The_header_rate_is_the_deposit_slope_alone_for_a_currency_no_bar_pays()
        {
            var fx = new Fixture();
            fx.Tier2.generatorCounts["flyers"] = 1;
            fx.Tick(1);
            var report = fx.Session.LastTick;
            Assert.AreEqual(BigNumber.Zero, report.CompletionSlope(fx.Tier2, fx.Buzz.Id), "no gig pays Buzz");
            Assert.Greater(report.DepositSlope(fx.Tier2, fx.Buzz.Id).ToDouble(), 0, "a flyer builds Buzz");

            var header = (CurrencyHeaderUI)fx.Widget("currency_line", "CurrencyLine.uxml", fx.Buzz);
            header.Refresh();

            Assert.AreEqual(report.DepositSlope(fx.Tier2, fx.Buzz.Id), header.ShownRate);
        }

        // ---- the cycle buy control ----

        [Test]
        public void A_pair_card_draws_the_pair_on_every_row()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var calendar = fx.Calendar();
            var row = calendar.Row(fx.OpenMic);

            Assert.AreEqual(BuyControlKind.Pair, calendar.BuyControl);
            Assert.AreEqual(BuyControlKind.Pair, row.ControlKind);
            Assert.AreEqual(0, row.ShownCount, "the pair draws no Hire button");
            Assert.Throws<InvalidOperationException>(row.Buy, "and there is no Hire press to make");
        }

        [Test]
        public void The_cycle_button_steps_one_max_next_and_back_to_one()
        {
            var calendar = new Fixture().Calendar(BuyControlKind.Cycle);
            Assert.AreEqual(BuyMode.One, calendar.Mode, "a card is built at Buy One");

            calendar.Cycle();
            Assert.AreEqual(BuyMode.Max, calendar.Mode);
            calendar.Cycle();
            Assert.AreEqual(BuyMode.Next, calendar.Mode);
            calendar.Cycle();
            Assert.AreEqual(BuyMode.One, calendar.Mode);
        }

        [Test]
        public void A_cycle_row_draws_one_hire_button_and_no_pair()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var row = fx.Calendar(BuyControlKind.Cycle).Row(fx.OpenMic);

            Assert.AreEqual(BuyControlKind.Cycle, row.ControlKind);
            Assert.AreEqual(0, row.ShownMax, "the cycle draws no max button");
            Assert.Throws<InvalidOperationException>(row.BuyOne);
            Assert.Throws<InvalidOperationException>(row.BuyMax);
        }

        [Test]
        public void Buy_One_hires_one_level_at_one_levels_price()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Give(1000);
            var calendar = fx.Calendar(BuyControlKind.Cycle);
            var row = calendar.Row(fx.OpenMic);

            Assert.AreEqual(1, row.ShownCount);
            AssertClose(Purchasing.CostOf(fx.OpenMicDraw, fx.Ctx, 1).ToDouble(), row.ShownCost);

            row.Buy();
            Assert.AreEqual(2, fx.Owned(fx.OpenMicDraw), "the press hired the one it printed");
        }

        [Test]
        public void Buy_Max_hires_the_most_the_balance_affords_at_their_price()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            fx.Give(100);
            var calendar = fx.Calendar(BuyControlKind.Cycle);
            calendar.Cycle();
            var row = calendar.Row(fx.OpenMic);

            var max = Purchasing.MaxAffordable(fx.Ctx, fx.OpenMicDraw);
            Assert.Greater(max, 1, "100 Cash covers several draws");
            Assert.AreEqual(max, row.ShownCount);
            AssertClose(Purchasing.CostOf(fx.OpenMicDraw, fx.Ctx, max).ToDouble(), row.ShownCost,
                "the price of all N, not of one");

            row.Buy();
            Assert.AreEqual(1 + max, fx.Owned(fx.OpenMicDraw));
        }

        // Nothing affordable: the button still names one, the row dims, and the
        // press is refused whole like every buy.
        [Test]
        public void Buy_Max_with_nothing_affordable_names_one_and_buys_nothing()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var calendar = fx.Calendar(BuyControlKind.Cycle);
            calendar.Cycle();
            var row = calendar.Row(fx.OpenMic);
            Assert.AreEqual(0, Purchasing.MaxAffordable(fx.Ctx, fx.OpenMicDraw), "the opening 5 Cash bought the first");

            Assert.AreEqual(1, row.ShownCount);
            Assert.IsTrue(row.Root.ClassListContains("row-unaffordable"));

            row.Buy();
            Assert.AreEqual(1, fx.Owned(fx.OpenMicDraw), "a press it cannot pay for buys nothing");
        }

        [Test]
        public void Buy_Next_names_the_levels_still_needed_to_the_next_threshold()
        {
            var fx = new Fixture();
            fx.Give(1000000);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 3);
            var calendar = fx.Calendar(BuyControlKind.Cycle);
            calendar.Cycle();
            calendar.Cycle();
            var row = calendar.Row(fx.OpenMic);

            var next = fx.OpenMicDraw.NextThreshold(fx.Ctx);
            Assert.IsNotNull(next);
            Assert.AreEqual(next.count - 3, row.ShownCount);
            AssertClose(Purchasing.CostOf(fx.OpenMicDraw, fx.Ctx, row.ShownCount).ToDouble(), row.ShownCost);
        }

        [Test]
        public void Buy_Next_buys_exactly_to_the_threshold()
        {
            var fx = new Fixture();
            fx.Give(1000000);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 3);
            var calendar = fx.Calendar(BuyControlKind.Cycle);
            calendar.Cycle();
            calendar.Cycle();
            var row = calendar.Row(fx.OpenMic);
            var first = fx.OpenMicDraw.NextThreshold(fx.Ctx);

            row.Buy();
            Assert.AreEqual(first.count, fx.Owned(fx.OpenMicDraw), "to the threshold and not one past it");

            calendar.Refresh();
            var second = fx.OpenMicDraw.NextThreshold(fx.Ctx);
            Assert.AreEqual(second.count - first.count, row.ShownCount, "and the next press aims at the one after");
        }

        [Test]
        public void Buy_Next_with_no_threshold_left_names_one()
        {
            var fx = new Fixture();
            fx.Tier2.generatorCounts[fx.OpenMicDraw.Id] = 100;
            var calendar = fx.Calendar(BuyControlKind.Cycle);
            calendar.Cycle();
            calendar.Cycle();

            Assert.IsNull(fx.OpenMicDraw.NextThreshold(fx.Ctx), "past every threshold");
            Assert.AreEqual(1, calendar.Row(fx.OpenMic).ShownCount);
        }

        // ---- the figures toggle ----

        // Every row's shown payouts are BarSystem.CompletionPayout's lines and
        // its rates are empty.
        private static void AssertPayouts(Fixture fx, LeveledBarRowUI row, BarDefinition bar)
        {
            var expected = BarSystem.CompletionPayout(fx.Ctx, bar);
            Assert.IsEmpty(row.ShownRates, $"{bar.Id}: Raw prints no rates");
            Assert.AreEqual(expected.Count, row.ShownPayouts.Count, $"{bar.Id}: one line per currency the gig pays");
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.AreSame(expected[i].currency, row.ShownPayouts[i].currency);
                Assert.AreEqual(expected[i].amount, row.ShownPayouts[i].amount, expected[i].currency.Id);
            }
        }

        // Every row's shown rates are BarSystem.CompletionRate's lines and its
        // payouts are empty.
        private static void AssertRates(Fixture fx, LeveledBarRowUI row, BarDefinition bar)
        {
            var expected = BarSystem.CompletionRate(fx.Ctx, bar, fx.GameSpeed);
            Assert.IsEmpty(row.ShownPayouts, $"{bar.Id}: Per Second prints no payouts");
            Assert.AreEqual(expected.Count, row.ShownRates.Count, $"{bar.Id}: one line per currency the gig pays");
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.AreSame(expected[i].currency, row.ShownRates[i].currency);
                Assert.AreEqual(expected[i].perSecond, row.ShownRates[i].perSecond, expected[i].currency.Id);
            }
        }

        // Two gigs booked and levelled, so every assertion covers more than
        // one row.
        private static Fixture TwoGigs()
        {
            var fx = new Fixture();
            fx.Give(1000);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 3);
            fx.Session.TryBuy(fx.Ctx, fx.HousePartyDraw, 2);
            return fx;
        }

        [Test]
        public void A_card_is_built_at_Per_Second_and_its_rows_show_rates()
        {
            var fx = TwoGigs();
            var calendar = fx.Calendar();

            Assert.AreEqual(FigureMode.PerSecond, calendar.Figures);
            foreach (var bar in new[] { fx.OpenMic, fx.HouseParty })
            {
                Assert.IsTrue(calendar.Row(bar).Visible, $"{bar.Id} is booked");
                AssertRates(fx, calendar.Row(bar), bar);
            }
        }

        [Test]
        public void The_toggle_switches_every_row_to_payouts_and_back_to_rates()
        {
            var fx = TwoGigs();
            var calendar = fx.Calendar();

            calendar.ToggleFigures();
            Assert.AreEqual(FigureMode.Raw, calendar.Figures);
            foreach (var bar in new[] { fx.OpenMic, fx.HouseParty })
                AssertPayouts(fx, calendar.Row(bar), bar);
            Assert.Greater(calendar.Row(fx.OpenMic).ShownPayouts[0].amount.ToDouble(), 0,
                "three draws pay something per completion");

            calendar.ToggleFigures();
            Assert.AreEqual(FigureMode.PerSecond, calendar.Figures);
            foreach (var bar in new[] { fx.OpenMic, fx.HouseParty })
                AssertRates(fx, calendar.Row(bar), bar);
        }

        [Test]
        public void The_figures_survive_a_refresh()
        {
            var fx = TwoGigs();
            var calendar = fx.Calendar();
            calendar.ToggleFigures();

            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            calendar.Refresh();

            Assert.AreEqual(FigureMode.Raw, calendar.Figures);
            foreach (var bar in new[] { fx.OpenMic, fx.HouseParty })
                AssertPayouts(fx, calendar.Row(bar), bar);
        }

        // The toggle is the first of the top line's two buttons, shown on a
        // Pair card and a Cycle card alike; only a Cycle card shows the
        // cycle beside it. The toggle and the cycle hold separate modes.
        [TestCase(BuyControlKind.Pair)]
        [TestCase(BuyControlKind.Cycle)]
        public void The_toggle_stands_at_the_top_left_of_every_card(BuyControlKind control)
        {
            var fx = TwoGigs();
            var calendar = fx.Calendar(control);
            var top = calendar.Root.Q<VisualElement>("top");
            var toggle = calendar.Root.Q<Button>("figures");
            var cycle = calendar.Root.Q<Button>("cycle");

            Assert.IsNotNull(toggle);
            Assert.AreSame(top, toggle.parent);
            Assert.AreEqual(0, top.IndexOf(toggle), "the toggle is the top line's first button");
            Assert.AreEqual(1, top.IndexOf(cycle), "the cycle follows it");
            Assert.AreNotEqual(DisplayStyle.None, toggle.style.display.value, "every card shows the toggle");
            Assert.AreEqual(control == BuyControlKind.Cycle ? DisplayStyle.Flex : DisplayStyle.None,
                cycle.style.display.value, "only a Cycle card shows the cycle");

            calendar.ToggleFigures();
            Assert.AreEqual(FigureMode.Raw, calendar.Figures);
            Assert.AreEqual(BuyMode.One, calendar.Mode, "the toggle leaves the buy mode alone");
            AssertPayouts(fx, calendar.Row(fx.OpenMic), fx.OpenMic);

            calendar.Cycle();
            Assert.AreEqual(FigureMode.Raw, calendar.Figures, "and the cycle leaves the figures alone");
        }

        // ---- threshold crossings ----

        // What was already reached when the row is built is not news.
        [Test]
        public void A_rows_first_refresh_raises_nothing()
        {
            var fx = new Fixture();
            fx.Give(1000000);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 12);

            fx.Calendar();

            Assert.IsEmpty(fx.Toasts.Raised);
        }

        [Test]
        public void A_purchase_across_thresholds_raises_each_effect_in_order()
        {
            var fx = new Fixture();
            fx.Give(1000000);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var calendar = fx.Calendar();

            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 24);
            calendar.Refresh();

            var expected = fx.OpenMicDraw.ThresholdsCrossed(1, 25).SelectMany(t => t.effects).ToArray();
            Assert.Greater(expected.Length, 1, "chapter 2 authors more than one effect up to 25");
            CollectionAssert.AreEqual(expected, fx.Toasts.Raised);
        }

        // Any cause moves the count, so any cause raises it: a grant here.
        [Test]
        public void A_granted_count_crossing_a_threshold_raises_it()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var calendar = fx.Calendar();
            var first = fx.OpenMicDraw.thresholds[0];

            fx.Tier2.grantedCounts[fx.OpenMicDraw.Id] = first.count - 1;
            calendar.Refresh();

            CollectionAssert.AreEqual(first.effects, fx.Toasts.Raised);
        }

        // A reset lowering the count raises nothing, and the lowered count is
        // what the next crossing is measured from.
        [Test]
        public void A_lowered_count_raises_nothing_and_is_measured_from()
        {
            var fx = new Fixture();
            fx.Give(1000000);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 12);
            var calendar = fx.Calendar();
            var first = fx.OpenMicDraw.thresholds[0];

            fx.Tier2.generatorCounts[fx.OpenMicDraw.Id] = 1;
            calendar.Refresh();
            Assert.IsEmpty(fx.Toasts.Raised, "a fall is not a crossing");

            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, first.count - 1);
            calendar.Refresh();
            CollectionAssert.AreEqual(first.effects, fx.Toasts.Raised, "climbing back is");
        }

        // ---- the toast ----

        // The host's toast: nothing on the first render, then the first
        // crossing's effect shown and the rest queued behind it in order.
        [Test]
        public void The_host_shows_the_first_crossed_effect_and_queues_the_rest()
        {
            var fx = new Fixture();
            fx.Give(1000000);
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var host = fx.Host();
            var toast = host.Toast;
            Assert.IsNull(toast.Shown, "the first render raises nothing");
            Assert.IsEmpty(toast.Queued);

            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 24);

            var raised = fx.OpenMicDraw.ThresholdsCrossed(1, 25).SelectMany(t => t.effects).ToList();
            Assert.AreSame(raised[0], toast.Shown);
            CollectionAssert.AreEqual(raised.Skip(1).ToArray(), toast.Queued);

            toast.Advance();
            Assert.AreSame(raised[1], toast.Shown, "one after another");
            for (var i = 2; i < raised.Count; i++)
                toast.Advance();
            toast.Advance();
            Assert.IsNull(toast.Shown, "the queue runs dry");
            Assert.IsEmpty(toast.Queued);
        }

        [Test]
        public void The_chapter_2_calendar_is_built_with_the_cycle()
        {
            var fx = new Fixture();
            fx.Session.TryBuy(fx.Ctx, fx.OpenMicDraw, 1);
            var calendar = Fixture.Calendar(fx.Host());

            Assert.AreEqual(BuyControlKind.Cycle, calendar.BuyControl, "the module's authored control");
            Assert.AreEqual(BuyControlKind.Cycle, calendar.Row(fx.OpenMic).ControlKind);
            Assert.AreEqual(BuyMode.One, calendar.Mode);
        }
    }
}
