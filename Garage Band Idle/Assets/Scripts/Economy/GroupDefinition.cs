using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // A set of things a scope declares and a cap on how many of them may be
    // active at once (design doc 12.7). That is the group's whole job: it
    // carries no currency and no throughput, and throttling a member is the
    // member's own rate rather than a second cap over the set.
    //
    // Owning no number, a group is not an effect target - buffing a set is a
    // tag its members share, which is the mechanism that already fans one
    // effect out to many owners.
    [CreateAssetMenu(menuName = "Garage Band Idle/Group")]
    public class GroupDefinition : Definition
    {
        public int maxActive = 1;

        // The members are definitions declared on the SAME scope as the group -
        // a bar, a generator, a producer, a currency, an upgrade - so the active
        // set and the member share one home and one lifetime (12.3). A
        // definition may be listed by several groups, and it is on only while
        // every one of them holds it.
        public List<Definition> members = new();
    }
}
