using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // The Chapter 1 fill mode, JSON (fillMode "perBar", delivery "continuous"):
    // each bar accrues its OWN progress (never cumulative thresholds on one
    // counter), the player selects the group's active bar, and the shared fill
    // currency pool streams into it as it arrives. A tap-a-chunk or
    // dump-the-pool variant is a sibling BarFillBehavior carrying its own
    // parameters, not a switch in here.
    [Serializable]
    public class PerBarContinuousFill : BarFillBehavior
    {
        public override BarGroupRuntime CreateRuntime(BarGroupDefinition group, List<BarState> bars,
            ICurrencies currencies, RewardManager rewards, EffectContext effectContext)
            => new PerBarContinuousRuntime(group, bars, currencies, rewards, effectContext);

        // no authored fields, so nothing to resolve
        public override void Validate(ConditionContext context, string source) { }
    }

    // Runtime for PerBarContinuousFill. Selection is a standing prioritization
    // decision: the pool only holds a balance while nothing is selected, and
    // completion clears the target rather than auto-advancing (design doc
    // section 6 - which bar to work next is the player's call).
    public class PerBarContinuousRuntime : BarGroupRuntime
    {
        private BarState _activeBar;

        public BarState ActiveBar => _activeBar;

        public event Action ActiveBarChanged;

        public PerBarContinuousRuntime(BarGroupDefinition group, List<BarState> bars,
            ICurrencies currencies, RewardManager rewards, EffectContext effectContext)
            : base(group, bars, currencies, rewards, effectContext) { }

        // player-directed targeting: null bar id clears the selection and lets
        // the pool accumulate. Completed bars cannot be selected.
        public void SetActiveBar(string barId)
        {
            BarState target = null;
            if (!string.IsNullOrEmpty(barId))
            {
                target = FindBar(barId);
                if (target == null)
                {
                    Debug.LogError($"PerBarContinuousRuntime: SetActiveBar on unknown bar id '{barId}' in group '{Group.Id}'.");
                    return;
                }
                if (target.Completed)
                    return;
            }

            if (_activeBar == target)
                return;

            _activeBar = target;
            ActiveBarChanged?.Invoke();

            // pool built up while nothing was selected pours in immediately
            if (target != null)
                Drain();
        }

        // continuous delivery: each tick, whatever sits in the fill currency
        // pool moves into the active bar. Accrual itself happens elsewhere
        // (ProductionSystem), so ordering only affects latency by one tick.
        public override void Tick() => Drain();

        private void Drain()
        {
            var bar = _activeBar;
            if (bar == null || bar.Completed)
                return;

            var pool = Currencies.Get(bar.Definition.FillCurrencyId);
            var transfer = BigNumber.Min(pool, bar.Remaining);
            if (transfer <= BigNumber.Zero)
                return;

            // all bar state settles before the spend: Add fires BalanceChanged
            // synchronously, and no subscriber may ever observe the pool
            // drained with the progress or completion not yet recorded
            // (state, then notify)
            var completed = bar.AddProgress(transfer);

            // completion clears the selection rather than auto-advancing: which
            // bar to work next is the player's call (design doc section 6)
            if (completed)
                _activeBar = null;

            Currencies.Add(bar.Definition.FillCurrencyId, -transfer);
            RaiseProgressChanged(bar);

            if (!completed)
                return;

            ActiveBarChanged?.Invoke();
            NotifyCompleted(bar);
        }

        // run reset empties every bar, so a standing selection is a stale
        // prioritization decision - it clears unconditionally
        internal override bool ReconcileAfterRunReset()
        {
            if (_activeBar == null)
                return false;

            _activeBar = null;
            return true;
        }

        // A restore REPLACES, so the selection it replaces goes first -
        // unconditionally, exactly as a run reset drops it. Clearing only a
        // COMPLETED target (which is what this did) left an unrelated pre-restore
        // bar selected, and the next drain poured restored fill currency into it:
        // the snapshot decided every balance and every progress value, and then a
        // decision from before the restore decided where the currency went.
        //
        // Whatever selection the snapshot itself carries is re-established
        // afterwards by RestoreActiveBar, so a group ends up selected if and only if
        // the snapshot said so.
        internal override bool ReconcileAfterRestore()
        {
            if (_activeBar == null)
                return false;

            _activeBar = null;
            return true;
        }

        internal override string CaptureActiveBarId() => _activeBar?.Definition.Id;

        // Re-establishes a saved selection, silently. Refuses a completed target for
        // the reason the drain does: Drain must never hold a completed bar, so a
        // snapshot naming one (stale save data, or a bar whose requirement was
        // retuned downwards) leaves the group unselected rather than stuck.
        internal override bool RestoreActiveBar(string barId)
        {
            if (string.IsNullOrEmpty(barId))
                return false;

            var target = FindBar(barId);
            if (target == null)
            {
                Debug.LogError($"PerBarContinuousRuntime: RestoreActiveBar with unknown bar id '{barId}' in group '{Group.Id}'. Leaving the group unselected.");
                return false;
            }
            if (target.Completed)
                return false;

            if (_activeBar == target)
                return false;

            _activeBar = target;
            return true;
        }

        internal override void NotifyModeStateChanged() => ActiveBarChanged?.Invoke();
    }
}
