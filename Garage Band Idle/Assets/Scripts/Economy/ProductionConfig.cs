using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // What fires a production config (design doc section 12, rule 13). A closed,
    // code-defined set for the same reason ContentScope is: every trigger has a
    // system path that fires it, so a new trigger is a code change, never
    // designer data. Explicit values because Unity serializes enums as their
    // integral value; zero is reserved for the uninitialized state so a
    // hand-built config is detectable.
    public enum ProductionTrigger
    {
        None = 0,

        // fires every real-time tick (amount is per second)
        Tick = 1,

        // fires on each Jam tap (amount is per tap)
        Tap = 2,
    }

    // One flat-rate currency source (design doc section 12, rule 13), held by
    // its producer - never by the currency. A currency is pure state (a
    // balance, a group, formatting); the dependency points from producer to
    // currency, the same direction a multiplier points at its targets. The
    // gate is an ordinary Condition, so activation runs through the one
    // evaluator like every other rule in the game.
    [Serializable]
    public class ProductionConfig
    {
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency this config creates.")]
        private string _currencyId;

        [SerializeField]
        [Tooltip("Amount created per firing: per second for tick, per tap for tap.")]
        private double _amount;

        [SerializeField]
        private ProductionTrigger _trigger;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Must hold for the config to fire, checked per firing. None = always on.")]
        private Condition _gate;

        [SerializeField]
        [Tooltip("Modifier target kind whose composition scales this config's output (the Jam cash config declares TapValue). None = the raw amount.")]
        private ModifierTarget _composes;

        public string CurrencyId => _currencyId;
        public double Amount => _amount;
        public ProductionTrigger Trigger => _trigger;
        public Condition Gate => _gate;
        public ModifierTarget Composes => _composes;

        // Which targets a config may declare, in ONE place - the runtime guard
        // and boot validation both ask here, so they cannot drift into
        // disagreeing about what is authorable (the trap 5.7 walked into by
        // generalizing ProductionSystem and leaving ContentValidator behind).
        //
        // None is always legal: the config pays its raw amount. Anything else
        // must be a defined target whose qualifier family is CURRENCY, because a
        // config composes through ModifierTargetKey.Of(kind, its own currency id)
        // - so a generator- or bar-group-qualified target can never be one. Given
        // a currency id it would address a key nobody grants against, reading an
        // empty bucket and scaling by nothing, which is worse than a refusal
        // because it looks like it worked.
        public static bool IsComposable(ModifierTarget composes)
            => composes == ModifierTarget.None
                || (Enum.IsDefined(typeof(ModifierTarget), composes)
                    && ModifierTargetKey.QualifierDefinitionType(composes) == typeof(CurrencyDefinition));

        public ProductionConfig() { }

#if UNITY_EDITOR
        // importer-only: producer assets are generated from chapter JSON
        public ProductionConfig(string currencyId, double amount, ProductionTrigger trigger,
            Condition gate, ModifierTarget composes)
        {
            _currencyId = currencyId;
            _amount = amount;
            _trigger = trigger;
            _gate = gate;
            _composes = composes;
        }
#endif
    }
}
