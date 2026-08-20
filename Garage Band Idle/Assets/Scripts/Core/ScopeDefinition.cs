using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // A scope's authored shape: what it declares, its children, and (for tiers
    // and chapters) its rung. Lifetime is placement - a fact survives a reset by
    // being declared further out (design doc 12.3). Bar groups join these lists
    // when the bar family lands.
    [CreateAssetMenu(menuName = "Garage Band Idle/Scope")]
    public class ScopeDefinition : Definition
    {
        public List<ScopeDefinition> children = new();

        // Currencies homed here: balance and earned total live and die with this
        // scope. Direct references like every other declaration - declaration IS
        // ownership, and the runtime keys are derived from the assets.
        public List<Economy.CurrencyDefinition> declaredCurrencies = new();

        // Flags homed here. Declaration is what gives SetFlag its write target
        // and the flag its lifetime; reads walk the whole chain.
        public List<string> declaredFlags = new();

        public List<TriggerDefinition> triggers = new();

        // Economy declarations: the facts these create live and die with this
        // scope - a generator's ownedCount, an upgrade's purchase latch. Direct
        // references like triggers, because declaration IS ownership; the
        // [DefinitionId] indirection is for cross-references.
        public List<Economy.ProducerDefinition> producers = new();
        public List<Economy.GeneratorDefinition> generators = new();
        public List<Economy.UpgradeDefinition> upgrades = new();

        // Formula-shaped multipliers that exist from minute one and contribute
        // 1x until their facts do (design doc 12.6). Declared where the facts
        // they read live - Chapter 1's three are all root's.
        public List<Economy.CareerEffectDefinition> careerEffects = new();

        // The album release (tier) or capstone (chapter). Null for scopes with
        // no rung; forbidden on the root (validated at load, 12.12).
        // SerializeReference so "no rung" stays null instead of an auto-created
        // empty instance.
        [SerializeReference] public Rung rung;

        // The declared ids, in authored order. Every runtime fact is keyed by
        // id, so this is what state and the save walk; a null slot is a load
        // error the validator reports rather than a key nothing can hold.
        public IEnumerable<string> currencyIds
        {
            get
            {
                foreach (var currency in declaredCurrencies)
                    if (currency != null)
                        yield return currency.Id;
            }
        }

        public bool DeclaresCurrency(string currencyId)
        {
            foreach (var currency in declaredCurrencies)
                if (currency != null && currency.Id == currencyId)
                    return true;
            return false;
        }

        public bool DeclaresFlag(string flagId) => declaredFlags.Contains(flagId);
    }
}
