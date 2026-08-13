using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The one home for every stat modifier in the game. Systems do not keep
    // their own multiplier stacks: each asks for the composition on the number
    // it owns and applies it, so there is one composition rule, one reset, and
    // one shape to save. A new effect kind is a handler that grants a modifier -
    // no new state, no new reset call, no new save field.
    //
    // A modifier says which numbers it reaches with a ModifierSelector, and a
    // number says what it is with a ModifierSubject (design doc section 12, rule
    // 11). Nothing here names a stat: there is no enum of modifiable things,
    // because a modifiable number is identified by its own id and tags like every
    // other reference in the game. That is what lets one generator's cash line be
    // buffed without touching its fans line.
    //
    // Two kinds live here. A GRANTED modifier is a fact established at a moment
    // (a bought buff, a completed bar, a cleared event tier) and carries a
    // ContentScope because nothing else records how long it lasts. A DERIVED
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
            public ModifierSelector Selector;
            public ModifierOperation Operation;
            public ContentScope Scope;
            public BigNumber Value;
        }

        // Flat lists rather than a dictionary keyed by address. A selector is not
        // a lookup key - it describes a SET, and which numbers fall in it is a
        // question about each number, not about a string. Composition therefore
        // walks what was granted, which is bounded by how many modifiers exist
        // rather than by how much content does; the dictionary it replaced had to
        // union two buckets by hand to express "reaches everything" and still
        // could not express a set.
        private readonly List<Granted> _granted = new();
        private readonly List<DerivedModifier> _derived = new();

        // fires after the modifiers matching a selector change, carrying that
        // selector. A subscriber asks it about its own subject, which is the same
        // question the composition asks, so a display can never refresh on a
        // modifier the composition ignored or miss one it counted.
        public event Action<ModifierSelector> Changed;

        // nesting depth of deferral scopes, and the selectors touched while deferring
        private int _deferDepth;
        private List<ModifierSelector> _deferred;

        public void Grant(ModifierSelector selector, ModifierOperation operation, ContentScope scope,
            BigNumber value)
        {
            if (!IsWellFormed(operation, selector, "Grant"))
                return;

            // fail closed on broken content: a modifier with no scope has no
            // lifetime, so nothing could ever reset it correctly
            if (scope == ContentScope.None)
            {
                Debug.LogError($"ModifierSystem: Grant on '{selector}' with scope None. Ignoring - an unscoped modifier has no lifetime.");
                return;
            }

            if (!IsApplicable(selector, operation, value, "Grant"))
                return;

            _granted.Add(new Granted
            {
                Selector = selector,
                Operation = operation,
                Scope = scope,
                Value = value,
            });
            Raise(selector);
        }

        // Defers Changed for the duration of a rebuild (design doc section 12, rule
        // 6). A projection CLEARS the store and then re-grants from the surviving
        // facts, so it necessarily passes through a state where a number's
        // composition is wrong - every grant not yet re-applied is missing. Nothing
        // may observe that: GeneratorListModule refreshes a row's rate off this
        // event, so an undeferred projection redraws the fleet once per cleared
        // selector and once per re-grant, each read against a half-rebuilt store.
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
            foreach (var selector in pending)
                Changed?.Invoke(selector);
        }

        // One selector, once, in the order it was first touched: a projection
        // grants several modifiers against the same set, and a subscriber only
        // needs to know its composition moved.
        private void Raise(ModifierSelector selector)
        {
            if (_deferDepth == 0)
            {
                Changed?.Invoke(selector);
                return;
            }

            _deferred ??= new List<ModifierSelector>();
            if (!_deferred.Contains(selector))
                _deferred.Add(selector);
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
            if (!IsWellFormed(modifier.Operation, modifier.Selector, "AddDerived"))
                return;

            _derived.Add(modifier);
            Raise(modifier.Selector);
        }

        // Everything modifying this number, composed. Derived values are read
        // here on every call, which is why they can never be stale.
        //
        // Each modifier is asked whether its selector reaches this subject, and
        // the subject answers per term. One rule, asked in one place - the change
        // notification asks the same one, so a row can never refresh on a grant
        // the composition ignored, or miss one it counted.
        public ModifierComposition For(in ModifierSubject subject)
        {
            var multiply = BigNumber.One;

            foreach (var granted in _granted)
            {
                if (granted.Selector.Matches(subject))
                    multiply *= granted.Value;
            }

            foreach (var derived in _derived)
            {
                if (derived.Selector.Matches(subject))
                    multiply *= derived.Value;
            }

            return new ModifierComposition(multiply);
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
        // Every grant settles before any notification fires, so no subscriber
        // observes one set cleared while another still holds its grants (state,
        // then notify). Returns whether anything changed, so a no-op stays silent.
        public bool ResetGranted()
        {
            if (_granted.Count == 0)
                return false;

            var cleared = new List<ModifierSelector>();
            foreach (var granted in _granted)
            {
                if (!cleared.Contains(granted.Selector))
                    cleared.Add(granted.Selector);
            }

            _granted.Clear();

            foreach (var selector in cleared)
                Raise(selector);
            return true;
        }

        // A serialized enum is an int, so an asset can hold a value no member
        // defines. Both writers (Grant, AddDerived) come through here, which is
        // what keeps such a value out of the store entirely: an undefined
        // operation is the dangerous one, because IsApplicable's value guard tests
        // for Multiply by name, so it would skip the guard and then compose as a
        // multiply anyway - a zero there wipes the whole product for the rest of
        // the run.
        //
        // There is no check on the selector's SHAPE, because every shape is legal:
        // an empty selector reaches everything by rule 11, and a term naming
        // nothing reachable is a content error boot validation reports against the
        // asset that authored it, where the id can be named. Refusing it here
        // would mean this class knowing what content exists.
        private static bool IsWellFormed(ModifierOperation operation, ModifierSelector selector, string source)
        {
            if (!Enum.IsDefined(typeof(ModifierOperation), operation))
            {
                Debug.LogError($"ModifierSystem: {source} on '{selector}' with operation {(int)operation}, which no ModifierOperation defines. Ignoring.");
                return false;
            }
            if (operation == ModifierOperation.None)
            {
                Debug.LogError($"ModifierSystem: {source} on '{selector}' with operation None (uninitialized). Ignoring.");
                return false;
            }
            return true;
        }

        // fail closed on tuning that would break the composition it lands in
        private static bool IsApplicable(ModifierSelector selector, ModifierOperation operation,
            BigNumber value, string source)
        {
            if (operation == ModifierOperation.Multiply && value <= BigNumber.Zero)
            {
                Debug.LogError($"ModifierSystem: {source} on '{selector}' with a non-positive Multiply value '{value.ToDouble()}'. Ignoring - it would zero or negate the whole product.");
                return false;
            }
            return true;
        }
    }
}
