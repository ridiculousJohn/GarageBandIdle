namespace RidiculousGaming.GarageBandIdle.Economy
{
    // How a modifier combines with the value it reaches. Closed and code-defined,
    // and deliberately down to ONE operation: a modifier is a multiplier (design
    // doc section 12, rule 11). A flat bonus is not a modifier at all - it is a
    // ProductionContribution to the number it raises, authored by whatever fact
    // pays it.
    //
    // Add used to live here, and removing it removes a question with no correct
    // answer: what a flat add against a SET means. A multiplier against a set is
    // unambiguous - each number it reaches is scaled - while "+1 to every cash
    // line" and "+1 to cash" are different numbers with one spelling. As a
    // contribution the same bonus is one identified line that sums with the rest,
    // so every composed number in the game has one shape: the sum of its
    // contributions times the product of the multipliers matching it.
    //
    // The enum survives its own second member because zero still has to mean
    // uninitialized: a serialized enum is an int, so an un-migrated asset can hold
    // one, and None is what lets that be reported rather than silently composed.
    public enum ModifierOperation
    {
        None = 0,

        // multiplied into the running product; 1.5 is +50%
        Multiply = 2,
    }
}
