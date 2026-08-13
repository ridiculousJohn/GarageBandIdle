using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Loop
{
    // One story beat: a piece of authored narrative shown to the player at a
    // progression moment (design doc section 2).
    //
    // This is content, and being content is the whole point. Beats used to be two
    // inline string fields on ChapterDefinition - the only content kind with no
    // definition type - which is why they could not be revealed, listed, or
    // referenced the way everything else can. Generators, upgrades and bars each
    // have a definition, a chapter id list, and a module that renders them; a beat
    // now has the same three, so "any number of beats, each unlocked by a
    // milestone" needs no new mechanism.
    //
    // It carries NO unlock condition and NO scope, deliberately:
    //
    // - Reveal is its SECTION's visibleWhen, exactly as it is for the Jam button.
    //   A per-beat gate would be a second answer to a question the section already
    //   answers, and the section is where every other module's reveal lives. This
    //   is what parameterized module entries made possible: two beats can share one
    //   card prefab because the entry names which beat it presents.
    // - Nothing here is granted, so there is no effect lifetime to declare. The
    //   read latch below is an ordinary flag, and a flag's lifetime is declared
    //   once on its FlagDeclaration (rule 11).
    [CreateAssetMenu(
        fileName = "NewStoryBeat",
        menuName = "GarageBandIdle/Story Beat")]
    public class StoryBeatDefinition : Definition
    {
        [SerializeField]
        [TextArea]
        [Tooltip("The narrative text shown on the card.")]
        private string _text;

        [SerializeField]
        [Tooltip("Optional: flag latched when the player dismisses this beat, for content that gates on " +
            "having read it. Must be declared in the chapter's flags list. Empty means nothing records the read.")]
        private string _readFlagId;

        public string Text => _text;
        public string ReadFlagId => _readFlagId;

#if UNITY_EDITOR
        // importer-only: story beat assets are generated from chapter JSON
        public void EditorInitialize(string id, string text, string readFlagId)
        {
            SetIdentity(id);
            _text = text;
            _readFlagId = readFlagId;
        }
#endif
    }
}
