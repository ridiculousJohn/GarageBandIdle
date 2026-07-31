using System;
using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // Runtime handler for one bar group, created by the group's BarFillBehavior.
    // The base surface is only what a generic host needs: tick, the bar states,
    // completed counts, and change notifications. Everything mode-shaped (what
    // the fill currency pours into, when, and how) lives in the subclass; UI
    // that presents a specific mode binds against the concrete runtime.
    public abstract class BarGroupRuntime
    {
        private readonly List<BarState> _bars;
        private readonly RewardManager _rewards;
        private readonly EffectContext _effectContext;

        protected readonly CurrencyManager Currencies;

        public BarGroupDefinition Group { get; }
        public IReadOnlyList<BarState> Bars => _bars;

        public event Action<BarState> ProgressChanged;
        public event Action<BarState> Completed;

        protected BarGroupRuntime(BarGroupDefinition group, List<BarState> bars,
            CurrencyManager currencies, RewardManager rewards, EffectContext effectContext)
        {
            Group = group;
            _bars = bars;
            Currencies = currencies;
            _rewards = rewards;
            _effectContext = effectContext;
        }

        public abstract void Tick();

        // completed bars in the group this run, for barsCompleted conditions
        public int CompletedCount()
        {
            var count = 0;
            foreach (var bar in _bars)
            {
                if (bar.Completed)
                    count++;
            }
            return count;
        }

        protected internal BarState FindBar(string barId)
            => _bars.Find(bar => bar.Definition.Id == barId);

        protected void RaiseProgressChanged(BarState bar)
            => ProgressChanged?.Invoke(bar);

        // the occurrence side of a completion: after the mode latched it
        // through BarState.AddProgress and settled its own state, applies the
        // bar's pool reward exactly once and notifies. Only live accrual takes
        // this path - a restored completion is recorded fact, not an
        // occurrence, so BarSystem.RestoreProgress never calls it.
        protected void NotifyCompleted(BarState bar)
        {
            // the group's scope is the reward's lifetime: run-scoped bars reset each
            // demo, so what they granted must clear with them. One declaration, so a
            // reward asset and the content that pays it can never disagree about how
            // long the effect lives.
            if (!string.IsNullOrEmpty(bar.Definition.RewardId))
                _rewards.Apply(bar.Definition.RewardId, _effectContext, Group.Scope);

            Completed?.Invoke(bar);
        }

        // Mode-state reconciliation hooks for the host's atomic transitions
        // (BarSystem.ResetRunScopedGroups, BarSystem.RestoreProgress). The
        // host re-establishes every bar's state first, then the mode settles
        // whatever it holds on top (e.g. a selection); each returns whether
        // mode state changed. Notification is deferred - the host calls
        // NotifyModeStateChanged only after ALL groups have settled (state,
        // then notify), so no subscriber ever observes a half-applied
        // transition.
        internal virtual bool ReconcileAfterRunReset() => false;
        internal virtual bool ReconcileAfterRestore() => false;
        internal virtual void NotifyModeStateChanged() { }
    }
}
