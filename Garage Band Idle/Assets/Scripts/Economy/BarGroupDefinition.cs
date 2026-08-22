using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // A set of bars and a cap on how many of them may run at once (design doc
    // 12.7). That is the group's whole job. It carries no currency and no
    // throughput of its own: a bar drinks what it names, at its own rate, and
    // throttling a bar is its rate rather than a second cap over the set.
    //
    // Owning no number, a group is not an effect target - buffing a set of bars
    // is a tag they share, which is the mechanism that already fans one effect
    // out to many owners.
    [CreateAssetMenu(menuName = "Garage Band Idle/Bar Group")]
    public class BarGroupDefinition : Definition
    {
        public int maxActive = 1;

        // The group owns its bars: a bar's home is its group's, so membership is
        // placement rather than an id pointing back.
        public List<BarDefinition> bars = new();
    }
}
