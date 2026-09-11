using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Root's direct children. Idle is per-chapter, so its claim and clock live
    // on the state this makes (design doc 12.9).
    [CreateAssetMenu(menuName = "Garage Band Idle/Scope/Chapter")]
    public class ChapterDefinition : InteriorDefinition
    {
        // Evaluated from the chapter scope, so a later chapter can read the
        // root-owned completion flag of an earlier one by the ordinary outward
        // condition walk. Existing chapters are open unless they author a gate.
        [SerializeReference, SubclassPicker] public Condition unlock = new Always();

        // The authored screen, in order (design doc 12.11). Only a chapter has
        // one, and nothing outside references a section, so the sections are
        // inline data here rather than assets of their own.
        public List<UI.SectionDefinition> sections = new();

        // Chapter-boundary content (section 10): declared on the chapter, bound
        // by a story_row module, and read by the host's marked-beat walk over
        // this list. Only a chapter has one, because a beat tells a chapter's
        // own opening or its capstone.
        public List<Story.StoryBeatDefinition> storyBeats = new();

        // Declaration is ownership (12.3): a beat's declaring scope is found by
        // walking outward to the chapter, exactly as an event's host is.
        internal override bool Declares(Definition definition) =>
            base.Declares(definition) || Holds(storyBeats, definition);

        internal override ScopeState CreateState(ScopeState parent) => new ChapterScopeState(this, parent);
    }
}
