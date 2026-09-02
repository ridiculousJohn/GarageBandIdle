using System.IO;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The importer against fixture documents (design doc 12.14.5). Every test
    // authors its own JSON, imports it, and reads the assets back - the one
    // place the authoring boundary is exercised end to end.
    public class ChapterImporterTests
    {
        private const string SandboxRoot = "Assets/ImporterTestSandbox";
        private const string GroupName = "ImporterTestContent";

        // One folder per TEST rather than one folder deleted and recreated:
        // AssetDatabase.DeleteAsset does not settle before a CreateAsset at the
        // same path moments later, and the file that results reads back empty.
        // Nothing about the importer needs this - a repeat import into a live
        // folder is exactly what the idempotence test covers.
        private static string sandbox;
        private static string ContentDirectory => sandbox + "/Content";
        private static string ObjectRoot => sandbox + "/Objects";

        private static ChapterJsonImporter.Options Options() => new()
        {
            ContentDirectory = ContentDirectory,
            AssetRoot = ObjectRoot,
            GroupName = GroupName,
        };

        // Every block on the closed list carries a displayName (12.11), so the
        // preflight these fixtures all run through has nothing to say about the
        // fixture itself - a producer stays unnamed, since only a binding would
        // require one.
        private const string RootJson = @"{
            ""type"": ""RootDefinition"",
            ""id"": ""root"",
            ""declaredTags"": [""income""],
            ""currencies"": [{ ""id"": ""records"", ""displayName"": ""Records"" }]
        }";

        private const string ChapterJson = @"{
            ""type"": ""ChapterDefinition"",
            ""id"": ""ch1"",
            ""displayName"": ""The Garage"",
            ""currencies"": [{ ""id"": ""cash"", ""displayName"": ""Cash"", ""tags"": [""income""] }],
            ""producers"": [
                { ""id"": ""tap"", ""produces"": [{ ""currency"": ""cash"", ""stat"": ""yield"", ""value"": 1 }] }
            ],
            ""children"": [{ ""type"": ""TierDefinition"", ""id"": ""tier1"" }]
        }";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(SandboxRoot))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(SandboxRoot));
            var name = TestContext.CurrentContext.Test.MethodName;
            sandbox = SandboxRoot + "/" + name;
            AssetDatabase.CreateFolder(SandboxRoot, name);
            AssetDatabase.CreateFolder(sandbox, "Content");
            AssetDatabase.Refresh();
        }

        // The group goes per test: an entry left from a deleted asset would sit
        // in it still claiming the 'root' address.
        [TearDown]
        public void TearDown()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings.FindGroup(GroupName);
            if (group != null)
                settings.RemoveGroup(group);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            AssetDatabase.DeleteAsset(SandboxRoot);
            AssetDatabase.Refresh();
        }

        private static void Write(string name, string json)
        {
            File.WriteAllText(ContentDirectory + "/" + name, json);
            AssetDatabase.Refresh();
        }

        private static void WritePair()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson);
        }

        private static void Import() => ChapterJsonImporter.Import(Options());

        private static T Load<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(ObjectRoot + "/" + path);

        // ---- the happy path ----

        // The one authored member a currency has (12.2), resolved from the
        // scope that declares it - which is the home the gate is judged at.
        [Test]
        public void A_currency_gate_imports_onto_the_currency()
        {
            Write("root.json", RootJson);
            Write("ch1.json", @"{
                ""type"": ""ChapterDefinition"",
                ""id"": ""ch1"",
                ""displayName"": ""The Garage"",
                ""flags"": [""revealed""],
                ""currencies"": [
                    { ""id"": ""cash"", ""displayName"": ""Cash"", ""tags"": [""income""],
                      ""activeWhen"": { ""type"": ""FlagSet"", ""flagId"": ""revealed"" } },
                    { ""id"": ""plain"", ""displayName"": ""Plain"" }
                ],
                ""children"": [{ ""type"": ""TierDefinition"", ""id"": ""tier1"" }]
            }");

            Import();

            Assert.AreEqual("revealed",
                ((FlagSet)Load<CurrencyDefinition>("ch1/Currencies/cash.asset").activeWhen).flagId);
            Assert.IsNull(Load<CurrencyDefinition>("ch1/Currencies/plain.asset").activeWhen,
                "an unauthored gate stays null, which is always active");
        }

        [Test]
        public void A_minimal_pair_imports_and_wires()
        {
            WritePair();

            Import();

            var root = Load<RootDefinition>("root/root.asset");
            var chapter = Load<ChapterDefinition>("ch1/ch1.asset");
            var cash = Load<CurrencyDefinition>("ch1/Currencies/cash.asset");
            var tap = Load<ProducerDefinition>("ch1/Producers/tap.asset");
            var tier1 = Load<TierDefinition>("ch1/tier1.asset");

            Assert.IsNotNull(root, "the root asset landed at its document folder");
            Assert.AreEqual(new[] { "income" }, root.declaredTags.ToArray());
            // Root's serialized child list stays EMPTY: the chapter documents are
            // the roster (12.14.5).
            Assert.AreEqual(0, root.children.Count);
            Assert.AreEqual(new[] { tier1 }, chapter.children.ToArray());
            Assert.AreSame(cash, tap.produces[0].currency, "the reference is the asset, not a copy");
            Assert.AreEqual(new[] { "income" }, cash.Tags.ToArray());
        }

        [Test]
        public void The_root_and_chapter_entries_share_one_pack_together_group()
        {
            WritePair();
            Import();

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings.FindGroup(GroupName);
            var schema = group.GetSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();

            // Root-owned assets are implicit dependencies of BOTH entries, and
            // Addressables duplicates an implicit dependency into every bundle
            // referencing it - two `records` at runtime would break the asset
            // identity composition leans on.
            Assert.AreEqual(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema
                .BundlePackingMode.PackTogether, schema.BundleMode);
            var rootEntry = group.entries.Single(e => e.address == ContentDatabase.RootAddress);
            var chapterEntry = group.entries.Single(e => e.address == "ch1");
            Assert.IsFalse(rootEntry.labels.Contains(ContentDatabase.ChapterLabel), "root is not on the roster");
            Assert.IsTrue(chapterEntry.labels.Contains(ContentDatabase.ChapterLabel), "the label IS the roster act");
        }

        // ---- the lints ----

        [Test]
        public void An_unknown_type_aborts()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson.Replace(@"""stat"": ""yield""", @"""stat"": ""yield"", ""condition"": { ""type"": ""Nonsense"" }"));

            var thrown = Assert.Throws<ContentImportException>(Import);
            StringAssert.Contains("Nonsense", thrown.Message);
        }

        [Test]
        public void An_unknown_key_aborts()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson.Replace(@"""value"": 1", @"""amount"": 1"));

            Assert.Throws<ContentImportException>(Import);
        }

        [Test]
        public void A_duplicate_id_on_one_chain_aborts()
        {
            // 'records' is root's; the chapter is on its chain, so this is the
            // collision the runtime's own uniqueness rule refuses (12.12).
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson.Replace(
                @"""currencies"": [{ ""id"": ""cash"", ""displayName"": ""Cash"", ""tags"": [""income""] }]",
                @"""currencies"": [{ ""id"": ""cash"", ""displayName"": ""Cash"", ""tags"": [""income""] }, { ""id"": ""records"", ""displayName"": ""Records"" }]"));

            var thrown = Assert.Throws<ContentImportException>(Import);
            StringAssert.Contains("records", thrown.Message);
        }

        // Sibling subtrees cannot see each other, so reusing an id across them
        // is legal authoring rather than a collision.
        [Test]
        public void Sibling_chapters_may_reuse_an_id()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson);
            Write("ch2.json", ChapterJson.Replace(@"""id"": ""ch1""", @"""id"": ""ch2""")
                                         .Replace(@"""id"": ""tier1""", @"""id"": ""tier2"""));

            Import();

            Assert.AreNotSame(Load<CurrencyDefinition>("ch1/Currencies/cash.asset"),
                              Load<CurrencyDefinition>("ch2/Currencies/cash.asset"));
        }

        [Test]
        public void An_unresolved_reference_aborts()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson.Replace(@"""currency"": ""cash""", @"""currency"": ""ghost"""));

            var thrown = Assert.Throws<ContentImportException>(Import);
            StringAssert.Contains("ghost", thrown.Message);
        }

        [Test]
        public void An_id_outside_the_grammar_aborts()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson.Replace(@"""id"": ""tap""", @"""id"": ""../tap"""));

            Assert.Throws<ContentImportException>(Import);
        }

        // A trailing newline is a legal JSON string, and a $-anchored grammar
        // matches before it - the id would become a path segment and a runtime
        // key that nothing typed by hand can ever name.
        [Test]
        public void An_id_with_a_trailing_newline_aborts()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson.Replace(@"""id"": ""tap""", @"""id"": ""tap\n"""));

            Assert.Throws<ContentImportException>(Import);
        }

        // ---- the union contract ----

        [Test]
        public void A_failing_document_leaves_every_documents_assets_unwritten()
        {
            WritePair();
            Import();
            var before = File.ReadAllText(ObjectRoot + "/ch1/Currencies/cash.asset");

            // Root now names a kind with no class behind it. Nothing is written -
            // not root's assets, and not the chapter's either.
            Write("root.json", RootJson.Replace(@"""currencies"": [{ ""id"": ""records"", ""displayName"": ""Records"" }]",
                @"""currencies"": [{ ""id"": ""records"", ""displayName"": ""Records"" }], ""modifiers"": [{ ""id"": ""m"", ""appliesWhen"": { ""type"": ""Nope"" } }]"));
            Write("ch1.json", ChapterJson.Replace(@"""id"": ""tap""", @"""id"": ""tap_renamed"""));

            Assert.Throws<ContentImportException>(Import);

            Assert.IsNull(Load<ProducerDefinition>("ch1/Producers/tap_renamed.asset"), "the good document waited too");
            Assert.AreEqual(before, File.ReadAllText(ObjectRoot + "/ch1/Currencies/cash.asset"));
        }

        // One command writes the union it validated, so a rename spanning two
        // documents cannot land half-applied.
        [Test]
        public void A_cross_document_rename_lands_in_one_run()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson.Replace(@"""produces"": [{ ""currency"": ""cash""",
                                                  @"""produces"": [{ ""currency"": ""records"""));
            Import();

            Write("root.json", RootJson.Replace(@"""id"": ""records""", @"""id"": ""albums"""));
            Write("ch1.json", ChapterJson.Replace(@"""produces"": [{ ""currency"": ""cash""",
                                                  @"""produces"": [{ ""currency"": ""albums"""));
            Import();

            var albums = Load<CurrencyDefinition>("root/Currencies/albums.asset");
            var tap = Load<ProducerDefinition>("ch1/Producers/tap.asset");
            Assert.IsNotNull(albums);
            Assert.AreSame(albums, tap.produces[0].currency, "the chapter follows root's rename in the same run");
        }

        // ---- re-import ----

        [Test]
        public void Reimport_keeps_the_asset_guid_and_the_chapters_label()
        {
            WritePair();
            Import();
            var guid = AssetDatabase.AssetPathToGUID(ObjectRoot + "/ch1/ch1.asset");
            var currencyGuid = AssetDatabase.AssetPathToGUID(ObjectRoot + "/ch1/Currencies/cash.asset");

            Import();

            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(ObjectRoot + "/ch1/ch1.asset"));
            Assert.AreEqual(currencyGuid, AssetDatabase.AssetPathToGUID(ObjectRoot + "/ch1/Currencies/cash.asset"));
            var group = AddressableAssetSettingsDefaultObject.Settings.FindGroup(GroupName);
            Assert.IsTrue(group.entries.Single(e => e.address == "ch1").labels.Contains(ContentDatabase.ChapterLabel));
        }

        [Test]
        public void A_second_import_of_identical_json_changes_nothing()
        {
            WritePair();
            Import();
            var before = Directory.GetFiles(ObjectRoot, "*.asset", SearchOption.AllDirectories)
                .OrderBy(p => p).Select(File.ReadAllText).ToArray();

            Import();

            var after = Directory.GetFiles(ObjectRoot, "*.asset", SearchOption.AllDirectories)
                .OrderBy(p => p).Select(File.ReadAllText).ToArray();
            Assert.AreEqual(before, after);
        }

        // ---- the chapter's screen ----

        // Two tiers deep on purpose: a module's default scope is its CONTENT's
        // home (12.11), and only a home distinct from the section's own scope
        // tells the two apart. The four modules are the three default branches
        // plus an authored scope over the top of one.
        private const string SectionsJson = @"{
            ""type"": ""ChapterDefinition"",
            ""id"": ""ch1"",
            ""displayName"": ""The Garage"",
            ""flags"": [""album""],
            ""sections"": [
                {
                    ""title"": ""The Garage Floor"",
                    ""visibleWhen"": { ""type"": ""Always"" },
                    ""scopeId"": ""tier1a"",
                    ""modules"": [
                        { ""prefabId"": ""currency_line"", ""contentId"": ""cash"" },
                        { ""prefabId"": ""currency_line"", ""contentId"": ""records"" },
                        { ""prefabId"": ""generator_list"" },
                        {
                            ""prefabId"": ""rung_button"",
                            ""scopeId"": ""tier1"",
                            ""visibleWhen"": { ""type"": ""FlagSet"", ""flagId"": ""album"" }
                        }
                    ]
                }
            ],
            ""children"": [
                {
                    ""type"": ""TierDefinition"",
                    ""id"": ""tier1"",
                    ""currencies"": [{ ""id"": ""cash"", ""displayName"": ""Cash"", ""tags"": [""income""] }],
                    ""children"": [{ ""type"": ""TierDefinition"", ""id"": ""tier1a"" }]
                }
            ]
        }";

        private static void WriteSectionsPair()
        {
            Write("root.json", RootJson);
            Write("ch1.json", SectionsJson);
        }

        [Test]
        public void A_sections_block_imports_as_direct_references()
        {
            WriteSectionsPair();

            Import();

            var chapter = Load<ChapterDefinition>("ch1/ch1.asset");
            var section = chapter.sections.Single();

            Assert.AreEqual("The Garage Floor", section.title);
            Assert.IsInstanceOf<Always>(section.visibleWhen, "Always is how an author says the gate is open");
            Assert.AreSame(Load<TierDefinition>("ch1/tier1a.asset"), section.scope,
                "the section's scope is the asset, not a copy");
            Assert.AreEqual(new[] { "currency_line", "currency_line", "generator_list", "rung_button" },
                section.modules.Select(m => m.prefabId).ToArray());
            Assert.AreSame(Load<CurrencyDefinition>("ch1/Currencies/cash.asset"), section.modules[0].content,
                "the binding is the asset the tier declares");
            Assert.IsNull(section.modules[2].content, "a list module's content is its scope's own lists");
            Assert.AreEqual("album", ((FlagSet)section.modules[3].visibleWhen).flagId,
                "the module gate is built from the module's own scope, which reaches ch1's flag");
        }

        // Import NORMALIZES: every written module carries a concrete scope, so
        // the runtime computes no default (12.11).
        [Test]
        public void Every_imported_module_carries_a_concrete_scope()
        {
            WriteSectionsPair();

            Import();

            var chapter = Load<ChapterDefinition>("ch1/ch1.asset");
            var tier1 = Load<TierDefinition>("ch1/tier1.asset");
            var modules = chapter.sections.Single().modules;

            Assert.AreSame(tier1, modules[0].scope, "descendant content lands on its home, not the section's scope");
            Assert.AreSame(chapter, modules[1].scope, "root-owned content lands on the chapter");
            Assert.AreSame(Load<TierDefinition>("ch1/tier1a.asset"), modules[2].scope,
                "a contentless module lands on its section's scope");
            Assert.AreSame(tier1, modules[3].scope, "an authored scope wins over every default");
        }

        [Test]
        public void An_unknown_key_in_a_section_block_aborts()
        {
            Write("root.json", RootJson);
            Write("ch1.json", SectionsJson.Replace(@"""title"": ""The Garage Floor""",
                                                   @"""heading"": ""The Garage Floor"""));

            Assert.Throws<ContentImportException>(Import);
        }

        [Test]
        public void A_section_naming_a_scope_no_document_declares_aborts()
        {
            Write("root.json", RootJson);
            Write("ch1.json", SectionsJson.Replace(@"""scopeId"": ""tier1a""", @"""scopeId"": ""ghost_tier"""));

            var thrown = Assert.Throws<ContentImportException>(Import);
            StringAssert.Contains("section scope", thrown.Message);
        }

        [Test]
        public void A_module_naming_content_nothing_declares_aborts()
        {
            Write("root.json", RootJson);
            Write("ch1.json", SectionsJson.Replace(@"""contentId"": ""cash""", @"""contentId"": ""ghost"""));

            var thrown = Assert.Throws<ContentImportException>(Import);
            StringAssert.Contains("module content", thrown.Message);
        }

        // The key is real on every scope block, so a scope that cannot hold one
        // names itself in the error rather than reading as a misspelling.
        [Test]
        public void Sections_on_a_tier_or_on_the_root_abort()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson.Replace(
                @"{ ""type"": ""TierDefinition"", ""id"": ""tier1"" }",
                @"{ ""type"": ""TierDefinition"", ""id"": ""tier1"", ""sections"": [{ ""title"": ""T"", ""visibleWhen"": { ""type"": ""Always"" }, ""scopeId"": ""tier1"" }] }"));

            var onATier = Assert.Throws<ContentImportException>(Import);
            StringAssert.Contains("scope 'tier1' authors sections", onATier.Message);

            Write("root.json", RootJson.Replace(
                @"""currencies"": [{ ""id"": ""records"", ""displayName"": ""Records"" }]",
                @"""currencies"": [{ ""id"": ""records"", ""displayName"": ""Records"" }], ""sections"": [{ ""title"": ""T"", ""visibleWhen"": { ""type"": ""Always"" }, ""scopeId"": ""root"" }]"));
            Write("ch1.json", ChapterJson);

            var onTheRoot = Assert.Throws<ContentImportException>(Import);
            StringAssert.Contains("scope 'root' authors sections", onTheRoot.Message);
        }

        // The importer copies prefabId as authored; the preflight is the one
        // check, and it gates the writes before any of them happen.
        [Test]
        public void An_empty_prefab_id_aborts_through_the_preflight()
        {
            Write("root.json", RootJson);
            Write("ch1.json", SectionsJson.Replace(@"""prefabId"": ""generator_list""", @"""prefabId"": """""));

            // No LogAssert here: an expected refusal prints nothing, and an
            // unexpected error log would fail the row on its own. The findings
            // ride the exception instead.
            var thrown = Assert.Throws<ContentImportException>(Import);

            StringAssert.Contains("content validation failed", thrown.Message);
            Assert.IsTrue(thrown.Report.OfCheck(ValidationCheck.NullEntry).Any(), "the refusal carries its findings");
            Assert.IsNull(Load<ChapterDefinition>("ch1/ch1.asset"), "nothing was written");
        }

        // Sections rebuild wholesale, so a second run appends nothing and the
        // chapter keeps the guid every reference to it holds.
        [Test]
        public void A_second_import_rebuilds_the_sections_unchanged()
        {
            WriteSectionsPair();
            Import();
            var guid = AssetDatabase.AssetPathToGUID(ObjectRoot + "/ch1/ch1.asset");

            Import();

            var chapter = Load<ChapterDefinition>("ch1/ch1.asset");
            var section = chapter.sections.Single();
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(ObjectRoot + "/ch1/ch1.asset"));
            Assert.AreEqual(4, section.modules.Count);
            Assert.AreSame(Load<CurrencyDefinition>("ch1/Currencies/cash.asset"), section.modules[0].content);
            Assert.AreSame(Load<TierDefinition>("ch1/tier1.asset"), section.modules[0].scope);
        }

        // ---- authored numbers span what the runtime can compute ----

        private const string GeneratorJson = @"{
            ""type"": ""ChapterDefinition"",
            ""id"": ""ch1"",
            ""displayName"": ""The Garage"",
            ""currencies"": [{ ""id"": ""cash"", ""displayName"": ""Cash"", ""tags"": [""income""] }],
            ""generators"": [
                {
                    ""id"": ""amp"",
                    ""displayName"": ""Practice Amp"",
                    ""availableWhen"": { ""type"": ""Always"" },
                    ""costCurrency"": ""cash"",
                    ""baseCost"": 60,
                    ""growth"": 1.15,
                    ""produces"": [{ ""currency"": ""cash"", ""stat"": ""rate"", ""value"": 0.5 }]
                }
            ]
        }";

        [Test]
        public void An_ordinary_number_lands_exactly()
        {
            Write("root.json", RootJson);
            Write("ch1.json", GeneratorJson);

            Import();

            var amp = Load<GeneratorDefinition>("ch1/Generators/amp.asset");
            Assert.AreEqual((BigNumber)60, amp.baseCost);
            Assert.AreEqual((BigNumber)1.15, amp.growth);
            Assert.AreEqual((BigNumber)0.5, amp.produces[0].value);
        }

        // The runtime's range is a long exponent, so the authoring boundary has
        // to reach it too. Past double range the value is authored QUOTED - the
        // one spelling whose exponent survives the reader.
        [Test]
        public void A_quoted_number_past_double_range_keeps_its_exponent()
        {
            Write("root.json", RootJson);
            Write("ch1.json", GeneratorJson.Replace(@"""baseCost"": 60", @"""baseCost"": ""1.5e400"""));

            Import();

            var amp = Load<GeneratorDefinition>("ch1/Generators/amp.asset");
            Assert.AreEqual(400, amp.baseCost.Exponent);
            Assert.AreEqual(1.5, amp.baseCost.Mantissa, 1e-12);
        }

        // Unquoted, the reader has already collapsed it to infinity before any
        // converter sees it, so the abort names the spelling that works.
        [Test]
        public void An_unquoted_number_past_double_range_aborts()
        {
            Write("root.json", RootJson);
            Write("ch1.json", GeneratorJson.Replace(@"""baseCost"": 60", @"""baseCost"": 1e400"));

            Assert.Throws<ContentImportException>(Import);
        }

        // ---- the write pass never deletes ----

        [Test]
        public void An_occupied_path_is_refused_rather_than_overwritten()
        {
            AssetDatabase.CreateFolder(sandbox, "Objects");
            AssetDatabase.CreateFolder(ObjectRoot, "ch1");
            AssetDatabase.CreateFolder(ObjectRoot + "/ch1", "Currencies");
            var occupant = UnityEngine.ScriptableObject.CreateInstance<GameConfig>();
            AssetDatabase.CreateAsset(occupant, ObjectRoot + "/ch1/Currencies/cash.asset");
            WritePair();

            var thrown = Assert.Throws<ContentImportException>(Import);
            StringAssert.Contains("GameConfig", thrown.Message);
            Assert.IsNotNull(Load<GameConfig>("ch1/Currencies/cash.asset"), "the occupant is still there");
        }

        // CreateAsset replaces whatever sits at its target, so the check is
        // filesystem occupancy: a file the AssetDatabase cannot load is
        // invisible to a load and still very much in the way.
        [Test]
        public void An_unloadable_file_at_a_target_path_is_refused()
        {
            const string junk = "not a serialized asset at all";
            AssetDatabase.CreateFolder(sandbox, "Objects");
            AssetDatabase.CreateFolder(ObjectRoot, "ch1");
            AssetDatabase.CreateFolder(ObjectRoot + "/ch1", "Currencies");
            var occupied = ObjectRoot + "/ch1/Currencies/cash.asset";
            File.WriteAllText(occupied, junk);
            LogAssert.ignoreFailingMessages = true;   // the AssetDatabase logs its own failure to import it
            AssetDatabase.Refresh();
            WritePair();

            var thrown = Assert.Throws<ContentImportException>(Import);

            StringAssert.Contains(occupied, thrown.Message);
            Assert.AreEqual(junk, File.ReadAllText(occupied), "left exactly as it was");
        }

        [Test]
        public void A_root_document_authoring_children_aborts()
        {
            Write("root.json", RootJson.Replace(@"""currencies"": [{ ""id"": ""records"", ""displayName"": ""Records"" }]",
                @"""currencies"": [{ ""id"": ""records"", ""displayName"": ""Records"" }], ""children"": [{ ""type"": ""ChapterDefinition"", ""id"": ""ch1"" }]"));

            var thrown = Assert.Throws<ContentImportException>(Import);
            StringAssert.Contains("roster", thrown.Message);
        }

        [Test]
        public void A_reserved_device_name_aborts()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson.Replace(@"""id"": ""tap""", @"""id"": ""aux"""));

            var thrown = Assert.Throws<ContentImportException>(Import);
            StringAssert.Contains("reserved device name", thrown.Message);
        }

        // A renamed root leaves the old asset claiming the fixed address, and an
        // address is a primary key - two 'root' entries means boot loads
        // whichever the catalogue reached first.
        [Test]
        public void A_renamed_root_leaves_no_second_entry_at_the_fixed_address()
        {
            WritePair();
            Import();

            Write("root.json", RootJson.Replace(@"""id"": ""root""", @"""id"": ""core"""));
            Import();

            var group = AddressableAssetSettingsDefaultObject.Settings.FindGroup(GroupName);
            Assert.AreEqual(1, group.entries.Count(e => e.address == ContentDatabase.RootAddress));
            Assert.IsNotNull(Load<RootDefinition>("root/root.asset"), "the old asset stays on disk");
        }

        // The preflight builds native objects; an abort inside it has to destroy
        // them too, or repeated failures accumulate until a domain reload.
        [Test]
        public void An_abort_inside_the_build_leaves_no_transient_objects()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson.Replace(@"""currency"": ""cash""", @"""currency"": ""ghost"""));
            var before = Transients();

            Assert.Throws<ContentImportException>(Import);

            Assert.AreEqual(before, Transients());
        }

        private static int Transients() =>
            Resources.FindObjectsOfTypeAll<Definition>().Count(d => !EditorUtility.IsPersistent(d));

        // Off the runtime roster, still on disk: deleting content is a human's
        // call, so the import only takes the label back.
        [Test]
        public void A_removed_chapter_document_de_labels_its_root()
        {
            Write("root.json", RootJson);
            Write("ch1.json", ChapterJson);
            Write("ch2.json", ChapterJson.Replace(@"""id"": ""ch1""", @"""id"": ""ch2""")
                                         .Replace(@"""id"": ""tier1""", @"""id"": ""tier2"""));
            Import();

            File.Delete(ContentDirectory + "/ch2.json");
            AssetDatabase.Refresh();
            Import();

            var group = AddressableAssetSettingsDefaultObject.Settings.FindGroup(GroupName);
            Assert.IsFalse(group.entries.Single(e => e.address == "ch2").labels.Contains(ContentDatabase.ChapterLabel));
            Assert.IsNotNull(Load<ChapterDefinition>("ch2/ch2.asset"), "still on disk");
        }
    }
}
