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

        [SerializeField]
        private AlbumConfig _album = new();

        [SerializeField]
        private CapstoneConfig _capstone = new();

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
        public AlbumConfig Album => _album;
        public CapstoneConfig Capstone => _capstone;
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
            RecordBuffConfig recordBuff, FansConfig fans, AlbumConfig album, CapstoneConfig capstone,
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
            _album = album ?? new AlbumConfig();
            _capstone = capstone ?? new CapstoneConfig();
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

    // The chapter capstone (design doc sections 1-2 and 5): the gig that ends the
    // chapter. Parallel to AlbumConfig, and for the same reason - it is a thing the
    // chapter declares about itself rather than an entry in one of the content id
    // lists.
    //
    // The unlock Condition is the SOLE authored source of the gate. A scalar
    // `capstoneRecordsGate` used to sit on the chapter as well, stating the same
    // threshold in a second place while the authored Condition was never imported at
    // all - so the two could disagree and the one the designer wrote was the one
    // being ignored. Nothing re-derives a threshold from anywhere now: slice 7 asks
    // this Condition through the same evaluator every other gate uses.
    [Serializable]
    public class CapstoneConfig
    {
        [SerializeField]
        [Tooltip("Stable string id, e.g. backyard_party.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Must hold for the capstone to be offered - the chapter's primary pacing knob " +
            "(Ch1: recordsCumulative >= 30). Asked through the one evaluator like every gate.")]
        private Condition _unlock;

        [SerializeField]
        [Tooltip("Flag latched when the capstone completes - set by the completion OPERATION itself " +
            "(slice 7), from this declaration, never authored as a payload effect: one declaration owns " +
            "the fact, so payload and config cannot disagree. ONE fact, not two: it is both 'this chapter " +
            "is finished' and 'chapter 2 may open', and nothing in Chapter 1 can tell those apart. " +
            "Must be declared permanent-in-chapter in the chapter's flags list.")]
        private string _completionFlagId;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Re-applicable state completing it grants (modifiers, flags beyond the completion flag). " +
            "Ch1 authors none - its awards are one-shot Actions below, and the completion flag is the " +
            "operation's own job.")]
        private GameEffect _onComplete;

        // the one-shot awards - Ch1: one Roadie. Executed by the completion
        // operation exactly once; no release, load, or reprojection ever sees an
        // action, which is what "paid once ever" means by construction.
        [SerializeReference]
        [SubclassPicker]
        [Tooltip("One-shot awards completing it pays - Ch1: one Roadie. Executed once by the completion operation.")]
        private List<GameAction> _actions = new();

        public string Id => _id;
        public string DisplayName => _displayName;
        public Condition Unlock => _unlock;
        public string CompletionFlagId => _completionFlagId;
        public GameEffect OnComplete => _onComplete;
        public IReadOnlyList<GameAction> Actions => _actions;

        // whether the chapter authors a capstone at all. Chapter 1 does; a
        // hand-made fixture chapter usually does not, and validation must not
        // demand one of every chapter that exists.
        public bool IsAuthored => !string.IsNullOrEmpty(_id);

        public CapstoneConfig() { }

#if UNITY_EDITOR
        public CapstoneConfig(string id, string displayName, Condition unlock, string completionFlagId,
            GameEffect onComplete, List<GameAction> actions = null)
        {
            _id = id;
            _displayName = displayName;
            _unlock = unlock;
            _completionFlagId = completionFlagId;
            _onComplete = onComplete;
            _actions = actions ?? new List<GameAction>();
        }
#endif
    }

    // What the chapter declares about fans that is NOT production (design doc
    // section 6): the currency id, which is not a binding for accrual but the
    // answer to "which currency is this chapter's fans", asked by the checks that
    // keep fans resetting on release and out of the Records multiplier (section
    // 11).
    //
    // The per-bandmate bonus used to sit here too, as a chapter-level number a
    // derived modifier turned into a flat Add on the fan rate. It is now each
    // bandmate generator's own fans CONTRIBUTION (rule 13), which is what makes
    // band size raise the rate: a generator's lines always scale with its owned
    // count. That removed a rate "modifier" that was really a source, and with it
    // the isBandmate bool the modifier had to read off every generator.
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
