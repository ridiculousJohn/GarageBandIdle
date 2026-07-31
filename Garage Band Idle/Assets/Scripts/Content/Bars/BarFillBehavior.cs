using System;
using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // How a bar group fills (design doc section 6). A polymorphic family
    // serialized via [SerializeReference], like Condition and GameEffect:
    // the concrete type IS the fill mode, so a mode can never be authored
    // without its handler. The chapter JSON's (fillMode, delivery) strings map
    // onto a subclass at import; runtime code never inspects a mode value.
    // The serialized object is the definition-side spec (stateless, authored
    // data only); per-run state lives in the BarGroupRuntime it creates.
    [Serializable]
    public abstract class BarFillBehavior
    {
        // creates the runtime handler that owns this group's fill state
        public abstract BarGroupRuntime CreateRuntime(BarGroupDefinition group, List<BarState> bars,
            CurrencyManager currencies, RewardManager rewards, EffectContext effectContext);

        // load-time check that every id the behavior references resolves;
        // failures are reported loudly with the owning group named in source
        public abstract void Validate(ConditionContext context, string source);
    }
}
