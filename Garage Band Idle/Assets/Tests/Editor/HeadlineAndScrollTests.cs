using System;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.UI;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The arithmetic behind the page's mouse drag and its pinned headline, and
    // the headline's following of a currency line. Edit mode has no panel and
    // dispatches no pointer, so the drag and the collapse are read through the
    // static functions their behaviours call, and the following is driven on a
    // hand-built tree that needs no content at all.
    public class HeadlineAndScrollTests
    {
        private const float Tolerance = 1e-5f;

        private const string PinnedSource = "currency-pinned-source";

        // ---- the drag (DragScrollManipulator) ----

        // A press is a press until the pointer has travelled the start
        // distance, in either direction; from there on it is a drag.
        [Test]
        public void ADragStartsAtTheStartDistanceInEitherDirection()
        {
            Assert.IsFalse(DragScrollManipulator.Starts(0f), "no travel");
            Assert.IsFalse(DragScrollManipulator.Starts(23.9f), "just short, downward");
            Assert.IsFalse(DragScrollManipulator.Starts(-23.9f), "just short, upward");
            Assert.IsTrue(DragScrollManipulator.Starts(24f), "the start distance, downward");
            Assert.IsTrue(DragScrollManipulator.Starts(-24f), "the start distance, upward");
            Assert.IsTrue(DragScrollManipulator.Starts(300f), "well past it");
        }

        // The page follows the pointer: dragging down pulls the page down,
        // which is scrolling back toward the top, and the offset never leaves
        // the range the content allows - a page with nothing to scroll stays
        // at zero.
        [TestCase(100f, 0f, 500f, 100f)]
        [TestCase(100f, -40f, 500f, 140f)]
        [TestCase(100f, 40f, 500f, 60f)]
        [TestCase(100f, 250f, 500f, 0f)]
        [TestCase(100f, -900f, 500f, 500f)]
        [TestCase(100f, -40f, 0f, 0f)]
        [TestCase(100f, -40f, -5f, 0f)]
        public void TheDragOffsetFollowsThePointerWithinTheScrollRange(
            float startOffset, float dragDeltaY, float maxOffset, float expected)
        {
            Assert.AreEqual(expected, DragScrollManipulator.Offset(startOffset, dragDeltaY, maxOffset), Tolerance);
        }

        // ---- the pinned headline's arithmetic (PinnedHeadlineUI) ----

        // How far the line has travelled from its rest position to the pin
        // line, as a fraction: zero at rest or below it, one at the pin line
        // or past it, and one for a line that already rests at or above the
        // pin line.
        [TestCase(400f, 100f, 400f, 0f)]
        [TestCase(400f, 100f, 250f, 0.5f)]
        [TestCase(400f, 100f, 100f, 1f)]
        [TestCase(400f, 100f, 40f, 1f)]
        [TestCase(400f, 100f, 460f, 0f)]
        [TestCase(100f, 100f, 100f, 1f)]
        [TestCase(80f, 100f, 80f, 1f)]
        public void TheCollapseIsTheShareOfTheWayFromRestToThePinLine(
            float restTop, float pinTop, float sourceTop, float expected)
        {
            Assert.AreEqual(expected, PinnedHeadlineUI.Collapse(restTop, pinTop, sourceTop), Tolerance);
        }

        // The headline sits over its line until the line passes the pin line,
        // and holds at the pin line from there.
        [TestCase(100f, 400f, 400f)]
        [TestCase(100f, 100f, 100f)]
        [TestCase(100f, 40f, 100f)]
        public void TheHeadlineSitsOnItsLineAndStopsAtThePinLine(float pinTop, float sourceTop, float expected)
        {
            Assert.AreEqual(expected, PinnedHeadlineUI.Top(pinTop, sourceTop), Tolerance);
        }

        [Test]
        public void ThePinnedScaleShrinksTheLine()
        {
            Assert.Greater(PinnedHeadlineUI.PinnedScale, 0f, "a pinned line is still drawn");
            Assert.Less(PinnedHeadlineUI.PinnedScale, 1f, "a pinned line is smaller than at rest");
        }

        // ---- the headline's following, on a hand-built tree ----

        // A currency line's shape as CurrencyLine.uxml has it: three Labels
        // named name, value and rate.
        private static VisualElement Line(string name, string value, string rate)
        {
            var line = new VisualElement();
            line.Add(new Label(name) { name = "name" });
            line.Add(new Label(value) { name = "value" });
            line.Add(new Label(rate) { name = "rate" });
            return line;
        }

        // The headline's element with the names Screen.uxml gives it, beside a
        // stand-in top bar and the page's ScrollView under one screen root.
        // The constructor is the one place this file depends on the
        // headline's construction.
        private static PinnedHeadlineUI Headline()
        {
            var screen = new VisualElement();
            var topBar = new VisualElement { name = "top-bar" };
            var scroll = new ScrollView { name = "screen" };
            var headline = new VisualElement { name = "headline" };
            headline.AddToClassList("headline");
            headline.style.display = DisplayStyle.None;
            headline.Add(new VisualElement { name = "headline-backdrop" });
            var line = new VisualElement { name = "headline-line" };
            line.Add(new Label { name = "headline-name" });
            line.Add(new Label { name = "headline-value" });
            line.Add(new Label { name = "headline-rate" });
            headline.Add(line);
            screen.Add(topBar);
            screen.Add(scroll);
            screen.Add(headline);
            return new PinnedHeadlineUI(headline, topBar, scroll);
        }

        private static int PinnedCount(VisualElement element) =>
            element.GetClasses().Count(c => c == PinnedSource);

        // The followed line carries the class once however often it is
        // followed, and a new source takes it from the old one; following
        // nothing takes it away and the headline down.
        [Test]
        public void FollowingMovesTheSourceClassAndFollowingNothingHidesTheHeadline()
        {
            var headline = Headline();
            var a = Line("Cash", "1.00", "(0.00/s)");
            var b = Line("Fans", "2.00", "(0.50/s)");

            headline.Follow(a);
            headline.Follow(a);
            Assert.AreSame(a, headline.Source);
            Assert.AreEqual(1, PinnedCount(a), "a source followed twice carries the class once");

            headline.Follow(b);
            Assert.AreSame(b, headline.Source);
            Assert.AreEqual(0, PinnedCount(a), "the line followed before gave the class up");
            Assert.AreEqual(1, PinnedCount(b));

            headline.Follow(null);
            Assert.IsNull(headline.Source);
            Assert.AreEqual(0, PinnedCount(b), "following nothing takes the class away");
            Assert.AreEqual(DisplayStyle.None, headline.Root.style.display.value, "and takes the headline down");
        }

        // The headline mirrors the line it follows: its three labels carry the
        // source's three texts, and it is shown. With no panel there is no
        // layout to place it by, and that is not a failure.
        [Test]
        public void InterpolateCopiesTheSourcesThreeTextsAndShowsTheHeadline()
        {
            var headline = Headline();
            var a = Line("Cash", "1.00", "(0.00/s)");
            var b = Line("Fans", "2.00", "(0.50/s)");

            headline.Follow(a);
            Assert.DoesNotThrow(() => headline.Interpolate());
            Assert.AreEqual("Cash", headline.Root.Q<Label>("headline-name").text);
            Assert.AreEqual("1.00", headline.Root.Q<Label>("headline-value").text);
            Assert.AreEqual("(0.00/s)", headline.Root.Q<Label>("headline-rate").text);
            Assert.AreEqual(DisplayStyle.Flex, headline.Root.style.display.value);

            headline.Follow(b);
            headline.Interpolate();
            Assert.AreEqual("Fans", headline.Root.Q<Label>("headline-name").text, "the texts follow the source");
            Assert.AreEqual("2.00", headline.Root.Q<Label>("headline-value").text);
            Assert.AreEqual("(0.50/s)", headline.Root.Q<Label>("headline-rate").text);
        }

        // A source without the three labels a currency line promises is not a
        // currency line, so it is refused where it is followed.
        [Test]
        public void ASourceMissingALabelIsRefused()
        {
            var headline = Headline();
            var partial = new VisualElement();
            partial.Add(new Label("Cash") { name = "name" });
            partial.Add(new Label("1.00") { name = "value" });

            Assert.Throws<InvalidOperationException>(() => headline.Follow(partial));
        }
    }
}
