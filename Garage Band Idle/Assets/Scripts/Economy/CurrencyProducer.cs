using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The one thing that produces a currency (design doc section 12, rule 13).
    // It owns two numbers - a RATE in units per second and a YIELD in units
    // per firing - and nothing else in the game creates currency, which is why
    // Fire and Accrue are here and not on whatever drives them. "What makes
    // cash" therefore has one answer, ask cash's producer, instead of being a
    // scan across every holder that might name it.
    //
    // Rate and yield are different quantities, not two flavours of one. A rate
    // is per unit time and accrues whether or not anyone is present; a yield
    // is per occurrence and does not exist until something fires the producer.
    // They compose separately, present separately, and only a rate can earn
    // offline. Nothing in here multiplies one by seconds to reach the other -
    // that is a unit error which silently couples two numbers that must be
    // authored independently.
    //
    // FIRING IS EXTERNAL AND UNNAMED. Fire pays the yield and records nothing
    // about what called it: a button, an automation, a story beat and a test
    // are indistinguishable below this line. "Tap" belongs to the module
    // presenting a button and appears nowhere in here.
    public class CurrencyProducer
    {
        // Kept as contributions rather than a running scalar, because a
        // generator row has to show what THAT generator makes. Same shape the
        // modifier store already has, so production stops being a second,
        // weaker pattern beside it.
        private readonly List<ProductionEntry> _rate = new();
        private readonly List<ProductionEntry> _yield = new();

        private readonly ICurrencies _currencies;
        private readonly IModifierResolver _modifiers;
        private readonly ConditionContext _conditions;

        private readonly ModifierSubject _rateSubject;
        private readonly ModifierSubject _yieldSubject;

        public CurrencyProducer(string currencyId, ICurrencies currencies, IModifierResolver modifiers,
            ConditionContext conditions)
        {
            CurrencyId = currencyId ?? "";
            _currencies = currencies;
            _modifiers = modifiers;
            _conditions = conditions;
            _rateSubject = new ModifierSubject(NumberId(CurrencyId, ProductionFeed.Rate), null, CurrencyId);
            _yieldSubject = new ModifierSubject(NumberId(CurrencyId, ProductionFeed.Yield), null, CurrencyId);
        }

        public string CurrencyId { get; }

        // The id of one of a producer's two numbers (rule 11). Derived from the
        // currency and the quantity rather than authored, so it cannot drift from
        // the producer it belongs to and no chapter has to write two ids per
        // currency. `cash_rate` names the aggregate; a contribution feeding it has
        // its own id, so no selector reaches both and a multiplier can never apply
        // once per line and again over their sum.
        public static string NumberId(string currencyId, ProductionFeed feed)
            => feed == ProductionFeed.Yield ? $"{currencyId}_yield" : $"{currencyId}_rate";

        // What this producer's two numbers ARE, for a selector to match: each has
        // its own id and carries the currency as its OWNER, so ["cash_rate"]
        // reaches one and ["cash"] reaches both. Exposed for the same reason
        // Generator.Subject is: a display asking whether a composition change is
        // one of its own must not rebuild the subject and risk a different answer
        // than the composition used.
        public ModifierSubject RateSubject => _rateSubject;
        public ModifierSubject YieldSubject => _yieldSubject;

        public IReadOnlyList<ProductionEntry> RateContributions => _rate;
        public IReadOnlyList<ProductionEntry> YieldContributions => _yield;

        // ASSEMBLED, NEVER REGISTERED. The caller enumerates the contributions
        // in reach that name this currency and hands over the whole list, which
        // replaces what was here. A contributor that assigned ITSELF in would
        // also have to remove itself, and every teardown bug in this repo is
        // that shape - CurrencyRouter and ConditionContext are both IDisposable
        // for exactly this reason. Rebuilding at the boundary that already
        // re-composes means enable, disable and reset need no bookkeeping.
        //
        // Only a change in the SET of reachable contributors needs this call.
        // Buying a generator, granting a buff and a gate transitioning all
        // change what the existing entries are worth, and that is read live.
        public void Rebuild(IEnumerable<ProductionEntry> contributions)
        {
            _rate.Clear();
            _yield.Clear();

            if (contributions == null)
                return;

            foreach (var entry in contributions)
            {
                var contribution = entry.Contribution;

                // Fail closed on a broken assembler as well as on broken
                // content: a contribution filed under the wrong producer would
                // pay out of the wrong currency's composition, and one that
                // never declared what it feeds must not be guessed into a rate.
                if (entry.Contributor == null || contribution == null)
                {
                    Debug.LogError($"CurrencyProducer '{CurrencyId}': a contribution arrived with no contributor or no data. Ignoring it.");
                    continue;
                }

                if (contribution.CurrencyId != CurrencyId)
                {
                    Debug.LogError($"CurrencyProducer '{CurrencyId}': '{entry.Contributor.ContributorId}' contributes to '{contribution.CurrencyId}', not this currency. Ignoring it.");
                    continue;
                }

                switch (contribution.Feeds)
                {
                    case ProductionFeed.Rate:
                        _rate.Add(entry);
                        break;
                    case ProductionFeed.Yield:
                        _yield.Add(entry);
                        break;
                    default:
                        Debug.LogError($"CurrencyProducer '{CurrencyId}': contribution from '{entry.Contributor.ContributorId}' feeds '{contribution.Feeds}', which names neither of a producer's two numbers. Ignoring it.");
                        break;
                }
            }
        }

        public BigNumber Rate => Compose(_rate, _rateSubject);
        public BigNumber Yield => Compose(_yield, _yieldSubject);

        // Whether anything can feed this number RIGHT NOW - gates honoured, not
        // a count of authored entries. It answers "is there a rate to show"
        // beside a balance, so the readout appears exactly while something can
        // fill the currency, and it asks the same question the composition does.
        public bool HasRate => IsAnyLive(_rate);
        public bool HasYield => IsAnyLive(_yield);

        // What ONE contribution is worth to its number right now: the
        // contributor's own composition, zero while its gate is unmet, floored
        // at zero so no contributor can subtract from another's. This is what
        // "individually addressable" buys - the producer keeps its
        // contributions, so a row can show what its own generator makes.
        //
        // The currency-level composition is deliberately NOT applied here: the
        // multipliers reaching `cash_rate` are the PRODUCER's number, not any
        // one line's, so a row folding them in would credit its own generator
        // with what the whole currency's buffs make of it. These values
        // therefore sum to the producer's BASE, and Rate composes over that sum.
        public BigNumber ValueOf(ProductionEntry entry)
        {
            if (!IsLive(entry))
                return BigNumber.Zero;

            var value = entry.Contributor.ValueOf(entry.Contribution);
            return value < BigNumber.Zero ? BigNumber.Zero : value;
        }

        // Pays the composed yield. What fired it is neither recorded nor asked
        // for - that is the whole of "firing is external and unnamed".
        public void Fire()
        {
            var yield = Yield;
            if (yield > BigNumber.Zero)
                _currencies.Add(CurrencyId, yield);
        }

        // Pays the composed rate over a span of elapsed real time. The caller
        // owns the clock; this only turns a rate into an amount, which is the
        // one place per-second becomes a quantity.
        public void Accrue(double seconds)
        {
            if (seconds <= 0)
                return;

            var rate = Rate;
            if (rate > BigNumber.Zero)
                _currencies.Add(CurrencyId, rate * seconds);
        }

        // (sum of the live contributions) composed with the modifiers reaching
        // this currency's number, applied ONCE over the sum. A line's own
        // multipliers are already inside ValueOf, and the aggregate's id is not
        // one a line answers to (see NumberId), so no term scales both.
        //
        // Nothing live means nothing to compose, and the composition is skipped
        // rather than applied to zero: a modifier scales what contributions
        // make, so it can never be the sole source of a number - a producer with
        // nothing feeding it, or with every contribution gated off, produces zero
        // however many multipliers name it. Anything landing below zero yields
        // nothing and no multiplier resurrects it, so production can never drain
        // a balance.
        private BigNumber Compose(List<ProductionEntry> entries, in ModifierSubject subject)
        {
            var live = false;
            var total = BigNumber.Zero;

            foreach (var entry in entries)
            {
                if (!IsLive(entry))
                    continue;

                live = true;
                total += ValueOf(entry);
            }

            if (!live)
                return BigNumber.Zero;

            var composed = _modifiers.For(subject).ApplyTo(total);
            return composed < BigNumber.Zero ? BigNumber.Zero : composed;
        }

        // The one place a contribution counts or does not: both halves present,
        // gate holding. Compose, ValueOf and HasRate/HasYield all ask here, so
        // a readout, the payout behind it and the guard deciding whether to
        // show the readout at all can never disagree.
        private bool IsLive(ProductionEntry entry)
            => entry.Contributor != null && entry.Contribution != null
                && ConditionEvaluator.IsMet(entry.Contribution.Gate, _conditions);

        private bool IsAnyLive(List<ProductionEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (IsLive(entry))
                    return true;
            }
            return false;
        }
    }
}
