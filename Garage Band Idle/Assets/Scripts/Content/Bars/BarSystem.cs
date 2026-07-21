using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // The generic fillable-bar system (design doc sections 3 and 6). Every bar
    // declares its own fillCurrency, so the transfer logic works for any
    // currency - Learn Covers is just the Chapter 1 instance. PerBar fill mode:
    // each bar accrues its OWN progress by draining the shared pool, and the
    // player chooses the target (a standing prioritization decision). With
    // Continuous delivery the pool streams into the group's active bar every
    // tick; the pool only holds a balance while nothing is selected. Completion
    // latches, applies the bar's reward through the shared pool, and feeds
    // barsCompleted conditions via IBarCompletionSource.
    public class BarSystem : IBarCompletionSource
    {
        // runtime state for one bar; progress is spent fill currency, so it
        // never exceeds the requirement and never refunds
        public class BarState
        {
            public BarDefinition Definition { get; }
            public BarGroupDefinition Group { get; }
            public BigNumber Progress { get; internal set; }
            public bool Completed { get; internal set; }

            public BigNumber Remaining => (BigNumber)Definition.FillRequirement - Progress;

            public BarState(BarDefinition definition, BarGroupDefinition group)
            {
                Definition = definition;
                Group = group;
                Progress = BigNumber.Zero;
            }
        }

        private class GroupState
        {
            public BarGroupDefinition Definition;
            public readonly List<BarState> Bars = new();
            public BarState ActiveBar;
        }

        private readonly Dictionary<string, GroupState> _groups = new();
        private readonly List<BarGroupDefinition> _groupOrder = new();
        private readonly CurrencyManager _currencies;
        private readonly RewardManager _rewards;
        private readonly RewardContext _rewardContext;

        // UI listens here, nothing polls
        public event Action<BarState> BarProgressChanged;
        public event Action<BarState> BarCompleted;
        public event Action<string> ActiveBarChanged; // group id

        public BarSystem(IReadOnlyList<BarGroupDefinition> groups, IEnumerable<BarDefinition> bars,
            CurrencyManager currencies, RewardManager rewards, RewardContext rewardContext)
        {
            _currencies = currencies;
            _rewards = rewards;
            _rewardContext = rewardContext;

            var barsById = new Dictionary<string, BarDefinition>();
            foreach (var bar in bars)
            {
                if (bar != null && !barsById.TryAdd(bar.Id, bar))
                    Debug.LogError($"BarSystem: duplicate bar id '{bar.Id}'. Keeping the first.");
            }

            foreach (var group in groups)
            {
                var state = new GroupState { Definition = group };
                foreach (var barId in group.BarIds)
                {
                    if (barsById.TryGetValue(barId, out var bar))
                        state.Bars.Add(new BarState(bar, group));
                    else
                        Debug.LogError($"BarSystem: bar group '{group.Id}' references unknown bar id '{barId}'.");
                }

                if (_groups.TryAdd(group.Id, state))
                    _groupOrder.Add(group);
                else
                    Debug.LogError($"BarSystem: duplicate bar group id '{group.Id}'. Keeping the first.");
            }
        }

        // the chapter's bar groups in declaration order, for UI layout
        public IReadOnlyList<BarGroupDefinition> Groups => _groupOrder;

        public IReadOnlyList<BarState> GetBars(string groupId)
            => TryGetGroup(groupId, out var group) ? group.Bars : Array.Empty<BarState>();

        public BarState GetActiveBar(string groupId)
            => TryGetGroup(groupId, out var group) ? group.ActiveBar : null;

        // player-directed targeting: null bar id clears the selection and lets
        // the pool accumulate. Completed bars cannot be selected.
        public void SetActiveBar(string groupId, string barId)
        {
            if (!TryGetGroup(groupId, out var group))
                return;

            BarState target = null;
            if (!string.IsNullOrEmpty(barId))
            {
                target = group.Bars.Find(bar => bar.Definition.Id == barId);
                if (target == null)
                {
                    Debug.LogError($"BarSystem: SetActiveBar on unknown bar id '{barId}' in group '{groupId}'.");
                    return;
                }
                if (target.Completed)
                    return;
            }

            if (group.ActiveBar == target)
                return;

            group.ActiveBar = target;
            ActiveBarChanged?.Invoke(groupId);

            // pool built up while nothing was selected pours in immediately
            if (target != null && group.Definition.Delivery == BarFillDelivery.Continuous)
                Drain(group);
        }

        // continuous delivery: each tick, whatever sits in the fill currency
        // pool moves into the active bar. Accrual itself happens elsewhere
        // (RehearsalSystem), so ordering only affects latency by one tick.
        public void Tick()
        {
            foreach (var group in _groups.Values)
            {
                if (group.Definition.Delivery == BarFillDelivery.Continuous)
                    Drain(group);
            }
        }

        private void Drain(GroupState group)
        {
            var bar = group.ActiveBar;
            if (bar == null || bar.Completed)
                return;

            var pool = _currencies.Get(bar.Definition.FillCurrencyId);
            var transfer = BigNumber.Min(pool, bar.Remaining);
            if (transfer <= BigNumber.Zero)
                return;

            _currencies.Add(bar.Definition.FillCurrencyId, -transfer);
            bar.Progress += transfer;
            BarProgressChanged?.Invoke(bar);

            if (bar.Remaining <= BigNumber.Zero)
                Complete(group, bar);
        }

        private void Complete(GroupState group, BarState bar)
        {
            bar.Completed = true;

            // completion clears the selection rather than auto-advancing: which
            // bar to work next is the player's call (design doc section 6)
            if (group.ActiveBar == bar)
            {
                group.ActiveBar = null;
                ActiveBarChanged?.Invoke(group.Definition.Id);
            }

            if (!string.IsNullOrEmpty(bar.Definition.RewardId))
                _rewards.Apply(bar.Definition.RewardId, _rewardContext);

            BarCompleted?.Invoke(bar);
        }

        // completed bars in the group this run, for barsCompleted conditions
        public int CompletedCount(string groupId)
        {
            if (!TryGetGroup(groupId, out var group))
                return 0;

            var count = 0;
            foreach (var bar in group.Bars)
            {
                if (bar.Completed)
                    count++;
            }
            return count;
        }

        private bool TryGetGroup(string groupId, out GroupState group)
        {
            if (!string.IsNullOrEmpty(groupId) && _groups.TryGetValue(groupId, out group))
                return true;

            Debug.LogError($"BarSystem: unknown bar group id '{groupId}'.");
            group = null;
            return false;
        }
    }
}
