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
        }

        private void OnDestroy()
        {
            if (_context == null)
                return;

            _context.Game.Currencies.BalanceChanged -= HandleBalanceChanged;
            _context.Game.Generators.GeneratorUnlocked -= HandleGeneratorUnlocked;
        }

        private void HandleBalanceChanged(string currencyId, BigNumber balance)
        {
            foreach (var row in _rows)
                row.HandleBalanceChanged(currencyId);
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
