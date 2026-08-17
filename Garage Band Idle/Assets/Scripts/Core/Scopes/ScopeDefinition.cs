using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // One scope of the tree (design doc section 12, rule 12): the unit of
    // economy, lifetime, and presentation at once. A scope declares its truth -
    // the currencies, flags, and content whose facts it holds - what presents
    // that truth (its sections), and an ORDERED list of child scopes, the
    // ladder of section 1. A fact's lifetime is where it lives: the scope
    // holding it is the scope whose reset takes it, so no declaration can
    // disagree with the reset acting on it.
    //
    // This is the definition half of the same definition/instance split
    // ChapterDefinition and GeneratorDefinition already have; Scope is the
    // instance. A replay economy (rule 7) is a second INSTANCE of one
    // definition, which is why nothing here is runtime state.
    //
    // No prestige rung yet: its shape (PrestigeTierDefinition) lands with the
    // reset operation in 7.5 step 3, and a field without its type is not a
    // declaration.
    [CreateAssetMenu(
        fileName = "NewScope",
        menuName = "GarageBandIdle/Scope")]
    public class ScopeDefinition : Definition
    {
        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Must hold for this scope to be enabled (7.5 step 4). None = always enabled while its parent is.")]
        private Condition _activeWhen;

        [SerializeField]
        [DefinitionId(typeof(ScopeDefinition))]
        [Tooltip("Child scopes in ladder order (design doc section 1). Order is meaningful: it is display " +
            "order and, later, same-depth reset order.")]
        private List<string> _childScopeIds = new();

        [Header("Truth")]
        [SerializeField]
        [Tooltip("Progress flags this scope's content may set - the reveal registry for facts that live " +
            "HERE. A flag's lifetime is this scope: whatever resets the scope takes the flag with it.")]
        private List<FlagDeclaration> _flags = new();

        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currencies whose balances live here. Ids are unique tree-wide; moving one outward is a " +
            "pure data edit, and that is what makes it more durable.")]
        private List<string> _currencyIds = new();

        [SerializeField]
        [DefinitionId(typeof(ProducerDefinition))]
        [Tooltip("Module-held production sources (the Jam button) whose facts live here.")]
        private List<string> _producerIds = new();

        [SerializeField]
        [DefinitionId(typeof(GeneratorDefinition))]
        [Tooltip("Display order is list order.")]
        private List<string> _generatorIds = new();

        [SerializeField]
        [DefinitionId(typeof(UpgradeDefinition))]
        private List<string> _upgradeIds = new();

        [SerializeField]
        [DefinitionId(typeof(BarGroupDefinition))]
        private List<string> _barGroupIds = new();

        [Header("Presentation")]
        [SerializeField]
        [DefinitionId(typeof(SectionDefinition))]
        [Tooltip("Sections in layout order; each reveals when its own visibleWhen holds. A scope presents " +
            "its own truth, so a section lives beside the facts its modules show.")]
        private List<string> _sectionIds = new();

        public Condition ActiveWhen => _activeWhen;
        public IReadOnlyList<string> ChildScopeIds => _childScopeIds;
        public IReadOnlyList<FlagDeclaration> Flags => _flags;
        public IReadOnlyList<string> CurrencyIds => _currencyIds;
        public IReadOnlyList<string> ProducerIds => _producerIds;
        public IReadOnlyList<string> GeneratorIds => _generatorIds;
        public IReadOnlyList<string> UpgradeIds => _upgradeIds;
        public IReadOnlyList<string> BarGroupIds => _barGroupIds;
        public IReadOnlyList<string> SectionIds => _sectionIds;

#if UNITY_EDITOR
        // importer-only: scope assets are generated from chapter JSON (7.5 step 7)
        public void EditorInitialize(string id, Condition activeWhen, List<string> childScopeIds,
            List<FlagDeclaration> flags, List<string> currencyIds, List<string> producerIds,
            List<string> generatorIds, List<string> upgradeIds, List<string> barGroupIds,
            List<string> sectionIds)
        {
            SetIdentity(id);
            _activeWhen = activeWhen;
            _childScopeIds = childScopeIds ?? new List<string>();
            _flags = flags ?? new List<FlagDeclaration>();
            _currencyIds = currencyIds ?? new List<string>();
            _producerIds = producerIds ?? new List<string>();
            _generatorIds = generatorIds ?? new List<string>();
            _upgradeIds = upgradeIds ?? new List<string>();
            _barGroupIds = barGroupIds ?? new List<string>();
            _sectionIds = sectionIds ?? new List<string>();
        }
#endif
    }
}
