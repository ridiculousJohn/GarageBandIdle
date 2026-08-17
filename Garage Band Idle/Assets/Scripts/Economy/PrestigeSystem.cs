using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime home of one scope's prestige rungs (design doc rules 12 and 14).
    // A completed rung is a fact source like any latch, and the declared
    // completion flag IS the latch - there is no state here beyond what the
    // flag registry already holds, so a restore that brings the flag back
    // brings the completion back with no second copy to disagree.
    //
    // The press itself lives on Scope (the operation spans scopes; this system
    // knows only its own). What lives here is everything that is a fact of ONE
    // scope's ladder: rung lookup, completion, per-rung preflight and
    // execution against this scope's own effect context - a rung's actions
    // read and pay through the scope the rung is filed on, which is what lets
    // a rung bank outward without its parent being the recipient.
    public class PrestigeSystem : IModifierFactSource
    {
        private readonly List<PrestigeTierDefinition> _rungs = new();
        private readonly Dictionary<string, PrestigeTierDefinition> _byId = new();
        private readonly FlagSystem _flags;
        private readonly EffectContext _effects;

        public IReadOnlyList<PrestigeTierDefinition> Rungs => _rungs;

        // Takes a list even though a scope definition authors at most one rung:
        // the pre-step-7 chapter path files BOTH legacy rungs (album, capstone)
        // on its single scope, and the machinery must not care which source fed
        // it. Duplicate ids within one scope are refused - PrestigeModule
        // resolves by id, and two claimants would make every press ambiguous.
        public PrestigeSystem(IEnumerable<PrestigeTierDefinition> rungs, FlagSystem flags, EffectContext effects)
        {
            _flags = flags;
            _effects = effects;

            if (rungs == null)
                return;

            foreach (var rung in rungs)
            {
                if (rung == null || !rung.IsAuthored)
                    continue;

                if (_byId.ContainsKey(rung.Id))
                {
                    Debug.LogError($"PrestigeSystem: duplicate rung id '{rung.Id}' on one scope - a press resolves by id, and two claimants make it ambiguous. Keeping the first.");
                    continue;
                }

                _rungs.Add(rung);
                _byId.Add(rung.Id, rung);
            }
        }

        // silent lookup: an unknown id is the CALLER's content mistake to report
        // with the operation named
        public bool TryGet(string rungId, out PrestigeTierDefinition rung)
            => _byId.TryGetValue(rungId ?? "", out rung);

        // Completion is the declared latch flag, read live. A rung with no
        // latch can never read as completed - it is repeatable (the album),
        // and answering false is what keeps it offerable forever.
        public bool IsCompleted(PrestigeTierDefinition rung)
            => rung != null && rung.HasLatch && _flags.IsSet(rung.CompletionLatch.FlagId);

        // Whether every action of this rung would execute, asked by the press
        // BEFORE anything irreversible. One unexecutable action refuses the
        // whole press (the capstone's stranding rule, now per rung): a null
        // slot is broken content (boot validation reports it) and fails closed.
        // The latch is deliberately included when authored - it sits outside
        // Actions and runs last, and a latch that cannot execute after every
        // payout has landed is the exact stranding this preflight prevents.
        public bool CanExecuteActions(PrestigeTierDefinition rung)
        {
            if (rung == null)
                return false;

            foreach (var action in rung.Actions)
            {
                if (action == null || !action.CanExecute(_effects))
                    return false;
            }

            return !rung.HasLatch || rung.CompletionLatch.CanExecute(_effects);
        }

        // the rung's one-shots, in authored order, against THIS scope's effect
        // context - never the latch, which the press runs separately and last
        public void ExecuteActions(PrestigeTierDefinition rung)
        {
            foreach (var action in rung.Actions)
                action?.Execute(_effects);
        }

        // the latch, from the slot and nowhere else - the last fact of a press,
        // so nothing evaluating mid-press observes a completed rung whose
        // awards have not landed
        public void ExecuteLatch(PrestigeTierDefinition rung)
        {
            if (rung.HasLatch)
                rung.CompletionLatch.Execute(_effects);
        }

        // What one grant action of this rung would pay, for the preview: the
        // same Evaluate the press runs, but READING through the surface the
        // caller supplies - the preview walks the press's plan with an overlay
        // that carries the earlier planned grants, because a later formula
        // measures what the earlier actions have already banked, and a preview
        // reading original balances would promise a different number than the
        // press pays. Flags and modifiers stay this scope's own; only the
        // balance surface is the caller's to shift. Non-grant actions answer
        // false, as does a broken or negative grant (the press's preflight is
        // where those refuse loudly).
        public bool TryPendingGrant(GameAction action, ICurrencies read, out string currencyId, out BigNumber amount)
        {
            switch (action)
            {
                case GrantComputedCurrencyAction computed when computed.Amount != null:
                    currencyId = computed.CurrencyId;
                    amount = computed.Amount.Evaluate(new EffectContext(read, _effects.Flags, _effects.Modifiers));
                    if (amount < BigNumber.Zero)
                        amount = BigNumber.Zero;
                    return !string.IsNullOrEmpty(currencyId);
                case GrantCurrencyAction flat when flat.Amount > 0:
                    currencyId = flat.CurrencyId;
                    amount = flat.Amount;
                    return !string.IsNullOrEmpty(currencyId);
                default:
                    currencyId = null;
                    amount = BigNumber.Zero;
                    return false;
            }
        }

        // The rebuild half (rule 6): whenever a rung's latch is set, its
        // onComplete re-applies with permanent scope - not authored per effect,
        // because boot validation requires the latch flag's declaration to be
        // permanent-in-chapter, and the projected state inherits the latch's
        // durability (rule 11). The press never executes onComplete; this is
        // the only door it enters through, which is what lets it survive a
        // release, a load and a reprojection unchanged.
        public void ProjectModifiers()
        {
            foreach (var rung in _rungs)
            {
                if (IsCompleted(rung))
                    rung.OnComplete?.Apply(_effects, ContentScope.PermanentInChapter);
            }
        }

        public string FactSourceName => "prestige completion latches";
    }
}
