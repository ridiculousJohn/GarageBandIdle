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
        // Grants the payload to the running game. The scope is the owning
        // upgrade's and travels with the grant rather than living on the payload,
        // so an upgrade's declared lifetime and its effect's lifetime cannot
        // disagree (UpgradeDefinition.Scope is the one declaration).
        public abstract void Apply(UpgradePayloadContext context, ContentScope scope);

        // load-time check that every id the payload references resolves;
        // failures are reported loudly with the owning upgrade named in source
        public abstract void Validate(ConditionContext context, string source);
    }
}
