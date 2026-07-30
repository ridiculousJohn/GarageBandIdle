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
            Changed?.Invoke(target);
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
            Changed?.Invoke(modifier.Target);
        }

        // Everything modifying this target, composed. Derived values are read
        // here on every call, which is why they can never be stale.
        public ModifierComposition For(ModifierTargetKey target)
        {
            var add = BigNumber.Zero;
            var multiply = BigNumber.One;

            if (_granted.TryGetValue(target, out var grants))
            {
                for (var i = 0; i < grants.Count; i++)
                {
                    if (grants[i].Operation == ModifierOperation.Add)
                        add += grants[i].Value;
                    else
                        multiply *= grants[i].Value;
                }
            }

            if (_derived.TryGetValue(target, out var derived))
            {
                for (var i = 0; i < derived.Count; i++)
                {
                    if (derived[i].Operation == ModifierOperation.Add)
                        add += derived[i].Value;
                    else
                        multiply *= derived[i].Value;
                }
            }

            return new ModifierComposition(add, multiply);
        }

        // The run reset (album release, event baseline): drops every run-scoped
        // grant and keeps permanent-in-chapter ones. Derived modifiers are
        // untouched by design. Every target settles before any notification
        // fires, so no subscriber observes one target cleared while another
        // still holds its run grants (state, then notify). Returns whether
        // anything changed, so a no-op reset stays silent.
        public bool ResetRunScoped()
        {
            List<ModifierTargetKey> cleared = null;

            foreach (var entry in _granted)
            {
                if (entry.Value.RemoveAll(granted => granted.Scope == ContentScope.Run) == 0)
                    continue;

                cleared ??= new List<ModifierTargetKey>();
                cleared.Add(entry.Key);
            }

            if (cleared == null)
                return false;

            foreach (var target in cleared)
                Changed?.Invoke(target);
            return true;
        }

        // a target that names nothing, or names something its kind has no room
        // for, would silently modify a value nobody reads
        private static bool IsAddressable(ModifierTargetKey target, ModifierOperation operation, string source)
        {
            if (target.Kind == ModifierTarget.None)
            {
                Debug.LogError($"ModifierSystem: {source} with target kind None (uninitialized). Ignoring.");
                return false;
            }
            if (operation == ModifierOperation.None)
            {
                Debug.LogError($"ModifierSystem: {source} on '{target}' with operation None (uninitialized). Ignoring.");
                return false;
            }
            if (ModifierTargetKey.RequiresQualifier(target.Kind) && target.Qualifier.Length == 0)
            {
                Debug.LogError($"ModifierSystem: {source} on '{target}' names no {target.Kind} id. Ignoring - it would address nothing.");
                return false;
            }
            if (!ModifierTargetKey.RequiresQualifier(target.Kind) && target.Qualifier.Length > 0)
            {
                Debug.LogError($"ModifierSystem: {source} on '{target.Kind}' carries a qualifier '{target.Qualifier}', which that target has no room for. Ignoring.");
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
