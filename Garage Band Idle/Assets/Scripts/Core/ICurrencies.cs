using System;

namespace RidiculousGaming.GarageBandIdle
{
    // The balance surface every system reads and writes through. Extracted from
    // CurrencyManager so that a consumer cannot tell whether it is talking to
    // one pool or to several: a scope reaches every pool in its chain - its own,
    // then each ancestor's outward, then the startup pool holding the global
    // currencies the chain ends at (design doc section 12, rule 12) - and a
    // system that produces cash has no business knowing which of them owns
    // Records.
    //
    // That is the whole reason this exists rather than systems holding the pools
    // themselves and choosing: the choice would be made per call site, by
    // whoever wrote it, from the currency's name - exactly the named-currency
    // special-casing the design forbids. Here it is made once, at construction,
    // by whoever owns the pools (see CurrencyRouter).
    //
    // CurrencyManager implements this for the single-pool case, so a fixture
    // that needs one flat set of balances still just constructs a
    // CurrencyManager and hands it over.
    public interface ICurrencies
    {
        // fires on every balance change with the currency id and new balance;
        // one subscription covers every pool reachable through this surface,
        // so a consumer never holds a list of sources to keep in step
        event Action<string, BigNumber> BalanceChanged;

        BigNumber Get(string id);

        // whether the id resolves to a currency this surface can reach - the
        // SILENT probe, for callers deciding whether to act (GetDefinition and
        // ValidateReference both report, which is wrong for a preflight asked
        // per attempt)
        bool Contains(string id);

        void Add(string id, BigNumber amount);

        void Set(string id, BigNumber value);

        // total earned over the scope the currency's group declares (starting
        // value excluded); spends never lower it
        BigNumber GetEarned(string id);

        CurrencyDefinition GetDefinition(string id);

        // startup check for any system holding a currency id: a reference that
        // resolves to no definition gets reported with the referencing context
        // named, at load rather than mid-run
        bool ValidateReference(string id, string context);

        // whether the currency's group opts into the album-release reset. A
        // global currency answers false - it has no release to reset on.
        bool ResetsOnAlbumRelease(string currencyId);
    }
}
