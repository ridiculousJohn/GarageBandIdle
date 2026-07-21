using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Loop
{
    // One chapter (design doc section 2): story framing, tuning constants, the
    // chapter's declared flags, and ordered id lists naming its content. Every
    // definition asset is discovered through Addressables by label (rule 10);
    // the chapter references content by id only, resolved via ContentDatabase,
    // so it holds no direct asset references.
    [CreateAssetMenu(
        fileName = "NewChapter",
        menuName = "GarageBandIdle/Chapter")]
    public class ChapterDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        [Tooltip("1-based chapter order; the lowest index is the starting chapter.")]
        private int _index;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [TextArea]
        private string _theme;

        [Header("Story")]
        [SerializeField]
        [TextArea]
        private string _storyBeatOpen;

        [SerializeField]
        [TextArea]
        private string _storyBeatCapstone;

        [Header("Tuning")]
        [SerializeField]
        [Tooltip("Cumulative Records required to unlock the capstone gig. The primary pacing knob.")]
        private int _capstoneRecordsGate;

        [SerializeField]
        [Tooltip("Cash granted per Jam tap before tap buffs.")]
        private double _tapBaseValue;

        [SerializeField]
        [Tooltip("Permanent global income bonus per Record, e.g. 0.02 for +2% each.")]
        private double _recordBuffPerRecord;

        [SerializeField]
        private FansConfig _fans = new();

        [SerializeField]
        private RehearsalConfig _rehearsal = new();

        [Header("Content")]
        [SerializeField]
        [Tooltip("Progress flags this chapter's content may set — the single reveal registry. Anything not listed here is a typo.")]
        private List<string> _flagIds = new();

        [SerializeField]
        [DefinitionId(typeof(SectionDefinition))]
        [Tooltip("Sections in layout order; each reveals when its own visibleWhen holds.")]
        private List<string> _sectionIds = new();

        [SerializeField]
        [DefinitionId(typeof(GeneratorDefinition))]
        [Tooltip("Display order is list order.")]
        private List<string> _generatorIds = new();

        [SerializeField]
        [DefinitionId(typeof(UpgradeDefinition))]
        private List<string> _upgradeIds = new();

        [SerializeField]
        [DefinitionId(typeof(BarGroupDefinition))]
        private List<string> _barGroupIds = new();

        [SerializeField]
        [DefinitionId(typeof(EventDefinition))]
        private List<string> _eventIds = new();

        public string Id => _id;
        public int Index => _index;
        public string DisplayName => _displayName;
        public string Theme => _theme;
        public string StoryBeatOpen => _storyBeatOpen;
        public string StoryBeatCapstone => _storyBeatCapstone;
        public int CapstoneRecordsGate => _capstoneRecordsGate;
        public double TapBaseValue => _tapBaseValue;
        public double RecordBuffPerRecord => _recordBuffPerRecord;
        public FansConfig Fans => _fans;
        public RehearsalConfig Rehearsal => _rehearsal;
        public IReadOnlyList<string> FlagIds => _flagIds;
        public IReadOnlyList<string> SectionIds => _sectionIds;
        public IReadOnlyList<string> GeneratorIds => _generatorIds;
        public IReadOnlyList<string> UpgradeIds => _upgradeIds;
        public IReadOnlyList<string> BarGroupIds => _barGroupIds;
        public IReadOnlyList<string> EventIds => _eventIds;

#if UNITY_EDITOR
        // importer-only: chapter assets are generated from chapter JSON
        public void EditorInitialize(string id, int index, string displayName, string theme,
            string storyBeatOpen, string storyBeatCapstone, int capstoneRecordsGate,
            double tapBaseValue, double recordBuffPerRecord, FansConfig fans, RehearsalConfig rehearsal,
            List<string> flagIds, List<string> sectionIds, List<string> generatorIds,
            List<string> upgradeIds, List<string> barGroupIds, List<string> eventIds)
        {
            _id = id;
            _index = index;
            _displayName = displayName;
            _theme = theme;
            _storyBeatOpen = storyBeatOpen;
            _storyBeatCapstone = storyBeatCapstone;
            _capstoneRecordsGate = capstoneRecordsGate;
            _tapBaseValue = tapBaseValue;
            _recordBuffPerRecord = recordBuffPerRecord;
            _fans = fans;
            _rehearsal = rehearsal;
            _flagIds = flagIds;
            _sectionIds = sectionIds;
            _generatorIds = generatorIds;
            _upgradeIds = upgradeIds;
            _barGroupIds = barGroupIds;
            _eventIds = eventIds;
        }
#endif
    }

    // Fan accrual tuning (design doc section 6): fan rate is a function of band
    // size and time only, never Cash.
    [Serializable]
    public class FansConfig
    {
        [SerializeField]
        private double _baseFansPerSec;

        [SerializeField]
        [Tooltip("Bonus fans/sec per owned bandmate unit (not gear like the practice amp).")]
        private double _perBandmateOwnedBonus;

        public double BaseFansPerSec => _baseFansPerSec;
        public double PerBandmateOwnedBonus => _perBandmateOwnedBonus;

        public FansConfig() { }

#if UNITY_EDITOR
        public FansConfig(double baseFansPerSec, double perBandmateOwnedBonus)
        {
            _baseFansPerSec = baseFansPerSec;
            _perBandmateOwnedBonus = perBandmateOwnedBonus;
        }
#endif
    }

    // Rehearsal earn tuning: the fill currency accrues from a passive tick plus
    // Jam taps (engagement, never Cash) — behavior arrives in the bars slice.
    [Serializable]
    public class RehearsalConfig
    {
        [SerializeField]
        private double _pointsPerSec;

        [SerializeField]
        private double _pointsPerTap;

        public double PointsPerSec => _pointsPerSec;
        public double PointsPerTap => _pointsPerTap;

        public RehearsalConfig() { }

#if UNITY_EDITOR
        public RehearsalConfig(double pointsPerSec, double pointsPerTap)
        {
            _pointsPerSec = pointsPerSec;
            _pointsPerTap = pointsPerTap;
        }
#endif
    }
}
