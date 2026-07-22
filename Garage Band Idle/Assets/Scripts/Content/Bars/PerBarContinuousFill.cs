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
            CurrencyManager currencies, RewardManager rewards, RewardContext rewardContext)
            => new PerBarContinuousRuntime(group, bars, currencies, rewards, rewardContext);

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
            CurrencyManager currencies, RewardManager rewards, RewardContext rewardContext)
            : base(group, bars, currencies, rewards, rewardContext) { }

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
        // (RehearsalSystem), so ordering only affects latency by one tick.
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

            Currencies.Add(bar.Definition.FillCurrencyId, -transfer);
            bar.Progress += transfer;
            RaiseProgressChanged(bar);

            if (bar.Remaining <= BigNumber.Zero)
                Complete(bar);
        }

        protected override void OnBarCompleted(BarState bar)
        {
            if (_activeBar != bar)
                return;

            _activeBar = null;
            ActiveBarChanged?.Invoke();
        }
    }
}
