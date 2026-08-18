using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // A group of bars sharing a fill pool and a pipe (design doc 12.7). Rate is
    // the pipe: each active unfilled bar demands its own fillRate; a short pipe
    // or pool throttles all drawing bars proportionally.
    [CreateAssetMenu(menuName = "Garage Band Idle/Bar Group")]
    public class BarGroupDefinition : Definition
    {
        [DefinitionId(typeof(CurrencyDefinition))] public string fillCurrencyId;
        public BigNumber pipeRate;          // total throughput the group can spend per second
        public int maxActive = 1;
        [SerializeReference, SubclassPicker] public BarFillBehavior behavior;
    }
}
