using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Root's direct children. Idle is per-chapter, so its claim and clock live
    // on the state this makes (design doc 12.9).
    [CreateAssetMenu(menuName = "Garage Band Idle/Scope/Chapter")]
    public class ChapterDefinition : InteriorDefinition
    {
        // The authored screen, in order (design doc 12.11). Only a chapter has
        // one, and nothing outside references a section, so the sections are
        // inline data here rather than assets of their own.
        public List<UI.SectionDefinition> sections = new();

        internal override ScopeState CreateState(ScopeState parent) => new ChapterScopeState(this, parent);
    }
}
