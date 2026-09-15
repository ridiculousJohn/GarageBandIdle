using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Duplicate-grant policy and count growth are one closed choice (design doc
    // 12.5): Replace keeps a re-grant at count 1; Linear and Multiply increment
    // and the name picks the count-scaling formula (1 + (m-1)*n vs m^n) applied
    // on read.
    public enum StackingKind
    {
        Replace,
        Linear,
        Multiply
    }

    // A named List<Effect> granted as a pointer-fact {modifierId, count} by
    // AddModifier (design doc 12.5). Reserved for grants from moments that leave
    // no other trace; when a count already exists as state, derive from it
    // instead (12.6).
    [CreateAssetMenu(menuName = "Garage Band Idle/Modifier")]
    public class ModifierDefinition : Definition
    {
        public StackingKind stacking = StackingKind.Replace;
        public List<Effect> effects = new();

        // Judged at gather time against the ORIGIN context; absent means always.
        // A false condition skips the whole modifier - permanent membership and
        // granted stacks alike (design doc 12.5). The effects stay unconditional
        // atoms; the timing lives here, on the carrier.
        [SerializeReference, SubclassPicker] public Condition appliesWhen;

        // The timer this modifier's BuffActive reads, resolved outward from the scope
        // declaring the modifier like a flag (section 9); empty when no condition of
        // this modifier reads one. activeAfterSeconds is the band: the time that must
        // still remain on the timer for the buff to count as active, so two modifiers
        // over one timer author a speed ladder and the code knows neither of them.
        public string timer;
        public double activeAfterSeconds;
    }
}
