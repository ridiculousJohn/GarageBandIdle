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
        // REPLACEMENT, not a merge: the walk is over every group this chapter
        // declares rather than over the snapshot's keys, so a bar the snapshot
        // omits is restored to zero progress instead of keeping whatever it held.
        // That is what makes a new run's empty seed and a load the same operation,
        // and it is why every group reconciles afterwards - a group whose bars were
        // all zeroed has a selection to settle just as much as one that was filled.
        //
        // notify: false defers publication to the context-wide restore (see
        // Scope.Restore); the default is unchanged for every existing
        // caller.
        public void RestoreProgress(IReadOnlyDictionary<string, IReadOnlyDictionary<string, BigNumber>> progressByGroupAndBarId,
            bool notify = true)
        {
            if (progressByGroupAndBarId == null)
            {
                Debug.LogError("BarSystem: RestoreProgress with no saved progress.");
                return;
            }

            var restoredBars = new List<BarState>();
            var changedModes = new List<BarGroupRuntime>();

            foreach (var definition in _groupOrder)
            {
                var runtime = _groups[definition.Id];
                progressByGroupAndBarId.TryGetValue(definition.Id, out var savedBars);

                foreach (var bar in runtime.Bars)
                {
                    var progress = BigNumber.Zero;
                    if (savedBars != null && savedBars.TryGetValue(bar.Definition.Id, out var saved))
                        progress = saved;

                    // nothing to say about a bar that was already where the
                    // snapshot puts it, which keeps the notification set to what
                    // actually moved
                    if (bar.Progress == progress)
                        continue;

                    bar.RestoreProgress(progress);
                    restoredBars.Add(bar);
                }

                if (runtime.ReconcileAfterRestore())
                    changedModes.Add(runtime);
            }

            // stale saved state naming content this chapter no longer declares.
            // Reported after the restore rather than instead of it: the ids that DO
            // resolve are still restored, and a rename should not cost the player
            // every other bar's progress.
            foreach (var groupEntry in progressByGroupAndBarId)
            {
                if (!_groups.TryGetValue(groupEntry.Key ?? "", out var runtime))
                {
                    Debug.LogError($"BarSystem: RestoreProgress with unknown bar group id '{groupEntry.Key}'. Skipping it.");
                    continue;
                }
                if (groupEntry.Value == null)
                {
                    Debug.LogError($"BarSystem: RestoreProgress with no saved bars for group '{groupEntry.Key}'.");
                    continue;
                }
                foreach (var barEntry in groupEntry.Value)
                {
                    if (runtime.FindBar(barEntry.Key) == null)
                        Debug.LogError($"BarSystem: RestoreProgress with unknown bar id '{barEntry.Key}' in group '{groupEntry.Key}'. Skipping it.");
                }
            }

            if (!notify)
                return;

            foreach (var bar in restoredBars)
                BarProgressChanged?.Invoke(bar);
            foreach (var runtime in changedModes)
                runtime.NotifyModeStateChanged();
        }

        // Re-announces every bar's progress and every group's mode state. The
        // notification half of a silent restore: both events carry current state
        // rather than a delta, so a total replay is a full refresh - which is what a
        // restore is, and cheaper to reason about than a computed change set that
        // has to stay in step with the restore above.
        //
        // BarCompleted is deliberately NOT among them. It is the occurrence signal
        // for a bar finishing, and a restored completion is recorded fact, not an
        // occurrence - the same distinction ProjectCompletedRewards is built on.
        public void RepublishAll()
        {
            foreach (var definition in _groupOrder)
            {
                var runtime = _groups[definition.Id];
                foreach (var bar in runtime.Bars)
                    BarProgressChanged?.Invoke(bar);
                runtime.NotifyModeStateChanged();
            }
        }

        // Re-establishes each group's saved selection, after RestoreProgress has
        // cleared whatever was selected before. Two calls rather than one parameter
        // on RestoreProgress because the two are separable facts - progress is what
        // was earned, a selection is what the player chose - and a mode with no
        // selection ignores this entirely.
        //
        // REPLACEMENT: a group the snapshot does not name stays unselected, since
        // RestoreProgress dropped its selection unconditionally.
        public void RestoreActiveBars(IReadOnlyDictionary<string, string> activeBarByGroup, bool notify = true)
        {
            if (activeBarByGroup == null)
            {
                Debug.LogError("BarSystem: RestoreActiveBars with no saved selections. Ignoring.");
                return;
            }

            List<BarGroupRuntime> changed = null;
            foreach (var entry in activeBarByGroup)
            {
                if (!_groups.TryGetValue(entry.Key ?? "", out var runtime))
                {
                    Debug.LogError($"BarSystem: RestoreActiveBars with unknown bar group id '{entry.Key}'. Skipping it.");
                    continue;
                }

                if (runtime.RestoreActiveBar(entry.Value))
                    (changed ??= new List<BarGroupRuntime>()).Add(runtime);
            }

            if (!notify || changed == null)
                return;

            foreach (var runtime in changed)
                runtime.NotifyModeStateChanged();
        }

        // Each group's selection for a capture; a group with none is absent.
        public IReadOnlyDictionary<string, string> CaptureActiveBars()
        {
            var byGroup = new Dictionary<string, string>();
            foreach (var definition in _groupOrder)
            {
                var barId = _groups[definition.Id].CaptureActiveBarId();
                if (!string.IsNullOrEmpty(barId))
                    byGroup.Add(definition.Id, barId);
            }
            return byGroup;
        }

        // Bar progress for a capture, group by group in declaration order. Only
        // non-zero progress is recorded, since zero is what an absent entry
        // restores to.
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, BigNumber>> CaptureProgress()
        {
            var byGroup = new Dictionary<string, IReadOnlyDictionary<string, BigNumber>>();
            foreach (var definition in _groupOrder)
            {
                var bars = new Dictionary<string, BigNumber>();
                foreach (var bar in _groups[definition.Id].Bars)
                {
                    if (bar.Progress > BigNumber.Zero)
                        bars.Add(bar.Definition.Id, bar.Progress);
                }

                if (bars.Count > 0)
                    byGroup.Add(definition.Id, bars);
            }
            return byGroup;
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
