using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime state for one generator, wrapping its GeneratorDefinition asset.
    // State lives here keyed by the definition; the definition itself is
    // immutable content.
    public class Generator
    {
        public GeneratorDefinition Definition { get; }

        public int Owned { get; private set; }

        // fires after a successful purchase changes Owned; code-only subscribers
        public event Action OwnedChanged;

        private readonly ModifierSystem _modifiers;
        private readonly ModifierTargetKey _outputTarget;

        public Generator(GeneratorDefinition definition, ModifierSystem modifiers)
        {
            Definition = definition;
            _modifiers = modifiers;
            _outputTarget = ModifierTargetKey.Of(ModifierTarget.GeneratorOutput, definition.Id);
        }

        public BigNumber NextCost => CostCalculator.Cost(Definition, Owned);

        // The address modifiers reach this generator's output at. Exposed so a
        // display can ask whether a composition change is one of its own
        // instead of rebuilding the key and risking a different answer.
        public ModifierTargetKey OutputTarget => _outputTarget;

        // This generator's output, composed with the modifiers targeting it
        // (amp_strings, kit_upgrade). Currency-wide multipliers compose over the
        // summed fleet in GeneratorSystem, not here. Fails closed twice: a
        // negative base output must never drain a currency (invalid data, boot
        // validation reports it), and an unowned generator produces nothing
        // whatever targets it - a flat add must not pay out gear you never bought.
        public BigNumber ProductionPerSecond => Owned == 0 || Definition.BaseOutput < 0
            ? BigNumber.Zero
            : _modifiers.For(_outputTarget).ApplyTo((BigNumber)Definition.BaseOutput * Owned);

        // What one unit currently contributes, buffs included. Derived from the
        // same composition as ProductionPerSecond rather than from BaseOutput,
        // so Owned x PerUnitProduction == ProductionPerSecond and a display
        // showing both cannot contradict itself. An Add on this target is a
        // fleet-level lump, so dividing spreads it evenly across the units -
        // the only split that keeps that identity true. An unowned generator
        // previews its first unit, which is what a row advertises before you
        // own one; a negative base output fails closed as it does above.
        public BigNumber PerUnitProduction
        {
            get
            {
                if (Definition.BaseOutput < 0)
                    return BigNumber.Zero;

                var units = Owned == 0 ? 1 : Owned;
                return _modifiers.For(_outputTarget).ApplyTo((BigNumber)Definition.BaseOutput * units) / units;
            }
        }

        // buys one unit if affordable; deducts the declared cost currency -
        // never the produced currency - and bumps Owned
        public bool TryBuy(ICurrencies currencies)
        {
            var cost = NextCost;

            // fail closed on broken content: a non-positive cost is invalid
            // data (boot validation reports it) and must never behave as an
            // endless free purchase
            if (cost <= BigNumber.Zero)
                return false;

            if (currencies.Get(Definition.CostCurrencyId) < cost)
                return false;

            // Owned settles before the spend: Add fires BalanceChanged
            // synchronously, and no subscriber may ever observe the cost
            // deducted with the purchase not yet counted (state, then notify)
            Owned++;
            currencies.Add(Definition.CostCurrencyId, -cost);
            OwnedChanged?.Invoke();
            return true;
        }

        // Whether this generator is on offer right now: a LIVE read of its
        // unlock condition, never a latch. It was a latch, and a latch is
        // one-way - after a release zeroed the fleet, every row the player had
        // ever seen stayed on screen because nothing could un-set it. What a
        // reveal remembers has to be remembered by the state the condition
        // reads (an earned total, a scoped flag), not by a bool out here.
        public bool IsUnlocked(ConditionContext context)
            => ConditionEvaluator.IsMet(Definition.Unlock, context);

        // run reset: state-only, no notification - GeneratorSystem fires
        // OwnedChanged after EVERY generator has settled, so a subscriber
        // never observes a half-reset fleet. Returns whether anything changed.
        internal bool ResetOwned()
        {
            if (Owned == 0)
                return false;

            Owned = 0;
            return true;
        }

        internal void NotifyOwnedChanged() => OwnedChanged?.Invoke();

        // save/load: state-only re-establishment - GeneratorSystem restores
        // the whole fleet and notifies after every count settles. A negative
        // count is corrupt save data and fails closed to zero (a negative
        // Owned would corrupt the cost curve and production). Returns whether
        // anything changed.
        internal bool RestoreOwned(int owned)
        {
            if (owned < 0)
            {
                Debug.LogError($"Generator: RestoreOwned with negative count '{owned}' for '{Definition.Id}'. Restoring zero.");
                owned = 0;
            }

            if (Owned == owned)
                return false;

            Owned = owned;
            return true;
        }
    }
}
