using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // What a piece of content grants when it lands (design doc sections 4 and
    // 6.1). A polymorphic family serialized via [SerializeReference], like
    // Condition: each subclass declares exactly the fields its kind needs and
    // implements its mutation, so an effect type can never exist without its
    // handler.
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
    //
    // TWO ENTRY POINTS, because "granted now" and "rebuilt from a surviving
    // fact" are different events (design doc section 12, rule 6). Acquisition
    // runs the whole payload. Projection runs only what may be re-run, and the
    // classification is the effect class's, declared below - not the author's,
    // and not a rule each call site remembers. A single Apply could not express
    // the difference, which is why a currency payout behind a latch would
    // otherwise be paid again at every release, load, and reprojection.
    //
    // Note what this does NOT protect: RE-ACQUISITION. UpgradeSystem clears
    // run-scoped latches at each release and re-applies any content unlock whose
    // gate still holds, through the acquisition path, by design - that is the
    // second-run reveal walk. A one-shot effect on a fact that can re-fire is
    // therefore a CONTENT mistake, refused by ContentValidator, not something
    // this class can guard.
    [Serializable]
    public abstract class GameEffect
    {
        // Whether the projection may re-apply this effect. Declared per class.
        public abstract EffectProjection Projection { get; }

        // Whether anything under this effect pays out - the question the content
        // rule above asks, which is NOT the same question Projection answers: a
        // compound is safe to project (it filters its own children) while still
        // containing a payout that no run-scoped fact may carry. A leaf answers
        // from its own kind; CompoundEffect answers over its children.
        public virtual bool ContainsOneShot => Projection == EffectProjection.OneShot;

        // Grants the effect to the running game, in full. The path a purchase, a
        // completed bar, a cleared tier, or a capstone takes.
        public abstract void ApplyOnAcquisition(EffectContext context, ContentScope scope);

        // Re-applies whatever may be re-applied from a fact that already exists.
        // The default is the whole of the behavior for every leaf: a projectable
        // effect re-runs its acquisition mutation, and a one-shot does nothing.
        //
        // A one-shot is a SILENT no-op, not an error, and the distinction matters.
        // "Nothing to re-apply" is this method's correct answer for a payout, not a
        // failure to report: a permanent latch carrying a currency grant is legal
        // content, and its projection runs at every release and every load, so an
        // error here would be a log line per boundary describing working content.
        // Filtering silently is also what makes every bulk caller uniform -
        // UpgradeSystem, BarGroupRuntime and CompoundEffect all just project their
        // children and let each one answer for itself, with no per-caller kind check
        // to get wrong.
        //
        // What stops a payout being paid twice is therefore two things, neither of
        // them a log: this method never paying one, and ContentValidator refusing a
        // one-shot on a fact that can re-FIRE through the acquisition path.
        public virtual void Project(EffectContext context, ContentScope scope)
        {
            if (Projection == EffectProjection.OneShot)
                return;

            ApplyOnAcquisition(context, scope);
        }

        // load-time check that every id the effect references resolves; failures
        // are reported loudly with the owning content named in source
        public abstract void Validate(ConditionContext context, string source);
    }
}
