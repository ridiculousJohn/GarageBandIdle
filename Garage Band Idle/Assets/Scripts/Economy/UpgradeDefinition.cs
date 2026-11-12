using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // A one-shot purchase whose fact is the latch in purchasedUpgrades at the
    // declaring scope (design doc 12.6): the effects apply for as long as the
    // latch exists, and the actions run once, at the moment of purchase. A pure
    // latch is legal - Chapter 1's stage_presence carries no effects and no
    // actions, and tap_producer's conditioned entry reads it.
    [CreateAssetMenu(menuName = "Garage Band Idle/Upgrade")]
    public class UpgradeDefinition : Definition
    {
        // Null refuses the buy, exactly like a generator's availableWhen.
        [SerializeReference, SubclassPicker] public Condition gate;

        // Optional; null is absent. While it holds the upgrade is owned with
        // no latch: a purchase priced in a tier's currency whose payload sets a
        // chapter flag declares ownedWhen on that flag, so it reads as owned
        // after the tier resets and is never bought again (design doc 4).
        [SerializeReference, SubclassPicker] public Condition ownedWhen;

        public CurrencyDefinition costCurrency;
        public BigNumber cost;                  // zero is legal content
        public List<Effect> effects = new();
        [SerializeReference, SubclassPicker] public List<GameAction> actions = new();

        public bool IsOffered(GameContext declaringCtx) =>
            gate != null && gate.Evaluate(declaringCtx);

        // The ownership rule, the one place it is stated (12.6): the purchase
        // latch at the declaring scope, or ownedWhen holding there - the
        // context the gate is asked in. Every reader of "owned" comes through
        // here, by Purchasing.IsOwned from any scope on the chain; the effects
        // alone read the bare latch (EffectLink).
        public bool IsOwned(GameContext declaringCtx) =>
            declaringCtx.Scope.purchasedUpgrades.Contains(Id)
            || (ownedWhen != null && ownedWhen.Evaluate(declaringCtx));
    }
}
