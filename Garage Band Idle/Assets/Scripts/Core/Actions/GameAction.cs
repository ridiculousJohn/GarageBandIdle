using System;

namespace RidiculousGaming.GarageBandIdle
{
    // A one-shot consequence of a player action (design doc section 4): pay a
    // currency award today; whatever else is awarded later is a new subclass. A
    // polymorphic family serialized via [SerializeReference], like GameEffect,
    // and split from it on purpose: an effect is re-applicable state that every
    // rebuild boundary re-runs, while an action executes exactly once, at the
    // moment the operation that earns it runs - a buff purchase, an event tier
    // clear, the capstone completion. No projection, release, or load ever sees
    // one, which is what makes "a payout paid twice" inexpressible: the fields
    // that hold actions belong to player-action moments, and the auto-re-firing
    // paths (content unlocks, reward projection) have no action fields to run.
    //
    // No ContentScope parameter, and its absence is the category speaking: a
    // one-shot has no lifetime to declare. What an award leaves behind (a paid
    // balance) takes its durability from the thing awarded - a currency's group
    // decides whether a release takes the balance back - never from the action.
    [Serializable]
    public abstract class GameAction
    {
        // The operation that earned this action calls it, once. The holder knows
        // WHEN; the action knows WHAT.
        public abstract void Execute(EffectContext context);

        // Whether Execute would grant anything, asked by the operation BEFORE it
        // commits: a purchase that would grant nothing must refuse before any
        // state moves, and boot validation's report cannot do that - it never
        // stops a boot. Execute still fails closed on its own (broken content
        // can arrive without ever being asked), but the refusal is here.
        public abstract bool CanExecute(EffectContext context);

        // load-time check that every id the action references resolves; failures
        // are reported loudly with the owning content named in source
        public abstract void Validate(ConditionContext context, string source);
    }
}
