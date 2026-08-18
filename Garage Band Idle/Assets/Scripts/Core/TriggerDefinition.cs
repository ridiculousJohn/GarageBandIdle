using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // The one sanctioned condition-observer (design doc 12.5): when the condition
    // holds and the id is not in the declaring scope's firedTriggers, the sweep
    // latches the id FIRST, then executes the actions. One-shot per scope-life -
    // the reset that clears the declaring scope re-arms it. The sweep itself
    // lands with the tick step; this is the authored shape.
    [CreateAssetMenu(menuName = "Garage Band Idle/Trigger")]
    public class TriggerDefinition : Definition
    {
        [SerializeReference, SubclassPicker] public Condition condition;
        [SerializeReference, SubclassPicker] public List<GameAction> actions = new();
    }
}
