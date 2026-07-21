using System;

namespace RidiculousGaming.GarageBandIdle.Events
{
    // JSON effect "automationDisabled": generators are paused for the event's
    // duration — tap only (garage_jam).
    [Serializable]
    public class AutomationDisabledDebuff : Debuff
    {
    }
}
