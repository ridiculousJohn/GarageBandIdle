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
        // Optional label rendered by the press feedback contract (12.11) when
        // this leg disarms a button. Presentation only - never read by Evaluate.
        public string uiText;

        public abstract bool Evaluate(GameContext ctx);
        public virtual void Validate(IDefinitionSource defs) { }
    }

    [Serializable]
    public class CurrencyAtLeast : Condition
    {
        [DefinitionId(typeof(Economy.CurrencyDefinition))] public string currencyId;
        public BigNumber threshold;

        public override bool Evaluate(GameContext ctx) => ctx.GetBalance(currencyId) >= threshold;
    }

    [Serializable]
    public class EarnedTotalAtLeast : Condition
    {
        [DefinitionId(typeof(Economy.CurrencyDefinition))] public string currencyId;
        public BigNumber threshold;

        public override bool Evaluate(GameContext ctx) => ctx.GetEarnedTotal(currencyId) >= threshold;
    }

    [Serializable]
    public class OwnedCountAtLeast : Condition
    {
        public string generatorId;
        public int count;

        public override bool Evaluate(GameContext ctx) => ctx.GetOwnedCount(generatorId) >= count;
    }

    [Serializable]
    public class FlagSet : Condition
    {
        public string flagId;

        public override bool Evaluate(GameContext ctx) => ctx.IsFlagSet(flagId);
    }

    [Serializable]
    public class UpgradePurchased : Condition
    {
        public string upgradeId;

        public override bool Evaluate(GameContext ctx) => ctx.IsUpgradePurchased(upgradeId);
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
    }

    [Serializable]
    public class All : Condition
    {
        [UnityEngine.SerializeReference, SubclassPicker] public List<Condition> conditions = new();

        public override bool Evaluate(GameContext ctx) => conditions.TrueForAll(c => c.Evaluate(ctx));
        public override void Validate(IDefinitionSource defs) => conditions.ForEach(c => c.Validate(defs));
    }

    [Serializable]
    public class Any : Condition
    {
        [UnityEngine.SerializeReference, SubclassPicker] public List<Condition> conditions = new();

        public override bool Evaluate(GameContext ctx) => conditions.Any(c => c.Evaluate(ctx));
        public override void Validate(IDefinitionSource defs) => conditions.ForEach(c => c.Validate(defs));
    }

    [Serializable]
    public class Not : Condition
    {
        [UnityEngine.SerializeReference, SubclassPicker] public Condition condition;

        public override bool Evaluate(GameContext ctx) => condition != null && !condition.Evaluate(ctx);
        public override void Validate(IDefinitionSource defs) => condition?.Validate(defs);
    }
}
