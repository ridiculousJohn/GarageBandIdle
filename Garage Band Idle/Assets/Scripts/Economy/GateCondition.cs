using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // One requirement inside an unlock or gate (design doc section 4: gates may
    // reference any currency). A gate is a list of these and all must hold.
    // Types are string keys with one code handler each (GateEvaluator); new
    // instances of an existing type are data only, a new type is data plus one
    // handler.
    [Serializable]
    public class GateCondition
    {
        // balance of CurrencyId is at least Amount
        public const string TypeCurrencyBalance = "currencyBalance";

        // lifetime earned total of CurrencyId is at least Amount (spending never
        // lowers it; see CurrencyManager.GetLifetimeEarned)
        public const string TypeCurrencyEarnedTotal = "currencyEarnedTotal";

        // owned count of generator GeneratorId is at least Amount
        public const string TypeOwnedCount = "ownedCount";

        // progress flag FlagId has been set (FlagSystem)
        public const string TypeFlagSet = "flagSet";

        // covers completed this run is at least Amount; handler arrives with the
        // covers slice — until then this type stores data and evaluates as unmet
        public const string TypeCoversCompleted = "coversCompleted";

        [SerializeField]
        [Tooltip("One of the Type* constants on GateCondition.")]
        private string _type;

        [SerializeField]
        [Tooltip("Currency id, for the currency-based types.")]
        private string _currencyId;

        [SerializeField]
        [Tooltip("Generator id, for the ownedCount type.")]
        private string _generatorId;

        [SerializeField]
        [Tooltip("Flag id, for the flagSet type.")]
        private string _flagId;

        [SerializeField]
        private double _amount;

        public string Type => _type;
        public string CurrencyId => _currencyId;
        public string GeneratorId => _generatorId;
        public string FlagId => _flagId;
        public double Amount => _amount;

        // Unity's serializer needs a parameterless constructor on plain classes
        public GateCondition() { }

#if UNITY_EDITOR
        // importer-only: conditions are generated from chapter JSON, not hand-built
        public GateCondition(string type, string currencyId, string generatorId, string flagId, double amount)
        {
            _type = type;
            _currencyId = currencyId;
            _generatorId = generatorId;
            _flagId = flagId;
            _amount = amount;
        }
#endif
    }
}
