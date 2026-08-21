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

        public CurrencyDefinition costCurrency;
        public BigNumber cost;                  // zero is legal - cut_demo is authored at 0
        public List<Effect> effects = new();
        [SerializeReference, SubclassPicker] public List<GameAction> actions = new();

        public bool IsOffered(GameContext declaringCtx) =>
            gate != null && gate.Evaluate(declaringCtx);
    }
}
