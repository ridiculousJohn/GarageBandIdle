using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The two pointer gestures (design doc 12.11), the drag that scrolls a view
    // and the hold, driven through a real panel: the shipping PanelSettings
    // under a UIDocument, real pointer events sent to the panel's root so the
    // dispatch picks the element under the pointer and the capture rules
    // apply, the real manipulators and real Buttons. Positions are panel
    // coordinates read off the laid-out elements. Layout and the scheduler run
    // on panel updates, so every row is a UnityTest that yields frames, and a
    // hold waits real time against the real config's threshold. A row proving
    // a hold does NOT fire ends with a still press on the same text that does,
    // so it could have failed.
    public class PointerGestureTests
    {
        // A drag of this many pixels is well past the start distance and well
        // inside every view's scroll range here.
        private const float Travel = 100f;

        // The steps a drag moves in, so the first moves are short of the start
        // distance the way a hand's are.
        private const int Steps = 10;

        private GameObject host;

        // Set when the editor has dispatched the hierarchy change that creating
        // the host raises (see Settle).
        private bool hierarchyChanged;

        private void OnHierarchyChanged() => hierarchyChanged = true;

        [TearDown]
        public void TearDown()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            if (host != null)
                UnityEngine.Object.DestroyImmediate(host);
            host = null;
        }

        // The hold's threshold, read from a GameConfig as the game builds one.
        private static double LongPressSeconds()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            var seconds = config.longPressSeconds;
            UnityEngine.Object.DestroyImmediate(config);
            return seconds;
        }

        // The smallest real panel an EditMode test can stand up: the shipping
        // PanelSettings under a UIDocument, the same arrangement
        // WidgetInterpolationTests uses.
        private IPanel StandUp(VisualElement content)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/PanelSettings.asset");
            Assert.IsNotNull(settings, "Assets/Settings/PanelSettings.asset is missing");
            hierarchyChanged = false;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            host = new GameObject("pointer_gesture_panel");
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            Assert.IsNotNull(document.rootVisualElement, "the document built no panel to dispatch through");
            document.rootVisualElement.Add(content);
            Assert.IsNotNull(content.panel, "the content is on no panel");
            return content.panel;
        }

        // The engine's own update of every runtime panel: layout, and the
        // scheduler a hold fires on. Play mode runs it from the player loop;
        // the editor outside play mode never does, for a UIDocument's panel
        // (an item scheduled on one stays pending across editor updates, and
        // queueing a player loop update does not run it). It is internal, so
        // it is reached by name, and every frame here runs it once.
        private static readonly MethodInfo UpdatePanels = typeof(PanelSettings).Assembly
            .GetType("UnityEngine.UIElements.UIElementsRuntimeUtility")
            ?.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);

        private static void UpdatePanel()
        {
            Assert.IsNotNull(UpdatePanels, "UIElementsRuntimeUtility.UpdatePanels is not in this Unity");
            UpdatePanels.Invoke(null, null);
        }

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                UpdatePanel();
                yield return null;
            }
        }

        private static IEnumerator Seconds(double seconds)
        {
            var until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until)
            {
                UpdatePanel();
                yield return null;
            }
            UpdatePanel();
        }

        // First the editor's reaction to the host's creation: it dispatches the
        // hierarchy change on a later editor tick, and the UIDocument answers
        // it in that same dispatch by removing its root from the panel and
        // adding it back, which detaches the whole page and so disarms any
        // hold armed at that moment. The editor runner's ticks fall inside a
        // few frames; a batchmode run's are spaced in real time, so the wait
        // is on the dispatch itself. Then frames for layout, then a pick,
        // which validates the layout itself, then a frame for what the layout
        // dispatched.
        private IEnumerator Settle(IPanel panel)
        {
            var until = EditorApplication.timeSinceStartup + 5;
            while (!hierarchyChanged)
            {
                Assert.Less(EditorApplication.timeSinceStartup, until,
                    "the editor never dispatched the hierarchy change the host's creation raises");
                UpdatePanel();
                yield return null;
            }
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            yield return Frames(3);
            panel.Pick(Vector2.zero);
            yield return Frames(2);
        }

        // ---- the pointer ----

        private static Event Mouse(EventType type, Vector2 position) => new Event
        {
            type = type,
            mousePosition = position,
            button = 0,
            clickCount = type == EventType.MouseDown ? 1 : 0,
        };

        private static void Send(IPanel panel, EventBase evt)
        {
            using (evt)
                panel.visualTree.SendEvent(evt);
        }

        private static void Down(IPanel panel, Vector2 position) =>
            Send(panel, PointerDownEvent.GetPooled(Mouse(EventType.MouseDown, position)));

        private static void Up(IPanel panel, Vector2 position) =>
            Send(panel, PointerUpEvent.GetPooled(Mouse(EventType.MouseUp, position)));

        // A move with the left button held.
        private static void DragTo(IPanel panel, Vector2 position) =>
            Send(panel, PointerMoveEvent.GetPooled(Mouse(EventType.MouseDrag, position)));

        // A move with no button held.
        private static void HoverTo(IPanel panel, Vector2 position) =>
            Send(panel, PointerMoveEvent.GetPooled(Mouse(EventType.MouseMove, position)));

        // A release the device registers and the panel never hears: the up is
        // built, which is where the device state lets go of the button, and
        // never sent. That is the lost release a native control can cause
        // (Unity's UUM-138133).
        private static void LoseRelease(Vector2 position)
        {
            using var lost = PointerUpEvent.GetPooled(Mouse(EventType.MouseUp, position));
        }

        // A drag from `from` by `by` in Steps moves, the button held, ending
        // at from + by. Returns the end.
        private static Vector2 Drag(IPanel panel, Vector2 from, Vector2 by)
        {
            for (var i = 1; i <= Steps; i++)
                DragTo(panel, from + by * i / Steps);
            return from + by;
        }

        private static bool Inside(VisualElement element, VisualElement picked) =>
            picked != null && (picked == element || element.Contains(picked));

        // ---- the page ----

        // A page as the screen has one: a vertical ScrollView with content
        // taller than it and the page's drag, holding a button, a hold target
        // made of two labels (a row's text), a button that is also a hold
        // target (Auto), and a sideways strip of button tiles with the strip's
        // own drag. Every size is explicit, so the geometry is the test's.
        private sealed class Page
        {
            public readonly ScrollView View;
            public readonly Button Buy;
            public readonly VisualElement Text;
            public readonly Label TextLeft;
            public readonly Label TextRight;
            public readonly Button Auto;
            public readonly ScrollView Strip;
            public readonly Button Tile;

            public int BuyClicks;
            public int AutoClicks;
            public int TileClicks;
            public int TextHolds;
            public int AutoHolds;

            public Page(double holdSeconds)
            {
                View = new ScrollView(ScrollViewMode.Vertical);
                View.style.position = Position.Absolute;
                View.style.left = 0;
                View.style.top = 0;
                View.style.width = 400;
                View.style.height = 800;
                View.AddManipulator(new DragScrollManipulator());

                View.Add(Block(new VisualElement(), 200));

                Buy = (Button)Block(new Button(), 100);
                Buy.clicked += () => BuyClicks++;
                View.Add(Buy);

                Text = Block(new VisualElement(), 100);
                Text.style.flexDirection = FlexDirection.Row;
                TextLeft = new Label("left");
                TextRight = new Label("right");
                foreach (var label in new[] { TextLeft, TextRight })
                {
                    label.style.width = 150;
                    label.style.height = 100;
                    label.style.marginLeft = 0;
                    label.style.marginRight = 0;
                    label.style.flexShrink = 0;
                    Text.Add(label);
                }
                Text.AddManipulator(new LongPressManipulator(() => TextHolds++, holdSeconds));
                View.Add(Text);

                Auto = (Button)Block(new Button(), 100);
                Auto.clicked += () => AutoClicks++;
                Auto.AddManipulator(new LongPressManipulator(() => AutoHolds++, holdSeconds));
                View.Add(Auto);

                Strip = new ScrollView(ScrollViewMode.Horizontal);
                Block(Strip, 120);
                Strip.AddManipulator(new DragScrollManipulator(ScrollViewMode.Horizontal));
                for (var i = 0; i < 10; i++)
                {
                    var tile = new Button();
                    tile.style.width = 150;
                    tile.style.height = 100;
                    tile.style.flexShrink = 0;
                    Strip.Add(tile);
                    if (i == 0)
                        Tile = tile;
                }
                Tile.clicked += () => TileClicks++;
                View.Add(Strip);

                View.Add(Block(new VisualElement(), 1500));
            }

            private static VisualElement Block(VisualElement element, float height)
            {
                element.style.height = height;
                element.style.flexShrink = 0;
                element.style.marginTop = 0;
                element.style.marginBottom = 0;
                return element;
            }
        }

        // The page stood up and laid out, with the ranges every row relies on
        // checked first.
        private IEnumerator StandUpPage(Page page)
        {
            var panel = StandUp(page.View);
            yield return Settle(panel);
            Assert.GreaterOrEqual(page.View.verticalScroller.highValue, Travel, "the page scrolls further than a drag");
            Assert.GreaterOrEqual(page.Strip.horizontalScroller.highValue, Travel, "the strip scrolls further than a drag");
            Assert.AreEqual(0f, page.View.scrollOffset.y);
            Assert.AreEqual(0f, page.Strip.scrollOffset.x);
        }

        // ---- the drag ----

        [UnityTest]
        public IEnumerator APressThatTravelsScrollsThePageByTheTravelAndClicksNothing()
        {
            var page = new Page(LongPressSeconds());
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.Buy.worldBound.center;
            Assert.AreSame(page.Buy, panel.Pick(at), "the press lands on the button");

            Down(panel, at);
            var end = Drag(panel, at, new Vector2(0, -Travel));
            Up(panel, end);
            yield return Frames(1);

            Assert.AreEqual(Travel, page.View.scrollOffset.y, 0.5f, "the content followed the pointer");
            Assert.AreEqual(0, page.BuyClicks, "the release of a drag buys nothing");
            Assert.IsNull(panel.GetCapturingElement(PointerId.mousePointerId), "nothing holds the pointer after");
        }

        [UnityTest]
        public IEnumerator APressShortOfTheStartDistanceIsAClick()
        {
            var page = new Page(LongPressSeconds());
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.Buy.worldBound.center;
            var wobble = new Vector2(0, -(DragScrollManipulator.StartDistance - 4));

            Down(panel, at);
            DragTo(panel, at + wobble);
            Up(panel, at + wobble);
            yield return Frames(1);

            Assert.AreEqual(1, page.BuyClicks);
            Assert.AreEqual(0f, page.View.scrollOffset.y, "a press short of the start scrolls nothing");
        }

        // The down of a view still holding the pointer lets it go, so a lost
        // release costs no more than that press: the page never keeps the
        // pointer, and the press after it reaches the button.
        [UnityTest]
        public IEnumerator AfterADragWhoseReleaseWasLostTheButtonStillClicks()
        {
            var page = new Page(LongPressSeconds());
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.Buy.worldBound.center;

            Down(panel, at);
            var end = Drag(panel, at, new Vector2(0, -Travel));
            Assert.AreSame(page.View, panel.GetCapturingElement(PointerId.mousePointerId), "the drag holds the pointer");
            LoseRelease(end);
            yield return Frames(1);

            // The content moved with the pointer, so the button is under it
            // again; a press there with no move between.
            at = page.Buy.worldBound.center;
            Down(panel, at);
            Up(panel, at);
            yield return Frames(1);
            Assert.IsNull(panel.GetCapturingElement(PointerId.mousePointerId), "the page let the pointer go");

            var before = page.BuyClicks;
            Down(panel, at);
            Up(panel, at);
            yield return Frames(1);
            Assert.AreEqual(before + 1, page.BuyClicks, "the button heard the press and the release");
            Assert.AreEqual(Travel, page.View.scrollOffset.y, 0.5f, "and no press since scrolled anything");
        }

        [UnityTest]
        public IEnumerator AMoveWithNoButtonPressedEndsTheDragAndFreesThePointer()
        {
            var page = new Page(LongPressSeconds());
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.Buy.worldBound.center;

            Down(panel, at);
            var end = Drag(panel, at, new Vector2(0, -Travel));
            Assert.AreSame(page.View, panel.GetCapturingElement(PointerId.mousePointerId), "the drag holds the pointer");
            LoseRelease(end);

            HoverTo(panel, end + new Vector2(0, 5));
            yield return Frames(1);
            Assert.IsNull(panel.GetCapturingElement(PointerId.mousePointerId), "the page let the pointer go");

            HoverTo(panel, end + new Vector2(0, 60));
            yield return Frames(1);
            Assert.AreEqual(Travel, page.View.scrollOffset.y, 0.5f, "a hovering pointer drags nothing");

            var before = page.BuyClicks;
            at = page.Buy.worldBound.center;
            HoverTo(panel, at);
            Down(panel, at);
            Up(panel, at);
            yield return Frames(1);
            Assert.AreEqual(before + 1, page.BuyClicks, "the next press clicks");
        }

        // ---- one press, one view ----

        [UnityTest]
        public IEnumerator ASidewaysDragOnTheStripMovesTheStripAndNotThePage()
        {
            var page = new Page(LongPressSeconds());
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.Tile.worldBound.center;
            Assert.AreSame(page.Tile, panel.Pick(at), "the press lands on a tile");

            Down(panel, at);
            var end = Drag(panel, at, new Vector2(-Travel, 0));
            Up(panel, end);
            yield return Frames(1);

            Assert.AreEqual(Travel, page.Strip.scrollOffset.x, 0.5f, "the strip followed the pointer");
            Assert.AreEqual(0f, page.View.scrollOffset.y, "the page did not move");
            Assert.AreEqual(0, page.TileClicks, "the release of a drag buys nothing");
            Assert.IsNull(panel.GetCapturingElement(PointerId.mousePointerId));
        }

        [UnityTest]
        public IEnumerator AVerticalDragStartingInTheStripMovesThePageAndNotTheStrip()
        {
            var page = new Page(LongPressSeconds());
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.Tile.worldBound.center;

            Down(panel, at);
            var end = Drag(panel, at, new Vector2(0, -Travel));
            Up(panel, end);
            yield return Frames(1);

            Assert.AreEqual(Travel, page.View.scrollOffset.y, 0.5f, "the page followed the pointer");
            Assert.AreEqual(0f, page.Strip.scrollOffset.x, "the strip did not move");
            Assert.AreEqual(0, page.TileClicks);
            Assert.IsNull(panel.GetCapturingElement(PointerId.mousePointerId));
        }

        // ---- an overlay list ----

        private sealed class NoStories : IStoryOpener
        {
            public int Opened;

            public void OpenStory(Story.StoryBeatDefinition beat, ScopeState scope) => Opened++;
        }

        // The story log as the host builds it, over the shipping Screen.uxml:
        // the list it drags is the element the log required, with the read
        // beats listed and enough beneath them that the list scrolls.
        [UnityTest]
        public IEnumerator TheStoryLogListDragsAndItsReleaseOpensNothing()
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Screen.uxml");
            Assert.IsNotNull(asset, "Assets/UI/Screen.uxml is missing");
            var screen = asset.Instantiate();
            screen.StretchToParentSize();
            var tree = new TestTree();
            tree.Root.flags.Add(tree.Opener.seenFlag);
            tree.Root.flags.Add(tree.Capstone.seenFlag);
            var stories = new NoStories();
            var log = new StoryLogUI(screen.Q<VisualElement>("story-log-window"), tree.Root,
                new GameClock(tree.Now), stories, () => { });
            var entries = screen.Q<ScrollView>("story-log-entries");
            for (var i = 0; i < 20; i++)
            {
                var filler = new VisualElement();
                filler.style.height = 150;
                filler.style.flexShrink = 0;
                entries.Add(filler);
            }

            var panel = StandUp(screen);
            log.Show();
            yield return Settle(panel);
            Assert.GreaterOrEqual(entries.verticalScroller.highValue, Travel, "the list scrolls further than a drag");

            var beat = entries.Q<Button>(className: "story-log-entry");
            Assert.IsNotNull(beat, "a read beat is listed");
            var at = beat.worldBound.center;
            Assert.IsTrue(Inside(beat, panel.Pick(at)), "the press lands on the beat's button");

            Down(panel, at);
            var end = Drag(panel, at, new Vector2(0, -Travel));
            Up(panel, end);
            yield return Frames(1);

            Assert.AreEqual(Travel, entries.scrollOffset.y, 0.5f, "the list followed the pointer");
            Assert.AreEqual(0, stories.Opened, "the release of a drag opens nothing");
            Assert.IsNull(panel.GetCapturingElement(PointerId.mousePointerId));
        }

        // ---- the hold ----

        [UnityTest]
        public IEnumerator AStillPressOnARowsTextHoldsOnceAfterTheThreshold()
        {
            var seconds = LongPressSeconds();
            var page = new Page(seconds);
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.TextLeft.worldBound.center;
            Assert.AreSame(page.TextLeft, panel.Pick(at), "the press lands on the text's label");

            Down(panel, at);
            yield return Seconds(seconds * 0.5);
            Assert.AreEqual(0, page.TextHolds, "nothing before the threshold");
            yield return Seconds(seconds * 0.5 + 0.3);
            Assert.AreEqual(1, page.TextHolds, "the hold fired at the threshold");
            yield return Seconds(seconds + 0.3);
            Assert.AreEqual(1, page.TextHolds, "and only once");
            Up(panel, at);
            yield return Frames(1);
            Assert.AreEqual(1, page.TextHolds);
        }

        // The control every negative row ends with: a still press on the text
        // where it stands now holds, in this same panel, so the row's "no
        // hold" was a hold that could have fired.
        private static IEnumerator AStillPressHolds(Page page, double seconds)
        {
            var panel = page.View.panel;
            var at = page.TextLeft.worldBound.center;
            Assert.AreSame(page.TextLeft, panel.Pick(at), "the control press lands on the text's label");
            var before = page.TextHolds;
            Down(panel, at);
            yield return Seconds(seconds + 0.3);
            Assert.AreEqual(before + 1, page.TextHolds, "a still press on the same text holds");
            Up(panel, at);
            yield return Frames(1);
        }

        [UnityTest]
        public IEnumerator AReleaseBeforeTheThresholdIsNoHold()
        {
            var seconds = LongPressSeconds();
            var page = new Page(seconds);
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.TextLeft.worldBound.center;

            Down(panel, at);
            yield return Seconds(seconds * 0.5);
            Up(panel, at);
            yield return Seconds(seconds + 0.3);
            Assert.AreEqual(0, page.TextHolds);

            yield return AStillPressHolds(page, seconds);
        }

        // The pointer crossing from one label of the text to the other leaves
        // a child, not the target, and travels less than the start distance.
        [UnityTest]
        public IEnumerator MovingBetweenTheTextsLabelsKeepsTheHold()
        {
            var seconds = LongPressSeconds();
            var page = new Page(seconds);
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var y = page.TextLeft.worldBound.center.y;
            var from = new Vector2(page.TextLeft.worldBound.xMax - 4, y);
            var to = new Vector2(page.TextRight.worldBound.xMin + 4, y);
            Assert.AreSame(page.TextLeft, panel.Pick(from), "the press lands on the left label");
            Assert.AreSame(page.TextRight, panel.Pick(to), "the move lands on the right label");
            Assert.Less((to - from).magnitude, DragScrollManipulator.StartDistance, "the crossing is short of a drag");

            Down(panel, from);
            DragTo(panel, to);
            yield return Seconds(seconds + 0.3);

            Assert.AreEqual(1, page.TextHolds);
            Up(panel, to);
            yield return Frames(1);
        }

        [UnityTest]
        public IEnumerator TravellingTheStartDistanceCancelsTheHold()
        {
            var seconds = LongPressSeconds();
            var page = new Page(seconds);
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.TextLeft.worldBound.center;

            // Sideways, so the page's vertical drag never starts and the move
            // is heard on the text itself.
            Down(panel, at);
            var moved = at + new Vector2(DragScrollManipulator.StartDistance + 6, 0);
            Assert.IsTrue(Inside(page.Text, panel.Pick(moved)), "the pointer is still over the text");
            DragTo(panel, moved);
            yield return Seconds(seconds + 0.3);

            Assert.AreEqual(0, page.TextHolds);
            Assert.AreEqual(0f, page.View.scrollOffset.y);
            Up(panel, moved);
            yield return Frames(1);

            yield return AStillPressHolds(page, seconds);
        }

        [UnityTest]
        public IEnumerator ADragStartingOnTheTextNeverHoldsWhileItDrags()
        {
            var seconds = LongPressSeconds();
            var page = new Page(seconds);
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.TextLeft.worldBound.center;

            Down(panel, at);
            var end = Drag(panel, at, new Vector2(0, -Travel));
            Assert.AreSame(page.View, panel.GetCapturingElement(PointerId.mousePointerId), "the page took the pointer");
            yield return Seconds(seconds + 0.3);
            Assert.AreEqual(0, page.TextHolds, "held past the threshold mid-drag");

            Up(panel, end);
            yield return Seconds(seconds + 0.3);
            Assert.AreEqual(0, page.TextHolds);
            Assert.AreEqual(Travel, page.View.scrollOffset.y, 0.5f);

            yield return AStillPressHolds(page, seconds);
        }

        [UnityTest]
        public IEnumerator AQuickDragFromTheTextAndAReleaseNeverHolds()
        {
            var seconds = LongPressSeconds();
            var page = new Page(seconds);
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.TextLeft.worldBound.center;

            Down(panel, at);
            var end = Drag(panel, at, new Vector2(0, -Travel));
            Up(panel, end);
            yield return Seconds(seconds + 0.3);

            Assert.AreEqual(0, page.TextHolds);
            Assert.AreEqual(Travel, page.View.scrollOffset.y, 0.5f);

            yield return AStillPressHolds(page, seconds);
        }

        // A hold on a button with nothing opened over it: the button's click
        // captured the pointer at the down, the hold lets go of it when it
        // fires, and the release that follows lands on the button itself and
        // is stopped there, so it is no click. The press after it is an
        // ordinary tap.
        [UnityTest]
        public IEnumerator AHoldOnAButtonFiresAndItsReleaseClicksNothing()
        {
            var seconds = LongPressSeconds();
            var page = new Page(seconds);
            yield return StandUpPage(page);
            var panel = page.View.panel;
            var at = page.Auto.worldBound.center;
            Assert.AreSame(page.Auto, panel.Pick(at), "the press lands on the button");

            Down(panel, at);
            yield return Seconds(seconds + 0.3);
            Assert.AreEqual(1, page.AutoHolds, "the hold fired");

            Up(panel, at);
            yield return Frames(1);
            Assert.AreEqual(0, page.AutoClicks, "the release of a hold is no click");
            Assert.AreEqual(1, page.AutoHolds);
            Assert.IsNull(panel.GetCapturingElement(PointerId.mousePointerId));

            Down(panel, at);
            Up(panel, at);
            yield return Frames(1);
            Assert.AreEqual(1, page.AutoClicks, "a tap after it clicks");
            Assert.AreEqual(1, page.AutoHolds);
        }
    }
}
