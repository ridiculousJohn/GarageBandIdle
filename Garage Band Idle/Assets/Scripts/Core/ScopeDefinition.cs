using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // A scope's authored shape: what it declares, its children, and (for tiers
    // and chapters) its rung. Lifetime is placement - a fact survives a reset by
    // being declared further out (design doc 12.3).
    public abstract class ScopeDefinition : Definition
    {
        public List<ScopeDefinition> children = new();

        // Currencies homed here: balance and earned total live and die with this
        // scope. Direct references like every other declaration - declaration IS
        // ownership, and the runtime keys are derived from the assets.
        public List<Economy.CurrencyDefinition> declaredCurrencies = new();

        // Flags homed here. Declaration is what gives SetFlag its write target
        // and the flag its lifetime; reads walk the whole chain.
        public List<string> declaredFlags = new();

        // The tag vocabulary this scope's subtree may carry - bare strings for
        // the same reason flags are, since a tag has no data beyond its own
        // existence. A definition CARRYING one resolves it by walking outward to
        // the scope declaring it; an Effect selector filtering on one resolves
        // nothing, so the declaration binds carriers alone (design doc 12.2).
        public List<string> declaredTags = new();

        public List<TriggerDefinition> triggers = new();

        // Economy declarations: the facts these create live and die with this
        // scope - a generator's ownedCount, an upgrade's purchase latch. Direct
        // references like triggers, because declaration IS ownership. Every
        // authored reference is direct; the only ids left are the ones FACTS
        // hold, and those resolve by walking their scope outward.
        public List<Economy.ProducerDefinition> producers = new();

        // Modifiers grantable within this scope's subtree. The grant writes a
        // stack on the target scope; the read resolves it outward to here.
        public List<Economy.ModifierDefinition> modifiers = new();

        // A USAGE list, the parallel of an AddModifier grant minus the moment:
        // each entry references a modifier declared on the reachable chain, and
        // the gather reads it directly - nothing granted, nothing saved,
        // reset-immune. Contributes an implicit application count of 1, merged
        // with this scope's stored stacks through the modifier's own stacking
        // kind (design doc 12.5).
        public List<Economy.ModifierDefinition> permanentModifiers = new();
        public List<Economy.GeneratorDefinition> generators = new();

        // Bar groups homed here; each group owns its bars (design doc 12.7).
        // The fill and settlement systems land with build step 5.
        public List<Economy.BarGroupDefinition> barGroups = new();
        public List<Economy.UpgradeDefinition> upgrades = new();

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

        // The state node this definition stands for, holding the payload this
        // kind of scope holds. Authoring picks the class, so nothing infers a
        // scope's kind from where it sits in the tree.
        internal abstract ScopeState CreateState(ScopeState parent);

        // Whether this scope declares that definition. A scope answers for its
        // OWN lists, so the outward walk never names one - which is what lets a
        // kind of scope declare something the other kinds cannot.
        // permanentModifiers is deliberately absent: it is usage, not
        // declaration - the modifiers it references are declared elsewhere.
        internal virtual bool Declares(Definition definition) =>
            Holds(declaredCurrencies, definition)
            || Holds(producers, definition)
            || Holds(modifiers, definition)
            || Holds(generators, definition)
            || Holds(barGroups, definition)
            || Holds(upgrades, definition)
            || Holds(triggers, definition);

        protected static bool Holds<T>(List<T> list, Definition definition) where T : Definition
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i] == definition)
                    return true;
            return false;
        }
    }
}
