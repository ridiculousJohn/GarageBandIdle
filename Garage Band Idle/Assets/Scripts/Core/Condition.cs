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

        // Threshold kinds expose current/target so an unmet leg renders as
        // progress ("37/50 fans", 12.11), read from the same fields Evaluate
        // reads - one implementation, no drift. Kinds with no number keep the
        // default.
        public virtual bool Progress(GameContext ctx, out BigNumber current, out BigNumber target)
        {
            current = BigNumber.Zero;
            target = BigNumber.Zero;
            return false;
        }

        // What this condition asks of the player, as the button renders it
        // (12.11). A leaf's text is its uiText; a compound formats its uiText as
        // a string.Format pattern over its children's texts ({0}, {1}, ...), so
        // one rule composes every gate - a pattern with no placeholders is a
        // whole override, and the kind's default stands in when none is
        // authored.
        public virtual string Text => uiText;

        // The compound rule All, Any and Not share. Context-free by design: the
        // text describes the requirement and UnmetLegs judges it, which is what
        // lets the load pass compute every gate's text once (12.12). A
        // FormatException is left to propagate - that pass is where it surfaces.
        protected static string FormatOver(string pattern, IReadOnlyList<Condition> children)
        {
            var texts = new object[children.Count];
            for (var i = 0; i < children.Count; i++)
                texts[i] = children[i]?.Text ?? string.Empty;
            return string.Format(pattern, texts);
        }

        // The default when a compound authors no pattern: the list a player
        // would read, in authored order - "A and B", "A, B, and C".
        protected static string JoinWith(IReadOnlyList<Condition> children, string connector)
        {
            var texts = new string[children.Count];
            for (var i = 0; i < children.Count; i++)
                texts[i] = children[i]?.Text ?? string.Empty;
            if (texts.Length == 0)
                return string.Empty;
            if (texts.Length == 1)
                return texts[0];
            if (texts.Length == 2)
                return $"{texts[0]} {connector} {texts[1]}";
            return string.Join(", ", texts, 0, texts.Length - 1) + ", " + connector + " " + texts[texts.Length - 1];
        }

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

        public override bool Progress(GameContext ctx, out BigNumber current, out BigNumber target)
        {
            current = ctx.GetBalance(currency.Id);
            target = threshold;
            return true;
        }

        public override void Validate(ValidationContext ctx) => ctx.RequireOnChain(currency, "CurrencyAtLeast");
    }

    [Serializable]
    public class EarnedTotalAtLeast : Condition
    {
        public Economy.CurrencyDefinition currency;
        public BigNumber threshold;

        public override bool Evaluate(GameContext ctx) => ctx.GetEarnedTotal(currency.Id) >= threshold;

        public override bool Progress(GameContext ctx, out BigNumber current, out BigNumber target)
        {
            current = ctx.GetEarnedTotal(currency.Id);
            target = threshold;
            return true;
        }

        public override void Validate(ValidationContext ctx) => ctx.RequireOnChain(currency, "EarnedTotalAtLeast");
    }

    [Serializable]
    public class OwnedCountAtLeast : Condition
    {
        public Economy.GeneratorDefinition generator;
        public int count;

        public override bool Evaluate(GameContext ctx) => ctx.GetOwnedCount(generator.Id) >= count;

        public override bool Progress(GameContext ctx, out BigNumber current, out BigNumber target)
        {
            current = ctx.GetOwnedCount(generator.Id);
            target = count;
            return true;
        }

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

        public override bool Evaluate(GameContext ctx) => Completed(ctx) >= count;

        public override bool Progress(GameContext ctx, out BigNumber current, out BigNumber target)
        {
            current = Completed(ctx);
            target = count;
            return true;
        }

        // The gate's answer and the progress readout are the same count, so it
        // has one implementation and the two can never disagree (12.11).
        private int Completed(GameContext ctx)
        {
            var completed = 0;
            foreach (var bar in group.bars)
                if (bar != null && ctx.GetBarProgress(bar.Id) >= bar.fillAmount)
                    completed++;
            return completed;
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

    // Any record at the named host - running, expired-undismissed, or armed
    // (design doc 12.4). The host is a direct scope reference like
    // ResetScope.scope, reached self-or-enclosed, and the read is a pure fact.
    [Serializable]
    public class EventRecordExists : Condition
    {
        public ScopeDefinition host;

        public override bool Evaluate(GameContext ctx) =>
            ctx.Scope.FindInSubtree(host) is InteriorScopeState interior && interior.activeEvent != null;

        public override void Validate(ValidationContext ctx)
        {
            var target = ctx.FindScope(host);
            if (target == null)
            {
                ctx.AddError(ValidationCheck.NullEntry, "EventRecordExists names no scope.");
                return;
            }
            if (!ctx.InActingSubtree(target))
            {
                ctx.AddError(ValidationCheck.ScopeReach,
                    $"EventRecordExists may name the acting scope or a scope it encloses (12.12); '{target.Id}' is neither from '{ctx.ActingScope.Id}'.");
                return;
            }
            // Root holds no record field at all, so a root host reads false
            // forever - a permanently closed gate the load pass refuses.
            if (target is not InteriorDefinition)
                ctx.AddError(ValidationCheck.ScopeReach,
                    $"EventRecordExists names '{target.Id}', which cannot host an event - the condition can never hold (12.12).");
        }
    }

    // A record whose goal latched, still undismissed - the reward is armed and
    // waiting (design doc 12.4). Negated, this is the guard the stranded-reward
    // check requires of a rung whose reset would reach the host.
    [Serializable]
    public class EventRewardPending : Condition
    {
        public ScopeDefinition host;

        public override bool Evaluate(GameContext ctx) =>
            ctx.Scope.FindInSubtree(host) is InteriorScopeState interior
            && interior.activeEvent != null && interior.activeEvent.goalReached;

        public override void Validate(ValidationContext ctx)
        {
            var target = ctx.FindScope(host);
            if (target == null)
            {
                ctx.AddError(ValidationCheck.NullEntry, "EventRewardPending names no scope.");
                return;
            }
            if (!ctx.InActingSubtree(target))
            {
                ctx.AddError(ValidationCheck.ScopeReach,
                    $"EventRewardPending may name the acting scope or a scope it encloses (12.12); '{target.Id}' is neither from '{ctx.ActingScope.Id}'.");
                return;
            }
            // Root holds no record field at all, so a root host reads false
            // forever - a permanently closed gate the load pass refuses.
            if (target is not InteriorDefinition)
                ctx.AddError(ValidationCheck.ScopeReach,
                    $"EventRewardPending names '{target.Id}', which cannot host an event - the condition can never hold (12.12).");
        }
    }

    // The open gate. A gate may not be null (12.12), so an author says
    // "always offered" with this kind rather than by omission.
    [Serializable]
    public class Always : Condition
    {
        public override bool Evaluate(GameContext ctx) => true;
    }

    // The claim's circumstance (design doc 12.5): true only under a context the
    // idle claim constructed. Composed with Not, a live-only buff is ordinary
    // authoring.
    [Serializable]
    public class IdleAccumulation : Condition
    {
        public override bool Evaluate(GameContext ctx) => ctx.IdleAccumulation;

        // Only a modifier's appliesWhen and a chapter-reachable RATE entry's
        // condition are ever evaluated under a claim's context; anywhere else
        // the circumstance is never set, so the condition is dead content
        // (12.5). A currency's activeWhen is the one site that refuses it
        // outright rather than reporting it inert - the circumstance IS
        // evaluated there, and that is the problem (12.2).
        public override void Validate(ValidationContext ctx)
        {
            // The gate decides whether a currency takes income AT ALL, and this
            // operand makes that answer depend on which gather is asking rather
            // than on the currency. What it COSTS depends on the spelling,
            // which is why the kind is refused and not one shape of it: bare,
            // the gate is shut for every acting scope but the claim, and since
            // no action list runs under one, a rung payout or event reward
            // throws on content that is otherwise correct; negated, those keep
            // working and the currency instead drops out of the idle offer with
            // nothing said. An author cannot tell the two apart by reading
            // them. Either mechanic is a wildcard x0 modifier on rate and yield
            // carrying appliesWhen, whichever way round it is wanted.
            if (ctx.CurrencyGate)
            {
                ctx.AddError(ValidationCheck.KindPlacement,
                    "IdleAccumulation in a currency's activeWhen: the gate would answer differently depending on which GATHER is asking, and what that costs depends on the spelling - a bare one throws on every authored payout, since no action list runs under a claim, while a negated one silently drops the currency out of the idle offer. Author the circumstance as a modifier's appliesWhen instead (12.2).");
                return;
            }
            if (!ctx.IdleCircumstancePossible)
                ctx.AddWarning(ValidationCheck.InertOperand,
                    "IdleAccumulation sits at a site never evaluated under a claim's context - only a modifier's appliesWhen and a chapter-reachable rate entry see the circumstance (12.5).");
        }
    }

    [Serializable]
    public class All : Condition
    {
        [UnityEngine.SerializeReference, SubclassPicker] public List<Condition> conditions = new();

        public override bool Evaluate(GameContext ctx) => conditions.TrueForAll(c => c.Evaluate(ctx));

        public override string Text => string.IsNullOrEmpty(uiText) ? JoinWith(conditions, "and") : FormatOver(uiText, conditions);

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

        public override string Text => string.IsNullOrEmpty(uiText) ? JoinWith(conditions, "or") : FormatOver(uiText, conditions);

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

        // No default: prose does not negate mechanically (12.4), so an
        // unauthored Not renders empty and an author writes the line.
        public override string Text => string.IsNullOrEmpty(uiText) ? string.Empty : FormatOver(uiText, new[] { condition });

        public override void Validate(ValidationContext ctx)
        {
            if (condition == null)
                ctx.AddError(ValidationCheck.NullEntry, "Not has no operand - it can never hold.");
            else
                condition.Validate(ctx);
        }
    }
}
