using System;

namespace RidiculousGaming.GarageBandIdle
{
    // What a piece of content grants when it lands (design doc sections 4 and
    // 6.1). A polymorphic family serialized via [SerializeReference], like
    // Condition: each subclass declares exactly the fields its kind needs and
    // implements Apply, so an effect type can never exist without its handler.
    //
    // One family serves both acquisition paths - an upgrade's payload and a
    // reward's grant - because the runtime mutation is identical either way. What
    // differs is the question each source answers (when and why something is
    // granted), and that stays with UpgradeSystem and RewardManager.
    //
    // The scope is the SOURCE's and travels with the grant rather than living on
    // the effect, so a source's declared lifetime and its effect's lifetime can
    // never disagree: an upgrade passes UpgradeDefinition.Scope, a bar reward
    // passes its group's Scope, an event tier passes its own. A reward asset is
    // reusable precisely because it carries no lifetime of its own.
    [Serializable]
    public abstract class GameEffect
    {
        // Grants the effect to the running game.
        public abstract void Apply(EffectContext context, ContentScope scope);

        // load-time check that every id the effect references resolves; failures
        // are reported loudly with the owning content named in source
        public abstract void Validate(ConditionContext context, string source);
    }
}
