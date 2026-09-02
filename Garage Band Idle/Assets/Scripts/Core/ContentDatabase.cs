using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RidiculousGaming.GarageBandIdle
{
    // Content loading (design doc 12.13). The content IS the scope tree: every
    // definition the game can reach hangs off a scope by direct reference, so
    // loading a scope loads its graph, and there is no id index to keep - a
    // reference is resolved by holding it, and an id stored in a FACT is
    // resolved by walking its scope outward.
    //
    // Two kinds of entry, and only two (12.14.5): the RootDefinition at a fixed
    // address, and every chapter root under one label. The label is consumed
    // exactly once, HERE, at the load boundary - like the save's scope names -
    // so no runtime read ever consults it and requirement 8 stands untouched.
    // Each chapter's own direct references pull its subtree with it.
    public class ContentDatabase
    {
        // The root asset's fixed address and the chapter roster's label, which
        // the importer assigns as a chapter document's whole roster act.
        public const string RootAddress = "root";
        public const string ChapterLabel = "chapter";

        private readonly List<AsyncOperationHandle> handles = new();

        // The composed pair, never a clone: scope operations resolve by asset
        // identity, so the tree's root must BE the loaded asset.
        public ComposedContent Root { get; private set; }

        public bool IsLoaded { get; private set; }

        // Blocking boot-time load of the content set, held for the database's
        // lifetime (releasing a handle releases its assets). A failed load
        // throws - missing content is a boot failure, not an empty tree
        // (12.14.6), and so is an empty roster: a game with no chapters is
        // broken content.
        //
        // The 12.12 pass runs HERE in development builds and fails loudly, on
        // the one production load path, so no boot code can forget it - the
        // findings ride out on the exception, or on Report when the load goes
        // through, for the driver to print. Release builds ship dev-validated
        // content and skip the cost.
        public static ContentDatabase LoadRoot(object rootKey, object chapterLabel)
        {
            var database = new ContentDatabase();
            try
            {
                var rootHandle = Addressables.LoadAssetAsync<RootDefinition>(rootKey);
                var root = rootHandle.WaitForCompletion();
                database.handles.Add(rootHandle);
                if (rootHandle.Status != AsyncOperationStatus.Succeeded || root == null)
                    throw new InvalidOperationException(
                        $"Addressables load for the root scope '{rootKey}' failed.", rootHandle.OperationException);

                var chapterHandle = Addressables.LoadAssetsAsync<ChapterDefinition>(chapterLabel, null);
                var chapters = chapterHandle.WaitForCompletion();
                database.handles.Add(chapterHandle);
                if (chapterHandle.Status != AsyncOperationStatus.Succeeded || chapters == null || chapters.Count == 0)
                    throw new InvalidOperationException(
                        $"no chapter is labeled '{chapterLabel}' - a game with no chapters is broken content (12.14.5).",
                        chapterHandle.OperationException);

                database.Root = ComposedContent.Compose(root, chapters);
                database.IsLoaded = true;
            }
            catch
            {
                database.Release();
                throw;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var report = database.Validate();
            if (report.HasErrors)
            {
                database.Release();
                throw new ContentValidationException("content validation failed (12.12).", report);
            }
            database.Report = report;
#endif
            return database;
        }

        // The pass's findings on a load that went through - warnings only, since
        // errors refuse the load. Null in release builds, which run no pass. The
        // driver prints it; the load itself prints nothing.
        public ValidationReport Report { get; private set; }

        public ValidationReport Validate() => ContentValidator.Validate(Root);

        public void Release()
        {
            foreach (var handle in handles)
                if (handle.IsValid())
                    Addressables.Release(handle);
            handles.Clear();
            Root = default;
            IsLoaded = false;
        }
    }
}
