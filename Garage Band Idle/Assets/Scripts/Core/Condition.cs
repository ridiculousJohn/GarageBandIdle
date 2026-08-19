using System;
using System.Collections.Generic;
using System.Linq;

namespace RidiculousGaming.GarageBandIdle
{
    // One polymorphic family for every gate, unlock, visibility, and pressability
    // rule (design doc 12.4). Pure - reads state, stores nothing. A kind can
    // never be authored without a class behind it (import error).
    [Serializable]
    public abstract class Condition
    {
        // Optional label rendered by the rung feedback contract (12.11) when
        // this leg disarms a button. Presentation only - never read by Evaluate.
        public string uiText;

        public abstract bool Evaluate(GameContext ctx);

        // Load-time reference and reach checks (design doc 12.12), driven by
        // ContentValidator: each kind validates its own references against the
        // acting scope, so a new kind ships its checks with its class.
        public virtual void Validate(ValidationContext ctx) { }
    }

    [Serializable]
    public class CurrencyAtLeast : Condition
    {
        [DefinitionId(typeof(Economy.CurrencyDefinition))] public string currencyId;
        public BigNumber threshold;

        public override bool Evaluate(GameContext ctx) => ctx.GetBalance(currencyId) >= threshold;
        public override void Validate(ValidationContext ctx) => ctx.RequireChainCurrency(currencyId, "CurrencyAtLeast");
    }

    [Serializable]
    public class EarnedTotalAtLeast : Condition
    {
        [DefinitionId(typeof(Economy.CurrencyDefinition))] public string currencyId;
        public BigNumber threshold;

        public override bool Evaluate(GameContext ctx) => ctx.GetEarnedTotal(currencyId) >= threshold;
        public override void Validate(ValidationContext ctx) => ctx.RequireChainCurrency(currencyId, "EarnedTotalAtLeast");
    }

    [Serializable]
    public class OwnedCountAtLeast : Condition
    {
        [DefinitionId(typeof(Economy.GeneratorDefinition))] public string generatorId;
        public int count;

        public override bool Evaluate(GameContext ctx) => ctx.GetOwnedCount(generatorId) >= count;

        public override void Validate(ValidationContext ctx) =>
            ctx.RequireChainDeclaration<Economy.GeneratorDefinition>(generatorId, "OwnedCountAtLeast");
    }

    [Serializable]
    public class FlagSet : Condition
    {
        public string flagId;

        public override bool Evaluate(GameContext ctx) => ctx.IsFlagSet(flagId);

        public override void Validate(ValidationContext ctx)
        {
            var home = ctx.FlagHome(flagId);
            if (home == null)
                ctx.AddError(ValidationCheck.UnresolvedReference, $"FlagSet references flag '{flagId}', which no scope declares.");
            else if (!ctx.OnActingChain(home))
                ctx.AddError(ValidationCheck.ChainReach, $"FlagSet reads flag '{flagId}' homed at '{home.Id}', which is not on the chain from '{ctx.ActingScope.Id}' - the read can never see it set (12.12).");
        }
    }

    [Serializable]
    public class UpgradePurchased : Condition
    {
        [DefinitionId(typeof(Economy.UpgradeDefinition))] public string upgradeId;

        public override bool Evaluate(GameContext ctx) => ctx.IsUpgradePurchased(upgradeId);

        public override void Validate(ValidationContext ctx) =>
            ctx.RequireChainDeclaration<Economy.UpgradeDefinition>(upgradeId, "UpgradePurchased");
    }

    // Counts the group's bars at full: completion is derived, progress >= the
    // bar's fillAmount, never stored (design doc 12.7).
    [Serializable]
    public class BarsCompleted : Condition
    {
        [DefinitionId(typeof(Economy.BarGroupDefinition))] public string groupId;
        public int count = 1;

        public override bool Evaluate(GameContext ctx)
        {
            var completed = 0;
            foreach (var bar in ctx.Defs.All<Economy.BarDefinition>())
            {
                if (bar.groupId != groupId)
                    continue;
                if (ctx.GetBarProgress(bar.Id) >= bar.fillAmount)
                    completed++;
            }
            return completed >= count;
        }

        // Group membership and progress reach validate further in build step 5,
        // when bars gain their scope attachment; today the reference resolving
        // is the whole check.
        public override void Validate(ValidationContext ctx)
        {
            if (ctx.Defs.Get<Economy.BarGroupDefinition>(groupId) == null)
                ctx.AddError(ValidationCheck.UnresolvedReference, $"BarsCompleted references unknown bar group '{groupId}'.");
        }
    }

    [Serializable]
    public class All : Condition
    {
        [UnityEngine.SerializeReference, SubclassPicker] public List<Condition> conditions = new();

        public override bool Evaluate(GameContext ctx) => conditions.TrueForAll(c => c.Evaluate(ctx));

        public override void Validate(ValidationContext ctx)
        {
            for (var i = 0; i < conditions.Count; i++)
            {
                if (conditions[i] == null)
                    ctx.AddError(ValidationCheck.NullEntry, $"All has a null conditions[{i}] entry.");
                else
                    conditions[i].Validate(ctx);
            }
        }
    }

    [Serializable]
    public class Any : Condition
    {
        [UnityEngine.SerializeReference, SubclassPicker] public List<Condition> conditions = new();

        public override bool Evaluate(GameContext ctx) => conditions.Any(c => c.Evaluate(ctx));

        public override void Validate(ValidationContext ctx)
        {
            for (var i = 0; i < conditions.Count; i++)
            {
                if (conditions[i] == null)
                    ctx.AddError(ValidationCheck.NullEntry, $"Any has a null conditions[{i}] entry.");
                else
                    conditions[i].Validate(ctx);
            }
        }
    }

    [Serializable]
    public class Not : Condition
    {
        [UnityEngine.SerializeReference, SubclassPicker] public Condition condition;

        public override bool Evaluate(GameContext ctx) => condition != null && !condition.Evaluate(ctx);

        public override void Validate(ValidationContext ctx)
        {
            if (condition == null)
                ctx.AddError(ValidationCheck.NullEntry, "Not has no operand - it can never hold.");
            else
                condition.Validate(ctx);
        }
    }
}
