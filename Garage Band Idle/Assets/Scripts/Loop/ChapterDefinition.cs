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
        private RecordBuffConfig _recordBuff = new();

        [SerializeField]
        private FansConfig _fans = new();

        [SerializeField]
        private AlbumConfig _album = new();

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

        public string Id => _id;
        public int Index => _index;
        public string DisplayName => _displayName;
        public string Theme => _theme;
        public string StoryBeatOpen => _storyBeatOpen;
        public string StoryBeatCapstone => _storyBeatCapstone;
        public int CapstoneRecordsGate => _capstoneRecordsGate;
        public RecordBuffConfig RecordBuff => _recordBuff;
        public FansConfig Fans => _fans;
        public AlbumConfig Album => _album;
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

#if UNITY_EDITOR
        // importer-only: chapter assets are generated from chapter JSON
        public void EditorInitialize(string id, int index, string displayName, string theme,
            string storyBeatOpen, string storyBeatCapstone, int capstoneRecordsGate,
            RecordBuffConfig recordBuff, FansConfig fans, AlbumConfig album,
            List<FlagDeclaration> flags, List<string> currencyIds, List<string> producerIds,
            List<string> sectionIds, List<string> generatorIds, List<string> upgradeIds,
            List<string> barGroupIds, List<string> eventIds)
        {
            _id = id;
            _index = index;
            _displayName = displayName;
            _theme = theme;
            _storyBeatOpen = storyBeatOpen;
            _storyBeatCapstone = storyBeatCapstone;
            _capstoneRecordsGate = capstoneRecordsGate;
            _recordBuff = recordBuff;
            _fans = fans;
            _album = album ?? new AlbumConfig();
            _flags = flags;
            _currencyIds = currencyIds;
            _producerIds = producerIds;
            _sectionIds = sectionIds;
            _generatorIds = generatorIds;
            _upgradeIds = upgradeIds;
            _barGroupIds = barGroupIds;
            _eventIds = eventIds;
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

    // What the chapter declares about the album beyond the payout formula
    // (design doc section 5). ReleaseWhen is the OFFER's gate, not the
    // operation's: the UI presents the release only while it holds (asked
    // through the one evaluator like every gate), re-met each run because its
    // inputs are run facts - while EconomyContext.ReleaseAlbum stays ungated,
    // since the capstone implicitly cuts an album regardless of any offer.
    // None means always offered once revealed, the same null-condition
    // convention every other gate site uses.
    [Serializable]
    public class AlbumConfig
    {
        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Must hold for a release to be offered (the button enabled); none = always offered. " +
            "Gates the offer only - the release operation itself stays callable (the capstone releases regardless).")]
        private Condition _releaseWhen;

        public Condition ReleaseWhen => _releaseWhen;

        public AlbumConfig() { }

#if UNITY_EDITOR
        public AlbumConfig(Condition releaseWhen)
        {
            _releaseWhen = releaseWhen;
        }
#endif
    }

    // What the chapter declares about fans that is NOT production (design doc
    // section 6). The base rate and its gate are an ordinary production config
    // on a producer (rule 13) like every other flat-rate source; what remains
    // here is the per-bandmate tuning, which is a rate MODIFIER rather than a
    // source, and the currency id - which is not a binding for accrual but the
    // answer to "which currency is this chapter's fans", asked by the checks
    // that keep fans resetting on release and out of the Records multiplier
    // (section 11).
    [Serializable]
    public class FansConfig
    {
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency id this chapter treats as fans. Accrual itself is a production config on a producer.")]
        private string _currencyId;

        [SerializeField]
        [Tooltip("Bonus fans/sec per owned bandmate unit (not gear like the practice amp). Applied as a derived Add on the FanRate target.")]
        private double _perBandmateOwnedBonus;

        public string CurrencyId => _currencyId;
        public double PerBandmateOwnedBonus => _perBandmateOwnedBonus;

        public FansConfig() { }

#if UNITY_EDITOR
        public FansConfig(string currencyId, double perBandmateOwnedBonus)
        {
            _currencyId = currencyId;
            _perBandmateOwnedBonus = perBandmateOwnedBonus;
        }
#endif
    }

}
