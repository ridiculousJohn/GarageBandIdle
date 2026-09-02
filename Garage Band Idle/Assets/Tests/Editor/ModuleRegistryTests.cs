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

        // The ids the factory answers, which is the closed set the asset has to
        // cover. Written out rather than derived: Answers is a predicate, and a
        // predicate cannot be enumerated.
        private static readonly string[] FactoryIds =
        {
            "currency_line", "jam_button", "generator_list", "upgrade_list",
            "bar_group", "rung_button", "event_row"
        };

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
            foreach (var id in FactoryIds)
            {
                Assert.IsTrue(ModuleWidgetFactory.Answers(id), $"the factory no longer answers '{id}'");
                Assert.IsNotNull(registry.Resolve(id), $"the registry resolves '{id}' to nothing");
            }
        }

        // The authored half of the closed set, over the IMPORTED chapter: a
        // prefabId is content, so an id no widget answers is a content fault
        // that this pass catches instead of a first render.
        [Test]
        public void EveryPrefabIdChapterOneAuthorsResolvesAndIsAnswered()
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
            {
                Assert.IsTrue(ModuleWidgetFactory.Answers(id),
                    $"chapter 1 authors '{id}', which no widget controller answers");
                Assert.IsNotNull(registry.Resolve(id),
                    $"chapter 1 authors '{id}', which the registry resolves to nothing");
            }
        }

        [Test]
        public void ResolvingAnUnknownIdThrows()
        {
            var registry = Load();
            Assert.Throws<InvalidOperationException>(() => registry.Resolve("not_a_widget"));
        }
    }
}
