using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // A group of bars sharing a fill pool and a pipe (design doc 12.7). Rate is
    // the pipe: each active unfilled bar demands its own fillRate; a short pipe
    // or pool throttles all drawing bars proportionally.
    [CreateAssetMenu(menuName = "Garage Band Idle/Bar Group")]
    public class BarGroupDefinition : Definition
    {
        public CurrencyDefinition fillCurrency;
        public BigNumber pipeRate;          // total throughput the group can spend per second
        public int maxActive = 1;
        [SerializeReference, SubclassPicker] public BarFillBehavior behavior;

        // The group owns its bars: a bar's home is its group's, so membership is
        // placement rather than an id pointing back (design doc 12.7).
        public List<BarDefinition> bars = new();
    }
}
