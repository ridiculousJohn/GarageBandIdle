using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Count-scaling vocabulary shared by cascade entries and modifier stacks
    // (design doc 12.7): multiply = m^n, linear = 1 + (m-1)*n.
    public enum GrowthKind
    {
        Multiply,
        Linear
    }

    // A cascade entry: the effect this bar applies per completed fill, scaled by
    // the bar's fillCount on read. Growth lives on the carrying entry, never on
    // the Effect atom (design doc 12.7).
    [Serializable]
    public class PerFillEntry
    {
        public Effect effect;
        public GrowthKind growth = GrowthKind.Multiply;
    }

    // A generic fillable (design doc 12.7): pacing bars, repeating currency bars,
    // cascade bars. Completion is derived - progress >= fillAmount - never
    // stored. The fill/settlement system lands with the bar step; this is the
    // authored shape BarsCompleted and the importer already need.
    [CreateAssetMenu(menuName = "Garage Band Idle/Bar")]
    public class BarDefinition : Definition
    {
        public BigNumber fillAmount;
        public BigNumber fillRate;          // this bar's own max fill speed (units/sec)
        public bool repeating;              // fill -> fire onComplete -> reset to 0 -> go again
        [SerializeReference, SubclassPicker] public Condition availableWhen;
        [SerializeReference, SubclassPicker] public List<GameAction> onComplete = new();
        public List<PerFillEntry> perFill = new();
    }
}
