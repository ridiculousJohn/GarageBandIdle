using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // What a widget SHOWS between refreshes (design doc 12.11): truth at the
    // snap, the tick's realized slope until the next one, and the clamps that
    // keep an extrapolation from drawing a state the game never reaches. The
    // widgets are built from the shipping UXML and bound by hand, so no host is
    // present - this suite calls Refresh and Interpolate itself and drives the
    // clock, which is what lets a frame land at an exact game time.
    public class WidgetInterpolationTests
    {
        // The one thing a story row asks of the host (12.11), answered by
        // nothing: no host is present here, and no row in this suite is a story
        // row - the factory takes the opener because only StoryRowUI reads it.
        private sealed class NoStories : IStoryOpener
        {
            public static readonly NoStories Instance = new();

            public void OpenStory(Story.StoryBeatDefinition beat, ScopeState scope) { }
        }

        // The one thing a generator row asks of the host (12.11), answered by
        // nothing for the same reason: the long press is panel time, and no row
        // in this suite is pressed at all.
        private sealed class NoInfos : IGeneratorInfoOpener
        {
            public static readonly NoInfos Instance = new();

            public void OpenGeneratorInfo(Economy.GeneratorDefinition generator, ScopeState scope) { }
        }

        // Computed amounts within tolerance, never bit-exact, for the reason
        // SessionPacingTests gives: BigDouble's base-10 mantissa is
        // binary-inexact. Label strings are compared exactly - a display string
        // is the thing under test.
        private static void AssertClose(double expected, double actual, string what = null) =>
            Assert.AreEqual(expected, actual,
                Math.Max(1e-9, Math.Abs(expected) * 1e-12), what ?? string.Empty);

        // A live session over the standing tree, plus the clock the widgets
        // read. The stamp at Now makes the entry skip the idle offer.
        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly GameSession Session;
            public readonly GameClock Clock;

            // `author` runs against the DEFINITIONS before the rebuild, because
            // the gather is compiled when the tree is built: a produces entry
            // authored afterward sits on no plan, and the session holds the tree
            // that pass ran on.
            public Fixture(Action<TestTree> author = null)
            {
                if (author != null)
                {
                    author(Tree);
                    Tree.Rebuild();
                }
                Tree.Ch1.lastActiveUtc = Tree.Now;
                Session = new GameSession(Tree.Root, Config());
                Session.SwitchChapter(Tree.Ch1, Tree.Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
                Clock = new GameClock(Tree.Now);
            }

            private static GameConfig Config()
            {
                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.tickIntervalSeconds = 1;
                return config;
            }

            public DateTime At(double seconds) => Tree.Now.AddSeconds(seconds);
            public GameContext Ctx(double seconds) => new GameContext(Tree.Tier1, At(seconds));

            // A widget as the host builds one: the shipping UXML instantiated,
            // the factory's controller over it, bound at tier1 by hand.
            public ModuleWidget Widget(string prefabId, string uxml, Definition content)
            {
                var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Widgets/" + uxml);
                Assert.IsNotNull(asset, "Assets/UI/Widgets/" + uxml + " is missing");
                var widget = ModuleWidgetFactory.Create(prefabId, asset.Instantiate(),
                    NoStories.Instance, NoInfos.Instance);
                widget.Bind(Session, Tree.Tier1, content, Clock);
                return widget;
            }

            // The selection through the command, so the fact is written the way
            // the game writes it. It is a NON-tick transaction, so every row
            // does this before the tick it measures.
            public void SelectCover1()
            {
                Session.SetActiveBars(Ctx(0), Tree.LearnCovers, new[] { Tree.Cover1 });
                Assert.IsTrue(Tree.Tier1.activeBars[Tree.LearnCovers.Id].Contains(Tree.Cover1.Id),
                    "cover_1 was selected");
            }
        }

        // A click is panel time: UI Toolkit dispatches an event through the
        // panel an element is attached to, and the widgets here are built off
        // one. The shipping PanelSettings under a UIDocument is the smallest
        // real panel an EditMode test can stand up, so a row's own handler runs
        // rather than a stand-in for it.
        private sealed class PanelHost : IDisposable
        {
            private readonly GameObject host;

            public PanelHost(VisualElement content)
            {
                var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/PanelSettings.asset");
                Assert.IsNotNull(settings, "Assets/Settings/PanelSettings.asset is missing");
                host = new GameObject("panel_host");
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                Assert.IsNotNull(document.rootVisualElement, "the document built no panel to dispatch through");
                document.rootVisualElement.Add(content);
            }

            public void Dispose() => UnityEngine.Object.DestroyImmediate(host);
        }

        // The press itself: a click carries the element it landed on, and the
        // row's handler reads that target to tell a tap from a selection.
        private static void Click(VisualElement target)
        {
            using var click = ClickEvent.GetPooled();
            click.target = target;
            target.SendEvent(click);
        }

        // A pool drained faster than it fills: the negative slope is honest
        // motion and the display follows it down, but a balance is never
        // negative, so the extrapolation stops at zero instead of drawing a debt.
        [Test]
        public void ADrainingPoolInterpolatesDownAndStopsAtZero()
        {
            var fx = new Fixture();
            fx.Tree.Tier1.flags.Add("rehearsal_revealed");
            fx.SelectCover1();
            fx.Tree.Tier1.balances["rehearsal"] = 10;

            // 0.5/s produced against the cover's 2/s draw: truth 8.5, slope -1.5/s.
            fx.Session.Tick(1, fx.At(1));

            var widget = fx.Widget("currency_line", "CurrencyLine.uxml", fx.Tree.Rehearsal);
            var value = widget.Root.Q<Label>("value");
            widget.Refresh();
            Assert.AreEqual("8.50", value.text);

            fx.Clock.Frame(fx.At(1), 1);
            widget.Interpolate();
            Assert.AreEqual("7.00", value.text);

            fx.Clock.Frame(fx.At(10), 9);
            widget.Interpolate();
            Assert.AreEqual("0.00", value.text, "ten seconds of that slope would read -6.5");
        }

        // A bar that overfilled in the tick that completed it: progress is
        // uncapped state and the row's picture is not, so the display holds at
        // the fill amount rather than drawing past a full bar.
        [Test]
        public void ACompletedBarNeverDrawsPastItsFillAmount()
        {
            var fx = new Fixture();
            fx.SelectCover1();
            fx.Tree.Tier1.barProgress["cover_1"] = 99;
            fx.Tree.Tier1.balances["rehearsal"] = 100;

            // The 2/s draw carries the progress to 101 and settles the crossing.
            fx.Session.Tick(1, fx.At(1));
            AssertClose(101, fx.Tree.Tier1.barProgress["cover_1"].ToDouble(), "progress is uncapped");

            var widget = fx.Widget("bar_group", "BarGroup.uxml", null);
            widget.Refresh();
            var row = widget.Root.Q<VisualElement>(className: "bar-row");
            var progress = row.Q<Label>(className: "bar-progress");
            var fill = row.Q<ProgressBar>(className: "bar-fill");
            Assert.AreEqual("100.00 / 100.00", progress.text);
            AssertClose(100, fill.value);

            fx.Clock.Frame(fx.At(2), 1);
            widget.Interpolate();
            Assert.AreEqual("100.00 / 100.00", progress.text, "a second of the 2/s slope changes nothing");
            AssertClose(100, fill.value);
        }

        // The frame that refreshed shows exactly truth: the stamp is the game
        // time the snap read, so the elapsed term is zero by construction and
        // no rounding stands between the two.
        [Test]
        public void TheFrameThatRefreshedShowsTruthExactly()
        {
            var fx = new Fixture();
            fx.Tree.Tier1.generatorCounts["practice_amp"] = 1;
            fx.Session.Tick(10, fx.At(10));
            fx.Clock.Frame(fx.At(10), 10);

            var widget = fx.Widget("currency_line", "CurrencyLine.uxml", fx.Tree.Cash);
            var value = widget.Root.Q<Label>("value");
            widget.Refresh();
            Assert.AreEqual("5.00", value.text, "ten seconds at 0.5/s");

            widget.Interpolate();
            Assert.AreEqual("5.00", value.text);
        }

        // The row's count follows the granted slope the last tick realized,
        // the rule a currency readout follows (12.11): truth at the snap, the
        // slope between, and the purchased half standing still.
        [Test]
        public void TheCountLabelFollowsTheGrantedSlopeBetweenTicks()
        {
            var fx = new Fixture(tree =>
            {
                var crew = TestTree.MakeDefinition<Economy.ProducerDefinition>("road_crew");
                crew.produces.Add(TestTree.Entry(tree.PracticeAmp, Economy.Stat.Rate, 0.25));
                tree.Tier1Def.producers.Add(crew);
            });
            fx.Tree.Tier1.generatorCounts["practice_amp"] = 3;

            // Ten seconds at 0.25/s: truth 2.5 granted, slope 0.25/s.
            fx.Session.Tick(10, fx.At(10));
            fx.Clock.Frame(fx.At(10), 10);

            var widget = fx.Widget("generator_row", "GeneratorRow.uxml", fx.Tree.PracticeAmp);
            var count = widget.Root.Q<Label>("count");
            widget.Refresh();
            Assert.AreEqual("(3+2.50)", count.text);

            fx.Clock.Frame(fx.At(12), 2);
            widget.Interpolate();
            Assert.AreEqual("(3+3.00)", count.text, "two seconds of the slope, the purchased three unmoved");
        }

        // The preview names the TARGET of each yield line whatever kind it is
        // (12.11): a line paying a generator's count reads that generator's
        // name, through the same resolution the firing deposits.
        [Test]
        public void TheJamPreviewNamesTheTargetOfEveryYieldLine()
        {
            var fx = new Fixture(tree =>
                tree.TapProducer.produces.Add(TestTree.Entry(tree.PracticeAmp, Economy.Stat.Yield, 2)));

            var widget = fx.Widget("jam_button", "JamButton.uxml", fx.Tree.TapProducer);
            widget.Refresh();

            // The rehearsal lines sit behind their reveal and pay nothing, and a
            // line paying nothing is not a line.
            Assert.AreEqual("+1.00 cash, +2.00 practice_amp",
                widget.Root.Q<Label>("yield").text);
        }

        // The row is the tap target when the bar names a producer (12.11): the
        // press issues the same command the Jam button does, and the producer's
        // yield entry naming the bar is how a tap adds time.
        [Test]
        public void ATapOnTheRowFiresTheBarsProducerAndTheSelectButtonDoesNot()
        {
            var fx = new Fixture(tree =>
            {
                var roadie = TestTree.MakeDefinition<Economy.ProducerDefinition>("roadie");
                roadie.produces.Add(TestTree.Entry(tree.Cover1, Economy.Stat.Yield, 25));
                tree.Tier1Def.producers.Add(roadie);
                tree.Cover1.tap = roadie;
            });

            var widget = fx.Widget("bar_group", "BarGroup.uxml", null);
            widget.Refresh();
            var row = widget.Root.Q<VisualElement>(className: "bar-row");
            Assert.IsTrue(row.ClassListContains("bar-tappable"), "the bar names a producer");
            using var panel = new PanelHost(widget.Root);

            Click(row.Q<Label>(className: "bar-name"));
            AssertClose(25, fx.Tree.Tier1.barProgress["cover_1"].ToDouble(), "one firing of the tap producer");

            // Choosing stays the button's and paying is the row's, so the
            // button's own press is never also a tap.
            Click(row.Q<Button>(className: "bar-select"));
            AssertClose(25, fx.Tree.Tier1.barProgress["cover_1"].ToDouble(), "the select button paid nothing in");
        }

        [Test]
        public void ARowWhoseBarNamesNoProducerIgnoresAClick()
        {
            var fx = new Fixture();

            var widget = fx.Widget("bar_group", "BarGroup.uxml", null);
            widget.Refresh();
            var row = widget.Root.Q<VisualElement>(className: "bar-row");
            Assert.IsFalse(row.ClassListContains("bar-tappable"), "cover_1 names no producer");
            using var panel = new PanelHost(widget.Root);

            Click(row.Q<Label>(className: "bar-name"));

            Assert.IsFalse(fx.Tree.Tier1.barProgress.ContainsKey("cover_1"), "a row with no tap is not a tap target");
        }

        // A row reads completion off the repeat condition (12.7): the bar that
        // fills once and stays full is finished, and one that goes again is
        // between fills however full it stands.
        [Test]
        public void OnlyABarThatCompletesOnceEverPrintsDone()
        {
            var fx = new Fixture(tree => tree.Cover2.repeatWhen = new Always());
            fx.Tree.Tier1.barProgress["cover_1"] = 100;
            fx.Tree.Tier1.barProgress["cover_2"] = 300;

            var widget = fx.Widget("bar_group", "BarGroup.uxml", null);
            widget.Refresh();
            var rows = widget.Root.Query<VisualElement>(className: "bar-row").ToList();
            var once = rows[0].Q<Button>(className: "bar-select");
            var loop = rows[1].Q<Button>(className: "bar-select");

            Assert.AreEqual("Done", once.text, "cover_1 fills once and stays full");
            Assert.IsFalse(once.enabledSelf, "and nothing reselects it");
            Assert.AreEqual("Select", loop.text, "cover_2 goes again, so full is between fills");
            Assert.IsTrue(loop.enabledSelf);
        }

        // A command leaves the tick's report standing: the tap's yield snaps in
        // and the display keeps counting at the slope the amp earned, instead
        // of freezing until the next tick. The tick is the report's only writer.
        [Test]
        public void ACommandSnapsItsYieldInAndTheDisplayKeepsCounting()
        {
            var fx = new Fixture();
            fx.Tree.Tier1.generatorCounts["practice_amp"] = 1;
            fx.Session.Tick(10, fx.At(10));
            var report = fx.Session.LastTick;
            Assert.IsNotNull(report, "the tick measured a slope");

            fx.Session.FireProducer(fx.Ctx(10), fx.Tree.TapProducer);
            Assert.AreSame(report, fx.Session.LastTick, "the tap touched nothing the tick owns");

            var widget = fx.Widget("currency_line", "CurrencyLine.uxml", fx.Tree.Cash);
            var value = widget.Root.Q<Label>("value");
            widget.Refresh();
            Assert.AreEqual("6.00", value.text, "five from the tick plus the tap's yield");

            fx.Clock.Frame(fx.At(14), 4);
            widget.Interpolate();
            Assert.AreEqual("8.00", value.text, "four seconds at the amp's 0.5/s, on top of the tap");
        }
    }
}
