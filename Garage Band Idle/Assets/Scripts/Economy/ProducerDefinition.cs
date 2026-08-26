using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Stat names are named, not enumerated (design doc 12.2): a stat means
    // something because a system consumes it - the tick consumes rate,
    // FireProducer consumes yield - so a later accumulation concept is a new
    // name plus its consumer, and no existing field grows. The vocabulary
    // SPLITS by consumer: produced stats are what a produces entry may name,
    // since a contribution has to be summed by something, while game_speed is
    // read through GetMultiplier alone (owner-less, by the tick) - one shared
    // list would let {cash, game_speed, 10} validate as a contribution nothing
    // ever sums. The validator warns on an authored stat outside its site's
    // set, which recovers the typo protection an enum would give.
    public static class Stat
    {
        public const string Rate = "rate";            // units/second - accrues idle time
        public const string Yield = "yield";          // units/firing - never accrues
        public const string GameSpeed = "game_speed"; // scales the tick's production dt; wall clocks never scale

        public static bool IsProduced(string stat) => stat == Rate || stat == Yield;

        public static bool IsEffectAddress(string stat) => stat == GameSpeed;

        // For validation messages. An effect's stat coordinate may name any of
        // the three; a produces entry only the first two.
        public const string ProducedNames = Rate + ", " + Yield;
        public const string EffectStatNames = ProducedNames + ", " + GameSpeed;
    }

    // One authored number: which currency, which stat, the base value, plus an
    // optional condition that must hold for the entry to count. A null condition
    // means the entry is active - the condition is optional, and an entry is not
    // a gate (design doc 12.2).
    [Serializable]
    public class ProducesEntry
    {
        public CurrencyDefinition currency;
        public string stat;
        public BigNumber value;
        [SerializeReference, SubclassPicker] public Condition condition;

        // Judged in the DECLARING scope, never the caller's (design doc 12.4).
        public bool Holds(GameContext declaringCtx) =>
            condition == null || condition.Evaluate(declaringCtx);
    }

    // A named owner of base contributions (design doc 12.2). No availableWhen:
    // the entries carry their own conditions, so a producer is never gated as a
    // whole. Firing is external and unnamed - a button, an automation, and a
    // test are indistinguishable below the module layer.
    [CreateAssetMenu(menuName = "Garage Band Idle/Producer")]
    public class ProducerDefinition : Definition
    {
        public List<ProducesEntry> produces = new();
    }
}
