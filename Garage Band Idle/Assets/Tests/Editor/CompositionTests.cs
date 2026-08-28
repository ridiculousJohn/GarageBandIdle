using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The one seam boot, the importer's preflight, and every test assemble
    // through (design doc 12.14.5): the root asset plus the chapter roster,
    // composed into the tree that "children of root" means.
    public class CompositionTests
    {
        [Test]
        public void A_chapters_action_targeting_root_resolves_against_the_composed_tree()
        {
            // The identity rule the pair exists to preserve: the tree's root
            // definition IS the loaded asset, so an authored reference to root
            // from inside a chapter still finds a node. A cloned root would
            // strand this reference, and a root-granted modifier is legal
            // authoring (12.5).
            var rootDef = TestTree.MakeRoot("root");
            var records = TestTree.DeclareCurrency(rootDef, "records");
            var chapterDef = TestTree.MakeChapter("ch1");
            var tierDef = TestTree.MakeTier("tier1");
            chapterDef.children.Add(tierDef);

            var boost = TestTree.MakeDefinition<ModifierDefinition>("boost");
            boost.effects.Add(new Effect { target = "records", stat = Stat.Rate, multiplier = 2 });
            rootDef.modifiers.Add(boost);
            var grant = new AddModifier { scope = rootDef, modifier = boost };

            var root = ScopeState.Build(ComposedContent.Compose(rootDef, new[] { chapterDef }));
            var tier1 = root.FindInSubtree(tierDef);

            grant.Execute(new GameContext(tier1, new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc)));

            Assert.AreEqual(1, root.modifierStacks["boost"], "the grant landed on the composed root itself");
            Assert.AreSame(rootDef, root.Definition, "the tree's root is the composed asset, not a copy");
            Assert.AreEqual((BigNumber)0, root.balances["records"], "root's own declarations came with it");
        }

        // A wired child would be a second roster - unvalidated, and invisible to
        // the label path that is supposed to be the only one.
        [Test]
        public void A_serialized_root_child_is_refused()
        {
            var rootDef = TestTree.MakeRoot("root");
            rootDef.children.Add(TestTree.MakeChapter("ch1"));

            var thrown = Assert.Throws<InvalidOperationException>(
                () => ComposedContent.Compose(rootDef, new List<ChapterDefinition>()));
            StringAssert.Contains("serialized children", thrown.Message);
        }

        // Root.json validates on its own, before any chapter document exists -
        // an empty roster is a boot failure, never a composition one.
        [Test]
        public void A_root_with_no_chapters_composes_and_validates()
        {
            var rootDef = TestTree.MakeRoot("root");
            TestTree.DeclareCurrency(rootDef, "records");

            var content = ComposedContent.Compose(rootDef);

            Assert.AreEqual(0, content.Chapters.Count);
            Assert.AreEqual(0, ContentValidator.Validate(content).Findings.Count);
            Assert.AreEqual(0, ScopeState.Build(content).Children.Count);
        }

        // A label set arrives in whatever order Addressables hands it over, and
        // the state tree's child order is observable.
        [Test]
        public void The_roster_is_sorted_by_id()
        {
            var rootDef = TestTree.MakeRoot("root");
            var ch2 = TestTree.MakeChapter("ch2");
            var ch1 = TestTree.MakeChapter("ch1");

            var content = ComposedContent.Compose(rootDef, new[] { ch2, ch1 });

            Assert.AreEqual(new[] { "ch1", "ch2" }, new[] { content.Chapters[0].Id, content.Chapters[1].Id });
        }

        [Test]
        public void A_null_roster_entry_is_refused()
        {
            var rootDef = TestTree.MakeRoot("root");

            Assert.Throws<InvalidOperationException>(
                () => ComposedContent.Compose(rootDef, new ChapterDefinition[] { null }));
        }

        // Tree-wide scope-id uniqueness is a 12.12 check, and the composed tree
        // is what it runs on - composition itself stays out of it.
        [Test]
        public void Two_chapters_sharing_an_id_are_a_validation_finding()
        {
            var rootDef = TestTree.MakeRoot("root");
            var content = ComposedContent.Compose(rootDef,
                new[] { TestTree.MakeChapter("ch1"), TestTree.MakeChapter("ch1") });

            var report = ContentValidator.Validate(content);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.OfCheck(ValidationCheck.DuplicateId).Any(f => f.Message.Contains("'ch1'")),
                string.Join("\n", report.Findings));
        }
    }
}
