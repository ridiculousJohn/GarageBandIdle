using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // A multiplier computed from career state rather than authored as a constant
    // (the career row of design doc 12.6). Deliberately not an Effect: an
    // Effect's multiplier is an authored double, and these are unbounded
    // BigNumber products of live facts.
    //
    // Compute receives the GATHER-ORIGIN context - the source scope in stage 1,
    // the currency home in stage 2 - never a context rebased to the declaring
    // scope. That is a deliberate asymmetry with 12.4, where conditions and
    // action lists evaluate in their declaring scope: a multiplier is addressed
    // to a NUMBER, and the number's identity includes the chain it resolves on.
    // RoadieActiveBoost is unimplementable from a root-rebased context, since
    // root's chain holds no chapter. Reads stay chain-only either way, so no
    // sibling reach opens up.
    [Serializable]
    public abstract class MultiplierFormula
    {
        public abstract BigNumber Compute(GameContext ctx);

        // Load-time reference and range checks (design doc 12.12), driven
        // through the owning definition's validation.
        public virtual void Validate(ValidationContext ctx) { }
    }

    // 1 + coefficient * balance: additive within the term, so Records at 20 with
    // 0.02 per Record gives 1.4x (design doc 3's records_income).
    [Serializable]
    public class LinearOnBalance : MultiplierFormula
    {
        [DefinitionId(typeof(CurrencyDefinition))] public string currencyId;
        public double coefficient;

        public override BigNumber Compute(GameContext ctx) =>
            BigNumber.One + coefficient * ctx.GetBalance(currencyId);

        public override void Validate(ValidationContext ctx)
        {
            ctx.RequireChainCurrency(currencyId, "LinearOnBalance");
            if (ctx.RequireFiniteDouble(coefficient, "LinearOnBalance coefficient") && coefficient < 0)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"LinearOnBalance coefficient is {coefficient} - a career multiplier never shrinks with the fact it derives from.");
        }
    }

    // Product over venues of (1 + perRoadie * stationed): across venues the
    // boosts multiply, which is what makes spreading Roadies beat concentrating
    // them (design doc 8.2).
    [Serializable]
    public class RoadieTotalBoost : MultiplierFormula
    {
        public override BigNumber Compute(GameContext ctx)
        {
            var product = BigNumber.One;
            foreach (var venue in ctx.Defs.All<RoadieVenueDefinition>())
                product *= venue.Boost(ctx);
            return product;
        }
    }

    // The active chapter's double-count (design doc 8.2): the played venue's
    // boost applies once inside the global product and again here, which is what
    // makes stationing Roadies speed the chapter being worked. "Active" means
    // the chapter on the resolution chain - the gather-origin ruling above is
    // what makes it derivable at all.
    [Serializable]
    public class RoadieActiveBoost : MultiplierFormula
    {
        public override BigNumber Compute(GameContext ctx)
        {
            var chapter = ChapterOnChain(ctx.Scope);
            if (chapter == null)
                return BigNumber.One;           // resolving off any chapter's chain: no local factor
            foreach (var venue in ctx.Defs.All<RoadieVenueDefinition>())
                if (venue.chapterScopeId == chapter.ScopeId)
                    return venue.Boost(ctx);
            return BigNumber.One;
        }

        // Chapters are structurally root's children (design doc 12.3), so the
        // chapter on a chain is the last node before the root.
        private static ScopeState ChapterOnChain(ScopeState from)
        {
            for (var node = from; node != null; node = node.Parent)
                if (node.Parent != null && node.Parent.Parent == null)
                    return node;
            return null;
        }
    }

    // The scope-attached career effect: the same coordinate triple an Effect
    // carries, plus the formula that computes the factor.
    [CreateAssetMenu(menuName = "Garage Band Idle/Career Effect")]
    public class CareerEffectDefinition : Definition
    {
        public string target;       // a currency id, a producer/generator id, or a tag
        public string currencyId;   // optional - narrow to entries paying this currency
        public string stat;         // optional - narrow to this stat
        [SerializeReference, SubclassPicker] public MultiplierFormula formula;
    }
}
