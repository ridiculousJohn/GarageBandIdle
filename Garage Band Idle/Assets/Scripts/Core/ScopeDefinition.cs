using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // One authored action list of a scope, as everything that walks lists sees
    // it: the actions, and the site text a finding names them by.
    public readonly struct ActionListSite
    {
        public readonly IReadOnlyList<GameAction> Actions;
        public readonly string Site;

        public ActionListSite(IReadOnlyList<GameAction> actions, string site)
        {
            Actions = actions;
            Site = site;
        }
    }

    // One source of base contributions a scope declares, as everything that
    // gathers sees it: the producer or generator itself, its authored entries,
    // and the count fact that scales them at a node (design doc 12.2). A
    // producer is one; a generator is its ownedCount, so the caller never asks
    // which kind it is holding.
    public readonly struct ScopeSource
    {
        public readonly Definition Source;
        public readonly List<Economy.ProducesEntry> Entries;

        // Null for a producer. The one field that distinguishes the two kinds,
        // and it is read only by CountAt.
        private readonly Economy.GeneratorDefinition generator;

        internal ScopeSource(Definition source, List<Economy.ProducesEntry> entries,
                             Economy.GeneratorDefinition generator)
        {
            Source = source;
            Entries = entries;
            this.generator = generator;
        }

        // The stored count that scales this source at one node (design doc
        // 12.2/12.6). A missing generator count is zero owned.
        public int CountAt(ScopeState node)
        {
            if (generator == null)
                return 1;
            return node.generatorCounts.TryGetValue(generator.Id, out var owned) ? owned : 0;
        }
    }

    // What decides whether one carrier's effect is LIVE, and how its stored
    // count scales it (design doc 12.6's table). Which effects can ever apply to
    // a number is static and compiled once; this is the half that stays a fact,
    // read on every gather.
    public enum LivenessKind
    {
        Upgrade,    // purchasedUpgrades holds the upgrade's id at the node
        Permanent,  // always, subject to appliesWhen; merged with the node's stack for the same modifier
        Granted,    // modifierStacks holds a count at the node, subject to appliesWhen
        Cascade,    // fillCounts holds a count for the repeating bar at the node
        Handicap    // the node's ActiveEvent record names the event
    }

    // One (carrier, effect) pair a scope can apply, with the liveness kind that
    // decides it - the unit ScopeDefinition.EffectCarriers enumerates and the
    // gather compiler turns into an EffectLink at a node.
    public readonly struct CarrierEffect
    {
        public readonly LivenessKind Liveness;

        // The upgrade, modifier, bar, or event whose fact decides this effect.
        public readonly Definition Carrier;
        public readonly Effect Effect;

        // Cascade entries only: growth lives on the carrying entry, never on
        // the Effect atom (design doc 12.7). A modifier scales through its own
        // stacking kind instead, read off the carrier.
        public readonly Economy.GrowthKind Growth;

        internal CarrierEffect(LivenessKind liveness, Definition carrier, in Effect effect,
                               Economy.GrowthKind growth = Economy.GrowthKind.Multiply)
        {
            Liveness = liveness;
            Carrier = carrier;
            Effect = effect;
            Growth = growth;
        }
    }

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

        // Every action list this scope declares, ONCE, in one place
        // (InteriorDefinition adds its own). The link pass iterates this and so
        // does the validator's action-list walk; no executor and no pass spells
        // the sites out by hand, so a new kind of list is added here and
        // nowhere else - which is what leaves no second site to forget. The
        // order is the kinds in declaration order: triggers, bar completions,
        // then upgrade payloads.
        public virtual IEnumerable<ActionListSite> ActionLists()
        {
            foreach (var trigger in triggers)
                if (trigger != null)
                    yield return new ActionListSite(trigger.actions, $"trigger '{trigger.Id}'");

            foreach (var group in barGroups)
            {
                if (group == null)
                    continue;
                foreach (var bar in group.bars)
                    if (bar != null)
                        yield return new ActionListSite(bar.onComplete, $"bar '{bar.Id}'");
            }

            foreach (var upgrade in upgrades)
                if (upgrade != null)
                    yield return new ActionListSite(upgrade.actions, $"upgrade '{upgrade.Id}'");
        }

        // Every source of base contributions this scope declares, ONCE, in one
        // place: producers then generators, each in declaration order. The
        // gather compiler iterates this and nothing else enumerates the kinds,
        // so a third source kind is an entry here and there is no other step.
        public IEnumerable<ScopeSource> Sources()
        {
            foreach (var producer in producers)
                if (producer != null)
                    yield return new ScopeSource(producer, producer.produces, null);

            foreach (var generator in generators)
                if (generator != null)
                    yield return new ScopeSource(generator, generator.produces, generator);
        }

        // Every (carrier, effect) pair this scope can apply, ONCE, in one place
        // (InteriorDefinition adds its handicaps). THE ORDER IS THE
        // MULTIPLICATION ORDER at a node: upgrades, permanent modifiers, granted
        // modifiers, bar cascades, then handicaps, each kind in declaration
        // order and effects in declaration order within a carrier. Adding a
        // sixth carrier kind is an entry here and nowhere else.
        //
        // `grantable` is the modifiers declared on this node's CHAIN, self
        // outward, since a grant can only ever stack one of those (design doc
        // 12.5) and a definition holds no parent - the compiler, which already
        // has the chain, supplies it. A modifier this scope also lists as a
        // permanent membership is skipped here: the two are ONE application,
        // merged through the modifier's stacking kind on the permanent entry, so
        // neither path can double-apply outside the vocabulary.
        public virtual IEnumerable<CarrierEffect> EffectCarriers(
            IReadOnlyList<Economy.ModifierDefinition> grantable)
        {
            foreach (var upgrade in upgrades)
            {
                if (upgrade == null)
                    continue;
                foreach (var effect in upgrade.effects)
                    yield return new CarrierEffect(LivenessKind.Upgrade, upgrade, effect);
            }

            foreach (var modifier in permanentModifiers)
            {
                if (modifier == null)
                    continue;
                foreach (var effect in modifier.effects)
                    yield return new CarrierEffect(LivenessKind.Permanent, modifier, effect);
            }

            if (grantable != null)
            {
                for (var i = 0; i < grantable.Count; i++)
                {
                    var modifier = grantable[i];
                    if (modifier == null || permanentModifiers.Contains(modifier))
                        continue;
                    foreach (var effect in modifier.effects)
                        yield return new CarrierEffect(LivenessKind.Granted, modifier, effect);
                }
            }

            foreach (var group in barGroups)
            {
                if (group == null)
                    continue;
                foreach (var bar in group.bars)
                {
                    if (bar == null)
                        continue;
                    foreach (var entry in bar.perFill)
                        if (entry != null)
                            yield return new CarrierEffect(LivenessKind.Cascade, bar, entry.effect, entry.growth);
                }
            }
        }

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
