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

        // renders a roster resolved from the chapter, so it presents no single
        // definition and its section entry names none
        public ModuleDefinitionKind RequiredDefinition => ModuleDefinitionKind.None;

        public void Initialize(ChapterContext context, string definitionId)
        {
            _context = context;

            foreach (var upgrade in context.Economy.Upgrades.All)
            {
                if (upgrade.Definition.Type != UpgradeType.Buff)
                    continue;

                var row = Instantiate(_rowPrefab, _listRoot);
                row.Bind(context, upgrade);
                _rows.Add(row);
            }

            // One subscription for availability, mirroring ChapterScreen: a buff
            // gate is any Condition, so every condition input moves availability
            // and the settled context is the one signal that covers all of them.
            // UpgradeApplied and UpgradeCleared stay separate because they are row
            // lifecycle rather than a gate: a row hides once bought and comes back
            // when the album release drops the latch.
            context.Economy.Conditions.Settled += HandleConditionsSettled;
            context.Economy.Upgrades.UpgradeApplied += HandleUpgradeApplied;
            context.Economy.Upgrades.UpgradeCleared += HandleUpgradeCleared;
        }

        private void OnDestroy()
        {
            if (_context == null)
                return;

            if (_context.Economy.Conditions != null)
                _context.Economy.Conditions.Settled -= HandleConditionsSettled;
            if (_context.Economy.Upgrades != null)
            {
                _context.Economy.Upgrades.UpgradeApplied -= HandleUpgradeApplied;
                _context.Economy.Upgrades.UpgradeCleared -= HandleUpgradeCleared;
            }
        }

        private void HandleConditionsSettled() => RefreshRows();

        private void HandleUpgradeApplied(Upgrade upgrade) => RefreshRows();

        private void HandleUpgradeCleared(Upgrade upgrade) => RefreshRows();

        private void RefreshRows()
        {
            foreach (var row in _rows)
                row.Refresh();
        }
    }
}
