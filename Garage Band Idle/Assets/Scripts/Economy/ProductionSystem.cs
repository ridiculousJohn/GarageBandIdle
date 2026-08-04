using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Fires the module-held production configs (design doc section 12, rule
    // 13): tick-triggered configs accrue on the real-time tick, tap-triggered
    // ones fire on the Jam tap. Gates are ordinary Conditions checked per
    // firing, and a config's output composes exactly the modifier target it
    // declares - the Jam cash config declares TapValue, so flat adds
    // (stage_presence) and multipliers (event-tier rewards) land exactly as
    // they did before; an undeclared config pays its raw amount. Generators
    // keep their own production path and are the only idle-eligible holder
    // (section 9). No producers leaves the system inert.
    //
    // TapValue can move for two reasons - a modifier changed, or a config's
    // gate transitioned (any Condition input: a flag, a balance, an owned
    // count) - and neither may notify the UI mid-mutation: a tick's
    // production has bars still to drain, a purchase has unlocks still to
    // evaluate. So nothing here pushes. GameManager calls RefreshTapValue
    // after each complete operation settles (tick, Jam, purchase - the same
    // boundary every unlock evaluation uses), and the event fires only when
    // the evaluated value actually moved.
    public class ProductionSystem
    {
        private static readonly ModifierTargetKey TapValueTarget = ModifierTargetKey.Global(ModifierTarget.TapValue);

        private readonly List<ProductionConfig> _tick = new();
        private readonly List<ProductionConfig> _tap = new();
        private readonly CurrencyManager _currencies;
        private readonly ModifierSystem _modifiers;
        private readonly ConditionContext _conditions;
        private BigNumber _lastTapValue;

        // fires from RefreshTapValue when the composed tap value moved (buff
        // bought, run reset, a config's gate transitioned); UI listens here,
        // nothing polls
        public event Action TapValueChanged;

        public ProductionSystem(IEnumerable<ProducerDefinition> producers, CurrencyManager currencies,
            ModifierSystem modifiers, ConditionContext conditions)
        {
            _currencies = currencies;
            _modifiers = modifiers;
            _conditions = conditions;

            foreach (var producer in producers)
            {
                if (producer == null)
                    continue;

                foreach (var config in producer.Production)
                {
                    // fail closed on broken content - the importer refuses
                    // these states and boot validation reports stale assets;
                    // a config that slipped through must not silently fire
                    if (config.Composes != ModifierTarget.None && config.Composes != ModifierTarget.TapValue)
                    {
                        Debug.LogError($"ProductionSystem: producer '{producer.Id}' config for '{config.CurrencyId}' declares composition '{config.Composes}', which no module-held config composes. Ignoring the config.");
                        continue;
                    }

                    switch (config.Trigger)
                    {
                        case ProductionTrigger.Tick:
                            _tick.Add(config);
                            break;
                        case ProductionTrigger.Tap:
                            _tap.Add(config);
                            break;
                        default:
                            Debug.LogError($"ProductionSystem: producer '{producer.Id}' config for '{config.CurrencyId}' has trigger None (uninitialized). Ignoring the config.");
                            break;
                    }
                }
            }

            _lastTapValue = TapValue;
        }

        // The composed Jam yield the button advertises: every tap config that
        // composes TapValue, gates honored (Chapter 1: the cash config).
        public BigNumber TapValue
        {
            get
            {
                var total = BigNumber.Zero;
                foreach (var config in _tap)
                {
                    if (config.Composes == ModifierTarget.TapValue && ConditionEvaluator.IsMet(config.Gate, _conditions))
                        total += Composed(config);
                }
                return total;
            }
        }

        // whether any module-held config produces this currency; drives the
        // rate readout beside a fill currency's balance
        public bool HasProduction(string currencyId)
            => Find(_tick, currencyId) != null || Find(_tap, currencyId) != null;

        // zero while every config for the currency is dormant (gate unmet)
        public BigNumber RatePerSecond(string currencyId)
        {
            var total = BigNumber.Zero;
            foreach (var config in _tick)
            {
                if (config.CurrencyId == currencyId && ConditionEvaluator.IsMet(config.Gate, _conditions))
                    total += Composed(config);
            }
            return total;
        }

        // the configured per-tap yield, for display; firing itself stays gated
        public BigNumber PerTap(string currencyId)
        {
            var total = BigNumber.Zero;
            foreach (var config in _tap)
            {
                if (config.CurrencyId == currencyId)
                    total += Composed(config);
            }
            return total;
        }

        public void Tick(double seconds)
        {
            foreach (var config in _tick)
            {
                if (!ConditionEvaluator.IsMet(config.Gate, _conditions))
                    continue;

                var rate = Composed(config);
                if (rate > BigNumber.Zero)
                    _currencies.Add(config.CurrencyId, rate * seconds);
            }
        }

        // the Jam tap: every tap-triggered config whose gate holds pays out,
        // in producer list order (cash, then the engagement yields)
        public void FireTap()
        {
            foreach (var config in _tap)
            {
                if (!ConditionEvaluator.IsMet(config.Gate, _conditions))
                    continue;

                var amount = Composed(config);
                if (amount > BigNumber.Zero)
                    _currencies.Add(config.CurrencyId, amount);
            }
        }

        // The post-mutation refresh: re-evaluates the tap value and publishes
        // only an actual move. Called by GameManager after each complete
        // operation settles (end of tick, end of Jam, a successful purchase,
        // a future reset/restore) - never from inside a system, so no
        // subscriber can observe a half-settled mutation (state, then notify).
        public void RefreshTapValue()
        {
            var value = TapValue;
            if (value == _lastTapValue)
                return;

            _lastTapValue = value;
            TapValueChanged?.Invoke();
        }

        // A config composes exactly the target it declares. Anything landing
        // below zero yields nothing and no multiplier resurrects it - the same
        // fail-closed rule TapSystem had, so a tap can never drain cash.
        private BigNumber Composed(ProductionConfig config)
        {
            var value = config.Composes == ModifierTarget.TapValue
                ? _modifiers.For(TapValueTarget).ApplyTo(config.Amount)
                : (BigNumber)config.Amount;
            return value < BigNumber.Zero ? BigNumber.Zero : value;
        }

        private static ProductionConfig Find(List<ProductionConfig> configs, string currencyId)
        {
            foreach (var config in configs)
            {
                if (config.CurrencyId == currencyId)
                    return config;
            }
            return null;
        }
    }
}
