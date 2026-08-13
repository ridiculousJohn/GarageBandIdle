namespace RidiculousGaming.GarageBandIdle.Economy
{
    // What a modifier acts on - a closed, code-defined set for the same reason
    // ContentScope is: every value has a system that composes it, so a new
    // target is a code change, never designer data. Explicit values because
    // Unity serializes enums as their integral value and saves will carry
    // permanent-in-chapter modifiers; zero is reserved for the uninitialized
    // state so a hand-built modifier is detectable. Append with new values only.
    //
    // Every member names a FAMILY, never one chapter's instance of it (design
    // doc section 12, rule 13). The retired values 1 and 2 were TapValue and
    // FanRate - "the Jam button's payout" and "the fan rate", which were global
    // only because Chapter 1 has exactly one of each; a second tap surface would
    // have shared one multiplier with the first, silently. Their numbers stay
    // vacant rather than being reused, so a stale asset holding one fails closed
    // in ModifierSystem instead of resolving to whatever was added later.
    public enum ModifierTarget
    {
        None = 0,

        // one generator's output, qualified by generator id
        GeneratorOutput = 3,

        // one currency's production per second, qualified by currency id
        CurrencyRate = 4,

        // one currency's payout per firing of its producer, qualified by
        // currency id (rule 13). A rate and a yield are different quantities -
        // per unit time against per occurrence - so they are separately
        // modifiable rather than one number scaled two ways.
        CurrencyYield = 5,

        // how fast a bar group consumes its fill currency, qualified by bar
        // group id (section 6). Nothing authors one yet.
        BarFillRate = 6,

        // the fraction of production an absence pays (section 9). The "Double
        // it" buff is a multiplier here. Nothing authors one yet.
        IdleRate = 7,

        // how much of an absence is paid for at all (section 9). The Backstage
        // Pass raises it. Nothing authors one yet.
        IdleCap = 8,
    }
}
