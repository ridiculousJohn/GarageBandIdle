using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RidiculousGaming.GarageBandIdle
{
    // Content loading (design doc 12.13). The content IS the scope tree: every
    // definition the game can reach hangs off the root scope by direct
    // reference, so loading the root loads the graph, and there is no id index
    // to keep - a reference is resolved by holding it, and an id stored in a
    // FACT is resolved by walking its scope outward.
    //
    // The whole graph therefore loads at boot, and that is the current policy:
    // children are direct references, so Addressables pulls every chapter as a
    // dependency of the root, and the 12.12 pass walks all of it anyway. Making
    // a chapter arrive when it opens would take an indirect handle per chapter,
    // staged validation, staged state construction, and handle lifetimes to
    // manage - a real architectural change, worth doing only if measurements
    // ever call for it.
    public class ContentDatabase
    {
        // Typed to the root's authored kind: the tree build needs a
        // RootDefinition, and an Addressables key naming a chapter or a tier
        // fails the load here rather than producing a tree rooted at one.

        private readonly List<AsyncOperationHandle<RootDefinition>> handles = new();

        public RootDefinition Root { get; private set; }

        // Blocking boot-time load of the root scope, held for the database's
        // lifetime (releasing a handle releases its assets). A failed load
        // throws - missing content is a boot failure, not an empty tree
        // (12.14.6).
        //
        // The 12.12 pass runs HERE in development builds and fails loudly, on
        // the one production load path, so no boot code can forget it. Release
        // builds ship dev-validated content and skip the cost.
        public static ContentDatabase LoadRoot(object rootKey)
        {
            var database = new ContentDatabase();
            var handle = Addressables.LoadAssetAsync<RootDefinition>(rootKey);
            var root = handle.WaitForCompletion();
            database.handles.Add(handle);
            if (handle.Status != AsyncOperationStatus.Succeeded || root == null)
            {
                database.Release();
                throw new InvalidOperationException(
                    $"Addressables load for the root scope '{rootKey}' failed.", handle.OperationException);
            }
            database.Root = root;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var report = database.Validate();
            report.LogAll();
            if (report.HasErrors)
            {
                database.Release();
                throw new InvalidOperationException(
                    "content validation failed - see the logged errors (12.12).");
            }
#endif
            return database;
        }

        public ValidationReport Validate() => ContentValidator.Validate(Root);

        public void Release()
        {
            foreach (var handle in handles)
                Addressables.Release(handle);
            handles.Clear();
            Root = null;
        }
    }
}
