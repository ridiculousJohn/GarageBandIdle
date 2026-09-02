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

            public Fixture()
            {
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
                var widget = ModuleWidgetFactory.Create(prefabId, asset.Instantiate());
                widget.Bind(Session, Tree.Tier1, content, Clock);
                return widget;
            }

            // The selection through the command, so the fact is written the way
            // the game writes it. It is a NON-tick transaction, so every row
            // does this before the tick it measures.
            public void SelectCover1() =>
                Assert.IsTrue(Session.SetActiveBars(Ctx(0), Tree.LearnCovers, new[] { Tree.Cover1 }),
                    "cover_1 was selected");
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

            Assert.IsTrue(fx.Session.FireProducer(fx.Ctx(10), fx.Tree.TapProducer), "the tap fired");
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
