using System;

namespace RidiculousGaming.GarageBandIdle.Events
{
    // How an event tier modifies the run (design doc section 6.1). A polymorphic
    // family serialized via [SerializeReference]; each debuff kind carries its
    // own parameters (a future "generation halved" carries its factor, a
    // "currency locked" carries the currency id). Apply/remove behavior arrives
    // with the events slice; until then this is data with a guaranteed type.
    [Serializable]
    public abstract class Debuff
    {
    }
}
