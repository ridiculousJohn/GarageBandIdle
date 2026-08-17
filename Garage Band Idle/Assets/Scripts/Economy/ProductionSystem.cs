using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The chapter's currency producers (design doc section 12, rule 13): ONE per
    // currency anything feeds, each owning that currency's rate and yield. This is
    // the only thing in the game that creates currency. "What makes cash" therefore
    // has one answer - ask cash's producer - instead of being a scan across
    // generators, producer assets and whatever else might have named it.
    //
    // A rate and a yield are told apart by what the number IS - per second against
    // per firing - never by who fired it, so nothing here asks whether a button, an
    // automation or a test made the call.
    //
    // ASSEMBLED, NEVER REGISTERED. Assemble walks the contributors in reach and
    // hands each producer its whole list; a contributor never files itself in, so
    // nothing has to remember to file itself out. Only a change in the SET needs
    // the call - buying a generator, granting a buff and a gate transitioning all
    // change what the existing lines are WORTH, and that is read live.
    //
    // Nothing here pushes a notification mid-mutation: a tick has bars still to
    // drain and a purchase has unlocks still to evaluate. Scope calls
    // RefreshYields once each operation has settled, and the event fires only when
    // a value actually moved.
    public class ProductionSystem : IDisposable
    {
        private readonly Dictionary<string, CurrencyProducer> _byCurrency = new();

        // first-seen order, so accrual and publication are deterministic
        private readonly List<string> _currencyOrder = new();

        // Which currencies a contributor's YIELD lines feed, which is what a firing
        // needs: pressing Jam pays the currencies Jam contributes a yield to, and
        // nothing else. Flattening this away would make one press pay every yield
        // line in the chapter - invisible with one surface, wrong with two.
        private readonly Dictionary<string, List<string>> _yieldCurrenciesByContributor = new();

        // declaration order, so RefreshYields publishes deterministically
        private readonly List<string> _firingOrder = new();
        private readonly Dictionary<string, BigNumber> _lastYield = new();

        private readonly List<AuthoredContributor> _authored = new();
        private readonly GeneratorSystem _generators;
        private readonly UpgradeSystem _upgrades;
        private readonly ICurrencies _currencies;
        private readonly ModifierSystem _modifiers;
        private readonly ConditionContext _conditions;

        // reused across assemblies so a re-assemble allocates nothing per entry
        private readonly Dictionary<string, List<ProductionEntry>> _staging = new();

        // Fires from RefreshYields when a currency's composed yield moved (buff
        // bought, run reset, a gate transitioned), carrying WHICH currency - the
        // same shape as BalanceChanged, so a surface advertising one currency
        // ignores another's movement instead of redrawing on it.
        public event Action<string> YieldChanged;

        public ProductionSystem(IEnumerable<ProducerDefinition> producers, GeneratorSystem generators,
            UpgradeSystem upgrades, ICurrencies currencies, ModifierSystem modifiers,
            ConditionContext conditions)
        {
            _generators = generators;
            _upgrades = upgrades;
            _currencies = currencies;
            _modifiers = modifiers;
            _conditions = conditions;

            foreach (var producer in producers)
            {
                if (producer == null)
                    continue;

                _authored.Add(new AuthoredContributor(producer, modifiers));
            }

            // An upgrade's contributions are live exactly while its latch holds, so
            // the SET changes when a latch does. Subscribing here rather than having
            // UpgradeSystem push contributions in keeps the direction one way: this
            // asks who is applied, nothing announces itself into a producer.
            if (_upgrades != null)
            {
                _upgrades.UpgradeApplied += HandleUpgradeLatchChanged;
                _upgrades.UpgradeCleared += HandleUpgradeLatchChanged;
            }

            Assemble();

            foreach (var currencyId in _firingOrder)
                _lastYield[currencyId] = YieldOf(currencyId);
        }

        public void Dispose()
        {
            if (_upgrades == null)
                return;

            _upgrades.UpgradeApplied -= HandleUpgradeLatchChanged;
            _upgrades.UpgradeCleared -= HandleUpgradeLatchChanged;
        }

        // Rebuilds every producer's contribution list from the contributors in
        // reach. Public because a restore replaces the upgrade latches silently -
        // deliberately, so no subscriber reads a half-restored economy - which means
        // the set can change with no event to hang this on.
        public void Assemble()
        {
            foreach (var entries in _staging.Values)
                entries.Clear();
            _yieldCurrenciesByContributor.Clear();
            _firingOrder.Clear();

            foreach (var contributor in _authored)
                Stage(contributor);

            // Generators are always in the set: an unowned one contributes zero
            // rather than being absent, so buying the first unit moves a value
            // instead of changing the shape of the economy.
            if (_generators != null)
            {
                foreach (var generator in _generators.All)
                    Stage(generator);
            }

            if (_upgrades != null)
            {
                foreach (var upgrade in _upgrades.All)
                {
                    if (upgrade.Applied)
                        Stage(upgrade);
                }
            }

            // Only an AUTHORED contributor is fireable, and that is a rule about
            // what a module may name rather than about what a contributor holds -
            // so it is recorded by walking the list that defines it, and nothing
            // has to be told which kind it is. Derived from "holds a yield line",
            // it made an applied upgrade fireable, because stage_presence
            // contributes to cash's yield. A bonus is not a button.
            foreach (var contributor in _authored)
                RecordFireable(contributor);

            // Every currency ever staged is rebuilt, including one nothing feeds
            // anymore - its list was cleared above, so it rebuilds EMPTY rather than
            // being dropped. Its rate is then zero by composition instead of by
            // absence, so a readout asking about it gets the same answer either way.
            foreach (var entry in _staging)
                ProducerFor(entry.Key).Rebuild(entry.Value);
        }

        // Whether a module may fire under this id: an authored contributor holding
        // at least one yield line. Asked by the surface naming it, so a stale
        // definitionId is reported where it can name the module rather than failing
        // silently on the first press.
        public bool CanFire(string contributorId)
            => !string.IsNullOrEmpty(contributorId)
               && _yieldCurrenciesByContributor.ContainsKey(contributorId);

        // Which currencies firing this contributor pays into, in the contributor's
        // own declaration order. The surface asks so it can advertise what pressing
        // it is worth WITHOUT assuming a currency - the Jam label read a hardcoded
        // cash before, which was right only by accident of chapter 1.
        public IReadOnlyList<string> FiredCurrencies(string contributorId)
            => _yieldCurrenciesByContributor.TryGetValue(contributorId ?? "", out var currencyIds)
                ? currencyIds
                : Array.Empty<string>();

        // One surface firing: every currency that contributor feeds a yield to pays
        // out its composed yield. Naming the contributor is the whole point - a
        // press pays what the pressed thing produces, and nothing else.
        //
        // The yield paid is the CURRENCY'S, not the contributor's share of it: rule
        // 13 says a currency has one yield, so a bonus another fact contributes to
        // cash's yield (stage_presence) is paid by the press exactly as the base
        // line is, with nothing having to know the bonus exists.
        public void Fire(string contributorId)
        {
            if (!_yieldCurrenciesByContributor.TryGetValue(contributorId ?? "", out var currencyIds))
            {
                // A surface firing an unknown contributor is broken wiring, not a
                // no-op worth swallowing: the button would silently pay nothing
                // forever, which reads as a tuning problem rather than a
                // mis-authored module entry.
                Debug.LogError($"ProductionSystem: Fire for '{contributorId}', which this chapter authors no yield lines for. Nothing paid.");
                return;
            }

            foreach (var currencyId in currencyIds)
                _byCurrency[currencyId].Fire();
        }

        // Every currency's rate, over a span of elapsed real time. One call rather
        // than a generator pass and a config pass: a rate is a rate whatever
        // declared it, which is what makes the idle payout (section 9) a question
        // about the quantity instead of about the holder kind.
        public void Accrue(double seconds)
        {
            foreach (var currencyId in _currencyOrder)
                _byCurrency[currencyId].Accrue(seconds);
        }

        // Every query below reports what production can do RIGHT NOW, gates
        // included - the same question Accrue and Fire answer when they decide what
        // to pay. A readout ignoring a gate would advertise a yield the press does
        // not deliver, which is worse than showing nothing because the number looks
        // authored rather than stale.

        // whether anything can currently produce this currency, by either quantity;
        // drives the rate readout beside a fill currency's balance, so the readout
        // appears exactly while something can fill it
        public bool HasProduction(string currencyId)
            => TryGet(currencyId, out var producer) && (producer.HasRate || producer.HasYield);

        // zero while every line feeding the currency is dormant (gate unmet)
        public BigNumber RateOf(string currencyId)
            => TryGet(currencyId, out var producer) ? producer.Rate : BigNumber.Zero;

        // the composed per-firing yield, on the same terms: a dormant line pays
        // nothing when Fire runs, so it contributes nothing here either
        public BigNumber YieldOf(string currencyId)
            => TryGet(currencyId, out var producer) ? producer.Yield : BigNumber.Zero;

        // The post-mutation refresh: re-evaluates each fireable currency's yield and
        // publishes only an actual move. Called by Scope after each
        // complete operation settles - never from inside a system, so no subscriber
        // can observe a half-settled mutation (state, then notify).
        public void RefreshYields()
        {
            foreach (var currencyId in _firingOrder)
            {
                var value = YieldOf(currencyId);
                if (_lastYield.TryGetValue(currencyId, out var last) && value == last)
                    continue;

                _lastYield[currencyId] = value;
                YieldChanged?.Invoke(currencyId);
            }
        }

        private void HandleUpgradeLatchChanged(Upgrade upgrade) => Assemble();

        // Files one contributor's lines under the currencies they name. A line
        // naming no currency is broken content the importer refuses and boot
        // validation reports; it is dropped here rather than filed under "" where it
        // would pay into nothing.
        //
        // It knows nothing about surfaces. Every contributor is staged the same way,
        // whatever kind it is - which is the whole reason a producer can be
        // assembled without learning what fed it.
        private void Stage(IProductionContributor contributor)
        {
            foreach (var contribution in contributor.Contributions)
            {
                if (contribution == null)
                    continue;

                var currencyId = contribution.CurrencyId;
                if (string.IsNullOrEmpty(currencyId))
                {
                    Debug.LogError($"ProductionSystem: '{contributor.ContributorId}' has a contribution naming no currency. Ignoring it.");
                    continue;
                }

                if (!_staging.TryGetValue(currencyId, out var entries))
                    _staging.Add(currencyId, entries = new List<ProductionEntry>());
                entries.Add(new ProductionEntry(contributor, contribution));

                // publication order covers every currency with a yield at all,
                // fireable or not: stage_presence's bonus moves cash's yield, and a
                // label must repaint for it even though the upgrade is not a surface
                if (contribution.Feeds == ProductionFeed.Yield && !_firingOrder.Contains(currencyId))
                    _firingOrder.Add(currencyId);
            }
        }

        // Which currencies firing this contributor pays into, in its own declaration
        // order. Called only for the authored producers, so being fireable is a fact
        // about which list a contributor came from rather than a property anything
        // has to carry.
        private void RecordFireable(IProductionContributor contributor)
        {
            foreach (var contribution in contributor.Contributions)
            {
                if (contribution == null || contribution.Feeds != ProductionFeed.Yield)
                    continue;

                var currencyId = contribution.CurrencyId;
                if (string.IsNullOrEmpty(currencyId))
                    continue;

                if (!_yieldCurrenciesByContributor.TryGetValue(contributor.ContributorId, out var fired))
                    _yieldCurrenciesByContributor.Add(contributor.ContributorId, fired = new List<string>());
                if (!fired.Contains(currencyId))
                    fired.Add(currencyId);
            }
        }

        // Get-or-create, because a currency's producer outlives any one assembly:
        // an upgrade whose latch cleared must leave cash's producer in place with
        // one fewer line, not remove cash's producer.
        private CurrencyProducer ProducerFor(string currencyId)
        {
            if (_byCurrency.TryGetValue(currencyId, out var producer))
                return producer;

            producer = new CurrencyProducer(currencyId, _currencies, _modifiers, _conditions);
            _byCurrency.Add(currencyId, producer);
            _currencyOrder.Add(currencyId);
            return producer;
        }

        private bool TryGet(string currencyId, out CurrencyProducer producer)
            => _byCurrency.TryGetValue(currencyId ?? "", out producer);
    }
}
