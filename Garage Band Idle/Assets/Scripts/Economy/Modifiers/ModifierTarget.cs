namespace RidiculousGaming.GarageBandIdle.Economy
{
    // What a modifier acts on - a closed, code-defined set for the same reason
    // ContentScope is: every value has a system that composes it, so a new
    // target is a code change, never designer data. Explicit values because
    // Unity serializes enums as their integral value and saves will carry
    // permanent-in-chapter modifiers; zero is reserved for the uninitialized
    // state so a hand-built modifier is detectable. Append with new values only.
    public enum ModifierTarget
    {
        None = 0,

        // cash granted per Jam tap
        TapValue = 1,

        // fan accrual per second
        FanRate = 2,

        // one generator's output, qualified by generator id
        GeneratorOutput = 3,

        // the summed production of one currency, qualified by currency id
        CurrencyProduction = 4,
    }
}
