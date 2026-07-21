using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Progress flags: string ids set once and observed anywhere — the single
    // reveal registry (design doc section 12, rule 9). Content-unlock upgrades
    // and setFlag rewards set them; sections, currencies, and gates observe them
    // through FlagSetCondition. Flags only ever latch on — scoping/reset rules
    // arrive with the save/prestige slices.
    public class FlagSystem
    {
        private readonly HashSet<string> _flags = new();

        // the chapter's declared flag ids; null means unrestricted (no chapter
        // loaded, or a test fixture that doesn't care about declarations)
        private readonly HashSet<string> _known;

        // fires once per flag, when it is first set
        public event Action<string> FlagSet;

        public FlagSystem() { }

        public FlagSystem(IEnumerable<string> knownIds)
        {
            if (knownIds != null)
                _known = new HashSet<string>(knownIds);
        }

        // false only when a declared-flags list exists and the id is not on it;
        // validation uses this to catch typos in content
        public bool IsKnown(string id) => _known == null || _known.Contains(id);

        public bool IsSet(string id) => _flags.Contains(id);

        public void Set(string id)
        {
            // an undeclared flag is a content mistake: report loudly but still
            // latch, so a typo degrades to a warning rather than lost progress
            if (!IsKnown(id))
                Debug.LogError($"FlagSystem: flag '{id}' is not declared by the chapter's flags list.");

            if (_flags.Add(id))
                FlagSet?.Invoke(id);
        }
    }
}
