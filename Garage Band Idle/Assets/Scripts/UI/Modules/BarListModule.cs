using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using TMPro;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: the fillable-bar list. Instantiates one BarRowUI per bar across
    // the chapter's bar groups (the count is content-driven) and shows the fill
    // currency readout. Rows of a group stay hidden until its reveal flag sets;
    // in Chapter 1 the hosting section gates on the same flag, but a later
    // chapter can put two groups with different flags in one section.
    public class BarListModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private RectTransform _listRoot;
        [SerializeField] private BarRowUI _rowPrefab;
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private TextMeshProUGUI _poolLabel;

        private ChapterContext _context;
        private readonly List<BarRowUI> _rows = new();

        // the pool readout derives from the bars on display: each distinct
        // fill currency, in bar order, tagged with the reveal flags of the
        // groups that fill from it. A currency renders only while at least
        // one owning group is revealed, so a hidden group can't leak its
        // pool ahead of its flag.
        private class PoolEntry
        {
            public string CurrencyId;
            public readonly List<string> RevealFlagIds = new();
        }

        private readonly List<PoolEntry> _pools = new();

        public void Initialize(ChapterContext context)
        {
            _context = context;
            var bars = context.Game.Bars;

            foreach (var group in bars.Groups)
            {
                foreach (var bar in bars.GetBars(group.Id))
                {
                    var row = Instantiate(_rowPrefab, _listRoot);
                    row.Bind(context.Game, bar);
                    row.gameObject.SetActive(context.Flags.IsSet(group.RevealFlagId));
                    _rows.Add(row);

                    var pool = _pools.Find(p => p.CurrencyId == bar.Definition.FillCurrencyId);
                    if (pool == null)
                        _pools.Add(pool = new PoolEntry { CurrencyId = bar.Definition.FillCurrencyId });
                    if (!pool.RevealFlagIds.Contains(group.RevealFlagId))
                        pool.RevealFlagIds.Add(group.RevealFlagId);
                }
            }

            // the title names the first group; a multi-group chapter gets
            // per-group headers when one exists to design for
            if (bars.Groups.Count > 0)
                _titleLabel.text = bars.Groups[0].DisplayName;

            bars.BarProgressChanged += HandleBarChanged;
            bars.BarCompleted += HandleBarChanged;
            bars.ActiveBarChanged += HandleActiveBarChanged;
            context.Game.Currencies.BalanceChanged += HandleBalanceChanged;
            context.Flags.FlagSet += HandleFlagSet;

            RefreshPool();
        }

        private void OnDestroy()
        {
            if (_context == null)
                return;

            _context.Game.Bars.BarProgressChanged -= HandleBarChanged;
            _context.Game.Bars.BarCompleted -= HandleBarChanged;
            _context.Game.Bars.ActiveBarChanged -= HandleActiveBarChanged;
            _context.Game.Currencies.BalanceChanged -= HandleBalanceChanged;
            _context.Flags.FlagSet -= HandleFlagSet;
        }

        private void HandleBarChanged(BarSystem.BarState bar)
        {
            foreach (var row in _rows)
            {
                if (row.Bar == bar)
                    row.Refresh();
            }
        }

        // selection moved: the old and new target both need their labels redrawn
        private void HandleActiveBarChanged(string groupId)
        {
            foreach (var row in _rows)
            {
                if (row.Bar.Group.Id == groupId)
                    row.Refresh();
            }
        }

        private void HandleBalanceChanged(string currencyId, BigNumber balance)
        {
            foreach (var pool in _pools)
            {
                if (pool.CurrencyId == currencyId && IsRevealed(pool))
                {
                    RefreshPool();
                    return;
                }
            }
        }

        private void HandleFlagSet(string flagId)
        {
            foreach (var row in _rows)
            {
                if (row.Bar.Group.RevealFlagId == flagId)
                    row.gameObject.SetActive(true);
            }
            RefreshPool();
        }

        private bool IsRevealed(PoolEntry pool)
        {
            foreach (var flagId in pool.RevealFlagIds)
            {
                if (_context.Flags.IsSet(flagId))
                    return true;
            }
            return false;
        }

        // the fill currency readout lives here rather than the currency header;
        // the playable pass (slice 10) makes the header data-driven. One line
        // per revealed fill currency; any earn-configured currency carries its
        // earn rates (the currency owns its earn config).
        private void RefreshPool()
        {
            var lines = new List<string>(_pools.Count);
            var earn = _context.Game.EngagementEarn;
            foreach (var pool in _pools)
            {
                if (!IsRevealed(pool))
                    continue;

                // an unresolvable id is a content error GetDefinition already
                // reported; the readout skips it rather than dying
                var definition = _context.Game.Currencies.GetDefinition(pool.CurrencyId);
                if (definition == null)
                    continue;

                var line = $"{definition.DisplayName}: {NumberFormatter.Format(_context.Game.Currencies.Get(pool.CurrencyId))}";
                if (earn.HasEarn(pool.CurrencyId))
                    line += $" (+{NumberFormatter.Format(earn.RatePerSecond(pool.CurrencyId))}/sec, +{NumberFormatter.Format(earn.PerTap(pool.CurrencyId))}/tap)";
                lines.Add(line);
            }
            _poolLabel.text = string.Join("\n", lines);
        }
    }
}
