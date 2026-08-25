using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // The tree's one parentless scope: career facts, no rung, no event. Its own
    // file because Unity binds one ScriptableObject class per script, by file
    // name - a CreateAssetMenu type sharing a file has no MonoScript, so its
    // assets cannot be created or reloaded.
    [CreateAssetMenu(menuName = "Garage Band Idle/Scope/Root")]
    public class RootDefinition : ScopeDefinition
    {
        // Typed, because the tree build must hand back a RootScopeState and the
        // polymorphic entry point can only promise a ScopeState.
        internal RootScopeState CreateRoot() => new RootScopeState(this);

        // Reached with a parent only when content authored a root inside the
        // tree. Validation refuses that (12.12), so arriving here means the pass
        // was skipped, and a node that reported itself parentless while sitting
        // in someone's Children would break every chain walk quietly.
        internal override ScopeState CreateState(ScopeState parent)
        {
            if (parent != null)
                throw new InvalidOperationException(
                    $"Root scope '{Id}' is declared as a child of '{parent.ScopeId}'; a root has no parent (12.12).");
            return CreateRoot();
        }
    }
}
