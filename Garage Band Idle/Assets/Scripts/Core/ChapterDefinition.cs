using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Root's direct children. Idle is per-chapter, so its claim and clock live
    // on the state this makes (design doc 12.9).
    [CreateAssetMenu(menuName = "Garage Band Idle/Scope/Chapter")]
    public class ChapterDefinition : InteriorDefinition
    {
        internal override ScopeState CreateState(ScopeState parent) => new ChapterScopeState(this, parent);
    }
}
