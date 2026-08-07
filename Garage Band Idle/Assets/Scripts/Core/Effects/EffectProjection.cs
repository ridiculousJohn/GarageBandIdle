namespace RidiculousGaming.GarageBandIdle
{
    // Whether an effect may be re-applied by the projection (design doc section
    // 12, rule 6). A closed, code-defined set: this is a property of the effect
    // TYPE, not authored data, which is the whole point - an author cannot mark a
    // currency payout as safe to replay, and cannot forget to mark a new payout
    // as unsafe, because the classification lives on the class that implements
    // the mutation.
    //
    // Unlike ContentScope and CurrencyPlacement this carries no explicit values
    // and no None member: it is never a serialized field, so there is no
    // integral contract with saved assets to protect and no un-migrated state to
    // detect. A new effect class must state its kind to compile.
    public enum EffectProjection
    {
        // Re-applying is either idempotent or additive-from-scratch over a store
        // the projection just cleared: granting a modifier, latching a flag. The
        // projection may call Project on these freely, at every boundary.
        Projectable,

        // Establishes something the game keeps a running total of, so applying
        // it twice pays twice: a currency award. These run once, on acquisition,
        // and the projection must refuse them.
        OneShot,
    }
}
