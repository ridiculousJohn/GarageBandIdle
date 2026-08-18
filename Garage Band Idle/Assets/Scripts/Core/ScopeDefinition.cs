using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // A scope's authored shape: what it declares, its children, and (for tiers
    // and chapters) its press. Lifetime is placement - a fact survives a reset by
    // being declared further out (design doc 12.3). Later build steps add the
    // remaining declaration lists (producers, generators, upgrades, bar groups)
    // as their families land.
    [CreateAssetMenu(menuName = "Garage Band Idle/Scope")]
    public class ScopeDefinition : Definition
    {
        public List<ScopeDefinition> children = new();

        // Currencies homed here: balance and earned total live and die with this
        // scope. Ids reference CurrencyDefinition assets.
        [DefinitionId(typeof(Economy.CurrencyDefinition))]
        public List<string> declaredCurrencyIds = new();

        // Flags homed here. Declaration is what gives SetFlag its write target
        // and the flag its lifetime; reads walk the whole chain.
        public List<string> declaredFlags = new();

        public List<TriggerDefinition> triggers = new();

        // The album release (tier) or capstone (chapter). Null for scopes that
        // press nothing; forbidden on the root (validated at load, 12.12).
        // SerializeReference so "no press" stays null instead of an auto-created
        // empty instance.
        [SerializeReference] public Press press;

        public IReadOnlyList<string> currencyIds => declaredCurrencyIds;

        public bool DeclaresFlag(string flagId) => declaredFlags.Contains(flagId);
    }
}
