using System;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class ChapterLockUITests
    {
        private sealed class Fixture
        {
            public readonly DateTime Now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
            public readonly TestTree Tree = new();
            public readonly ChapterScopeState Ch1;
            public readonly ChapterScopeState Ch2;
            public readonly GameSession Session;
            public readonly GameClock Clock;

            public Fixture()
            {
                var ch2 = TestTree.MakeChapter("ch2");
                ch2.displayName = "The Club";
                ch2.unlock = new FlagSet { flagId = "ch1_complete" };
                Tree.Chapters.Add(ch2);
                Tree.Rebuild();
                Ch1 = Tree.Ch1;
                Ch2 = (ChapterScopeState)TestNavigation.Node(Tree.Root, ch2);

                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.maxGameSpeed = 4;
                config.minimumAwaySeconds = 180;
                config.idleCapSeconds = 14400;
                Session = new GameSession(Tree.Root, config);
                Clock = new GameClock(Now);
            }

            public void UnlockChapter2() => new GameContext(Tree.Root, Now).SetFlag("ch1_complete");
        }

        [Test]
        public void ChapterSelectRefreshesLockedButtonsAndRefusesLockedSelection()
        {
            var f = new Fixture();
            var root = ChapterSelectRoot();
            var prepared = 0;
            var ui = new ChapterSelectUI(root, f.Session, f.Clock, () => prepared++, () => { });

            ui.Show(false);
            var buttons = root.Q<VisualElement>("chapters")
                .Query<Button>(className: "select-chapter").ToList();
            Assert.AreEqual(2, buttons.Count);
            Assert.IsTrue(buttons[0].enabledSelf, "chapter 1 starts unlocked");
            Assert.IsFalse(buttons[1].enabledSelf, "chapter 2 waits on the root completion flag");

            ui.Select(f.Ch2);
            Assert.AreEqual(0, prepared, "a locked pick does not close its screen");
            Assert.AreEqual(SessionPhase.NoChapter, f.Session.Phase);

            f.UnlockChapter2();
            ui.Show(false);
            Assert.IsTrue(buttons[1].enabledSelf, "the next UI refresh reads the new root flag");

            ui.Select(f.Ch2);
            Assert.AreEqual(1, prepared);
            Assert.AreSame(f.Ch2, f.Session.ForegroundChapter);
        }

        [Test]
        public void RoadieDraftPreservesUnlockedCountsAndAddsChapterWhenItUnlocks()
        {
            var f = new Fixture();
            new GameContext(f.Tree.Root, f.Now).Deposit("roadies", 2);
            var root = RoadieRoot();
            var closed = 0;
            var ui = new RoadieAllocationUI(root, f.Session, f.Clock, () => closed++);

            ui.Open();
            ui.Increase("ch1");
            ui.Increase("ch2");
            Assert.AreEqual("1", root.Q<Label>("roadie-count-ch1").text);
            Assert.AreEqual("0", root.Q<Label>("roadie-count-ch2").text);
            Assert.IsFalse(root.Q<Button>("roadie-plus-ch2").enabledSelf);

            f.UnlockChapter2();
            ui.Refresh();
            Assert.AreEqual("1", root.Q<Label>("roadie-count-ch1").text,
                "refresh preserves the eligible chapter's draft");
            Assert.IsTrue(root.Q<Button>("roadie-plus-ch2").enabledSelf);

            ui.Increase("ch2");
            ui.Done();

            Assert.AreEqual(1, f.Tree.Root.roadieAllocation["ch1"]);
            Assert.AreEqual(1, f.Tree.Root.roadieAllocation["ch2"]);
            Assert.AreEqual(1, closed);
        }

        [Test]
        public void LockedRoadieRowSubmitsAsNoEntryAndDoesNotBlockDone()
        {
            var f = new Fixture();
            new GameContext(f.Tree.Root, f.Now).Deposit("roadies", 1);
            var root = RoadieRoot();
            var closed = 0;
            var ui = new RoadieAllocationUI(root, f.Session, f.Clock, () => closed++);

            ui.Open();
            ui.Increase("ch1");
            ui.Increase("ch2");
            ui.Done();

            Assert.AreEqual(1, f.Tree.Root.roadieAllocation["ch1"]);
            Assert.IsFalse(f.Tree.Root.roadieAllocation.ContainsKey("ch2"),
                "the locked row's zero is omitted from the submitted map");
            Assert.AreEqual(1, closed, "the valid map is accepted and closes the overlay");
        }

        private static VisualElement ChapterSelectRoot()
        {
            var root = new VisualElement();
            root.Add(new VisualElement { name = "chapters" });
            root.Add(new Button { name = "select-close" });
            return root;
        }

        private static VisualElement RoadieRoot()
        {
            var root = new VisualElement();
            root.Add(new VisualElement { name = "roadie-rows" });
            root.Add(new Label { name = "roadie-unallocated" });
            root.Add(new Button { name = "roadie-done" });
            root.Add(new Button { name = "roadie-close" });
            return root;
        }
    }
}
