using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // How a section lays out its visible modules (design doc 12.11): List
    // stacks them down the card; Strip puts them in one row that scrolls
    // sideways, with a thin scroll bar beneath.
    public enum SectionLayout
    {
        List,
        Strip,
    }

    // One band of a chapter's screen: a header, a gate, and the widgets it
    // shows (design doc 12.11). Plain serialized data on the owning chapter -
    // nothing outside references a section, so it carries no id and no tags.
    [Serializable]
    public class SectionDefinition
    {
        // The on-screen header, optional. Authored text, because nothing else
        // can produce it - the content doc's row labels are for humans, not
        // ids. An empty title draws no header (12.11).
        public string title;

        // Optional player-facing text saying what the band is for, rendered
        // beneath the title by the section view (12.11). Never read by the
        // economy.
        public string description;

        // The gate, judged at this section's evaluation scope. Never null:
        // Always is how an author says the gate is open (12.12).
        [SerializeReference, SubclassPicker] public Condition visibleWhen;

        // The evaluation scope - the chapter or one of its descendants - which
        // is how a chapter-owned section legally gates on a tier flag (12.11).
        public ScopeDefinition scope;

        // How the modules container arranges the modules; List when unauthored.
        public SectionLayout layout = SectionLayout.List;

        public List<ModuleDefinition> modules = new();
    }
}
