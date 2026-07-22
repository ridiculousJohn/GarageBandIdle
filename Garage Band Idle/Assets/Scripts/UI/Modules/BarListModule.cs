using System;
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
    // This module presents PerBarContinuousRuntime groups; the bind-time type
    // check is content-pairing validation (a group with a different fill
    // behavior ships its own module prefab), not runtime mode dispatch.
    public class BarListModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private RectTransform _listRoot;
        [SerializeField] private BarRowUI _rowPrefab;
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private TextMeshProUGUI _poolLabel;

        private ChapterContext _context;
        private readonly List<BarRowUI> _rows = new();
        private readonly List<(PerBarContinuousRuntime runtime, Action handler)> _selectionHandlers = new();

        public void Initialize(ChapterContext context)
        {
            _context = context;
            var bars = context.Game.Bars;

            foreach (var group in bars.Groups)
            {
                if (bars.GetRuntime(group.Id) is not PerBarContinuousRuntime runtime)
                {
                    Debug.LogError($"BarListModule: bar group '{group.Id}' does not use PerBarContinuousFill; this module cannot present it.");
                    continue;
                }

                foreach (var bar in runtime.Bars)
                {
                    var row = Instantiate(_rowPrefab, _listRoot);
                    row.Bind(context.Game, runtime, bar);
                    row.gameObject.SetActive(context.Flags.IsSet(group.RevealFlagId));
                    _rows.Add(row);
                }

                // selection moved: the old and new target both need their labels
                // redrawn, so the whole group's rows refresh
                var groupId = group.Id;
                Action handler = () => RefreshGroupRows(groupId);
                runtime.ActiveBarChanged += handler;
                _selectionHandlers.Add((runtime, handler));
            }

            // the title names the first group; a multi-group chapter gets
            // per-group headers when one exists to design for
            if (bars.Groups.Count > 0)
                _titleLabel.text = bars.Groups[0].DisplayName;

            bars.BarProgressChanged += HandleBarChanged;
            bars.BarCompleted += HandleBarChanged;
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
            foreach (var (runtime, handler) in _selectionHandlers)
                runtime.ActiveBarChanged -= handler;
            _context.Game.Currencies.BalanceChanged -= HandleBalanceChanged;
            _context.Flags.FlagSet -= HandleFlagSet;
        }

        private void HandleBarChanged(BarState bar)
        {
            foreach (var row in _rows)
            {
                if (row.Bar == bar)
                    row.Refresh();
            }
        }

        private void RefreshGroupRows(string groupId)
        {
            foreach (var row in _rows)
            {
                if (row.Bar.Group.Id == groupId)
                    row.Refresh();
            }
        }

        private void HandleBalanceChanged(string currencyId, BigNumber balance)
        {
            if (currencyId == _context.Game.Rehearsal.CurrencyId)
                RefreshPool();
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

        // the fill currency readout lives here rather than the currency header;
        // the playable pass (slice 10) makes the header data-driven
        private void RefreshPool()
        {
            var rehearsal = _context.Game.Rehearsal;
            if (!rehearsal.Configured)
            {
                _poolLabel.text = "";
                return;
            }

            var definition = _context.Game.Currencies.GetDefinition(rehearsal.CurrencyId);
            _poolLabel.text = $"{definition.DisplayName}: {NumberFormatter.Format(_context.Game.Currencies.Get(rehearsal.CurrencyId))}" +
                $" (+{NumberFormatter.Format(rehearsal.RatePerSecond)}/sec, +{NumberFormatter.Format(_context.Chapter.Rehearsal.PointsPerTap)}/tap)";
        }
    }
}
