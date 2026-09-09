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

        // Load-time wiring (design doc 12.14.8), driven by ScopeLinker when a
        // tree is built: a kind holding a scope reference resolves it once,
        // checks it, and files the node under itself. Defaults to nothing -
        // most kinds reference no scope at all, and a kind that does not store
        // a link is a kind that never reads one.
        public virtual void Link(LinkContext ctx) { }

        // Whether this action refuses to run against the state it would act on
        // (design doc 12.5). Asked by ActionList and by nothing else; null is
        // "no reason of mine", which is every kind but the two resets.
        public virtual Refusal Refuses(GameContext ctx, ScopeState ignoring) => null;

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

        // The single evaluation every tied target shares (design doc 5): the
        // formula when one is authored, else the constant. Execute deposits it;
        // the rung feedback contract previews it through the same call (12.11).
        public BigNumber Compute(GameContext ctx) => formula != null ? formula.Compute(ctx) : amount;

        public override void Execute(GameContext ctx)
        {
            // One evaluation, and one all-or-nothing commit: the targets are
            // tied, so a refusal on the second may not leave the first paid.
            ctx.DepositAll(currencies, Compute(ctx));
        }

        public override void Validate(ValidationContext ctx)
        {
            foreach (var currency in currencies)
                ctx.RequireOnChain(currency, "AddCurrency");
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

        // The reference names one node the moment the tree exists, so it is
        // resolved and checked here rather than searched for at every firing.
        public override void Link(LinkContext ctx)
        {
            var target = ctx.ResolveEnclosed(scope, "ResetScope");
            if (target.Parent == null)
                // The root is structurally unresettable (12.12: "never the
                // root") - nothing exists outside it for a fact to survive into.
                throw new InvalidOperationException("ResetScope: the root scope is never resettable.");
            ctx.Store(this, target);
        }

        // The subtree's own answer, asked of the node this reference names
        // (design doc 12.5). The walk lives on ScopeState; the action calls it.
        public override Refusal Refuses(GameContext ctx, ScopeState ignoring) =>
            ctx.Scope.Link(this).RefusalInSubtree(ignoring);

        public override void Execute(GameContext ctx)
        {
            var target = ctx.Scope.Link(this);
            // Asked here too, so a reset run outside any list is as fail-closed
            // as one inside: a clear forced past a refusal throws (requirement
            // 7) rather than destroying the reward it was refused over.
            var refusal = target.RefusalInSubtree();
            if (refusal != null)
                throw new InvalidOperationException(
                    $"ResetScope of '{target.ScopeId}' was forced past a refusal: scope '{refusal.Host.ScopeId}' holds an unclaimed reward (design doc 12.5).");
            target.ClearSubtree(ctx.NowUtc);
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
            }
        }
    }

    // Runs another rung's action list through the same gate check every
    // invocation gets: gate met, it executes; gate unmet, it no-ops. The context
    // REBASES to the referenced rung's declaring scope (design doc 12.4/12.5).
    // Reach: a rung declared within the acting scope (12.12), never the rung
    // this action's own list belongs to.
    [Serializable]
    public class ExecuteRung : GameAction
    {
        public InteriorDefinition tier;

        public override void Link(LinkContext ctx)
        {
            var target = ctx.ResolveEnclosed(tier, "ExecuteRung");
            if (tier.rung == null)
                throw new InvalidOperationException($"ExecuteRung: scope '{tier.Id}' declares no rung.");
            // The one cycle the reach rule leaves possible: references only
            // ever point at self or below, so the only way back onto a running
            // rung is to name your own. That recursion is unbounded and a stack
            // overflow is uncatchable, so it is a content fault here (12.12)
            // rather than a first-run discovery.
            if (tier.rung.actions.Contains(this))
                throw new InvalidOperationException(
                    $"ExecuteRung at '{tier.Id}' names the rung it belongs to - a rung never runs itself (12.12).");
            ctx.Store(this, target);
        }

        // What this action would do is run the named rung's list, so its
        // answer is that list's answer, asked at the node the link names
        // (design doc 12.5) - the same shape as the two resets answering for
        // the subtree they would clear. Without it a nested refusal reads as a
        // closed gate one level down, and the outer list runs "whole" around a
        // rung that did nothing and showed no leg.
        public override Refusal Refuses(GameContext ctx, ScopeState ignoring) =>
            ActionList.Refuses(tier.rung.actions, ctx.Rebase(ctx.Scope.Link(this)), ignoring);

        // By the time a list reaches this, Refuses has answered null, so a
        // false from TryExecute can only mean the offer condition is unmet -
        // the designed no-op, never a swallowed refusal.
        public override void Execute(GameContext ctx) =>
            tier.rung.TryExecute(ctx.Rebase(ctx.Scope.Link(this)));

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
            if (tier.rung.actions.Contains(this))
                ctx.AddError(ValidationCheck.ScopeReach, $"ExecuteRung at '{target.Id}' names the rung it belongs to - a rung never runs itself (12.12).");
        }
    }

    // The restart idiom as one action (design doc 12.5): fire that scope's rung
    // through its own gate, then clear it - a bare ResetScope remains for a pure
    // wipe. A scope with no rung just clears. Reach is ResetScope's exactly: the
    // acting scope or a scope it encloses, never the root.
    [Serializable]
    public class RestartScope : GameAction
    {
        public ScopeDefinition scope;

        public override void Link(LinkContext ctx)
        {
            var target = ctx.ResolveEnclosed(scope, "RestartScope");
            if (target.Parent == null)
                throw new InvalidOperationException("RestartScope: the root scope is never resettable.");
            // The bank half runs the named scope's rung, so this is the same
            // self-cycle ExecuteRung refuses: a rung's own list restarting its
            // own scope recurses without bound (12.12).
            if (scope is InteriorDefinition interior && interior.rung != null && interior.rung.actions.Contains(this))
                throw new InvalidOperationException(
                    $"RestartScope at '{scope.Id}' names the rung it belongs to - a rung never runs itself (12.12).");
            ctx.Store(this, target);
        }

        // The same answer a bare reset gives: the clear is the half that can be
        // refused, and it is downward-closed either way (design doc 12.5).
        public override Refusal Refuses(GameContext ctx, ScopeState ignoring) =>
            ctx.Scope.Link(this).RefusalInSubtree(ignoring);

        public override void Execute(GameContext ctx)
        {
            var target = ctx.Scope.Link(this);
            // Before the bank as well as the clear: a refused restart changes
            // nothing at all, which is what "fail-closed" means outside a list
            // too (requirement 7).
            var refusal = target.RefusalInSubtree();
            if (refusal != null)
                throw new InvalidOperationException(
                    $"RestartScope of '{target.ScopeId}' was forced past a refusal: scope '{refusal.Host.ScopeId}' holds an unclaimed reward (design doc 12.5).");
            // Bank first, through the same gate check every invocation gets: an
            // unmet gate is the ordinary no-op, and the run's leavings are
            // cleared either way.
            if (scope is InteriorDefinition interior && interior.rung != null)
                interior.rung.TryExecute(ctx.Rebase(target));
            target.ClearSubtree(ctx.NowUtc);
        }

        public override void Validate(ValidationContext ctx)
        {
            var target = ctx.FindScope(scope);
            if (target == null)
            {
                ctx.AddError(ValidationCheck.NullEntry, "RestartScope names no scope.");
                return;
            }
            if (target == ctx.RootScope)
            {
                ctx.AddError(ValidationCheck.ScopeReach, "RestartScope targets the root - the root is never resettable (12.12).");
                return;
            }
            if (!ctx.InActingSubtree(target))
            {
                ctx.AddError(ValidationCheck.ScopeReach, $"RestartScope may target the acting scope or a scope it encloses (12.12); '{target.Id}' is neither from '{ctx.ActingScope.Id}'.");
                return;
            }
            if (scope is InteriorDefinition interior && interior.rung != null && interior.rung.actions.Contains(this))
                ctx.AddError(ValidationCheck.ScopeReach, $"RestartScope at '{target.Id}' names the rung it belongs to - a rung never runs itself (12.12).");
        }
    }
}
