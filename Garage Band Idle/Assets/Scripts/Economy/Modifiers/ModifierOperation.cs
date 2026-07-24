namespace RidiculousGaming.GarageBandIdle.Economy
{
    // How a modifier combines with the value it targets. Closed and
    // code-defined: the order the two operations compose in is a rule (see
    // ModifierComposition), not something content may vary. Zero is reserved
    // for the uninitialized state. Append with new values only.
    public enum ModifierOperation
    {
        None = 0,

        // summed with the base before any multiplier applies
        Add = 1,

        // multiplied into the running product after every add
        Multiply = 2,
    }
}
