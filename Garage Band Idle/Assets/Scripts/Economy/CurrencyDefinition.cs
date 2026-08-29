using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Pure state: a currency is {id, balance} and does not know how it is earned
    // (design doc 12.2). Durability is which scope declares it - there is no
    // lifetime field here. Tags carry the economy vocabulary ("income" is the
    // global-income tag, declared by income currencies, 12.2).
    [CreateAssetMenu(menuName = "Garage Band Idle/Currency")]
    public class CurrencyDefinition : Definition
    {
        // When this currency is ACTIVE; null is always. Inactive means every
        // SourceTerm returns zero for it and Deposit throws - so nothing
        // accrues from any source, present or later-authored, and an authored
        // payout naming it is a content bug that surfaces when it fires rather
        // than a silent write behind a reveal (12.2).
        //
        // This is the "does not exist for you yet" gate. "Exists, but income is
        // paused" is a different sentence with its own mechanism - an x0
        // modifier on rate and yield carrying an appliesWhen - and the two part
        // exactly where it matters: a freeze still takes an authored payout.
        [SerializeReference, SubclassPicker] public Condition activeWhen;

        // Judged at this currency's own HOME, so every source gets one answer.
        // Shaped like GeneratorDefinition.IsAvailable and UpgradeDefinition
        // .IsOffered: the caller supplies the context the read walks from.
        public bool IsActive(GameContext atHome) => activeWhen == null || activeWhen.Evaluate(atHome);
    }
}
