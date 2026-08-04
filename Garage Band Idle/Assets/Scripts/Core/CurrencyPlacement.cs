namespace RidiculousGaming.GarageBandIdle
{
    // Which pool holds a currency group's balances (design doc section 12, rule
    // 12). A closed, code-defined set: the boot sequence dispatches on it to
    // decide which CurrencyManager instance a currency lands in, so unlike
    // currency/group ids it is not designer-extensible data.
    //
    // This is placement, not lifetime. ContentScope says how long an effect or
    // a piece of state survives; placement says which instance owns a balance,
    // and lifetime then follows from who created that instance - the startup
    // pool is never reset by a run operation because no run operation holds it.
    // The two are independent: a chapter-placed currency may be run-scoped
    // (cash) or permanent within its chapter, while a global-placed one has no
    // release to reset on at all, which is why the combination
    // "resetsOnAlbumRelease + global" is refused rather than interpreted.
    //
    // Values are explicit because Unity serializes enum fields as their
    // integral value: the numbers are a stable contract with saved assets,
    // independent of declaration order. Append with new values only. Zero is
    // reserved for the uninitialized state so a hand-created asset or an
    // un-migrated field is detectable (boot validation flags None), never a
    // silent default - a group that silently defaulted to Chapter would put
    // Records in the run pool and lose the player's permanent progress on the
    // first release.
    public enum CurrencyPlacement
    {
        None = 0,

        // held by the chapter's own pool, created and discarded with the
        // chapter's economy context
        Chapter = 1,

        // held by the startup pool, created once and never reset by any run
        // operation; the permanent save block (slice 9)
        Global = 2,
    }
}
