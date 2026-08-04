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
    // callers (UI) take the concrete runtime from GetRuntime. The mode-agnostic
    // state transitions - run reset and save restore - live here, because bar
    // progress is shared state: both settle every group completely before any
    // notification fires (state, then notify).
    public class BarSystem : IBarCompletionSource, Economy.IModifierFactSource
    {
        private readonly Dictionary<string, BarGroupRuntime> _groups = new();
        private readonly List<BarGroupDefinition> _groupOrder = new();

        // UI listens here, nothing polls
        public event Action<BarState> BarProgressChanged;
        public event Action<BarState> BarCompleted;

        public BarSystem(IReadOnlyList<BarGroupDefinition> groups, IEnumerable<BarDefinition> bars,
            ICurrencies currencies, RewardManager rewards, EffectContext effectContext)
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
                    if (!barsById.TryGetValue(barId, out var bar))
                    {
                        Debug.LogError($"BarSystem: bar group '{group.Id}' references unknown bar id '{barId}'.");
                        continue;
                    }

                    // fail closed on broken content: a non-positive requirement
                    // can never be legitimately filled - rejecting the bar means
                    // it can never satisfy a barsCompleted gate or grant its
                    // reward (the importer and boot validation report it)
                    if (bar.FillRequirement <= 0)
                    {
                        Debug.LogError($"BarSystem: bar '{bar.Id}' has a non-positive fill requirement ({bar.FillRequirement}). Skipping it.");
                        continue;
                    }

                    states.Add(new BarState(bar, group));
                }

                var runtime = group.FillBehavior.CreateRuntime(group, states, currencies, rewards, effectContext);
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

        // Run reset (album release, event baseline): every group whose
        // declared scope is Run returns to empty - no progress, nothing
        // completed, no mode state (selection etc.) - and
        // permanent-in-chapter groups are untouched. Nothing completes here,
        // so no reward applies and BarCompleted never fires. All state
        // settles before any notification (state, then notify).
        public void ResetRunScopedGroups()
        {
            var changedBars = new List<BarState>();
            var changedModes = new List<BarGroupRuntime>();

            foreach (var definition in _groupOrder)
            {
                if (definition.Scope != ContentScope.Run)
                    continue;

                var runtime = _groups[definition.Id];
                foreach (var bar in runtime.Bars)
                {
                    if (bar.ResetForRun())
                        changedBars.Add(bar);
                }
                if (runtime.ReconcileAfterRunReset())
                    changedModes.Add(runtime);
            }

            foreach (var bar in changedBars)
                BarProgressChanged?.Invoke(bar);
            foreach (var runtime in changedModes)
                runtime.NotifyModeStateChanged();
        }

        // Save/load: re-establishes saved progress as one atomic operation,
        // keyed by group then bar id. A restored completion is recorded fact,
        // not a new occurrence - no reward applies and BarCompleted does not
        // fire; the reward's own effects are restored by their owning
        // systems. Each mode reconciles its own state on top (a selection
        // left on a now-completed bar clears, exactly as completing it by
        // drain would). The complete snapshot settles - including that
        // reconciliation - before any notification fires (state, then
        // notify), so a subscriber never observes a partially restored
        // system. Unknown ids are stale save data: reported and skipped.
        public void RestoreProgress(IReadOnlyDictionary<string, IReadOnlyDictionary<string, BigNumber>> progressByGroupAndBarId)
        {
            if (progressByGroupAndBarId == null)
            {
                Debug.LogError("BarSystem: RestoreProgress with no saved progress.");
                return;
            }

            var restoredBars = new List<BarState>();
            var changedModes = new List<BarGroupRuntime>();

            foreach (var groupEntry in progressByGroupAndBarId)
            {
                if (!_groups.TryGetValue(groupEntry.Key ?? "", out var runtime))
                {
                    Debug.LogError($"BarSystem: RestoreProgress with unknown bar group id '{groupEntry.Key}'. Skipping it.");
                    continue;
                }
                if (groupEntry.Value == null)
                {
                    Debug.LogError($"BarSystem: RestoreProgress with no saved bars for group '{groupEntry.Key}'. Skipping it.");
                    continue;
                }

                foreach (var barEntry in groupEntry.Value)
                {
                    var bar = runtime.FindBar(barEntry.Key);
                    if (bar == null)
                    {
                        Debug.LogError($"BarSystem: RestoreProgress with unknown bar id '{barEntry.Key}' in group '{groupEntry.Key}'. Skipping it.");
                        continue;
                    }

                    bar.RestoreProgress(barEntry.Value);
                    restoredBars.Add(bar);
                }

                if (runtime.ReconcileAfterRestore())
                    changedModes.Add(runtime);
            }

            foreach (var bar in restoredBars)
                BarProgressChanged?.Invoke(bar);
            foreach (var runtime in changedModes)
                runtime.NotifyModeStateChanged();
        }

        public string FactSourceName => "completed bars";

        // The projection (design doc section 12, rule 6): every group re-applies
        // the rewards of the bars it currently records as completed. Group order
        // is the chapter's declaration order rather than dictionary order, so a
        // rebuild composes its grants in the same sequence every time - which
        // matters the moment two rewards target one stat with a mix of adds and
        // multiplies.
        public void ProjectModifiers()
        {
            foreach (var definition in _groupOrder)
                _groups[definition.Id].ProjectCompletedRewards();
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
