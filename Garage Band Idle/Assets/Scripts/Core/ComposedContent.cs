using System;
using System.Collections.Generic;
using System.Linq;

namespace RidiculousGaming.GarageBandIdle
{
    // The runtime content set (design doc 12.14.5): the root asset plus the
    // chapter roster. Root's serialized child list is EMPTY by contract - the
    // chapter DOCUMENTS are the roster, carried in by the Addressables `chapter`
    // label - so this pair is what "children of root" means. Everything deeper
    // is a serialized child list, exactly as authored.
    //
    // A pair and never a clone: scope operations resolve by asset identity, so
    // the tree's root must BE the loaded asset. A clone would strand every
    // authored reference to root, and a root-granted modifier is legal
    // authoring. Nothing here is mutated, so nothing writes through to the
    // editor asset either.
    public readonly struct ComposedContent
    {
        public RootDefinition Root { get; }

        // Sorted by id, until ordering becomes an authored fact. A label set
        // arrives in whatever order Addressables hands it over, and the state
        // tree's child order is observable.
        public IReadOnlyList<ChapterDefinition> Chapters { get; }

        private ComposedContent(RootDefinition root, IReadOnlyList<ChapterDefinition> chapters)
        {
            Root = root;
            Chapters = chapters;
        }

        // The one seam boot, the importer's preflight, and the tests all
        // assemble through, so what the walkthroughs exercise is what ships.
        // Zero chapters is legal HERE - root.json validates on its own before
        // any chapter document exists; it is the boot load that refuses an
        // empty roster.
        public static ComposedContent Compose(RootDefinition root, IEnumerable<ChapterDefinition> chapters = null)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root), "ComposedContent: composing requires a root scope.");

            // A wired child would be a second roster, unvalidated and invisible
            // to the label path - two answers to "which chapters exist" with
            // nothing to reconcile them.
            if (root.children.Count > 0)
                throw new InvalidOperationException(
                    $"root scope '{root.Id}' has {root.children.Count} serialized children; the chapter documents are the roster (12.14.5).");

            var roster = chapters == null
                ? new List<ChapterDefinition>()
                : chapters.ToList();
            for (var i = 0; i < roster.Count; i++)
                if (roster[i] == null)
                    throw new InvalidOperationException($"the chapter roster has a null entry at [{i}].");

            // A duplicate chapter id is NOT refused here: tree-wide scope-id
            // uniqueness is already a 12.12 check, and the composed tree is what
            // it runs on.
            roster.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return new ComposedContent(root, roster);
        }
    }
}
