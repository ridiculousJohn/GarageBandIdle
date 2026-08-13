using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The one home for every stat modifier in the game. Systems do not keep
    // their own multiplier stacks: each asks for the composition on its target
    // and applies it, so there is one composition rule, one reset, and one
    // shape to save. A new effect kind is a handler that grants a modifier -
    // no new state, no new reset call, no new save field.
    //
    // Two kinds live here. A GRANTED modifier is a fact established at a
    // moment (a bought buff, a completed bar, a cleared event tier) and carries
    // a ContentScope because nothing else records how long it lasts. A DERIVED
    // modifier computes from a source that has its own lifetime and carries no
    // scope (see DerivedModifier).
    //
    // Grants are kept individually rather than accumulated into one number,
    // which is what makes a run reset exact: a collapsed product cannot say
    // which of its factors were run-scoped.
    public class ModifierSystem
    {
        private class Granted
        {
            public ModifierOperation Operation;
            public ContentScope Scope;
            public BigNumber Value;
        }

        private readonly Dictionary<ModifierTargetKey, List<Granted>> _granted = new();
        private readonly Dictionary<ModifierTargetKey, List<DerivedModifier>> _derived = new();

        // fires after a target's composition changes. Systems that advertise a
        // composed value re-broadcast it (ProductionSystem drives the Jam label
        // this way, so the button can never show a stale amount).
        public event Action<ModifierTargetKey> Changed;

        // nesting depth of deferral scopes, and the targets touched while deferring
        private int _deferDepth;
        private List<ModifierTargetKey> _deferred;

        public void Grant(ModifierTargetKey target, ModifierOperation operation, ContentScope scope, BigNumber value)
        {
            if (!IsAddressable(target, operation, "Grant"))
                return;

            // fail closed on broken content: a modifier with no scope has no
            // lifetime, so nothing could ever reset it correctly
            if (scope == ContentScope.None)
            {
                Debug.LogError($"ModifierSystem: Grant on '{target}' with scope None. Ignoring - an unscoped modifier has no lifetime.");
                return;
            }

            if (!IsApplicable(target, operation, value, "Grant"))
                return;

            if (!_granted.TryGetValue(target, out var grants))
            {
                grants = new List<Granted>();
                _granted.Add(target, grants);
            }

            grants.Add(new Granted { Operation = operation, Scope = scope, Value = value });
            Raise(target);
        }

        // Defers Changed for the duration of a rebuild (design doc section 12, rule
        // 6). A projection CLEARS the store and then re-grants from the surviving
        // facts, so it necessarily passes through a state where a target's
        // composition is wrong - every grant not yet re-applied is missing. Nothing
        // may observe that: GeneratorListModule refreshes a row's rate off this
        // event, so an undeferred projection redraws the fleet once per cleared
        // target and once per re-grant, each read against a half-rebuilt store.
        //
        // Silencing the restore's FACT primitives is not enough on its own, which is
        // the whole reason this exists - the projection between them is the loudest
        // part of a restore.
        //
        // Publication is on End rather than on a separate flush, so a caller cannot
        // defer and forget; the restore ends its deferral after the settle, which is
        // what makes the replayed set describe finished state.
        public void BeginDeferredNotifications() => _deferDepth++;

        public void EndDeferredNotifications()
        {
            if (_deferDepth == 0)
            {
                Debug.LogError("ModifierSystem: EndDeferredNotifications without a matching Begin. Ignoring.");
                return;
            }

            _deferDepth--;
            if (_deferDepth > 0 || _deferred == null)
                return;

            var pending = _deferred;
            _deferred = null;
            foreach (var target in pending)
                Changed?.Invoke(target);
        }

        // One target, once, in the order it was first touched: a projection grants
        // several modifiers per target, and a subscriber only needs to know the
        // composition moved.
        private void Raise(ModifierTargetKey target)
        {
            if (_deferDepth == 0)
            {
                Changed?.Invoke(target);
                return;
            }

            _deferred ??= new List<ModifierTargetKey>();
            if (!_deferred.Contains(target))
                _deferred.Add(target);
        }

        // boot-time registration for a modifier that computes its own value;
        // there is no matching removal because a derived modifier lives as long
        // as the source it reads
        public void AddDerived(DerivedModifier modifier)
        {
            if (modifier == null)
            {
                Debug.LogError("ModifierSystem: AddDerived with no modifier. Ignoring.");
                return;
            }
            if (!IsAddressable(modifier.Target, modifier.Operation, "AddDerived"))
                return;

            if (!_derived.TryGetValue(modifier.Target, out var derived))
            {
                derived = new List<DerivedModifier>();
                _derived.Add(modifier.Target, derived);
            }

            derived.Add(modifier);
            Raise(modifier.Target);
        }

        // Everything modifying this target, composed. Derived values are read
        // here on every call, which is why they can never be stale.
        //
        // Two buckets, not one: the target's own key, plus the UNQUALIFIED key of
        // its kind, which by rule 11 reaches every member in scope. Composing
        // only the exact key would make "double every generator's output" a
        // modifier that addresses nothing, and composing by walking every stored
        // key would make an unrelated qualifier's cost proportional to how much
        // content exists. Asking Covers keeps the rule in one place - the change
        // notification asks the same question, so a row can never refresh on a
        // grant the composition ignored, or miss one it counted.
        public ModifierComposition For(ModifierTargetKey target)
        {
            var add = BigNumber.Zero;
            var multiply = BigNumber.One;

            Accumulate(target, ref add, ref multiply);

            // a request that is itself unqualified IS that bucket; adding it
            // again would square every multiplier it holds
            if (target.IsQualified)
                Accumulate(ModifierTargetKey.All(target.Kind), ref add, ref multiply);

            return new ModifierComposition(add, multiply);
        }

        private void Accumulate(ModifierTargetKey key, ref BigNumber add, ref BigNumber multiply)
        {
            if (_granted.TryGetValue(key, out var grants))
            {
                for (var i = 0; i < grants.Count; i++)
                {
                    if (grants[i].Operation == ModifierOperation.Add)
                        add += grants[i].Value;
                    else
                        multiply *= grants[i].Value;
                }
            }

            if (!_derived.TryGetValue(key, out var derived))
                return;

            for (var i = 0; i < derived.Count; i++)
            {
                if (derived[i].Operation == ModifierOperation.Add)
                    add += derived[i].Value;
                else
                    multiply *= derived[i].Value;
            }
        }

        // Empties the grant store so a projection can rebuild it (design doc
        // section 12, rule 6). Derived modifiers are untouched: they carry no
        // scope because their lifetime is their source's, so there is nothing
        // here to rebuild for them.
        //
        // This is deliberately total rather than selective. The method it
        // replaced dropped run-scoped grants and left permanent ones sitting in
        // place, which made a release and a load two different mechanisms for
        // arriving at one modifier set - written by different slices, exercised
        // on different days, and able to disagree without anything noticing.
        // Re-projection is now the only door a modifier enters through, so a
        // boundary clears everything and re-runs the projection over the facts
        // that survived it; a store that gets rebuilt cannot hold a stale or
        // double-counted effect. Nothing may call this without projecting
        // afterwards - EconomyContext.ProjectModifiers is the only caller, and
        // it does both halves.
        //
        // Every target settles before any notification fires, so no subscriber
        // observes one target cleared while another still holds its grants
        // (state, then notify). Returns whether anything changed, so a no-op
        // stays silent.
        public bool ResetGranted()
        {
            if (_granted.Count == 0)
                return false;

            List<ModifierTargetKey> cleared = null;
            foreach (var entry in _granted)
            {
                if (entry.Value.Count == 0)
                    continue;

                entry.Value.Clear();
                cleared ??= new List<ModifierTargetKey>();
                cleared.Add(entry.Key);
            }

            if (cleared == null)
                return false;

            foreach (var target in cleared)
                Raise(target);
            return true;
        }

        // a target that names nothing, or names something its kind has no room
        // for, would silently modify a value nobody reads
        private static bool IsAddressable(ModifierTargetKey target, ModifierOperation operation, string source)
        {
            // A serialized enum is an int, so an asset can hold a value no member
            // defines. Both writers (Grant, AddDerived) come through here, which is
            // what keeps such a value out of the store entirely: an undefined target
            // would be filed as global and read by nobody, and an undefined
            // operation is worse - IsApplicable's value guards test for Multiply and
            // Add by name, so it would skip them all and then compose as a multiply.
            if (!Enum.IsDefined(typeof(ModifierTarget), target.Kind))
            {
                Debug.LogError($"ModifierSystem: {source} with target kind {(int)target.Kind}, which no ModifierTarget defines. Ignoring.");
                return false;
            }
            if (target.Kind == ModifierTarget.None)
            {
                Debug.LogError($"ModifierSystem: {source} with target kind None (uninitialized). Ignoring.");
                return false;
            }
            if (!Enum.IsDefined(typeof(ModifierOperation), operation))
            {
                Debug.LogError($"ModifierSystem: {source} on '{target}' with operation {(int)operation}, which no ModifierOperation defines. Ignoring.");
                return false;
            }
            if (operation == ModifierOperation.None)
            {
                Debug.LogError($"ModifierSystem: {source} on '{target}' with operation None (uninitialized). Ignoring.");
                return false;
            }
            // An ABSENT qualifier is legal on every kind and means "every member
            // in reach" (rule 11), so there is no refusal for one here - the old
            // check read an unqualified key as addressing nothing, which is the
            // opposite of what it now means. A qualifier on a kind with no
            // definition family is still refused: nothing could resolve it, so it
            // would file a modifier under a key no reader ever asks for.
            if (target.IsQualified && !ModifierTargetKey.AcceptsQualifier(target.Kind))
            {
                Debug.LogError($"ModifierSystem: {source} on '{target.Kind}' carries a qualifier '{target.Qualifier}', which that target has no id family to resolve against. Ignoring.");
                return false;
            }
            return true;
        }

        // fail closed on tuning that would break the composition it lands in
        private static bool IsApplicable(ModifierTargetKey target, ModifierOperation operation, BigNumber value, string source)
        {
            if (operation == ModifierOperation.Multiply && value <= BigNumber.Zero)
            {
                Debug.LogError($"ModifierSystem: {source} on '{target}' with a non-positive Multiply value '{value.ToDouble()}'. Ignoring - it would zero or negate the whole product.");
                return false;
            }
            if (operation == ModifierOperation.Add && value < BigNumber.Zero)
            {
                Debug.LogError($"ModifierSystem: {source} on '{target}' with a negative Add value '{value.ToDouble()}'. Ignoring.");
                return false;
            }
            return true;
        }
    }
}
