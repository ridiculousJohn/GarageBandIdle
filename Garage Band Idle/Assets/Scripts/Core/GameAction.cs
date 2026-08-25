using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // One polymorphic family for "something happens at a moment" (design doc
    // 12.5). Named GameAction in code because System.Action shadows the doc's
    // name in any file using System. One-shot: actions run at their moment and
    // are never replayed on load.
    [Serializable]
    public abstract class GameAction
    {
        public abstract void Execute(GameContext ctx);

        // Load-time reference and reach checks (design doc 12.12), driven by
        // ContentValidator: each kind validates its own references against the
        // acting scope and records into the context's ledgers what the
        // cross-container checks need (fact writes, resets, grants, rungs).
        public virtual void Validate(ValidationContext ctx) { }
    }

    // Pays one or more target currencies from a SINGLE evaluation - the album
    // pays root records and the chapter's gate counter identical amounts that
    // can never drift (design doc 5). Amount is the constant unless a formula is
    // authored.
    [Serializable]
    public class AddCurrency : GameAction
    {
        public List<Economy.CurrencyDefinition> currencies = new();
        public BigNumber amount;
        [SerializeReference, SubclassPicker] public PayoutFormula formula;

        public override void Execute(GameContext ctx)
        {
            BigNumber value = formula != null ? formula.Compute(ctx) : amount;
            foreach (var currency in currencies)
                ctx.Deposit(currency.Id, value);
        }

        public override void Validate(ValidationContext ctx)
        {
            foreach (var currency in currencies)
            {
                var home = ctx.RequireOnChain(currency, "AddCurrency");
                if (home != null)
                    ctx.RecordFactWrite($"currency '{currency.Id}'", home);
            }
            // A grant is never negative: Deposit moves the earned total too, and
            // section 2's strobe-proofing stands on that only ever rising.
            if (amount < BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"AddCurrency amount is {amount} - a grant never subtracts.");
            formula?.Validate(ctx);
        }
    }

    [Serializable]
    public class SetFlag : GameAction
    {
        public string flagId;

        public override void Execute(GameContext ctx) => ctx.SetFlag(flagId);

        public override void Validate(ValidationContext ctx)
        {
            // The write walks OUTWARD, so the home is whatever the acting scope's
            // own chain declares. A flag of the same name on another chain is a
            // different flag; finding it would be the tree-wide search the
            // runtime never performs - including one in a scope this action
            // encloses, which the acting scope could never read either (12.3).
            var home = ctx.FlagHome(flagId);
            if (home == null)
            {
                var elsewhere = ctx.AnyScopeDeclaringFlag(flagId);
                if (elsewhere == null)
                    ctx.AddError(ValidationCheck.UnresolvedReference, $"SetFlag names flag '{flagId}', which no scope declares (12.12).");
                else
                    ctx.AddError(ValidationCheck.ChainReach, $"SetFlag writes flag '{flagId}' homed at '{elsewhere.Id}', which is not on the chain from '{ctx.ActingScope.Id}' (12.12).");
                return;
            }
            ctx.RecordFlagSetter(flagId);
            ctx.RecordFactWrite($"flag '{flagId}'", home);
        }
    }

    // Appends/increments a pointer-fact {modifierId, count} on the target scope.
    // The numbers stay on the ModifierDefinition; its stacking enum decides what
    // a re-grant does (design doc 12.5). Target: the acting scope or an ancestor
    // - grants live outward (12.12).
    [Serializable]
    public class AddModifier : GameAction
    {
        public ScopeDefinition scope;
        public Economy.ModifierDefinition modifier;

        public override void Execute(GameContext ctx)
        {
            var target = ctx.Scope.FindOnChain(scope)
                ?? throw new InvalidOperationException(
                    $"AddModifier: scope '{scope.Id}' is not on the chain from '{ctx.Scope.ScopeId}'.");

            // Replace holds a re-grant at one; Linear and Multiply count up, and
            // the name picks the growth formula the read applies.
            target.modifierStacks.TryGetValue(modifier.Id, out var count);
            target.modifierStacks[modifier.Id] =
                modifier.stacking == Economy.StackingKind.Replace ? 1 : count + 1;
        }

        public override void Validate(ValidationContext ctx)
        {
            if (modifier == null)
            {
                ctx.AddError(ValidationCheck.NullEntry, "AddModifier names no modifier.");
                return;
            }
            var target = ctx.FindScope(scope);
            if (target == null)
            {
                ctx.AddError(ValidationCheck.NullEntry, $"AddModifier granting '{modifier.Id}' names no target scope.");
                return;
            }
            if (!ctx.OnActingChain(target))
            {
                ctx.AddError(ValidationCheck.ScopeReach, $"AddModifier may target the acting scope or an ancestor (grants live outward, 12.12); '{target.Id}' is neither from '{ctx.ActingScope.Id}'.");
                return;
            }
            // The stack lives at the target, and the read resolves it outward
            // from there - so the modifier must be declared at the target or
            // above it, or the grant would contribute nothing.
            ctx.RequireDeclaredFor(target, modifier, "AddModifier");
            ctx.RecordModifierGrant(modifier, target);
            ctx.RecordFactWrite($"modifier '{modifier.Id}' stack", target);
        }
    }

    // The exact inverse of AddModifier: one stack down, entry deleted at zero,
    // no-op when absent (design doc 12.5).
    [Serializable]
    public class RemoveModifier : GameAction
    {
        public ScopeDefinition scope;
        public Economy.ModifierDefinition modifier;

        public override void Execute(GameContext ctx)
        {
            var target = ctx.Scope.FindOnChain(scope)
                ?? throw new InvalidOperationException(
                    $"RemoveModifier: scope '{scope.Id}' is not on the chain from '{ctx.Scope.ScopeId}'.");

            if (!target.modifierStacks.TryGetValue(modifier.Id, out var count))
                return;                       // nothing granted here: the authored no-op (12.5)
            if (count <= 1)
                target.modifierStacks.Remove(modifier.Id);
            else
                target.modifierStacks[modifier.Id] = count - 1;
        }

        public override void Validate(ValidationContext ctx)
        {
            if (modifier == null)
            {
                ctx.AddError(ValidationCheck.NullEntry, "RemoveModifier names no modifier.");
                return;
            }
            var target = ctx.FindScope(scope);
            if (target == null)
            {
                ctx.AddError(ValidationCheck.NullEntry, $"RemoveModifier removing '{modifier.Id}' names no target scope.");
                return;
            }
            if (!ctx.OnActingChain(target))
            {
                ctx.AddError(ValidationCheck.ScopeReach, $"RemoveModifier may target the acting scope or an ancestor (grants live outward, 12.12); '{target.Id}' is neither from '{ctx.ActingScope.Id}'.");
                return;
            }
            ctx.RequireDeclaredFor(target, modifier, "RemoveModifier");
            ctx.RecordModifierRemove(modifier, target);
        }
    }

    // Clears the named scope and everything inside it (downward-closed). It only
    // clears - it never executes nested lists, so no recursion exists via resets
    // (design doc 12.5). Reach: the acting scope or a scope it encloses. Peers
    // are cleared by the scope that CONTAINS them, since resetting a parent is
    // downward-closed - so nothing reaches sideways.
    [Serializable]
    public class ResetScope : GameAction
    {
        public ScopeDefinition scope;

        public override void Execute(GameContext ctx)
        {
            var target = ctx.Scope.FindInSubtree(scope)
                ?? throw new InvalidOperationException(
                    $"ResetScope: '{scope.Id}' is not the acting scope or enclosed by '{ctx.Scope.ScopeId}'.");
            if (target.Parent == null)
                // The root is structurally unresettable (12.12: "never the
                // root") - nothing exists outside it for a fact to survive into.
                throw new InvalidOperationException("ResetScope: the root scope is never resettable.");
            ClearRecursive(target, ctx.NowUtc);
        }

        private static void ClearRecursive(ScopeState scope, DateTime nowUtc)
        {
            scope.Clear(nowUtc);
            foreach (var child in scope.Children)
                ClearRecursive(child, nowUtc);
        }

        public override void Validate(ValidationContext ctx)
        {
            var target = ctx.FindScope(scope);
            if (target == null)
            {
                ctx.AddError(ValidationCheck.NullEntry, "ResetScope names no scope.");
                return;
            }
            if (target == ctx.RootScope)
            {
                ctx.AddError(ValidationCheck.ScopeReach, "ResetScope targets the root - the root is never resettable (12.12).");
                return;
            }
            if (!ctx.InActingSubtree(target))
            {
                ctx.AddError(ValidationCheck.ScopeReach, $"ResetScope may target the acting scope or a scope it encloses (12.12); '{target.Id}' is neither from '{ctx.ActingScope.Id}'.");
                return;
            }
            ctx.RecordReset(target);
        }
    }

    // Runs another rung's action list through the same gate check every
    // invocation gets: gate met, it executes; gate unmet, it no-ops. The context
    // REBASES to the referenced rung's declaring scope (design doc 12.4/12.5).
    // Reach: a rung declared within the acting scope (12.12).
    [Serializable]
    public class ExecuteRung : GameAction
    {
        public InteriorDefinition tier;

        public override void Execute(GameContext ctx)
        {
            var target = ctx.Scope.FindInSubtree(tier)
                ?? throw new InvalidOperationException(
                    $"ExecuteRung: scope '{tier.Id}' is not within '{ctx.Scope.ScopeId}'.");
            if (tier.rung == null)
                throw new InvalidOperationException($"ExecuteRung: scope '{tier.Id}' declares no rung.");
            tier.rung.TryExecute(ctx.Rebase(target));
        }

        public override void Validate(ValidationContext ctx)
        {
            var target = ctx.FindScope(tier);
            if (target == null)
            {
                ctx.AddError(ValidationCheck.NullEntry, "ExecuteRung names no scope.");
                return;
            }
            if (!ctx.InActingSubtree(target))
            {
                ctx.AddError(ValidationCheck.ScopeReach, $"ExecuteRung may only reference a rung declared within the acting scope (12.12); '{target.Id}' is outside '{ctx.ActingScope.Id}'.");
                return;
            }
            if (tier.rung == null)
            {
                ctx.AddError(ValidationCheck.UnresolvedReference, $"ExecuteRung targets scope '{target.Id}', which declares no rung.");
                return;
            }
            ctx.RecordRungInvocation(target);
        }
    }
}
