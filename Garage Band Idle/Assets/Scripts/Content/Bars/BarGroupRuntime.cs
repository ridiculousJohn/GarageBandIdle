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
        private readonly RewardContext _rewardContext;

        protected readonly CurrencyManager Currencies;

        public BarGroupDefinition Group { get; }
        public IReadOnlyList<BarState> Bars => _bars;

        public event Action<BarState> ProgressChanged;
        public event Action<BarState> Completed;

        protected BarGroupRuntime(BarGroupDefinition group, List<BarState> bars,
            CurrencyManager currencies, RewardManager rewards, RewardContext rewardContext)
        {
            Group = group;
            _bars = bars;
            Currencies = currencies;
            _rewards = rewards;
            _rewardContext = rewardContext;
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

        protected BarState FindBar(string barId)
            => _bars.Find(bar => bar.Definition.Id == barId);

        protected void RaiseProgressChanged(BarState bar)
            => ProgressChanged?.Invoke(bar);

        // latches the bar, applies its pool reward exactly once, and notifies.
        // OnBarCompleted runs between the latch and the notifications so the
        // mode can settle its own state (e.g. clear a selection) before
        // listeners observe the completion.
        protected void Complete(BarState bar)
        {
            if (bar.Completed)
                return;

            bar.Completed = true;
            OnBarCompleted(bar);

            if (!string.IsNullOrEmpty(bar.Definition.RewardId))
                _rewards.Apply(bar.Definition.RewardId, _rewardContext);

            Completed?.Invoke(bar);
        }

        protected virtual void OnBarCompleted(BarState bar) { }
    }
}
