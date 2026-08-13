using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Loop
{
    // One module placed in a section: the prefab that presents it, and OPTIONALLY
    // which definition it presents.
    //
    // The id exists because a module address alone cannot say which of several
    // things a prefab is showing. Most modules do not need it - a generator list
    // renders the chapter's whole generator roster, a currency header the chapter's
    // revealed currencies - but a module presenting exactly ONE definition has no
    // other way to be told which: two story-beat cards would share one prefab and
    // one text, and the Jam button had a producer authored on the producer asset
    // that nothing read, so a tap fired every tap producer in the chapter.
    //
    // Which definition FAMILY the id names is the module's business, not this
    // type's - the tap module reads a producer id, a beat card reads a story-beat
    // id - which is what keeps this one field rather than one per family.
    [Serializable]
    public class SectionModule
    {
        [SerializeField]
        [Tooltip("Addressable address of the module prefab, e.g. module/tap.")]
        private string _address;

        [SerializeField]
        [Tooltip("Optional: the definition this module instance presents (a producer id for a tap " +
            "button, a story-beat id for a card). Leave empty for modules that render a whole roster.")]
        private string _definitionId;

        public string Address => _address;
        public string DefinitionId => _definitionId;

        public SectionModule() { }

        public SectionModule(string address, string definitionId = null)
        {
            _address = address;
            _definitionId = definitionId;
        }
    }

    // One section of a chapter: a group of UI/gameplay modules visible exactly
    // while its condition holds (the design doc's progressive reveal, section
    // 2). Visibility is a PURE function of visibleWhen - no latch here, and
    // deliberately so: persistence is a property of state, not of UI, so "stays
    // once earned" is authored by gating on a fact that persists (a flag, whose
    // declaration carries the lifetime; a monotonic earned-total) rather than
    // by the section remembering anything. Modules are addressable prefab
    // addresses resolved through the module registry, so a new module in a
    // section is a data change only. Discovered by Addressables label like
    // every definition.
    [CreateAssetMenu(
        fileName = "NewSection",
        menuName = "GarageBandIdle/Section")]
    public class SectionDefinition : Definition
    {
        [SerializeField]
        private string _displayName;

        [SerializeField]
        [Tooltip("Modules in this section: a prefab address plus, where the module presents one " +
            "specific definition, that definition's id.")]
        private List<SectionModule> _modules = new();

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Evaluated LIVE: the section shows exactly while this holds; none = always visible. " +
            "A section carries no latch of its own - gate on a flag (or another latched/monotonic fact) " +
            "and the section inherits that fact's lifetime.")]
        private Condition _visibleWhen;

        public string DisplayName => _displayName;
        public IReadOnlyList<SectionModule> Modules => _modules;
        public Condition VisibleWhen => _visibleWhen;

#if UNITY_EDITOR
        // importer-only: section assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, List<SectionModule> modules,
            Condition visibleWhen)
        {
            SetIdentity(id);
            _displayName = displayName;
            _modules = modules;
            _visibleWhen = visibleWhen;
        }
#endif
    }
}
