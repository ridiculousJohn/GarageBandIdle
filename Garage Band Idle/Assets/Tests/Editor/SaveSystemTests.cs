using System;
using System.IO;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Save;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class SaveSystemTests
    {
        private string dir;
        private string SavePath => Path.Combine(dir, "save.json");

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "gbi_save_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        // Load into a FRESH tree: the tests below only care that the loaded
        // state matches, not which definition instance answered.
        private static bool Load(string json, out RootScopeState root)
        {
            var tree = new TestTree();
            return SaveSystem.TryDeserialize(json, tree.RootDef, out root);
        }

        private LoadOutcome LoadDisk(out RootScopeState root)
        {
            var tree = new TestTree();
            return SaveSystem.LoadFromDisk(SavePath, tree.RootDef, out root);
        }

        private static TestTree Populate(TestTree tree)
        {
            tree.Tier1.balances["cash"] = 123.45;
            tree.Tier1.earnedTotals["cash"] = 300;
            tree.Tier1.balances["fans"] = BigNumber.FromMantissaExponent(1.5, 320);   // beyond double range
            tree.Tier1.generatorCounts["drummer"] = 3;
            tree.Tier1.flags.Add("fans_revealed");
            tree.Tier1.purchasedUpgrades.Add("stage_presence");
            tree.Tier1.firedTriggers.Add("tier1_trigger");   // declared by the fixture's tier1
            tree.Tier1.barProgress["cover_1"] = 42;
            tree.Tier1.fillCounts["cover_1"] = 2;
            tree.Tier1.activeBars["learn_covers"] = new System.Collections.Generic.HashSet<string> { "cover_1" };
            tree.Tier1.modifierStacks["gj_tap_1"] = 2;
            tree.Tier1.activeEvents.Add(new ActiveEvent { eventId = "garage_jam_1", remainingSeconds = 12.5, goalReached = true, claimed = false });
            tree.Tier1.songs.Add(new SongEntry { songId = "song_1", name = "Three-Chord Anthem" });
            tree.Ch1.balances["ch1_records"] = 17;
            tree.Ch1.flags.Add("album");
            tree.Ch1.pendingClaim = new PendingClaim { claimId = "claim_1", doubled = true };
            tree.Ch1.pendingClaim.amounts["cash"] = 604800;
            tree.Ch1.lastActiveUtc = new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc);
            tree.Root.balances["records"] = 20;
            tree.Root.timedBuffs.Add(new TimedBuff { buffId = "encore", expiresAtUtc = new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc) });
            tree.Root.roadieAllocation["ch1"] = 1;
            tree.Root.entitlements.Add("backstage_pass");
            return tree;
        }

        [Test]
        public void RoundTrip_preserves_the_full_state_tree()
        {
            var saved = Populate(new TestTree());
            var json = SaveSystem.Serialize(saved.Root);

            var fresh = new TestTree();
            Assert.IsTrue(SaveSystem.TryDeserialize(json, fresh.RootDef, out var root));

            var tier1 = root.FindInSubtree("tier1");
            var ch1 = (ChapterScopeState)root.FindInSubtree("ch1");
            Assert.AreEqual((BigNumber)123.45, tier1.balances["cash"]);
            Assert.AreEqual((BigNumber)300, tier1.earnedTotals["cash"]);
            Assert.AreEqual(BigNumber.FromMantissaExponent(1.5, 320), tier1.balances["fans"]);
            Assert.AreEqual(3, tier1.generatorCounts["drummer"]);
            Assert.IsTrue(tier1.flags.Contains("fans_revealed"));
            Assert.IsTrue(tier1.purchasedUpgrades.Contains("stage_presence"));
            Assert.IsTrue(tier1.firedTriggers.Contains("tier1_trigger"));
            Assert.AreEqual((BigNumber)42, tier1.barProgress["cover_1"]);
            Assert.AreEqual(2, tier1.fillCounts["cover_1"]);
            Assert.IsTrue(tier1.activeBars["learn_covers"].Contains("cover_1"));
            Assert.AreEqual(2, tier1.modifierStacks["gj_tap_1"]);
            Assert.AreEqual("garage_jam_1", tier1.activeEvents[0].eventId);
            Assert.AreEqual(12.5, tier1.activeEvents[0].remainingSeconds);
            Assert.IsTrue(tier1.activeEvents[0].goalReached);
            Assert.AreEqual("Three-Chord Anthem", tier1.songs[0].name);
            Assert.AreEqual((BigNumber)17, ch1.balances["ch1_records"]);
            Assert.IsTrue(ch1.flags.Contains("album"));
            Assert.IsTrue(ch1.pendingClaim.doubled);
            Assert.AreEqual((BigNumber)604800, ch1.pendingClaim.amounts["cash"]);
            Assert.AreEqual(new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc), ch1.lastActiveUtc);
            Assert.AreEqual((BigNumber)20, root.balances["records"]);
            Assert.AreEqual(new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc), root.timedBuffs[0].expiresAtUtc);
            Assert.AreEqual(1, root.roadieAllocation["ch1"]);
            Assert.IsTrue(root.entitlements.Contains("backstage_pass"));
        }

        [Test]
        public void Removed_scope_drops_with_a_warning_and_added_scope_starts_fresh()
        {
            // Save against a tree that has tier2; load against one where tier2
            // was removed and tier3 added.
            var oldTree = new TestTree();
            var tier2 = TestTree.MakeScope("tier2");
            TestTree.DeclareCurrency(tier2, "merch");
            oldTree.Ch1Def.children.Add(tier2);
            var oldRoot = ScopeState.Build(oldTree.RootDef);
            oldRoot.FindInSubtree("tier2").balances["merch"] = 5;
            var json = SaveSystem.Serialize(oldRoot);

            var newTree = new TestTree();
            var tier3 = TestTree.MakeScope("tier3");
            TestTree.DeclareCurrency(tier3, "vinyl");
            newTree.Ch1Def.children.Add(tier3);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("tier2"));
            Assert.IsTrue(SaveSystem.TryDeserialize(json, newTree.RootDef, out var root));

            Assert.IsNull(root.FindInSubtree("tier2"));
            Assert.AreEqual(BigNumber.Zero, root.FindInSubtree("tier3").balances["vinyl"]);
        }

        [Test]
        public void Removed_ids_drop_with_warnings_and_new_declarations_start_at_zero()
        {
            var saved = new TestTree();
            saved.Tier1.balances["cash"] = 100;
            saved.Tier1.flags.Add("fans_revealed");
            var json = SaveSystem.Serialize(saved.Root);

            // Same tree shape, but tier1 no longer declares cash or the flag,
            // and now declares a new currency.
            var newTree = new TestTree();
            newTree.Tier1Def.declaredCurrencies.RemoveAll(c => c != null && c.Id == "cash");
            newTree.Tier1Def.declaredFlags.Remove("fans_revealed");
            TestTree.DeclareCurrency(newTree.Tier1Def, "vinyl");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'cash'"));
            Assert.IsTrue(SaveSystem.TryDeserialize(json, newTree.RootDef, out var root));

            var tier1 = root.FindInSubtree("tier1");
            Assert.IsFalse(tier1.balances.ContainsKey("cash"));
            Assert.IsFalse(tier1.flags.Contains("fans_revealed"));
            Assert.AreEqual(BigNumber.Zero, tier1.balances["vinyl"]);
        }

        // A missing content tree is a code bug, and a half-filtered tree must
        // never reach the game - nor be reported as an unreadable save.
        [Test]
        public void Loading_without_a_content_tree_throws()
        {
            var tree = new TestTree();
            var json = SaveSystem.Serialize(tree.Root);
            Assert.Throws<ArgumentNullException>(
                () => SaveSystem.TryDeserialize(json, null, out _));
        }

        [Test]
        public void Newer_schema_version_is_refused()
        {
            var tree = Populate(new TestTree());
            var envelope = Newtonsoft.Json.Linq.JObject.Parse(SaveSystem.Serialize(tree.Root));
            envelope["schemaVersion"] = SaveSystem.CurrentSchemaVersion + 1;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("newer"));
            Assert.IsFalse(Load(Rechecksum(envelope), out _));
        }

        [Test]
        public void Older_version_with_no_registered_migration_is_refused()
        {
            var tree = Populate(new TestTree());
            var envelope = Newtonsoft.Json.Linq.JObject.Parse(SaveSystem.Serialize(tree.Root));
            envelope["schemaVersion"] = 0;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("migration"));
            Assert.IsFalse(Load(Rechecksum(envelope), out _));
        }

        [Test]
        public void Tampered_version_without_a_matching_checksum_reads_as_corruption()
        {
            var tree = Populate(new TestTree());
            var envelope = Newtonsoft.Json.Linq.JObject.Parse(SaveSystem.Serialize(tree.Root));
            envelope["schemaVersion"] = SaveSystem.CurrentSchemaVersion + 1;   // checksum NOT recomputed

            // The bound checksum makes a flipped version byte corruption - the
            // loader falls back to the backup instead of refusing a "newer" save.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("checksum"));
            Assert.IsFalse(Load(envelope.ToString(), out _));
        }

        // Recomputes the bound checksum after a test edits envelope fields, so
        // the test exercises the version logic rather than the corruption path.
        private static string Rechecksum(Newtonsoft.Json.Linq.JObject envelope)
        {
            var version = envelope.Value<int>("schemaVersion");
            var payload = envelope.Value<string>("payload");
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(version + "\n" + payload));
            var builder = new System.Text.StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                builder.Append(b.ToString("x2"));
            envelope["checksum"] = builder.ToString();
            return envelope.ToString();
        }

        [Test]
        public void Atomic_write_keeps_the_previous_save_as_the_backup()
        {
            var tree = new TestTree();
            tree.Root.balances["records"] = 1;
            SaveSystem.WriteAtomic(SavePath, tree.Root);
            tree.Root.balances["records"] = 2;
            SaveSystem.WriteAtomic(SavePath, tree.Root);

            Assert.IsTrue(File.Exists(SaveSystem.BackupPath(SavePath)));
            Assert.IsTrue(Load(File.ReadAllText(SaveSystem.BackupPath(SavePath)), out var backupRoot));
            Assert.AreEqual(BigNumber.One, backupRoot.balances["records"]);
            Assert.IsTrue(Load(File.ReadAllText(SavePath), out var primaryRoot));
            Assert.AreEqual((BigNumber)2, primaryRoot.balances["records"]);
        }

        [Test]
        public void Corrupt_primary_falls_back_to_the_backup()
        {
            var tree = new TestTree();
            tree.Root.balances["records"] = 1;
            SaveSystem.WriteAtomic(SavePath, tree.Root);
            tree.Root.balances["records"] = 2;
            SaveSystem.WriteAtomic(SavePath, tree.Root);
            Corrupt(SavePath);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("checksum"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("backup"));
            var outcome = LoadDisk(out var root);

            Assert.AreEqual(LoadOutcome.LoadedBackup, outcome);
            Assert.AreEqual(BigNumber.One, root.balances["records"]);
        }

        [Test]
        public void Saving_after_backup_recovery_never_rotates_the_corrupt_primary_into_the_backup()
        {
            var tree = new TestTree();
            tree.Root.balances["records"] = 1;
            SaveSystem.WriteAtomic(SavePath, tree.Root);
            tree.Root.balances["records"] = 2;
            SaveSystem.WriteAtomic(SavePath, tree.Root);   // backup now holds records=1
            Corrupt(SavePath);

            tree.Root.balances["records"] = 3;
            SaveSystem.WriteAtomic(SavePath, tree.Root);   // must NOT install the corrupt file as .bak

            Assert.IsTrue(Load(File.ReadAllText(SavePath), out var primary));
            Assert.AreEqual((BigNumber)3, primary.balances["records"]);
            Assert.IsTrue(Load(File.ReadAllText(SaveSystem.BackupPath(SavePath)), out var backup));
            Assert.AreEqual(BigNumber.One, backup.balances["records"]);   // the good backup survived
        }

        [Test]
        public void Unreadable_primary_falls_back_to_the_backup()
        {
            var tree = new TestTree();
            tree.Root.balances["records"] = 1;
            SaveSystem.WriteAtomic(SavePath, tree.Root);
            tree.Root.balances["records"] = 2;
            SaveSystem.WriteAtomic(SavePath, tree.Root);   // backup = records 1, primary = records 2

            using (new FileStream(SavePath, FileMode.Open, FileAccess.Read, FileShare.None))   // lock the primary
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("could not read"));
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("backup"));
                var outcome = LoadDisk(out var root);

                Assert.AreEqual(LoadOutcome.LoadedBackup, outcome);
                Assert.AreEqual(BigNumber.One, root.balances["records"]);
            }
        }

        [Test]
        public void Tree_scoped_ids_from_removed_content_are_dropped()
        {
            var savedTree = new TestTree();
            var trigger = TestTree.MakeDefinition<TriggerDefinition>("t1");
            savedTree.Tier1Def.triggers.Add(trigger);
            var savedRoot = ScopeState.Build(savedTree.RootDef);
            var tier1 = savedRoot.FindInSubtree("tier1");
            tier1.firedTriggers.Add("t1");
            tier1.firedTriggers.Add("ghost_trigger");
            savedRoot.roadieAllocation["ch1"] = 1;
            savedRoot.roadieAllocation["ghost_chapter"] = 2;
            var ch1 = (ChapterScopeState)savedRoot.FindInSubtree("ch1");
            ch1.pendingClaim = new PendingClaim { claimId = "c1" };
            ch1.pendingClaim.amounts["cash"] = 100;          // tier-declared - anywhere in the tree is valid
            ch1.pendingClaim.amounts["ghost_currency"] = 5;
            var json = SaveSystem.Serialize(savedRoot);

            // Load against a tree that still declares t1 so only the ghosts drop.
            var newTree = new TestTree();
            newTree.Tier1Def.triggers.Add(trigger);
            // Emission order follows the Apply recursion: root's allocation,
            // then ch1's claim, then tier1's latch.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ghost_chapter"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ghost_currency"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ghost_trigger"));
            Assert.IsTrue(SaveSystem.TryDeserialize(json, newTree.RootDef, out var root));

            var loadedTier1 = root.FindInSubtree("tier1");
            Assert.IsTrue(loadedTier1.firedTriggers.Contains("t1"));
            Assert.IsFalse(loadedTier1.firedTriggers.Contains("ghost_trigger"));
            Assert.AreEqual(1, root.roadieAllocation["ch1"]);
            Assert.IsFalse(root.roadieAllocation.ContainsKey("ghost_chapter"));
            var loadedCh1 = (ChapterScopeState)root.FindInSubtree("ch1");
            Assert.AreEqual((BigNumber)100, loadedCh1.pendingClaim.amounts["cash"]);
            Assert.IsFalse(loadedCh1.pendingClaim.amounts.ContainsKey("ghost_currency"));
        }

        [Test]
        public void Economy_ids_from_removed_content_are_dropped()
        {
            var saved = new TestTree();
            saved.Tier1.generatorCounts["drummer"] = 3;              // declared - survives
            saved.Tier1.generatorCounts["ghost_generator"] = 2;
            saved.Tier1.purchasedUpgrades.Add("amp_strings");        // declared - survives
            saved.Tier1.purchasedUpgrades.Add("ghost_upgrade");
            saved.Ch1.modifierStacks["gj_tap_1"] = 1;
            saved.Ch1.modifierStacks["ghost_modifier"] = 1;
            var json = SaveSystem.Serialize(saved.Root);

            // Emission order follows the Apply recursion: ch1's modifier, then
            // tier1's upgrade latch and generator count (latches filter first).
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ghost_modifier"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ghost_upgrade"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ghost_generator"));
            Assert.IsTrue(Load(json, out var root));

            var tier1 = root.FindInSubtree("tier1");
            Assert.AreEqual(3, tier1.generatorCounts["drummer"]);
            Assert.IsFalse(tier1.generatorCounts.ContainsKey("ghost_generator"));
            Assert.IsTrue(tier1.purchasedUpgrades.Contains("amp_strings"));
            Assert.IsFalse(tier1.purchasedUpgrades.Contains("ghost_upgrade"));
            var ch1 = root.FindInSubtree("ch1");
            Assert.AreEqual(1, ch1.modifierStacks.Count);
            Assert.IsTrue(ch1.modifierStacks.ContainsKey("gj_tap_1"));
        }

        [Test]
        public void Nonpositive_counts_are_not_facts()
        {
            var saved = new TestTree();
            saved.Tier1.generatorCounts["drummer"] = -3;             // would buy the next unit at a discount
            saved.Ch1.modifierStacks["gj_tap_1"] = 0;
            saved.Root.roadieAllocation["ch1"] = -2;                 // would pay a negative roadie boost
            var json = SaveSystem.Serialize(saved.Root);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("roadie allocation for 'ch1' is -2"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'gj_tap_1' has count 0"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'drummer' is -3"));
            Assert.IsTrue(Load(json, out var root));

            Assert.IsEmpty(root.roadieAllocation);
            Assert.IsEmpty(root.FindInSubtree("ch1").modifierStacks);
            Assert.IsEmpty(root.FindInSubtree("tier1").generatorCounts);
        }

        [Test]
        public void Newer_schema_primary_never_rotates_into_the_backup()
        {
            var tree = new TestTree();
            tree.Root.balances["records"] = 1;
            SaveSystem.WriteAtomic(SavePath, tree.Root);
            tree.Root.balances["records"] = 2;
            SaveSystem.WriteAtomic(SavePath, tree.Root);   // backup now holds records=1

            // The app-downgrade case: a checksum-VALID primary from a newer
            // build. Envelope integrity passes; loadability does not.
            var envelope = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(SavePath));
            envelope["schemaVersion"] = SaveSystem.CurrentSchemaVersion + 1;
            File.WriteAllText(SavePath, Rechecksum(envelope));

            tree.Root.balances["records"] = 3;
            SaveSystem.WriteAtomic(SavePath, tree.Root);   // must NOT install the newer-schema file as .bak

            Assert.IsTrue(Load(File.ReadAllText(SavePath), out var primary));
            Assert.AreEqual((BigNumber)3, primary.balances["records"]);
            Assert.IsTrue(Load(File.ReadAllText(SaveSystem.BackupPath(SavePath)), out var backup));
            Assert.AreEqual(BigNumber.One, backup.balances["records"]);   // the good backup survived
        }

        [Test]
        public void Tree_scoped_facts_enforce_ownership_and_reach()
        {
            // A sibling chapter with its own currency, to prove a claim cannot
            // reach across chapters.
            var savedTree = new TestTree();
            var ch2 = TestTree.MakeScope("ch2");
            TestTree.DeclareCurrency(ch2, "merch2");
            savedTree.RootDef.children.Add(ch2);
            var savedRoot = ScopeState.Build(savedTree.RootDef);

            savedRoot.roadieAllocation["ch1"] = 1;      // a chapter - valid
            savedRoot.roadieAllocation["tier1"] = 2;    // in the tree, but not a chapter
            var savedCh1 = (ChapterScopeState)savedRoot.FindInSubtree("ch1");
            savedCh1.pendingClaim = new PendingClaim { claimId = "y" };
            savedCh1.pendingClaim.amounts["cash"] = 10;      // homed in ch1's subtree - valid
            savedCh1.pendingClaim.amounts["records"] = 5;    // homed on the ancestor chain - valid
            savedCh1.pendingClaim.amounts["merch2"] = 7;     // homed in a SIBLING chapter - unreachable
            var json = SaveSystem.Serialize(savedRoot);

            var newTree = new TestTree();
            var ch2Again = TestTree.MakeScope("ch2");
            TestTree.DeclareCurrency(ch2Again, "merch2");
            newTree.RootDef.children.Add(ch2Again);

            // Emission order follows the Apply recursion: root, then ch1.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'tier1' is not a chapter"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'merch2' is not reachable"));
            Assert.IsTrue(SaveSystem.TryDeserialize(json, newTree.RootDef, out var root));

            Assert.AreEqual(1, root.roadieAllocation["ch1"]);
            Assert.IsFalse(root.roadieAllocation.ContainsKey("tier1"));
            var ch1 = (ChapterScopeState)root.FindInSubtree("ch1");
            Assert.AreEqual((BigNumber)10, ch1.pendingClaim.amounts["cash"]);
            Assert.AreEqual((BigNumber)5, ch1.pendingClaim.amounts["records"]);
            Assert.IsFalse(ch1.pendingClaim.amounts.ContainsKey("merch2"));
        }

        [Test]
        public void Inaccessible_primary_with_no_backup_is_Failed_never_NoSave()
        {
            var tree = new TestTree();
            SaveSystem.WriteAtomic(SavePath, tree.Root);   // primary exists, no backup yet

            using (new FileStream(SavePath, FileMode.Open, FileAccess.Read, FileShare.None))   // make it unreadable
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("could not read"));
                var outcome = LoadDisk(out _);

                // "Couldn't read your save" must never be answered by starting
                // a new game.
                Assert.AreEqual(LoadOutcome.Failed, outcome);
            }
        }

        [Test]
        public void Both_files_corrupt_is_Failed_and_no_files_is_NoSave()
        {
            Assert.AreEqual(LoadOutcome.NoSave, LoadDisk(out _));

            var tree = new TestTree();
            SaveSystem.WriteAtomic(SavePath, tree.Root);
            SaveSystem.WriteAtomic(SavePath, tree.Root);
            Corrupt(SavePath);
            Corrupt(SaveSystem.BackupPath(SavePath));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("checksum"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("checksum"));
            Assert.AreEqual(LoadOutcome.Failed, LoadDisk(out _));
        }

        // Flips the stored checksum so verification must fail.
        private static void Corrupt(string path)
        {
            var text = File.ReadAllText(path);
            var marker = "\"checksum\":\"";
            var at = text.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var replacement = text[at] == '0' ? "11111111" : "00000000";
            File.WriteAllText(path, text.Remove(at, 8).Insert(at, replacement));
        }
    }
}
