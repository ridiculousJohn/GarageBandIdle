using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Save;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The headless boot helper (design doc 12.13): each load outcome maps to
    // its tree - loaded, backup, fresh - and Failed is a hard stop, never a
    // silent new game. Plus the Addressables smoke test: the one production
    // load path composes the imported pair.
    public class GameBootTests
    {
        private string dir;

        private string SavePath => Path.Combine(dir, "save.json");

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "gbi_boot_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        // The real asset's numbers are GameConfig's own defaults.
        private static GameConfig Config() => ScriptableObject.CreateInstance<GameConfig>();

        private static readonly DateTime BootUtc = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void NoSave_builds_a_fresh_tree()
        {
            var tree = new TestTree();

            var session = GameBoot.Load(tree.Content, SavePath, Config());

            Assert.AreEqual(BigNumber.Zero, session.Root.balances["records"]);
            Assert.IsNull(session.Root.currentChapterId);
            Assert.AreEqual(SessionPhase.NoChapter, session.Phase);
        }

        [Test]
        public void LoadedPrimary_uses_the_loaded_tree()
        {
            var tree = new TestTree();
            tree.Root.balances["records"] = 20;
            tree.Root.currentChapterId = "ch1";
            SaveSystem.WriteAtomic(SavePath, tree.Root, tree.Content);

            var session = GameBoot.Load(tree.Content, SavePath, Config());

            Assert.AreEqual((BigNumber)20, session.Root.balances["records"]);
            Assert.AreEqual("ch1", session.Root.currentChapterId);
        }

        [Test]
        public void LoadedBackup_answers_for_a_corrupt_primary()
        {
            var tree = new TestTree();
            tree.Root.balances["records"] = 7;
            SaveSystem.WriteAtomic(SavePath, tree.Root, tree.Content);
            File.Copy(SavePath, SaveSystem.BackupPath(SavePath));
            File.WriteAllText(SavePath, "not a save");

            var session = GameBoot.Load(tree.Content, SavePath, Config());

            Assert.AreEqual((BigNumber)7, session.Root.balances["records"]);
        }

        // "Couldn't read your save" is never answered by starting a new game
        // (12.10): both files exist, neither loads, boot stops.
        [Test]
        public void Failed_is_a_hard_stop_rather_than_a_new_game()
        {
            var tree = new TestTree();
            File.WriteAllText(SavePath, "not a save");
            File.WriteAllText(SaveSystem.BackupPath(SavePath), "also not a save");

            Assert.Throws<InvalidOperationException>(() => GameBoot.Load(tree.Content, SavePath, Config()));
        }

        [Test]
        public void The_recorded_chapter_is_where_boot_returns()
        {
            var rootDef = TestTree.MakeRoot("root");
            var content = ComposedContent.Compose(rootDef,
                new[] { TestTree.MakeChapter("ch1"), TestTree.MakeChapter("ch2") });
            var root = ScopeState.Build(content);
            root.currentChapterId = "ch2";

            Assert.AreEqual("ch2", GameBoot.EntryChapter(root).ScopeId);
        }

        // The stopgap: a fresh game has no record and step 9 owns the chapter
        // select, so until then boot enters the first chapter by id - which the
        // sorted roster makes deterministic, and which is the sole authored
        // chapter while Chapter 1 stands alone.
        [Test]
        public void An_unrecorded_chapter_falls_back_to_the_first_chapter_by_id()
        {
            var tree = new TestTree();

            Assert.AreSame(tree.Ch1, GameBoot.EntryChapter(tree.Root));
        }

        // A chapter authored SINCE the save was written arrives freshly built -
        // the load leaves content it has no node for that way on purpose - so it
        // reaches entry with no stamp. Here the recorded chapter is gone from
        // content too, which clears the record and sends the fallback straight
        // into the new one. Its starter rate is the shape that would otherwise
        // bill the player for two millennia; Chapter 1 masks that only because
        // every fresh rate there happens to be zero.
        [Test]
        public void A_chapter_added_since_the_save_owes_no_idle_on_entry()
        {
            var saved = ComposedContent.Compose(TestTree.MakeRoot("root"),
                new[] { TestTree.MakeChapter("ch_old") });
            var savedRoot = ScopeState.Build(saved);
            savedRoot.currentChapterId = "ch_old";
            SaveSystem.WriteAtomic(SavePath, savedRoot, saved);

            // The same root id, a different roster: ch_old retired, ch_new authored.
            var newChapter = TestTree.MakeChapter("ch_new");
            var merch = TestTree.DeclareCurrency(newChapter, "merch");
            var press = TestTree.MakeDefinition<ProducerDefinition>("merch_press");
            press.produces.Add(TestTree.Entry(merch, Stat.Rate, 2));
            newChapter.producers.Add(press);

            var session = GameBoot.Load(
                ComposedContent.Compose(TestTree.MakeRoot("root"), new[] { newChapter }),
                SavePath, Config());
            var entered = GameBoot.EntryChapter(session.Root);
            session.SwitchChapter(entered, BootUtc);

            Assert.AreEqual("ch_new", entered.ScopeId, "the stale record cleared and the fallback took over");
            Assert.AreEqual(SessionPhase.Live, session.Phase, "a chapter never left owes no idle");
            Assert.IsNull(session.CurrentOffer);
            Assert.AreEqual(BootUtc, entered.lastActiveUtc, "entry stamped what it found unstamped");
        }

        // ---- the Addressables smoke test ----

        // The production boot load: the fixed root address plus the chapter
        // label, composed and validated (12.14.5/6). This is the only test
        // driving the Addressables path itself; everything downstream of the
        // pair is CompositionTests' and the walkthroughs' ground.
        [Test]
        public void LoadRoot_composes_the_imported_pair()
        {
            var database = ContentDatabase.LoadRoot(ContentDatabase.RootAddress, ContentDatabase.ChapterLabel);
            try
            {
                Assert.IsTrue(database.IsLoaded);
                Assert.AreEqual("root", database.Root.Root.Id);
                Assert.IsTrue(database.Root.Chapters.Any(c => c.Id == "ch1"),
                    "the chapter label carried ch1 into the roster");
            }
            finally
            {
                database.Release();
            }
        }
    }
}
