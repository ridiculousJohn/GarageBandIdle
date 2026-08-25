using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Every scope inside another one - chapters and tiers. Root is the sole
    // exclusion, which is what puts the rung here: a rung is the ladder step out
    // of a scope, and the root is what the ladder climbs toward.
    public abstract class InteriorDefinition : ScopeDefinition
    {
        // The album release (tier) or capstone (chapter). Null for scopes with
        // no rung. SerializeReference so "no rung" stays null instead of an
        // auto-created empty instance.
        [SerializeReference] public Rung rung;
    }
}
