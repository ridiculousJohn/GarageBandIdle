using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Loop
{
    // One chapter (design doc section 2): story framing, tuning constants, and
    // ordered references to the chapter's content. GameManager discovers chapter
    // assets through Addressables by label; everything a chapter references
    // loads with it and needs no discovery of its own.
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
        [Tooltip("Sections reveal in place as their conditions are met; module order within a section is list order.")]
        private List<ChapterSection> _sections = new();

        [SerializeField]
        [Tooltip("Display order is list order.")]
        private List<GeneratorDefinition> _generators = new();

        [SerializeField]
        private List<UpgradeDefinition> _upgrades = new();

        [SerializeField]
        private List<CoverDefinition> _covers = new();

        [SerializeField]
        private List<EventDefinition> _events = new();

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
        public IReadOnlyList<ChapterSection> Sections => _sections;
        public IReadOnlyList<GeneratorDefinition> Generators => _generators;
        public IReadOnlyList<UpgradeDefinition> Upgrades => _upgrades;
        public IReadOnlyList<CoverDefinition> Covers => _covers;
        public IReadOnlyList<EventDefinition> Events => _events;

#if UNITY_EDITOR
        // importer-only: chapter assets are generated from chapter JSON
        public void EditorInitialize(string id, int index, string displayName, string theme,
            string storyBeatOpen, string storyBeatCapstone, int capstoneRecordsGate,
            double tapBaseValue, double recordBuffPerRecord, FansConfig fans, RehearsalConfig rehearsal,
            List<ChapterSection> sections, List<GeneratorDefinition> generators, List<UpgradeDefinition> upgrades,
            List<CoverDefinition> covers, List<EventDefinition> events)
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
            _sections = sections;
            _generators = generators;
            _upgrades = upgrades;
            _covers = covers;
            _events = events;
        }
#endif
    }

    // Fan accrual tuning (design doc section 6): fan rate is a function of band
    // size and time only, never Cash — behavior arrives in the fans slice.
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

    // Rehearsal tuning: points that fill the Learn Covers bars — behavior
    // arrives in the covers slice.
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
