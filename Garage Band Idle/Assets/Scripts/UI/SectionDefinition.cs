using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One band of a chapter's screen: a header, a gate, and the widgets it
    // shows (design doc 12.11). Plain serialized data on the owning chapter -
    // nothing outside references a section, so it carries no id and no tags.
    [Serializable]
    public class SectionDefinition
    {
        // The on-screen header. Authored text, because nothing else can produce
        // it - the content doc's row labels are for humans, not ids.
        public string title;

        // The gate, judged at this section's evaluation scope. Never null:
        // Always is how an author says the gate is open (12.12).
        [SerializeReference, SubclassPicker] public Condition visibleWhen;

        // The evaluation scope - the chapter or one of its descendants - which
        // is how a chapter-owned section legally gates on a tier flag (12.11).
        public ScopeDefinition scope;

        public List<ModuleDefinition> modules = new();
    }
}
