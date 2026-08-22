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
        public Economy.CurrencyDefinition currency;
        public BigNumber threshold;

        public override bool Evaluate(GameContext ctx) => ctx.GetBalance(currency.Id) >= threshold;
        public override void Validate(ValidationContext ctx) => ctx.RequireOnChain(currency, "CurrencyAtLeast");
    }

    [Serializable]
    public class EarnedTotalAtLeast : Condition
    {
        public Economy.CurrencyDefinition currency;
        public BigNumber threshold;

        public override bool Evaluate(GameContext ctx) => ctx.GetEarnedTotal(currency.Id) >= threshold;
        public override void Validate(ValidationContext ctx) => ctx.RequireOnChain(currency, "EarnedTotalAtLeast");
    }

    [Serializable]
    public class OwnedCountAtLeast : Condition
    {
        public Economy.GeneratorDefinition generator;
        public int count;

        public override bool Evaluate(GameContext ctx) => ctx.GetOwnedCount(generator.Id) >= count;

        public override void Validate(ValidationContext ctx) => ctx.RequireOnChain(generator, "OwnedCountAtLeast");
    }

    [Serializable]
    public class FlagSet : Condition
    {
        public string flagId;

        public override bool Evaluate(GameContext ctx) => ctx.IsFlagSet(flagId);

        public override void Validate(ValidationContext ctx)
        {
            // The read walks outward from the acting scope and stops at the
            // first declaration, so a same-named flag on another chain is simply
            // a different flag (12.3).
            if (ctx.FlagHome(flagId) != null)
                return;
            var elsewhere = ctx.AnyScopeDeclaringFlag(flagId);
            if (elsewhere == null)
                ctx.AddError(ValidationCheck.UnresolvedReference, $"FlagSet references flag '{flagId}', which no scope declares.");
            else
                ctx.AddError(ValidationCheck.ChainReach, $"FlagSet reads flag '{flagId}' homed at '{elsewhere.Id}', which is not on the chain from '{ctx.ActingScope.Id}' - the read can never see it set (12.12).");
        }
    }

    [Serializable]
    public class UpgradePurchased : Condition
    {
        public Economy.UpgradeDefinition upgrade;

        public override bool Evaluate(GameContext ctx) => ctx.IsUpgradePurchased(upgrade.Id);

        public override void Validate(ValidationContext ctx) => ctx.RequireOnChain(upgrade, "UpgradePurchased");
    }

    // Counts the group's bars at full: completion is derived, progress >= the
    // bar's fillAmount, never stored (design doc 12.7).
    [Serializable]
    public class BarsCompleted : Condition
    {
        public Economy.BarGroupDefinition group;
        public int count = 1;

        public override bool Evaluate(GameContext ctx)
        {
            var completed = 0;
            foreach (var bar in group.bars)
                if (bar != null && ctx.GetBarProgress(bar.Id) >= bar.fillAmount)
                    completed++;
            return completed >= count;
        }

        // The count reads each bar's progress by walking OUTWARD from the
        // acting scope, and a bar's progress is homed at its group's scope - so
        // that scope must be the acting one or an ancestor, or the walk never
        // reaches the fact and the count is permanently zero.
        public override void Validate(ValidationContext ctx)
        {
            if (group == null)
            {
                ctx.AddError(ValidationCheck.NullEntry, "BarsCompleted names no bar group.");
                return;
            }
            ctx.RequireOnChain(group, "BarsCompleted");
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
