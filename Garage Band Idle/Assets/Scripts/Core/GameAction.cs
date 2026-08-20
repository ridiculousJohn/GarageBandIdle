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
        [DefinitionId(typeof(Economy.CurrencyDefinition))] public List<string> currencyIds = new();
        public BigNumber amount;
        [SerializeReference, SubclassPicker] public PayoutFormula formula;

        public override void Execute(GameContext ctx)
        {
            BigNumber value = formula != null ? formula.Compute(ctx) : amount;
            foreach (var currencyId in currencyIds)
                ctx.Deposit(currencyId, value);
        }

        public override void Validate(ValidationContext ctx)
        {
            foreach (var currencyId in currencyIds)
            {
                var home = ctx.RequireChainCurrency(currencyId, "AddCurrency");
                if (home != null)
                    ctx.RecordFactWrite($"currency '{currencyId}'", home);
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
            var home = ctx.FlagHome(flagId);
            if (home == null)
            {
                ctx.AddError(ValidationCheck.UnresolvedReference, $"SetFlag names flag '{flagId}', which no scope declares (12.12).");
                return;
            }
            ctx.RecordFlagSetter(flagId);
            // The write walks OUTWARD, so a home off this chain can never be
            // reached - including one in a scope this action encloses, which is
            // also a flag the acting scope could never read (12.3).
            if (!ctx.OnActingChain(home))
                ctx.AddError(ValidationCheck.ChainReach, $"SetFlag writes flag '{flagId}' homed at '{home.Id}', which is not on the chain from '{ctx.ActingScope.Id}' (12.12).");
            else
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
        public string scopeId;
        [DefinitionId(typeof(Economy.ModifierDefinition))] public string modifierId;

        public override void Execute(GameContext ctx)
        {
            var target = ctx.Scope.FindOnChain(scopeId)
                ?? throw new InvalidOperationException(
                    $"AddModifier: scope '{scopeId}' is not on the chain from '{ctx.Scope.ScopeId}'.");

            var entry = target.activeModifiers.Find(e => e.modifierId == modifierId);
            if (entry == null)
            {
                target.activeModifiers.Add(new ActiveModifierEntry { modifierId = modifierId, count = 1 });
                return;
            }

            var definition = ctx.Defs.Get<Economy.ModifierDefinition>(modifierId);
            if (definition == null || definition.stacking == Economy.StackingKind.Replace)
                return;                       // re-grant keeps count at 1
            entry.count++;                    // Linear / Multiply: the name picks the growth formula on read
        }

        public override void Validate(ValidationContext ctx)
        {
            var resolved = ctx.Defs.Get<Economy.ModifierDefinition>(modifierId) != null;
            if (!resolved)
                ctx.AddError(ValidationCheck.UnresolvedReference, $"AddModifier references unknown modifier '{modifierId}'.");
            var target = ctx.FindScope(scopeId);
            if (target == null)
            {
                ctx.AddError(ValidationCheck.UnresolvedReference, $"AddModifier targets unknown scope '{scopeId}'.");
                return;
            }
            if (!ctx.OnActingChain(target))
            {
                ctx.AddError(ValidationCheck.ScopeReach, $"AddModifier may target the acting scope or an ancestor (grants live outward, 12.12); '{scopeId}' is neither from '{ctx.ActingScope.Id}'.");
                return;
            }
            if (!resolved)
                return;
            ctx.RecordModifierGrant(modifierId, target);
            ctx.RecordFactWrite($"modifier '{modifierId}' stack", target);
        }
    }

    // The exact inverse of AddModifier: one stack down, entry deleted at zero,
    // no-op when absent (design doc 12.5).
    [Serializable]
    public class RemoveModifier : GameAction
    {
        public string scopeId;
        [DefinitionId(typeof(Economy.ModifierDefinition))] public string modifierId;

        public override void Execute(GameContext ctx)
        {
            var target = ctx.Scope.FindOnChain(scopeId)
                ?? throw new InvalidOperationException(
                    $"RemoveModifier: scope '{scopeId}' is not on the chain from '{ctx.Scope.ScopeId}'.");

            var entry = target.activeModifiers.Find(e => e.modifierId == modifierId);
            if (entry == null)
                return;
            entry.count--;
            if (entry.count <= 0)
                target.activeModifiers.Remove(entry);
        }

        public override void Validate(ValidationContext ctx)
        {
            var resolved = ctx.Defs.Get<Economy.ModifierDefinition>(modifierId) != null;
            if (!resolved)
                ctx.AddError(ValidationCheck.UnresolvedReference, $"RemoveModifier references unknown modifier '{modifierId}'.");
            var target = ctx.FindScope(scopeId);
            if (target == null)
            {
                ctx.AddError(ValidationCheck.UnresolvedReference, $"RemoveModifier targets unknown scope '{scopeId}'.");
                return;
            }
            if (!ctx.OnActingChain(target))
            {
                ctx.AddError(ValidationCheck.ScopeReach, $"RemoveModifier may target the acting scope or an ancestor (grants live outward, 12.12); '{scopeId}' is neither from '{ctx.ActingScope.Id}'.");
                return;
            }
            if (resolved)
                ctx.RecordModifierRemove(modifierId, target);
        }
    }

    // Clears the named scope and everything inside it (downward-closed). It only
    // clears - it never executes nested lists, so no recursion exists via resets
    // (design doc 12.5). Reach: the acting scope, a scope it encloses, or a
    // sibling (12.12).
    [Serializable]
    public class ResetScope : GameAction
    {
        public string scopeId;

        public override void Execute(GameContext ctx)
        {
            var target = ctx.Scope.FindInSubtree(scopeId);
            if (target == null && ctx.Scope.Parent != null)
            {
                foreach (var sibling in ctx.Scope.Parent.Children)
                {
                    if (sibling == ctx.Scope)
                        continue;
                    if (sibling.ScopeId == scopeId)
                    {
                        target = sibling;
                        break;
                    }
                }
            }
            if (target == null)
                throw new InvalidOperationException(
                    $"ResetScope: '{scopeId}' is not the acting scope, enclosed by it, or a sibling of '{ctx.Scope.ScopeId}'.");
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
            var target = ctx.FindScope(scopeId);
            if (target == null)
            {
                ctx.AddError(ValidationCheck.UnresolvedReference, $"ResetScope targets unknown scope '{scopeId}'.");
                return;
            }
            if (target == ctx.RootScope)
            {
                ctx.AddError(ValidationCheck.ScopeReach, "ResetScope targets the root - the root is never resettable (12.12).");
                return;
            }
            if (!ctx.InActingSubtree(target) && !ctx.IsSiblingOfActing(target))
            {
                ctx.AddError(ValidationCheck.ScopeReach, $"ResetScope may target the acting scope, a scope it encloses, or a sibling (12.12); '{scopeId}' is none of these from '{ctx.ActingScope.Id}'.");
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
        public string tierId;

        public override void Execute(GameContext ctx)
        {
            var target = ctx.Scope.FindInSubtree(tierId)
                ?? throw new InvalidOperationException(
                    $"ExecuteRung: scope '{tierId}' is not within '{ctx.Scope.ScopeId}'.");
            if (target.Definition.rung == null)
                throw new InvalidOperationException($"ExecuteRung: scope '{tierId}' declares no rung.");
            target.Definition.rung.TryExecute(ctx.Rebase(target));
        }

        public override void Validate(ValidationContext ctx)
        {
            var target = ctx.FindScope(tierId);
            if (target == null)
            {
                ctx.AddError(ValidationCheck.UnresolvedReference, $"ExecuteRung targets unknown scope '{tierId}'.");
                return;
            }
            if (!ctx.InActingSubtree(target))
            {
                ctx.AddError(ValidationCheck.ScopeReach, $"ExecuteRung may only reference a rung declared within the acting scope (12.12); '{tierId}' is outside '{ctx.ActingScope.Id}'.");
                return;
            }
            if (target.rung == null)
            {
                ctx.AddError(ValidationCheck.UnresolvedReference, $"ExecuteRung targets scope '{tierId}', which declares no rung.");
                return;
            }
            ctx.RecordRungInvocation(target);
        }
    }
}
