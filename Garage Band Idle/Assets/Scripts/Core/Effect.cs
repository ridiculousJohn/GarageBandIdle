using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // The modifier atom (design doc 12.2). Multipliers only; a flat bonus is a
    // produces entry, never an Effect. An Effect never carries a count or growth;
    // where a stored count scales an effect, the carrying entry declares it.
    [Serializable]
    public struct Effect
    {
        public string target;      // a currency/producer/generator/bar id or a tag; empty is the
                                   // wildcard - "every currency", applied at the currency stage
        public string currencyId;  // optional - narrow to entries paying this currency, by id OR tag
        public string stat;        // REQUIRED and exact - the one stat this factor answers for

        // BigNumber, not a ratio-sized double: the gather's product is BigNumber
        // and a formula factor is unbounded, so an authored constant has to span
        // the same range the runtime can reach.
        public BigNumber multiplier;

        // The factor is a constant or a formula (design doc 12.2): the authored
        // multiplier when this is absent, the formula computed against the
        // gather-origin context on every read when it is present.
        [SerializeReference, SubclassPicker] public Economy.MultiplierFormula formula;
    }
}
