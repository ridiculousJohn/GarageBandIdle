using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Anything that feeds currency producers (design doc section 12, rule 13):
    // a generator scaled by owned count, a module presenting a surface, and
    // whatever a later slice adds. It exists so a producer's list can be
    // ASSEMBLED from the contributors in reach without the producer knowing
    // what any of them is - the direction that lets a new contributor kind
    // ship without production changing at all.
    public interface IProductionContributor
    {
        // Stable id, for error messages and per-contributor readouts. Not
        // resolved against a registry here: which family it belongs to is the
        // contributor's own business, and production never looks one up.
        string ContributorId { get; }

        IReadOnlyList<ProductionContribution> Contributions { get; }

        // The live value of ONE of its contributions, contributor-side scaling
        // and the modifiers reaching that line included - a generator's owned
        // count times its per-unit amount, a module's flat amount.
        // Derived on every read and never stored, so buying a unit or granting
        // a buff changes no structure and needs no rebuild.
        //
        // The GATE is not applied here, and neither is the currency-level
        // composition: both belong to the producer, so exactly one place
        // decides what a contribution is worth to the number it feeds.
        BigNumber ValueOf(ProductionContribution contribution);
    }

    // One contribution together with the contributor holding it - what a
    // producer's list is made of, and what an assembler hands over. The pair
    // travels together because the value is the contributor's to compute while
    // the currency, the quantity and the gate are the contribution's to
    // declare.
    //
    // Deliberately behaviourless: every evaluation lives on the producer, so a
    // readout and a payout cannot reach two different answers.
    public readonly struct ProductionEntry
    {
        public IProductionContributor Contributor { get; }
        public ProductionContribution Contribution { get; }

        public ProductionEntry(IProductionContributor contributor, ProductionContribution contribution)
        {
            Contributor = contributor;
            Contribution = contribution;
        }
    }
}
