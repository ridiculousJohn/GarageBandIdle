using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Base of every content family: an id unique across the whole scope tree plus
    // free-form tags an Effect target can match (design doc 12.2). Identity is
    // read-only at runtime; the JSON importer and tests assign it in the editor.
    public abstract class Definition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private List<string> tags = new();

        public string Id => id;
        public IReadOnlyList<string> Tags => tags;

        public bool HasTag(string tag) => tags.Contains(tag);

#if UNITY_EDITOR
        // Editor-time initialization for the importer and tests. Runtime code
        // never assigns identity.
        public void EditorInit(string newId, params string[] newTags)
        {
            id = newId;
            tags = new List<string>(newTags);
        }
#endif
    }
}
