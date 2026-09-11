using NUnit.Framework;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The story beats as domain facts (design doc section 10): a beat's gate is
    // judged at the chapter that declares it, and reading one writes a latch
    // homed far enough out that the capstone's reset cannot replay an opener.
    // The row and the card are the host's, and ScreenHostTests owns them.
    public class StoryTests
    {
        private static GameConfig Config()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.maxGameSpeed = 4;
            return config;
        }

        // A live session over the standing tree, plus the refresh counter every
        // command row reads - the session shape GameSessionTests starts from.
        // The stamp at Now makes the entry skip the idle offer.
        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly GameSession Session;
            public int Refreshes;

            public Fixture()
            {
                Tree.Ch1.lastActiveUtc = Tree.Now;
                Session = new GameSession(Tree.Root, Config());
                Session.SwitchChapter(Tree.Ch1, Tree.Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
                Session.Refreshed += () => Refreshes++;
            }
        }

        // ---- the command ----

        [Test]
        public void AcknowledgeStory_writes_the_latch_at_its_home()
        {
            var f = new Fixture();

            f.Session.AcknowledgeStory(f.Tree.Ctx(f.Tree.Ch1), f.Tree.Opener);

            Assert.IsTrue(f.Tree.Root.flags.Contains("story_open_seen"),
                "the write walks outward to the scope that declares the flag");
            Assert.IsFalse(f.Tree.Ch1.flags.Contains("story_open_seen"),
                "and lands nowhere on the way out");
            Assert.AreEqual(1, f.Refreshes, "one refresh per completed transaction");
        }

        // The walk starts at the acting scope, so a deeper context reaches the
        // same home - which is what lets a row at any scope acknowledge.
        [Test]
        public void AcknowledgeStory_reaches_the_same_home_from_a_tier()
        {
            var f = new Fixture();

            f.Session.AcknowledgeStory(f.Tree.Ctx(f.Tree.Tier1), f.Tree.Capstone);

            Assert.IsTrue(f.Tree.Root.flags.Contains("story_end_seen"));
            Assert.IsFalse(f.Tree.Tier1.flags.Contains("story_end_seen"));
            Assert.IsFalse(f.Tree.Ch1.flags.Contains("story_end_seen"));
        }

        // The command checks nothing of its own: the host opens a card only for
        // a beat whose button is live or whose mark pops it, and setting a flag
        // already set changes nothing.
        [Test]
        public void Acknowledging_a_beat_already_read_changes_nothing_and_still_refreshes()
        {
            var f = new Fixture();
            f.Session.AcknowledgeStory(f.Tree.Ctx(f.Tree.Ch1), f.Tree.Opener);
            Assert.AreEqual(1, f.Refreshes);

            f.Session.AcknowledgeStory(f.Tree.Ctx(f.Tree.Ch1), f.Tree.Opener);

            Assert.IsTrue(f.Tree.Root.flags.Contains("story_open_seen"));
            Assert.AreEqual(2, f.Refreshes, "a command that ran is a transaction, and every one refreshes");
        }

        // ---- the gates ----

        [Test]
        public void The_opener_is_available_and_unseen_on_a_fresh_chapter()
        {
            var f = new Fixture();
            var ctx = f.Tree.Ctx(f.Tree.Ch1);

            Assert.IsTrue(f.Tree.Opener.IsAvailable(ctx),
                "Always is how an author says the button is live from a fresh chapter");
            Assert.IsFalse(ctx.IsFlagSet("story_open_seen"), "and nothing has read it yet");
        }

        [Test]
        public void The_capstone_waits_on_the_chapters_completion_flag()
        {
            var f = new Fixture();

            Assert.IsFalse(f.Tree.Capstone.IsAvailable(f.Tree.Ctx(f.Tree.Ch1)));

            f.Tree.Root.flags.Add("ch1_complete");
            Assert.IsTrue(f.Tree.Capstone.IsAvailable(f.Tree.Ctx(f.Tree.Ch1)),
                "the gate reads the root flag by the outward walk from the chapter");
        }

        // Why the latches are root's (section 10): the capstone resets the whole
        // chapter, and a beat read before the reset stays read after it.
        [Test]
        public void A_chapter_reset_leaves_both_latches_set()
        {
            var f = new Fixture();
            f.Session.AcknowledgeStory(f.Tree.Ctx(f.Tree.Ch1), f.Tree.Opener);
            f.Session.AcknowledgeStory(f.Tree.Ctx(f.Tree.Ch1), f.Tree.Capstone);
            f.Tree.Ch1.flags.Add("album");

            f.Tree.Ch1.ClearSubtree(f.Tree.Now);

            Assert.IsFalse(f.Tree.Ch1.flags.Contains("album"), "the chapter's own facts went with the reset");
            Assert.IsTrue(f.Tree.Root.flags.Contains("story_open_seen"));
            Assert.IsTrue(f.Tree.Root.flags.Contains("story_end_seen"));
        }
    }
}
