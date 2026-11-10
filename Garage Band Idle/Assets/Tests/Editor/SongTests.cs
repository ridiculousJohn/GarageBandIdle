using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The Discography (design doc 7): AddSong writes a song into the root's
    // list from whatever scope it runs in, and the session's RenameSong
    // command replaces a song's name. Display only - nothing in the economy
    // reads a song, so these rows read the list and nothing else.
    public class SongTests
    {
        private static IReadOnlyList<GameAction> Only(params GameAction[] actions) => actions;

        private static string[] Ids(List<SongEntry> songs) => songs.Select(song => song.songId).ToArray();

        private static string[] Names(List<SongEntry> songs) => songs.Select(song => song.name).ToArray();

        // ---- AddSong ----

        // Run in the tier, the song lands on root: the Discography is root's
        // list whatever scope the action acts from, and the tier's and the
        // chapter's own lists are never written.
        [Test]
        public void AddSong_run_from_a_tier_appends_to_the_roots_songs()
        {
            var tree = new TestTree();

            Assert.IsTrue(ActionList.TryRun(Only(new AddSong { title = "Untitled #{0}" }), tree.Ctx(tree.Tier1)),
                "the list ran");

            Assert.AreEqual(1, tree.Root.songs.Count);
            Assert.AreEqual("song_1", tree.Root.songs[0].songId);
            Assert.AreEqual("Untitled #1", tree.Root.songs[0].name);
            Assert.IsEmpty(tree.Tier1.songs, "the acting tier keeps no song");
            Assert.IsEmpty(tree.Ch1.songs, "the chapter between keeps no song");
        }

        // The number is the count the list holds plus one, so three writes in
        // a row number themselves 1, 2, 3 in the order written.
        [Test]
        public void Three_AddSongs_in_a_row_number_their_ids_and_names_in_order()
        {
            var tree = new TestTree();
            var write = new AddSong { title = "Untitled #{0}" };

            Assert.IsTrue(ActionList.TryRun(Only(write, write, write), tree.Ctx(tree.Tier1)));

            CollectionAssert.AreEqual(new[] { "song_1", "song_2", "song_3" }, Ids(tree.Root.songs));
            CollectionAssert.AreEqual(new[] { "Untitled #1", "Untitled #2", "Untitled #3" }, Names(tree.Root.songs));
        }

        // A title with no placeholder formats to itself; the id still counts.
        [Test]
        public void A_title_with_no_placeholder_is_used_as_it_stands()
        {
            var tree = new TestTree();
            var write = new AddSong { title = "Greatest Hits" };

            Assert.IsTrue(ActionList.TryRun(Only(write), tree.Ctx(tree.Tier1)));
            Assert.IsTrue(ActionList.TryRun(Only(write), tree.Ctx(tree.Tier1)));

            CollectionAssert.AreEqual(new[] { "song_1", "song_2" }, Ids(tree.Root.songs));
            CollectionAssert.AreEqual(new[] { "Greatest Hits", "Greatest Hits" }, Names(tree.Root.songs));
        }

        // Root is the one scope nothing resets, so a tier reset and a chapter
        // reset after the write both leave the Discography standing.
        [Test]
        public void Songs_survive_a_tier_reset_and_a_chapter_reset()
        {
            var tree = new TestTree();
            // Both authored before anything is written: Author rebuilds the
            // tree, and the resets read the links of the tree they run in.
            var tierReset = tree.Author(tree.Tier1Def, new ResetScope { scope = tree.Tier1Def });
            var chapterReset = tree.Author(tree.Ch1Def, new ResetScope { scope = tree.Ch1Def });
            var write = new AddSong { title = "Untitled #{0}" };
            Assert.IsTrue(ActionList.TryRun(Only(write, write), tree.Ctx(tree.Tier1)));

            tierReset.Execute(tree.Ctx(tree.Tier1));

            CollectionAssert.AreEqual(new[] { "song_1", "song_2" }, Ids(tree.Root.songs), "after the tier reset");
            CollectionAssert.AreEqual(new[] { "Untitled #1", "Untitled #2" }, Names(tree.Root.songs));

            chapterReset.Execute(tree.Ctx(tree.Ch1));

            CollectionAssert.AreEqual(new[] { "song_1", "song_2" }, Ids(tree.Root.songs), "after the chapter reset");
            CollectionAssert.AreEqual(new[] { "Untitled #1", "Untitled #2" }, Names(tree.Root.songs));
        }

        // ---- RenameSong ----

        // A session over the standing tree, with no chapter entered: RenameSong
        // is a root-context command, so it needs no foreground chapter. The
        // songs are written by the real AddSong through the session's reward
        // runner, numbered Untitled #1, #2, ... like a game's.
        private class Fixture
        {
            public readonly TestTree Tree = new TestTree();
            public readonly GameSession Session;

            public Fixture(int songs)
            {
                Session = new GameSession(Tree.Root, ScriptableObject.CreateInstance<GameConfig>());
                var writes = new GameAction[songs];
                for (var i = 0; i < songs; i++)
                    writes[i] = new AddSong { title = "Untitled #{0}" };
                bool? ran = null;
                Session.RunReward(writes, Tree.Now, r => ran = r);
                Assert.AreEqual(true, ran, "the writes ran");
                Assert.AreEqual(songs, Tree.Root.songs.Count);
            }

            public List<SongEntry> Songs => Tree.Root.songs;

            public bool? Rename(string songId, string name)
            {
                bool? answer = null;
                Session.RenameSong(songId, name, Tree.Now, r => answer = r);
                return answer;
            }
        }

        [Test]
        public void RenameSong_renames_the_named_song_and_leaves_the_others()
        {
            var f = new Fixture(3);

            Assert.AreEqual(true, f.Rename("song_2", "Garage Anthem"), "the command answers true");

            CollectionAssert.AreEqual(new[] { "Untitled #1", "Garage Anthem", "Untitled #3" }, Names(f.Songs));
        }

        [Test]
        public void RenameSong_trims_surrounding_whitespace()
        {
            var f = new Fixture(1);

            Assert.AreEqual(true, f.Rename("song_1", "  \tGarage Anthem \n"));

            Assert.AreEqual("Garage Anthem", f.Songs[0].name);
        }

        // Null counts as empty, and a name that trims to nothing is empty too.
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(" \t \n ")]
        [TestCase((string)null)]
        public void RenameSong_with_a_blank_name_answers_false_and_changes_nothing(string name)
        {
            var f = new Fixture(2);

            Assert.AreEqual(false, f.Rename("song_1", name));

            CollectionAssert.AreEqual(new[] { "song_1", "song_2" }, Ids(f.Songs));
            CollectionAssert.AreEqual(new[] { "Untitled #1", "Untitled #2" }, Names(f.Songs));
        }

        [TestCase("song_3")]
        [TestCase("song_0")]
        [TestCase("")]
        [TestCase((string)null)]
        public void RenameSong_for_an_id_no_song_holds_answers_false_and_changes_nothing(string songId)
        {
            var f = new Fixture(2);

            Assert.AreEqual(false, f.Rename(songId, "Garage Anthem"));

            CollectionAssert.AreEqual(new[] { "song_1", "song_2" }, Ids(f.Songs));
            CollectionAssert.AreEqual(new[] { "Untitled #1", "Untitled #2" }, Names(f.Songs));
        }

        // MaxNameLength is 40: a 41-character name keeps its first 40, and a
        // name of exactly 40 is stored whole.
        [TestCase("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO", "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN")]
        [TestCase("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN", "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN")]
        public void RenameSong_caps_the_name_at_forty_characters(string name, string stored)
        {
            var f = new Fixture(1);

            Assert.AreEqual(true, f.Rename("song_1", name));

            Assert.AreEqual(stored, f.Songs[0].name);
            Assert.AreEqual(40, f.Songs[0].name.Length);
            Assert.AreEqual(40, SongEntry.MaxNameLength);
        }

        // A rename replaces the name and nothing else: the id stays the one
        // the write numbered, so a second rename finds the song by it.
        [Test]
        public void RenameSong_leaves_the_id_alone()
        {
            var f = new Fixture(2);

            Assert.AreEqual(true, f.Rename("song_2", "Garage Anthem"));

            CollectionAssert.AreEqual(new[] { "song_1", "song_2" }, Ids(f.Songs));
            Assert.AreEqual("song_2", f.Songs[1].songId);
            Assert.AreEqual("Garage Anthem", f.Songs[1].name);
            Assert.AreEqual(true, f.Rename("song_2", "Second Thoughts"), "the id still names it");
            Assert.AreEqual("Second Thoughts", f.Songs[1].name);
        }
    }
}
