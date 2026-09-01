using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Base of every content family: an id unique along any CHAIN that can see it
    // - sibling subtrees may reuse one freely, since neither can reach the
    // other - plus free-form tags an Effect target can match (design doc 12.2,
    // 12.3). Identity is
    // read-only at runtime; the JSON importer and tests assign it in the editor.
    public abstract class Definition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private List<string> tags = new();

        // The on-screen name, authored because nothing derives "Three-Chord
        // Anthem" from `cover_3`. Required on the families the widgets render by
        // construction and on any content a module binds; optional elsewhere
        // (12.11). Public like every other authored member - only id and tags
        // are private, because they are identity.
        public string displayName;

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
