using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One widget on a section: what renders it, what it binds, and where it
    // evaluates (design doc 12.11). Inline serialized data on the section, the
    // same plain shape the section itself is.
    [Serializable]
    public class ModuleDefinition
    {
        // Names a widget through the ModuleRegistry rather than a content
        // asset: a new widget type is a prefab plus a registry entry (12.11).
        public string prefabId;

        // What the widget shows - a producer, a currency, an event. Base-typed
        // because modules bind different families, and null for a list module,
        // whose content is the evaluation scope's own declaration lists.
        public Definition content;

        // Optional gate, judged at this module's scope. Absent means always
        // visible - a module is a row of the section's band, not a gate (12.11).
        [SerializeReference, SubclassPicker] public Condition visibleWhen;

        // Where the bound content resolves and the gate is judged. Import
        // normalizes every module to a concrete scope, so nothing computes a
        // default at runtime (12.11).
        public ScopeDefinition scope;

        // prefabId becomes a registry key, and the same grammar ids use keeps
        // it a key. \z, not $: $ also matches before a final newline, which the
        // registry's exact lookup never would.
        public static readonly Regex PrefabIdGrammar = new(@"\A[a-z0-9_]+\z", RegexOptions.Compiled);
    }
}
