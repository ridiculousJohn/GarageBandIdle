using System;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // What an upgrade grants (design doc section 4). A polymorphic family
    // serialized via [SerializeReference], like Condition and RewardDefinition:
    // each subclass declares exactly the fields its kind needs and implements
    // Apply, so a payload type can never exist without its handler. The chapter
    // JSON's payload `effect` string maps onto a subclass at import.
    [Serializable]
    public abstract class UpgradePayload
    {
        // grants the payload to the running game
        public abstract void Apply(UpgradePayloadContext context);

        // load-time check that every id the payload references resolves;
        // failures are reported loudly with the owning upgrade named in source
        public abstract void Validate(ConditionContext context, string source);
    }
}
