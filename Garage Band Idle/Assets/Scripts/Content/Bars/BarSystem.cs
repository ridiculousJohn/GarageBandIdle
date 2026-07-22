using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // The generic fillable-bar host (design doc sections 3 and 6). Every bar
    // declares its own fillCurrency, so the transfer logic works for any
    // currency - Learn Covers is just the Chapter 1 instance. Fill behavior is
    // polymorphic: each group's BarFillBehavior creates the runtime that owns
    // its state, and this host only resolves content, routes ticks, aggregates
    // change notifications, and feeds barsCompleted conditions via
    // IBarCompletionSource. Nothing here inspects a fill mode; mode-specific
    // callers (UI) take the concrete runtime from GetRuntime.
    public class BarSystem : IBarCompletionSource
    {
        private readonly Dictionary<string, BarGroupRuntime> _groups = new();
        private readonly List<BarGroupDefinition> _groupOrder = new();

        // UI listens here, nothing polls
        public event Action<BarState> BarProgressChanged;
        public event Action<BarState> BarCompleted;

        public BarSystem(IReadOnlyList<BarGroupDefinition> groups, IEnumerable<BarDefinition> bars,
            CurrencyManager currencies, RewardManager rewards, RewardContext rewardContext)
        {
            var barsById = new Dictionary<string, BarDefinition>();
            foreach (var bar in bars)
            {
                if (bar != null && !barsById.TryAdd(bar.Id, bar))
                    Debug.LogError($"BarSystem: duplicate bar id '{bar.Id}'. Keeping the first.");
            }

            foreach (var group in groups)
            {
                if (group.FillBehavior == null)
                {
                    Debug.LogError($"BarSystem: bar group '{group.Id}' has no fill behavior. Skipping it.");
                    continue;
                }

                var states = new List<BarState>();
                foreach (var barId in group.BarIds)
                {
                    if (barsById.TryGetValue(barId, out var bar))
                        states.Add(new BarState(bar, group));
                    else
                        Debug.LogError($"BarSystem: bar group '{group.Id}' references unknown bar id '{barId}'.");
                }

                var runtime = group.FillBehavior.CreateRuntime(group, states, currencies, rewards, rewardContext);
                if (_groups.TryAdd(group.Id, runtime))
                {
                    _groupOrder.Add(group);
                    runtime.ProgressChanged += HandleProgressChanged;
                    runtime.Completed += HandleCompleted;
                }
                else
                {
                    Debug.LogError($"BarSystem: duplicate bar group id '{group.Id}'. Keeping the first.");
                }
            }
        }

        // the chapter's bar groups in declaration order, for UI layout
        public IReadOnlyList<BarGroupDefinition> Groups => _groupOrder;

        // the group's mode-specific handler; callers that need more than the
        // base surface (selection etc.) take the concrete runtime type
        public BarGroupRuntime GetRuntime(string groupId)
            => TryGetGroup(groupId, out var runtime) ? runtime : null;

        public IReadOnlyList<BarState> GetBars(string groupId)
            => TryGetGroup(groupId, out var runtime) ? runtime.Bars : Array.Empty<BarState>();

        public void Tick()
        {
            foreach (var runtime in _groups.Values)
                runtime.Tick();
        }

        // completed bars in the group this run, for barsCompleted conditions
        public int CompletedCount(string groupId)
            => TryGetGroup(groupId, out var runtime) ? runtime.CompletedCount() : 0;

        private void HandleProgressChanged(BarState bar) => BarProgressChanged?.Invoke(bar);

        private void HandleCompleted(BarState bar) => BarCompleted?.Invoke(bar);

        private bool TryGetGroup(string groupId, out BarGroupRuntime runtime)
        {
            if (!string.IsNullOrEmpty(groupId) && _groups.TryGetValue(groupId, out runtime))
                return true;

            Debug.LogError($"BarSystem: unknown bar group id '{groupId}'.");
            runtime = null;
            return false;
        }
    }
}
