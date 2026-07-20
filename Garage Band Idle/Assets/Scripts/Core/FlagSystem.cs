using System;
using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle
{
    // Progress flags: string ids set once and observed anywhere. Content-unlock
    // upgrades, modules, and capstones set them; sections and gates monitor them
    // (GateCondition.TypeFlagSet). Flags only ever latch on — scoping/reset
    // rules arrive with the save/prestige slices.
    public class FlagSystem
    {
        private readonly HashSet<string> _flags = new();

        // fires once per flag, when it is first set
        public event Action<string> FlagSet;

        public bool IsSet(string id) => _flags.Contains(id);

        public void Set(string id)
        {
            if (_flags.Add(id))
                FlagSet?.Invoke(id);
        }
    }
}
