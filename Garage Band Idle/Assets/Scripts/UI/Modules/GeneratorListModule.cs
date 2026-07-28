using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: the generator list. Instantiates one GeneratorRowUI per chapter
    // generator (the count is content-driven); rows reveal as their generators
    // unlock.
    public class GeneratorListModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private RectTransform _listRoot;
        [SerializeField] private GeneratorRowUI _rowPrefab;

        private ChapterContext _context;
        private readonly List<GeneratorRowUI> _rows = new();

        public void Initialize(ChapterContext context)
        {
            _context = context;

            foreach (var generator in context.Game.Generators.All)
            {
                var row = Instantiate(_rowPrefab, _listRoot);
                row.Bind(context.Game, generator);
                _rows.Add(row);
            }

            context.Game.Currencies.BalanceChanged += HandleBalanceChanged;
            context.Game.Generators.GeneratorUnlocked += HandleGeneratorUnlocked;

            // the third system signal a row's display reads: output modifiers.
            // Subscribed here rather than per row because Changed is a system
            // event carrying its target, the same shape as BalanceChanged -
            // each row decides whether the target is its own.
            context.Game.Modifiers.Changed += HandleModifierChanged;
        }

        private void OnDestroy()
        {
            if (_context == null)
                return;

            _context.Game.Currencies.BalanceChanged -= HandleBalanceChanged;
            _context.Game.Generators.GeneratorUnlocked -= HandleGeneratorUnlocked;
            _context.Game.Modifiers.Changed -= HandleModifierChanged;
        }

        private void HandleBalanceChanged(string currencyId, BigNumber balance)
        {
            foreach (var row in _rows)
                row.HandleBalanceChanged(currencyId);
        }

        private void HandleModifierChanged(ModifierTargetKey target)
        {
            foreach (var row in _rows)
                row.HandleModifierChanged(target);
        }

        private void HandleGeneratorUnlocked(Generator generator)
        {
            foreach (var row in _rows)
            {
                if (row.Generator == generator)
                    row.Show();
            }
        }
    }
}
