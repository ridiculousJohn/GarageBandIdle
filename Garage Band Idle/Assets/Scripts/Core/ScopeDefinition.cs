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
        public List<Economy.GeneratorDefinition> generators = new();

        // Bar groups homed here; each group owns its bars (design doc 12.7).
        // The fill and settlement systems land with build step 5.
        public List<Economy.BarGroupDefinition> barGroups = new();
        public List<Economy.UpgradeDefinition> upgrades = new();

        // Formula-shaped multipliers that exist from minute one and contribute
        // 1x until their facts do (design doc 12.6). Declared where the facts
        // they read live - Chapter 1's three are all root's.
        public List<Economy.CareerEffectDefinition> careerEffects = new();

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
        internal virtual bool Declares(Definition definition) =>
            Holds(declaredCurrencies, definition)
            || Holds(producers, definition)
            || Holds(modifiers, definition)
            || Holds(generators, definition)
            || Holds(barGroups, definition)
            || Holds(upgrades, definition)
            || Holds(careerEffects, definition)
            || Holds(triggers, definition);

        protected static bool Holds<T>(List<T> list, Definition definition) where T : Definition
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i] == definition)
                    return true;
            return false;
        }
    }

    // Every scope inside another one - chapters and tiers. Root is the sole
    // exclusion, which is what puts the rung here: a rung is the ladder step out
    // of a scope, and the root is what the ladder climbs toward.
    public abstract class InteriorDefinition : ScopeDefinition
    {
        // The album release (tier) or capstone (chapter). Null for scopes with
        // no rung. SerializeReference so "no rung" stays null instead of an
        // auto-created empty instance.
        [SerializeReference] public Rung rung;
    }

    // The tree's one parentless scope: career facts, no rung, no event.
    [CreateAssetMenu(menuName = "Garage Band Idle/Scope/Root")]
    public class RootDefinition : ScopeDefinition
    {
        // Typed, because the tree build must hand back a RootScopeState and the
        // polymorphic entry point can only promise a ScopeState.
        internal RootScopeState CreateRoot() => new RootScopeState(this);

        internal override ScopeState CreateState(ScopeState parent) => CreateRoot();
    }

    // Root's direct children. Idle is per-chapter, so its claim and clock live
    // on the state this makes (design doc 12.9).
    [CreateAssetMenu(menuName = "Garage Band Idle/Scope/Chapter")]
    public class ChapterDefinition : InteriorDefinition
    {
        internal override ScopeState CreateState(ScopeState parent) => new ChapterScopeState(this, parent);
    }

    // Everything below a chapter, at any depth - the tree nests freely.
    [CreateAssetMenu(menuName = "Garage Band Idle/Scope/Tier")]
    public class TierDefinition : InteriorDefinition
    {
        internal override ScopeState CreateState(ScopeState parent) => new TierScopeState(this, parent);
    }
}
