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
        public virtual void Validate(IDefinitionSource defs) { }
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

        public override void Validate(IDefinitionSource defs) => formula?.Validate(defs);
    }

    [Serializable]
    public class SetFlag : GameAction
    {
        public string flagId;

        public override void Execute(GameContext ctx) => ctx.SetFlag(flagId);
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
            var target = ctx.Scope.FindOnChain(scopeId);
            if (target == null)
            {
                Debug.LogError($"AddModifier: scope '{scopeId}' is not on the chain from '{ctx.Scope.ScopeId}'.");
                return;
            }

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
            var target = ctx.Scope.FindOnChain(scopeId);
            if (target == null)
            {
                Debug.LogError($"RemoveModifier: scope '{scopeId}' is not on the chain from '{ctx.Scope.ScopeId}'.");
                return;
            }

            var entry = target.activeModifiers.Find(e => e.modifierId == modifierId);
            if (entry == null)
                return;
            entry.count--;
            if (entry.count <= 0)
                target.activeModifiers.Remove(entry);
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
            {
                Debug.LogError($"ResetScope: '{scopeId}' is not the acting scope, enclosed by it, or a sibling of '{ctx.Scope.ScopeId}'.");
                return;
            }
            if (target.Parent == null)
            {
                // The root is structurally unresettable (12.12: "never the
                // root") - nothing exists outside it for a fact to survive into.
                Debug.LogError("ResetScope: the root scope is never resettable.");
                return;
            }
            ClearRecursive(target, ctx.NowUtc);
        }

        private static void ClearRecursive(ScopeState scope, DateTime nowUtc)
        {
            scope.Clear(nowUtc);
            foreach (var child in scope.Children)
                ClearRecursive(child, nowUtc);
        }
    }

    // Runs another press's action list through the same gate check every
    // invocation gets: gate met, it executes; gate unmet, it no-ops. The context
    // REBASES to the referenced press's declaring scope (design doc 12.4/12.5).
    // Reach: a press declared within the acting scope (12.12).
    [Serializable]
    public class ExecuteRung : GameAction
    {
        public string tierId;

        public override void Execute(GameContext ctx)
        {
            var target = ctx.Scope.FindInSubtree(tierId);
            if (target == null)
            {
                Debug.LogError($"ExecuteRung: scope '{tierId}' is not within '{ctx.Scope.ScopeId}'.");
                return;
            }
            if (target.Definition.press == null)
            {
                Debug.LogError($"ExecuteRung: scope '{tierId}' declares no press.");
                return;
            }
            target.Definition.press.TryExecute(ctx.Rebase(target));
        }
    }
}
