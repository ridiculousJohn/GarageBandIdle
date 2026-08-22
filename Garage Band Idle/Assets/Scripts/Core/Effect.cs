using System;

namespace RidiculousGaming.GarageBandIdle
{
    // The modifier atom (design doc 12.2). Multipliers only; a flat bonus is a
    // produces entry, never an Effect. An Effect never carries a count or growth;
    // where a stored count scales an effect, the carrying entry declares it.
    [Serializable]
    public struct Effect
    {
        public string target;      // a currency id, a producer/generator/bar id, or a tag
        public string currencyId;  // optional - narrow to entries paying this currency, by id OR tag
        public string stat;        // optional - narrow to this stat ("rate"/"yield")
                                   // both empty = every number the target has; both set = one entry
        public double multiplier;
    }
}
