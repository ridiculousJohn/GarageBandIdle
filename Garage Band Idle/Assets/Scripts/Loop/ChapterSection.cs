using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Loop
{
    // One section of a chapter: a group of UI/gameplay modules that becomes
    // visible together when its conditions hold (the design doc's progressive
    // reveal, section 2). Modules are addressable prefab addresses, so a new
    // module in a section is a data change only.
    [Serializable]
    public class ChapterSection
    {
        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [Tooltip("Addressable addresses of module prefabs, e.g. module/tap.")]
        private List<string> _moduleAddresses = new();

        [SerializeField]
        [Tooltip("All conditions must hold for the section to reveal; empty = visible from chapter start. Reveal latches on.")]
        private List<GateCondition> _visibleWhen = new();

        public string Id => _id;
        public string DisplayName => _displayName;
        public IReadOnlyList<string> ModuleAddresses => _moduleAddresses;
        public IReadOnlyList<GateCondition> VisibleWhen => _visibleWhen;

        public ChapterSection() { }

#if UNITY_EDITOR
        // importer-only: sections are generated from chapter JSON
        public ChapterSection(string id, string displayName, List<string> moduleAddresses, List<GateCondition> visibleWhen)
        {
            _id = id;
            _displayName = displayName;
            _moduleAddresses = moduleAddresses;
            _visibleWhen = visibleWhen;
        }
#endif
    }
}
