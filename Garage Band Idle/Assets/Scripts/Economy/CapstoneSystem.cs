using RidiculousGaming.GarageBandIdle.Loop;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime home of the chapter capstone (design doc sections 1-2 and 5): the
    // completed capstone is a fact source like any latch, and the declared
    // completion flag IS the latch. There is no state here beyond what the flag
    // registry already holds - completion is asked of the flag, never cached -
    // so a restore that brings the flag back brings the completion back with no
    // second copy to disagree.
    //
    // The completion itself (ExecuteCompletion) runs only from
    // EconomyContext.CompleteCapstone, the player-action operation that earned
    // it. The projection half (ProjectModifiers) re-applies OnComplete from the
    // latch at every rebuild boundary, which is what lets capstone-authored
    // state survive a release, a load, and a reprojection exactly as the
    // GameEffect contract requires (rule 6). Actions are deliberately absent
    // from the projection: no rebuild executes a one-shot, structurally.
    public class CapstoneSystem : IModifierFactSource
    {
        private readonly CapstoneConfig _capstone;
        private readonly FlagSystem _flags;
        private readonly EffectContext _effects;

        public CapstoneSystem(CapstoneConfig capstone, FlagSystem flags, EffectContext effects)
        {
            _capstone = capstone;
            _flags = flags;
            _effects = effects;
        }

        // Completion is the declared flag, read live. A capstone that declares
        // no flag can never read as completed - boot validation already reports
        // that as broken content, and answering false keeps the operation
        // offerable rather than inventing a latch the declaration never named.
        public bool IsCompleted => _capstone != null && _capstone.IsAuthored
            && !string.IsNullOrEmpty(_capstone.CompletionFlagId)
            && _flags.IsSet(_capstone.CompletionFlagId);

        // Whether every capstone action would execute, asked by the operation
        // BEFORE the irreversible release. Stricter than TryBuy's any-executable
        // check on purpose: a purchase refuses only when it would charge for
        // nothing, but a completion that releases the album and then fails to
        // award even one action would strand the run - so one unexecutable
        // action refuses the whole completion. A null slot is broken content
        // (boot validation reports it) and fails closed here.
        public bool CanExecuteActions()
        {
            if (_capstone == null)
                return false;

            foreach (var action in _capstone.Actions)
            {
                if (action == null || !action.CanExecute(_effects))
                    return false;
            }
            return true;
        }

        // The completion's own facts, in the order the operation promises: the
        // re-applicable state first, then the one-shots, then the latch. Setting
        // the declared flag is the LAST fact so nothing evaluating mid-grant can
        // observe a completed chapter whose awards have not landed - and the
        // operation owns the flag from the declaration; no payload carries a
        // copy (the importer refuses to author one).
        public void ExecuteCompletion()
        {
            _capstone.OnComplete?.Apply(_effects, ContentScope.PermanentInChapter);

            foreach (var action in _capstone.Actions)
                action?.Execute(_effects);

            _flags.Set(_capstone.CompletionFlagId);
        }

        // The rebuild half: whenever the declared flag is set, OnComplete
        // re-applies with permanent scope - the scope is not authored because
        // boot validation already requires the completion flag's declaration to
        // be permanent-in-chapter, and the projected state inherits the latch's
        // durability (rule 11). Ch1 authors no OnComplete, so this is wiring
        // until a later chapter authors one; without it, that chapter's
        // modifier would vanish at its first release.
        public void ProjectModifiers()
        {
            if (IsCompleted)
                _capstone.OnComplete?.Apply(_effects, ContentScope.PermanentInChapter);
        }

        public string FactSourceName => "capstone completion latch";
    }
}
