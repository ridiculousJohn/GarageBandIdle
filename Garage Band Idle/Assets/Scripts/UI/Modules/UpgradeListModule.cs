using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: the buff list. One UpgradeRowUI per chapter buff (content unlocks
    // apply on their own gate and have no row). Rows appear and disappear on
    // their own availability, so this module only has to say when to re-ask.
    public class UpgradeListModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private RectTransform _listRoot;
        [SerializeField] private UpgradeRowUI _rowPrefab;

        private ChapterContext _context;
        private readonly List<UpgradeRowUI> _rows = new();

        public void Initialize(ChapterContext context)
        {
            _context = context;

            foreach (var upgrade in context.Game.Upgrades.All)
            {
                if (upgrade.Definition.Type != UpgradeType.Buff)
                    continue;

                var row = Instantiate(_rowPrefab, _listRoot);
                row.Bind(context.Game, upgrade);
                _rows.Add(row);
            }

            // one subscription per condition input a gate can read, mirroring
            // ChapterScreen: a buff gate is any Condition, so balances/earned
            // totals, flags, owned counts and completed bars all move
            // availability. UpgradeApplied hides a row once bought and
            // UpgradeCleared brings it back when the album release drops the latch.
            context.Game.Currencies.BalanceChanged += HandleBalanceChanged;
            context.Game.Flags.FlagSet += HandleFlagSet;
            context.Game.Generators.GeneratorOwnedChanged += HandleGeneratorOwnedChanged;
            context.Game.Bars.BarCompleted += HandleBarCompleted;
            context.Game.Upgrades.UpgradeApplied += HandleUpgradeApplied;
            context.Game.Upgrades.UpgradeCleared += HandleUpgradeCleared;
        }

        private void OnDestroy()
        {
            if (_context == null)
                return;

            _context.Game.Currencies.BalanceChanged -= HandleBalanceChanged;
            _context.Game.Flags.FlagSet -= HandleFlagSet;
            if (_context.Game.Generators != null)
                _context.Game.Generators.GeneratorOwnedChanged -= HandleGeneratorOwnedChanged;
            if (_context.Game.Bars != null)
                _context.Game.Bars.BarCompleted -= HandleBarCompleted;
            if (_context.Game.Upgrades != null)
            {
                _context.Game.Upgrades.UpgradeApplied -= HandleUpgradeApplied;
                _context.Game.Upgrades.UpgradeCleared -= HandleUpgradeCleared;
            }
        }

        private void HandleBalanceChanged(string currencyId, BigNumber balance) => RefreshRows();

        private void HandleFlagSet(string flagId) => RefreshRows();

        private void HandleGeneratorOwnedChanged(Generator generator) => RefreshRows();

        private void HandleBarCompleted(Content.BarState bar) => RefreshRows();

        private void HandleUpgradeApplied(Upgrade upgrade) => RefreshRows();

        private void HandleUpgradeCleared(Upgrade upgrade) => RefreshRows();

        private void RefreshRows()
        {
            foreach (var row in _rows)
                row.Refresh();
        }
    }
}
