using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Everything below a chapter, at any depth - the tree nests freely.
    [CreateAssetMenu(menuName = "Garage Band Idle/Scope/Tier")]
    public class TierDefinition : InteriorDefinition
    {
        internal override ScopeState CreateState(ScopeState parent) => new TierScopeState(this, parent);
    }
}
