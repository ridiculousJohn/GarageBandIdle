using System.Collections.Generic;
using System.Linq;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RidiculousGaming.GarageBandIdle
{
    // The production IDefinitionSource (design doc 12.13): every Definition
    // asset, discovered through Addressables by label (a label per type,
    // 12.14.5), indexed by id. Indexing is deliberately lenient: duplicate ids
    // are stored as found and refused by the validation pass (12.12), which
    // owns all findings - Get returns the first typed match so the validator
    // can describe a collision instead of an indexing exception hiding it.
    public class ContentDatabase : IDefinitionSource
    {
        private readonly List<Definition> definitions = new();
        private readonly Dictionary<string, List<Definition>> byId = new();
        private readonly List<AsyncOperationHandle<IList<Definition>>> handles = new();

        public IReadOnlyList<Definition> Definitions => definitions;

        public void Add(Definition definition)
        {
            if (definition == null)
                return;
            definitions.Add(definition);
            var id = definition.Id ?? string.Empty;
            if (!byId.TryGetValue(id, out var bucket))
                byId[id] = bucket = new List<Definition>();
            bucket.Add(definition);
        }

        public void AddRange(IEnumerable<Definition> range)
        {
            foreach (var definition in range)
                Add(definition);
        }

        public T Get<T>(string id) where T : Definition =>
            id != null && byId.TryGetValue(id, out var bucket) ? bucket.OfType<T>().FirstOrDefault() : null;

        public IEnumerable<T> All<T>() where T : Definition => definitions.OfType<T>();

        public ValidationReport Validate() => ContentValidator.Validate(this);

        // Blocking boot-time discovery: one Addressables load per label, held
        // for the database's lifetime (releasing a handle releases its assets).
        // A failed load throws - missing content is a boot failure, not an
        // empty database (12.14.6). Exercised against real content in build
        // step 8, when the importer creates the first labeled assets.
        //
        // The 12.12 pass runs HERE in development builds and fails loudly
        // (12.14.6) - on the one production load path, so no boot code can
        // forget it. Release builds ship dev-validated content and skip the
        // cost.
        public static ContentDatabase LoadFromAddressables(IEnumerable<string> labels)
        {
            var database = new ContentDatabase();
            foreach (var label in labels)
            {
                var handle = Addressables.LoadAssetsAsync<Definition>(label, null);
                var assets = handle.WaitForCompletion();
                database.handles.Add(handle);
                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    database.Release();
                    throw new System.InvalidOperationException(
                        $"Addressables load for label '{label}' failed.", handle.OperationException);
                }
                database.AddRange(assets);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var report = database.Validate();
            report.LogAll();
            if (report.HasErrors)
            {
                database.Release();
                throw new System.InvalidOperationException(
                    "content validation failed - see the logged errors (12.12).");
            }
#endif
            return database;
        }

        public void Release()
        {
            foreach (var handle in handles)
                Addressables.Release(handle);
            handles.Clear();
        }
    }
}
