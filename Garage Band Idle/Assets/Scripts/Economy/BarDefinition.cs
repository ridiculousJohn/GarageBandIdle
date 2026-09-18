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

    // One currency a bar consumes as it fills, and how much of it one unit of
    // fill costs (design doc 12.7). The amount is a per-unit price, so the
    // draw's cover is the balance divided by it and the spend is the fill
    // times it; a consumption effect on the bar scales the price (12.2).
    [Serializable]
    public class ConsumesEntry
    {
        public CurrencyDefinition currency;
        public BigNumber amount;            // per unit of fill
    }

    // A generic fillable (design doc 12.7): pacing bars, currency bars that go
    // again, cascade bars. Completion is derived - progress >= fillAmount -
    // never stored, and whether the bar goes again is repeatWhen's answer.
    [CreateAssetMenu(menuName = "Garage Band Idle/Bar")]
    public class BarDefinition : Definition
    {
        // What this bar consumes as it fills, one entry per currency. An empty
        // list is a bar that fills from time alone, and the fill is capped by
        // the tightest entry's cover (design doc 12.7).
        public List<ConsumesEntry> consumes = new();
        public BigNumber fillAmount;
        public BigNumber fillRate;          // this bar's own fill speed (units/sec)
        // Whether the bar goes again after a completion, judged at its home when
        // it completes (design doc 12.7). Null is a bar that fills once and
        // stays full (the covers, what BarsCompleted counts). Present and true:
        // pay, subtract fillAmount, keep the residual, go again. Present and
        // false: the manual team - pay once, return to zero, leave the active
        // set; selecting it again is how it runs again. One condition, so an
        // upgrade or a handicap flips a team between the two without a second
        // field.
        [SerializeReference, SubclassPicker] public Condition repeatWhen;

        // The producer this bar's row fires when the row is tapped, resolved
        // outward from the bar's scope (design doc 12.11); its yield entry
        // naming this bar is how a tap adds time (12.7). Null is a row that is
        // not a tap target. Membership stays the group's control: the tap pays,
        // it does not choose.
        public ProducerDefinition tap;

        [SerializeReference, SubclassPicker] public Condition availableWhen;
        [SerializeReference, SubclassPicker] public List<GameAction> onComplete = new();
        public List<PerFillEntry> perFill = new();
    }
}
