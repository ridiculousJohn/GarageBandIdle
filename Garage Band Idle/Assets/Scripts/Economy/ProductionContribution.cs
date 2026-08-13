using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Which of a producer's two numbers a contribution feeds (design doc
    // section 12, rule 13). Rate and yield are different QUANTITIES - per unit
    // time against per occurrence - so this names what the contribution IS,
    // never who fires it. ProductionTrigger named the callers instead (Tick,
    // Tap), which put a UI gesture in the economy's vocabulary and left the
    // first demand-fired producer that is not a button press with nowhere to
    // go.
    //
    // Explicit values because Unity serializes enums as their integral value;
    // zero is the uninitialized state, so a contribution that never declared
    // what it feeds is detectable rather than silently becoming a rate.
    public enum ProductionFeed
    {
        None = 0,

        // units per second
        Rate = 1,

        // units per firing
        Yield = 2,
    }

    // One contributor's declared input to a currency's producer (design doc
    // section 12, rule 13). It names the currency it FEEDS, which is what lets
    // a producer be assembled without knowing contributor kinds - a generator,
    // a module and whatever comes later all declare the same shape, so a new
    // kind of contributor touches nothing here.
    //
    // IT CARRIES ITS OWN ID (rule 11), because it is a modifiable number and
    // every modifiable number is selectable by name. That is what makes
    // "double the drummer's cash" sayable: the drummer holds a cash line and a
    // fans line, and a buff names the line rather than the generator. A buff
    // naming the generator instead still reaches both, through the owner the
    // subject offers.
    //
    // What it does NOT carry is as load-bearing as what it does:
    //   - no trigger, because feeds is the quantity and firing is external;
    //   - no separate "composes" target, because which composition scales it
    //     follows from what it IS - its own id, its tags, and its owner's;
    //   - no lifetime, because a contribution's durability is its
    //     contributor's - it goes away when the thing holding it does;
    //   - no idle-eligibility flag, because a rate accrues while a scope is
    //     disabled and a yield does not, nothing having fired it. Section 9
    //     settles that structurally rather than by authoring.
    //
    // The gate is an ordinary rule-8 Condition checked per composition, so a
    // dormant contribution is worth zero to the readout and to the payout
    // alike and the two cannot disagree.
    [Serializable]
    public class ProductionContribution
    {
        [SerializeField]
        [Tooltip("Stable id. A modifier selects this line by it.")]
        private string _id;

        [SerializeField]
        [Tooltip("Sets this line belongs to, e.g. rhythm_section. A modifier selects a tag exactly as it selects an id.")]
        private string[] _tags = Array.Empty<string>();

        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency whose producer this feeds.")]
        private string _currencyId;

        [SerializeField]
        [Tooltip("Per second for a rate contribution, per firing for a yield one.")]
        private double _amount;

        [SerializeField]
        [Tooltip("Which of the producer's two numbers this feeds.")]
        private ProductionFeed _feeds;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Must hold for this to count, checked per composition. None = always on.")]
        private Condition _gate;

        public string Id => _id;
        public IReadOnlyList<string> Tags => _tags ?? Array.Empty<string>();
        public string CurrencyId => _currencyId;
        public double Amount => _amount;
        public ProductionFeed Feeds => _feeds;
        public Condition Gate => _gate;

        // What this line IS, for a selector to match: its own id and tags, plus
        // the contributor holding it. One implementation for every contributor
        // kind, so `["drummer_cash"]` reaching one line and `["drummer"]` reaching
        // all of them is a single rule rather than a convention each kind
        // re-establishes.
        public ModifierSubject SubjectUnder(string ownerId, IReadOnlyList<string> ownerTags)
            => new ModifierSubject(_id, Tags, ownerId, ownerTags);

        public ProductionContribution() { }

#if UNITY_EDITOR
        // importer-only: contributor assets are generated from chapter JSON
        public ProductionContribution(string id, string currencyId, double amount, ProductionFeed feeds,
            Condition gate = null, string[] tags = null)
        {
            _id = id;
            _tags = tags ?? Array.Empty<string>();
            _currencyId = currencyId;
            _amount = amount;
            _feeds = feeds;
            _gate = gate;
        }
#endif
    }
}
