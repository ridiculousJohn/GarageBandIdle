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
    public class ChapterDefinition : Definition
    {
        [SerializeField]
        [Tooltip("1-based chapter order; the lowest index is the starting chapter.")]
        private int _index;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [TextArea]
        private string _theme;

        [Header("Tuning")]
        [SerializeField]
        private RecordBuffConfig _recordBuff = new();

        [SerializeField]
        private FansConfig _fans = new();

        // The chapter's prestige rungs (design doc rule 14) - the album and the
        // capstone as two instances of ONE shape, where two bespoke config
        // classes used to encode the same fixed-depth assumption twice. A LIST
        // because the pre-step-7 chapter is a single scope filing both; step 7
        // re-authors them onto the scope tree (the album rung on the tier
        // scope, the capstone rung on the chapter scope) and this field goes
        // with the rest of the chapter's content lists.
        [SerializeField]
        private List<PrestigeTierDefinition> _rungs = new();

        [Header("Content")]
        [SerializeField]
        [Tooltip("Progress flags this chapter's content may set - the single reveal registry, each with its " +
            "latch's declared lifetime. Anything not listed here is a typo.")]
        private List<FlagDeclaration> _flags = new();

        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currencies this chapter declares (fill currencies like rehearsal). How they are earned lives on producers, never here.")]
        private List<string> _currencyIds = new();

        [SerializeField]
        [DefinitionId(typeof(ProducerDefinition))]
        [Tooltip("Module-held production sources (the Jam button). Only the CURRENT chapter's producers fire.")]
        private List<string> _producerIds = new();

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

        [SerializeField]
        [DefinitionId(typeof(StoryBeatDefinition))]
        [Tooltip("Story beats this chapter authors. A section's module entry names which one a card " +
            "presents; reveal is that section's visibleWhen, like every other module.")]
        private List<string> _storyBeatIds = new();

        public int Index => _index;
        public string DisplayName => _displayName;
        public string Theme => _theme;
        public RecordBuffConfig RecordBuff => _recordBuff;
        public FansConfig Fans => _fans;
        public IReadOnlyList<PrestigeTierDefinition> Rungs => _rungs;
        public IReadOnlyList<FlagDeclaration> Flags => _flags;

        // the declared ids alone, for consumers that only resolve or validate
        // identity (the scope is FlagSystem's business, given the declarations)
        public IReadOnlyList<string> FlagIds
        {
            get
            {
                var ids = new List<string>(_flags.Count);
                foreach (var flag in _flags)
                    ids.Add(flag?.Id);
                return ids;
            }
        }

        public IReadOnlyList<string> CurrencyIds => _currencyIds;
        public IReadOnlyList<string> ProducerIds => _producerIds;
        public IReadOnlyList<string> SectionIds => _sectionIds;
        public IReadOnlyList<string> GeneratorIds => _generatorIds;
        public IReadOnlyList<string> UpgradeIds => _upgradeIds;
        public IReadOnlyList<string> BarGroupIds => _barGroupIds;
        public IReadOnlyList<string> EventIds => _eventIds;
        public IReadOnlyList<string> StoryBeatIds => _storyBeatIds;

#if UNITY_EDITOR
        // importer-only: chapter assets are generated from chapter JSON
        public void EditorInitialize(string id, int index, string displayName, string theme,
            RecordBuffConfig recordBuff, FansConfig fans, List<PrestigeTierDefinition> rungs,
            List<FlagDeclaration> flags, List<string> currencyIds, List<string> producerIds,
            List<string> sectionIds, List<string> generatorIds, List<string> upgradeIds,
            List<string> barGroupIds, List<string> eventIds, List<string> storyBeatIds)
        {
            SetIdentity(id);
            _index = index;
            _displayName = displayName;
            _theme = theme;
            _recordBuff = recordBuff;
            _fans = fans;
            _rungs = rungs ?? new List<PrestigeTierDefinition>();
            _flags = flags;
            _currencyIds = currencyIds;
            _producerIds = producerIds;
            _sectionIds = sectionIds;
            _generatorIds = generatorIds;
            _upgradeIds = upgradeIds;
            _barGroupIds = barGroupIds;
            _eventIds = eventIds;
            _storyBeatIds = storyBeatIds ?? new List<string>();
        }
#endif
    }

    // The Records buff tuning (design doc sections 3 and 5). A multiplier
    // declares which currencies it affects - it is an output effect, not a
    // property of the currency being generated - so production of a currency
    // no multiplier names is untouched. Records affects Cash in Chapter 1.
    [Serializable]
    public class RecordBuffConfig
    {
        [SerializeField]
        [Tooltip("Permanent global income bonus per Record, e.g. 0.02 for +2% each.")]
        private double _perRecord;

        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency ids whose generator production this multiplier applies to. Anything not listed is untouched.")]
        private List<string> _affectsCurrencyIds = new();

        public double PerRecord => _perRecord;
        public IReadOnlyList<string> AffectsCurrencyIds => _affectsCurrencyIds;

        public RecordBuffConfig() { }

#if UNITY_EDITOR
        public RecordBuffConfig(double perRecord, List<string> affectsCurrencyIds)
        {
            _perRecord = perRecord;
            _affectsCurrencyIds = affectsCurrencyIds;
        }
#endif
    }

    // What the chapter declares about fans that is NOT production (design doc
    // section 6): the currency id, which is not a binding for accrual but the
    // answer to "which currency is this chapter's fans", asked by the checks that
    // keep fans resetting on release and out of the Records multiplier (section
    // 11).
    //
    // No per-bandmate bonus sits here: that number is each bandmate generator's own
    // fans CONTRIBUTION (rule 13), which is what makes band size raise the rate,
    // since a generator's lines always scale with its owned count. A chapter-level
    // number would be a source dressed as a modifier, and would need a bool on every
    // generator to say who it applied to.
    [Serializable]
    public class FansConfig
    {
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency id this chapter treats as fans. Accrual itself is a contribution on a producer or generator.")]
        private string _currencyId;

        public string CurrencyId => _currencyId;

        public FansConfig() { }

#if UNITY_EDITOR
        public FansConfig(string currencyId)
        {
            _currencyId = currencyId;
        }
#endif
    }

}
