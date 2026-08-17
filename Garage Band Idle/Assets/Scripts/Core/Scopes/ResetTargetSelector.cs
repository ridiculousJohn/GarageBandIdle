using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Which scopes a rung's reset clears (design doc rule 14): polymorphic,
    // subclass-picked like Condition, because the target set is authored
    // content. It lives on the rung - which is filed on a scope - never on the
    // module presenting it: a prefab can be placed twice, and a target list on
    // it would be two sources of truth for one lifetime.
    //
    // Output CLOSES DOWNWARD, in the base class so no member can forget: a
    // scope's contents include its ladder, and clearing a scope while a child
    // kept its facts would leave the child's formulas reading state the reset
    // claims is gone. Selection is resolved against the tree the OWNER sits in;
    // ordering questions (preceding siblings) are answered by the parent's
    // authored child order, never by anything a scope knows about itself.
    [Serializable]
    public abstract class ResetTargetSelector
    {
        // The selected set, de-duplicated, downward-closed. The seed is the
        // member's whole personality; the closure is everyone's.
        public void Select(Scope owner, HashSet<Scope> into)
        {
            if (owner == null)
                return;

            foreach (var seed in Seeds(owner))
                AddWithDescendants(seed, into);
        }

        // which scopes this selector names, before closure
        protected abstract IEnumerable<Scope> Seeds(Scope owner);

        private static void AddWithDescendants(Scope scope, HashSet<Scope> into)
        {
            if (scope == null || !into.Add(scope))
                return;

            foreach (var child in scope.Children)
                AddWithDescendants(child, into);
        }
    }

    // The rung clears its own scope and everything under it - the album rung's
    // shape: a within-chapter prestige resets the ladder it sits on.
    [Serializable]
    public class SelfAndContainedSelector : ResetTargetSelector
    {
        protected override IEnumerable<Scope> Seeds(Scope owner)
        {
            yield return owner;
        }
    }

    // The owner's scope PLUS every sibling before it in their parent's authored
    // child order (rule 14's table says exactly that) - the deep-rung shape: a
    // late rung resets itself and the rungs climbed to reach it, or its own
    // facts would survive its own press. Resolved from the parent's list
    // because only the parent owns that order; an owner with no parent has no
    // siblings and still selects itself.
    [Serializable]
    public class PrecedingSiblingsSelector : ResetTargetSelector
    {
        protected override IEnumerable<Scope> Seeds(Scope owner)
        {
            if (owner.Parent != null)
            {
                foreach (var sibling in owner.Parent.Children)
                {
                    if (sibling == owner)
                        break;

                    yield return sibling;
                }
            }

            yield return owner;
        }
    }

    // Named child scopes, by definition id, resolved within the owner's own
    // subtree - the capstone's shape: the chapter scope selects its tier
    // scopes. Downward-only by construction: a selector reaching a sibling or
    // an ancestor would clear truth its rung cannot even read, and anything
    // two branches share lives in their common ancestor instead.
    [Serializable]
    public class NamedScopesSelector : ResetTargetSelector
    {
        [SerializeField]
        [DefinitionId(typeof(ScopeDefinition))]
        [Tooltip("Scope definition ids to clear, resolved among the owning scope's descendants.")]
        private List<string> _scopeIds = new();

        public IReadOnlyList<string> ScopeIds => _scopeIds;

        // Unity's serializer needs a parameterless constructor on plain classes
        public NamedScopesSelector() { }

        public NamedScopesSelector(List<string> scopeIds)
        {
            _scopeIds = scopeIds ?? new List<string>();
        }

        protected override IEnumerable<Scope> Seeds(Scope owner)
        {
            foreach (var id in _scopeIds)
            {
                var named = FindDescendant(owner, id);
                if (named == null)
                    Debug.LogError($"ResetTargetSelector: rung on scope instance '{owner.InstanceId}' names scope '{id}', which is not among its descendants - a selector only reaches downward. Skipping it.");
                else
                    yield return named;
            }
        }

        private static Scope FindDescendant(Scope scope, string definitionId)
        {
            foreach (var child in scope.Children)
            {
                if (child.Definition != null && child.Definition.Id == definitionId)
                    return child;

                var deeper = FindDescendant(child, definitionId);
                if (deeper != null)
                    return deeper;
            }
            return null;
        }
    }
}
