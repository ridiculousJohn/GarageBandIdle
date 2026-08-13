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
        private readonly List<ProductionConfig> _tick = new();

        // every tap config, for the currency-scoped readouts, which ask "what can
        // fill this currency" across the whole chapter
        private readonly List<ProductionConfig> _tap = new();

        // Tap configs BY PRODUCER, which is what a firing needs. Flattening these
        // into one list is what made a tap fire every producer in the chapter:
        // harmless while Jam is the only tap surface, and wrong the moment a
        // Merch/Sell module exists, since pressing Jam would sell merch too. Which
        // producer a module presents was always authored - only the runtime
        // ignored it.
        private readonly Dictionary<string, List<ProductionConfig>> _tapByProducer = new();

        // declaration order, so RefreshTapValue publishes deterministically
        private readonly List<string> _tapProducerOrder = new();
        private readonly Dictionary<string, BigNumber> _lastTapValue = new();

        private readonly ICurrencies _currencies;
        private readonly ModifierSystem _modifiers;
        private readonly ConditionContext _conditions;

        // Fires from RefreshTapValue when a producer's composed tap value moved
        // (buff bought, run reset, a config's gate transitioned), carrying WHICH
        // producer - the same shape as BalanceChanged, so a module showing one tap
        // surface ignores another's movement instead of redrawing on it.
        public event Action<string> TapValueChanged;

        public ProductionSystem(IEnumerable<ProducerDefinition> producers, ICurrencies currencies,
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
                    // a config that slipped through must not silently fire.
                    // ProductionConfig.IsComposable owns the rule, so this and
                    // boot validation cannot disagree about what is authorable.
                    if (!ProductionConfig.IsComposable(config.Composes))
                    {
                        Debug.LogError($"ProductionSystem: producer '{producer.Id}' config for '{config.CurrencyId}' declares composition '{config.Composes}', which a config cannot compose - it must be a defined target that composes globally. Ignoring the config.");
                        continue;
                    }

                    switch (config.Trigger)
                    {
                        case ProductionTrigger.Tick:
                            _tick.Add(config);
                            break;
                        case ProductionTrigger.Tap:
                            _tap.Add(config);
                            if (!_tapByProducer.TryGetValue(producer.Id, out var configs))
                            {
                                _tapByProducer.Add(producer.Id, configs = new List<ProductionConfig>());
                                _tapProducerOrder.Add(producer.Id);
                            }
                            configs.Add(config);
                            break;
                        default:
                            Debug.LogError($"ProductionSystem: producer '{producer.Id}' config for '{config.CurrencyId}' has trigger None (uninitialized). Ignoring the config.");
                            break;
                    }
                }
            }

            foreach (var producerId in _tapProducerOrder)
                _lastTapValue[producerId] = TapValue(producerId);
        }

        // Whether this chapter authors a tap surface under this id. Asked by the
        // module that presents one, so a stale definitionId is reported where it
        // can name the module rather than failing silently on the first press.
        public bool HasTapProducer(string producerId)
            => !string.IsNullOrEmpty(producerId) && _tapByProducer.ContainsKey(producerId);

        // The composed yield ONE tap surface advertises: that producer's tap
        // configs that compose TapValue, gates honored (Chapter 1: the jam
        // producer's cash config). Per producer rather than chapter-wide, so a
        // button's label describes what pressing that button pays.
        public BigNumber TapValue(string producerId)
        {
            if (!_tapByProducer.TryGetValue(producerId ?? "", out var configs))
                return BigNumber.Zero;

            var total = BigNumber.Zero;
            foreach (var config in configs)
            {
                if (config.Composes == ModifierTarget.CurrencyYield && ConditionEvaluator.IsMet(config.Gate, _conditions))
                    total += Composed(config);
            }
            return total;
        }

        // Every query below reports what production can do RIGHT NOW, gates
        // included - the same question Tick and FireTap answer when they decide
        // what to pay. A readout that ignored a gate would advertise a yield the
        // tap does not deliver, which is worse than showing nothing because the
        // number looks authored rather than stale.

        // whether any module-held config can currently produce this currency;
        // drives the rate readout beside a fill currency's balance, so the
        // readout appears exactly while something can fill it
        public bool HasProduction(string currencyId)
            => HasLiveConfig(_tick, currencyId) || HasLiveConfig(_tap, currencyId);

        // zero while every config for the currency is dormant (gate unmet)
        public BigNumber RatePerSecond(string currencyId)
            => Sum(_tick, currencyId);

        // the composed per-tap yield, on the same terms: a dormant config pays
        // nothing when FireTap runs, so it contributes nothing here either
        public BigNumber PerTap(string currencyId)
            => Sum(_tap, currencyId);

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

        // One tap surface firing: every tap-triggered config THAT PRODUCER holds
        // whose gate holds pays out, in the producer's own config order (cash, then
        // the engagement yields). Naming the producer is the whole point - a press
        // pays what the pressed thing produces, and nothing else.
        public void FireTap(string producerId)
        {
            if (!_tapByProducer.TryGetValue(producerId ?? "", out var configs))
            {
                // A module firing an unknown producer is broken wiring, not a
                // no-op worth swallowing: the button would silently pay nothing
                // forever, which reads as a tuning problem rather than a
                // mis-authored module entry.
                Debug.LogError($"ProductionSystem: FireTap for producer '{producerId}', which this chapter authors no tap configs for. Nothing paid.");
                return;
            }

            foreach (var config in configs)
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
            // per producer, in declaration order: each tap surface publishes only
            // its own movement, so a chapter with two of them never redraws one
            // because the other changed
            foreach (var producerId in _tapProducerOrder)
            {
                var value = TapValue(producerId);
                if (value == _lastTapValue[producerId])
                    continue;

                _lastTapValue[producerId] = value;
                TapValueChanged?.Invoke(producerId);
            }
        }

        // A config composes exactly the target it declares, QUALIFIED BY ITS OWN
        // CURRENCY - the yield of the currency it pays, or that currency's rate -
        // with no per-target branch here. Qualifying by the config's own currency
        // is what keeps a buff on one currency's payout off another's, which a
        // single global bucket per kind could not express. Anything landing below
        // zero yields nothing and no multiplier resurrects it, so a firing can
        // never drain a balance.
        private BigNumber Composed(ProductionConfig config)
        {
            var value = config.Composes == ModifierTarget.None
                ? (BigNumber)config.Amount
                : _modifiers.For(ModifierTargetKey.Of(config.Composes, config.CurrencyId)).ApplyTo(config.Amount);
            return value < BigNumber.Zero ? BigNumber.Zero : value;
        }

        // the one place a query decides which configs count: same currency, gate
        // holding. Sum and HasLiveConfig share it so a readout and the guard that
        // decides whether to show the readout can never disagree.
        private bool Counts(ProductionConfig config, string currencyId)
            => config.CurrencyId == currencyId && ConditionEvaluator.IsMet(config.Gate, _conditions);

        private BigNumber Sum(List<ProductionConfig> configs, string currencyId)
        {
            var total = BigNumber.Zero;
            foreach (var config in configs)
            {
                if (Counts(config, currencyId))
                    total += Composed(config);
            }
            return total;
        }

        private bool HasLiveConfig(List<ProductionConfig> configs, string currencyId)
        {
            foreach (var config in configs)
            {
                if (Counts(config, currencyId))
                    return true;
            }
            return false;
        }
    }
}
