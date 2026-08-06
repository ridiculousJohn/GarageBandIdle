using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Loop
{
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
    public class SectionDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [Tooltip("Addressable addresses of module prefabs, e.g. module/tap.")]
        private List<string> _moduleAddresses = new();

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Evaluated LIVE: the section shows exactly while this holds; none = always visible. " +
            "A section carries no latch of its own - gate on a flag (or another latched/monotonic fact) " +
            "and the section inherits that fact's lifetime.")]
        private Condition _visibleWhen;

        public string Id => _id;
        public string DisplayName => _displayName;
        public IReadOnlyList<string> ModuleAddresses => _moduleAddresses;
        public Condition VisibleWhen => _visibleWhen;

#if UNITY_EDITOR
        // importer-only: section assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, List<string> moduleAddresses,
            Condition visibleWhen)
        {
            _id = id;
            _displayName = displayName;
            _moduleAddresses = moduleAddresses;
            _visibleWhen = visibleWhen;
        }
#endif
    }
}
