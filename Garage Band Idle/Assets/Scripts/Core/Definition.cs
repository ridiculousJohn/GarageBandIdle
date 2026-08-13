using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // What every piece of content is (design doc section 12, rule 10): a stable
    // id, and a list of tags naming the sets it belongs to.
    //
    // Both were previously per class. All twelve definition types declared their
    // own `_id`/`Id` with nothing enforcing it, so two places worked around its
    // absence: ContentDatabase passed `d => d.Id` twelve times because Load<T>
    // could only constrain to ScriptableObject, and DefinitionIdDrawer found the
    // property by REFLECTION and logged a runtime error for a type that lacked
    // one. Declaring it once turns both into a type check.
    //
    // Tags live here rather than on whichever class first needs a set, because
    // the alternative is what the codebase already did: GeneratorDefinition grew
    // an `isBandmate` bool, which is a named set of generators with a system
    // branching on it, unavailable to anything else and duplicated the next time
    // some other family wants one. A tag is open content, so `bandmate` and
    // `rhythm_section` are authored rather than coded, and a modifier can select
    // by them exactly as it selects by id (rule 11).
    public abstract class Definition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        [Tooltip("Sets this belongs to. A modifier can select a tag exactly as it selects an id.")]
        private string[] _tags = Array.Empty<string>();

        public string Id => _id;

        public IReadOnlyList<string> Tags => _tags ?? Array.Empty<string>();

        // Whether this definition carries a tag. Asked through the definition
        // rather than by indexing its list, so a later tag form (a hierarchy, a
        // path) changes what answers here and nothing that asks.
        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || _tags == null)
                return false;

            foreach (var owned in _tags)
            {
                if (owned == tag)
                    return true;
            }
            return false;
        }

#if UNITY_EDITOR
        // importer-only: every definition asset is generated from chapter JSON,
        // so the identity is set by the same call that sets the rest of the type's
        // fields. Protected rather than public because a definition's own
        // EditorInitialize is the one door - two ways to set an id is how one of
        // them ends up never called.
        protected void SetIdentity(string id, IEnumerable<string> tags = null)
        {
            _id = id;
            _tags = ToArray(tags);
        }

        private static string[] ToArray(IEnumerable<string> tags)
        {
            if (tags == null)
                return Array.Empty<string>();

            var list = new List<string>();
            foreach (var tag in tags)
            {
                // an empty entry is an authoring slip, not a set nothing belongs
                // to: dropping it here keeps every reader from re-checking
                if (!string.IsNullOrEmpty(tag) && !list.Contains(tag))
                    list.Add(tag);
            }
            return list.ToArray();
        }
#endif
    }
}
