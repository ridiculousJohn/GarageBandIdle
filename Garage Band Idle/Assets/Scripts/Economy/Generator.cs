using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime state for one generator, wrapping its GeneratorDefinition asset.
    // State lives here keyed by the definition; the definition itself is
    // immutable content.
    //
    // It is a production contributor (design doc section 12, rule 13): it does not
    // pay a currency, it declares what each owned unit is worth to whichever
    // currency producers its contributions name, and those producers do the paying.
    // A generator therefore has no production path of its own, and can feed a
    // yield as readily as a rate.
    public class Generator : IProductionContributor
    {
        public GeneratorDefinition Definition { get; }

        public int Owned { get; private set; }

        // fires after a successful purchase changes Owned; code-only subscribers
        public event Action OwnedChanged;

        private readonly ModifierSystem _modifiers;
        private readonly ModifierSubject _subject;

        // one subject per contribution, built once: the definition's line list is
        // immutable content, and a subject rebuilt per read could drift from the
        // one the change notification asked about
        private readonly Dictionary<ProductionContribution, ModifierSubject> _lineSubjects = new();

        public Generator(GeneratorDefinition definition, ModifierSystem modifiers)
        {
            Definition = definition;
            _modifiers = modifiers;
            _subject = new ModifierSubject(definition.Id, definition.Tags);

            foreach (var contribution in definition.Contributions)
            {
                if (contribution != null)
                    _lineSubjects[contribution] = contribution.SubjectUnder(definition.Id, definition.Tags);
            }
        }

        public BigNumber NextCost => CostCalculator.Cost(Definition, Owned);

        // What this GENERATOR is, for a selector to match: its id and its tags, so
        // `["practice_amp"]` reaches it and `["bandmate"]` reaches every generator
        // carrying that tag. Its lines carry it as their owner, so a term matching
        // here matches all of them. Exposed so a display can ask whether a
        // composition change is one of its own instead of rebuilding the subject
        // and risking a different answer than the composition used.
        public ModifierSubject Subject => _subject;

        public string ContributorId => Definition.Id;

        public IReadOnlyList<ProductionContribution> Contributions => Definition.Contributions;

        // What one line of this generator is worth to the number it feeds: its
        // per-unit amount times the owned count, composed with the modifiers
        // reaching THAT line. The line's own id is what makes "double the drummer's
        // cash" sayable; the generator's id reaches every line it holds, because
        // the subject offers the owner too.
        //
        // Fails closed twice: a negative amount must never drain a currency
        // (invalid data, boot validation reports it), and an unowned generator
        // contributes nothing whatever reaches it - a multiplier on gear you never
        // bought scales zero, which is the only answer that cannot pay out.
        //
        // The currency-level composition is deliberately absent: cash_rate is the
        // producer's to compose, once, over the sum of every line feeding it.
        public BigNumber ValueOf(ProductionContribution contribution)
        {
            if (contribution == null || Owned == 0 || contribution.Amount < 0)
                return BigNumber.Zero;

            return _modifiers.For(SubjectOf(contribution))
                .ApplyTo((BigNumber)contribution.Amount * Owned);
        }

        // What ONE unit contributes on that line, buffs included. Derived from the
        // same composition as ValueOf rather than from the raw amount, so
        // Owned x PerUnitValueOf == ValueOf and a display showing both cannot
        // contradict itself. An unowned generator previews its first unit, which is
        // what a row advertises before you own one.
        public BigNumber PerUnitValueOf(ProductionContribution contribution)
        {
            if (contribution == null || contribution.Amount < 0)
                return BigNumber.Zero;

            var units = Owned == 0 ? 1 : Owned;
            return _modifiers.For(SubjectOf(contribution))
                .ApplyTo((BigNumber)contribution.Amount * units) / units;
        }

        // Whether a selector reaches ANYTHING this generator holds - itself or one
        // of its lines. A display refreshing on a modifier change asks this rather
        // than testing the generator's subject alone, because a buff naming one line
        // (`drummer_cash`) is invisible to the generator's own subject while very
        // much changing what the row shows. Asked of the thing being matched, the
        // same seam the composition uses, so the two cannot disagree.
        public bool IsReachedBy(in ModifierSelector selector)
        {
            if (selector.Matches(_subject))
                return true;

            foreach (var subject in _lineSubjects.Values)
            {
                if (selector.Matches(subject))
                    return true;
            }
            return false;
        }

        // The subject for one line, from the map built at construction. A line the
        // map does not know is one that arrived from outside this generator's
        // definition, which would compose against a subject naming the wrong owner:
        // it fails closed to the generator's own subject so the value is at worst
        // coarse rather than attributed to something else.
        public ModifierSubject SubjectOf(ProductionContribution contribution)
            => contribution != null && _lineSubjects.TryGetValue(contribution, out var subject)
                ? subject
                : _subject;

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
        // unlock condition, never a latch. A latch is one-way, so a release that
        // zeroed the fleet would leave every row the player had ever seen on
        // screen with nothing able to un-set it. What a reveal remembers has to
        // be remembered by the state the condition reads (an earned total, a
        // scoped flag), not by a bool out here.
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
