using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // One prestige rung (design doc rules 12 and 14): the declaration a press
    // operation runs from, replacing AlbumConfig and CapstoneConfig as one
    // shape parameterized by data. Filed on the scope whose ladder it belongs
    // to - placement is part of the declaration, which is why this is an
    // embedded [Serializable] class and not a Definition asset with a scope
    // backref (two sources of truth for one filing).
    //
    // The gates differ by role. `offer` governs whether the rung is PRESENTED -
    // asked by the module, never by the press. `operationGate` governs whether
    // the press is LEGAL: null is ungated (the album - repeatable, harmless
    // anytime), set is fail-closed (the capstone - asked by the operation
    // itself, because a press that latches a permanent flag must not be
    // reachable through a row the player is merely still looking at).
    //
    // The completionLatch slot is typed to SetFlagAction CONCRETELY, so a
    // non-flag action in that slot is not authorable rather than validated
    // after the fact - and the target flag stays readable off the declaration
    // without executing anything, which the already-completed refusal needs.
    // An inline class cannot be null after deserialization, so "no latch" is a
    // latch with no flag id (HasLatch below), the same authored-or-not answer
    // CapstoneConfig.IsAuthored gives.
    //
    // onComplete is a GameEffect the projection re-applies FROM the latched
    // flag; the press never executes it. It therefore REQUIRES the latch:
    // without a flag there is nothing to project from, and the authored effect
    // would silently never exist (refused at import and at boot).
    //
    // The payout is one of the GameActions (a GrantComputedCurrencyAction),
    // never a field here: a rung that awards nothing has an empty list, so
    // there is no null payout to represent and no branch to write.
    [Serializable]
    public class PrestigeTierDefinition
    {
        [SerializeField]
        [Tooltip("Stable rung id, e.g. cut_demo / backyard_party. PrestigeModule resolves by it; unique within the owning scope.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Must hold for the rung to be OFFERED (the button enabled); none = always offered once revealed. Presentation only - never asked by the press.")]
        private Condition _offer;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Must hold for the press to be LEGAL. None = ungated (the album); set = fail-closed, asked by the operation itself (the capstone).")]
        private Condition _operationGate;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Re-applicable state completion grants, re-applied by projection FROM the latched flag - the press never executes it. Requires the latch.")]
        private GameEffect _onComplete;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("One-shot awards the press runs exactly once - the payout (a grantComputedCurrency action) among them. Empty = the rung awards nothing.")]
        private List<GameAction> _actions = new();

        [SerializeField]
        [Tooltip("The completion latch: one flag-setting action, run last, from this slot only. Leave the flag id empty for a repeatable rung (the album).")]
        private SetFlagAction _completionLatch = new();

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Which scopes the press clears - output closes downward. Self-and-contained for the album; the capstone names the tier scopes.")]
        private ResetTargetSelector _resetTargets;

        public string Id => _id;
        public string DisplayName => _displayName;
        public Condition Offer => _offer;
        public Condition OperationGate => _operationGate;
        public GameEffect OnComplete => _onComplete;
        public IReadOnlyList<GameAction> Actions => _actions;
        public SetFlagAction CompletionLatch => _completionLatch;
        public ResetTargetSelector ResetTargets => _resetTargets;

        // whether a latch is authored at all: an inline slot deserializes as an
        // instance regardless, so the flag id is the authored-or-not fact
        public bool HasLatch => _completionLatch != null && !string.IsNullOrEmpty(_completionLatch.FlagId);

        // whether this rung is authored at all, the same question
        // CapstoneConfig.IsAuthored answered: fixture scopes usually author none
        public bool IsAuthored => !string.IsNullOrEmpty(_id);

        public PrestigeTierDefinition() { }

#if UNITY_EDITOR
        // importer-only: rung declarations are generated from chapter JSON
        public PrestigeTierDefinition(string id, string displayName, Condition offer,
            Condition operationGate, GameEffect onComplete, List<GameAction> actions,
            SetFlagAction completionLatch, ResetTargetSelector resetTargets)
        {
            _id = id;
            _displayName = displayName;
            _offer = offer;
            _operationGate = operationGate;
            _onComplete = onComplete;
            _actions = actions ?? new List<GameAction>();
            _completionLatch = completionLatch ?? new SetFlagAction();
            _resetTargets = resetTargets;
        }
#endif
    }
}
