using System;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Editor;
using RidiculousGaming.GarageBandIdle.UI;
using UnityEditor;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The registry asset itself (design doc 12.11). It is hand-made settings
    // rather than imported content, so nothing regenerates it and a bad entry
    // survives until a render reads it - which is exactly the fault requirement
    // 7 refuses to let sit behind a blank widget.
    //
    // The cross-check runs the other way too: every prefabId chapter 1 authors
    // has to resolve here and be answered by the factory, since a module's id
    // is content and a miss surfaces only when the section holding it first
    // becomes visible - which may be hours into a run.
    public class ModuleRegistryTests
    {
        private const string RegistryPath = "Assets/Settings/ModuleRegistry.asset";

        private static ModuleRegistry Load()
        {
            var registry = AssetDatabase.LoadAssetAtPath<ModuleRegistry>(RegistryPath);
            Assert.IsNotNull(registry,
                RegistryPath + " is missing - it is hand-made settings, created with the PanelSettings and the Boot scene wiring.");
            return registry;
        }

        [Test]
        public void EveryEntryCarriesAGrammaticalIdAndAPrefab()
        {
            var registry = Load();
            Assert.IsNotEmpty(registry.entries, "the registry declares no entries");
            foreach (var entry in registry.entries)
            {
                Assert.IsNotNull(entry, "the registry holds a null entry");
                Assert.IsTrue(ModuleDefinition.PrefabIdGrammar.IsMatch(entry.prefabId ?? string.Empty),
                    $"prefabId '{entry.prefabId}' is not in the id grammar");
                Assert.IsNotNull(entry.prefab, $"entry '{entry.prefabId}' carries no VisualTreeAsset");
            }
        }

        // A duplicate id would resolve to whichever entry the authored order put
        // first, which is a coin toss dressed as a lookup.
        [Test]
        public void PrefabIdsAreUnique()
        {
            var ids = Load().PrefabIds.ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "a prefabId is registered twice");
        }

        [Test]
        public void TheFactoryAnswersEveryRegisteredId()
        {
            foreach (var id in Load().PrefabIds)
                Assert.IsTrue(ModuleWidgetFactory.Answers(id),
                    $"the registry maps '{id}' to a UXML no widget controller answers");
        }

        [Test]
        public void TheRegistryResolvesEveryIdTheFactoryAnswers()
        {
            var registry = Load();
            foreach (var id in ModuleWidgetFactory.PrefabIds)
                Assert.IsNotNull(registry.Resolve(id), $"the registry resolves '{id}' to nothing");
        }

        [Test]
        public void CombinedValidationAcceptsTheShippedRegistry()
        {
            var root = TestTree.MakeRoot("root");
            try
            {
                var report = ModuleWidgetFactory.Validate(ComposedContent.Compose(root), Load());

                Assert.IsFalse(report.HasErrors,
                    string.Join("\n", report.Findings.Select(finding => finding.ToString())));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CombinedValidationRejectsAControllerWhoseLayoutIsMissing()
        {
            var root = TestTree.MakeRoot("root");
            var registry = UnityEngine.Object.Instantiate(Load());
            try
            {
                registry.entries.RemoveAll(entry => entry != null && entry.prefabId == "story_row");

                var report = ModuleWidgetFactory.Validate(ComposedContent.Compose(root), registry);

                Assert.IsTrue(report.HasErrors);
                Assert.IsTrue(report.OfCheck(ValidationCheck.UnresolvedReference)
                    .Any(finding => finding.Message.Contains("'story_row' has no layout")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(registry);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CombinedValidationRejectsAnAuthoredIdWithoutAControllerOrLayout()
        {
            var root = TestTree.MakeRoot("root");
            var chapter = TestTree.MakeChapter("ch1");
            chapter.sections.Add(new SectionDefinition
            {
                modules = { new ModuleDefinition { prefabId = "ghost_widget" } }
            });
            try
            {
                var report = ModuleWidgetFactory.Validate(
                    ComposedContent.Compose(root, new[] { chapter }), Load());
                var messages = report.OfCheck(ValidationCheck.UnresolvedReference)
                    .Select(finding => finding.Message)
                    .ToList();

                Assert.IsTrue(messages.Any(message => message.Contains("'ghost_widget' has no widget controller")));
                Assert.IsTrue(messages.Any(message => message.Contains("'ghost_widget' has no layout")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(chapter);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // The authored-to-settings half, over the IMPORTED chapter. The
        // combined factory validation above owns the controller cross-check;
        // this asset test keeps the shipped chapter and registry paired too.
        [Test]
        public void EveryPrefabIdChapterOneAuthorsResolvesInTheRegistry()
        {
            var registry = Load();
            var ch1 = AssetDatabase.LoadAssetAtPath<ChapterDefinition>(
                ChapterJsonImporter.AssetRootPath + "/ch1/ch1.asset");
            Assert.IsNotNull(ch1, "chapter-01.json has not been imported - run Garage Band Idle/Import Content.");

            var authored = ch1.sections
                .SelectMany(section => section.modules)
                .Select(module => module.prefabId)
                .Distinct()
                .ToList();
            Assert.IsNotEmpty(authored, "the chapter authors no modules at all");
            foreach (var id in authored)
                Assert.IsNotNull(registry.Resolve(id),
                    $"chapter 1 authors '{id}', which the registry resolves to nothing");
        }

        [Test]
        public void ResolvingAnUnknownIdThrows()
        {
            var registry = Load();
            Assert.Throws<InvalidOperationException>(() => registry.Resolve("not_a_widget"));
        }
    }
}
