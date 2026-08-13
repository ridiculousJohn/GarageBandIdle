using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: the generator list. Instantiates one GeneratorRowUI per chapter
    // generator (the count is content-driven) and sets each row's visibility
    // from a live read of its unlock condition every time conditions settle -
    // the same way ChapterScreen decides a section's visibility. Nothing
    // latches, so a row disappears again when its gate stops holding (a
    // release zeroes the fleet an ownedCount unlock reads).
    public class GeneratorListModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private RectTransform _listRoot;
        [SerializeField] private GeneratorRowUI _rowPrefab;

        private ChapterContext _context;
        private readonly List<GeneratorRowUI> _rows = new();

        // renders a roster resolved from the chapter, so it presents no single
        // definition and its section entry names none
        public ModuleDefinitionKind RequiredDefinition => ModuleDefinitionKind.None;

        public void Initialize(ChapterContext context, string definitionId)
        {
            _context = context;

            foreach (var generator in context.Economy.Generators.All)
            {
                var row = Instantiate(_rowPrefab, _listRoot);
                row.Bind(context, generator);
                _rows.Add(row);
            }

            RefreshVisibility();

            context.Economy.Currencies.BalanceChanged += HandleBalanceChanged;
            context.Economy.Conditions.Settled += RefreshVisibility;

            // the third system signal a row's display reads: output modifiers.
            // Subscribed here rather than per row because Changed is a system
            // event carrying its target, the same shape as BalanceChanged -
            // each row decides whether the target is its own.
            context.Economy.Modifiers.Changed += HandleModifierChanged;
        }

        private void OnDestroy()
        {
            if (_context == null)
                return;

            _context.Economy.Currencies.BalanceChanged -= HandleBalanceChanged;
            _context.Economy.Conditions.Settled -= RefreshVisibility;
            _context.Economy.Modifiers.Changed -= HandleModifierChanged;
        }

        private void HandleBalanceChanged(string currencyId, BigNumber balance)
        {
            foreach (var row in _rows)
                row.HandleBalanceChanged(currencyId);
        }

        private void HandleModifierChanged(ModifierSelector selector)
        {
            foreach (var row in _rows)
                row.HandleModifierChanged(selector);
        }

        private void RefreshVisibility()
        {
            var conditions = _context.Economy.Conditions;
            foreach (var row in _rows)
                row.SetVisible(row.Generator.IsUnlocked(conditions));
        }
    }
}
