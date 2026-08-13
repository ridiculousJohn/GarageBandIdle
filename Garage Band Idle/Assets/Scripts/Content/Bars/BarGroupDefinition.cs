using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // An ordered group of fillable bars that reveals as one unit (Learn Covers).
    // Visibility is an ordinary Condition asked through the one evaluator, like
    // every other gate in the game (design doc section 12, rules 8 and 9) - a
    // group can gate on a flag, a balance or a completed bar, not just a flag.
    // The group's scope drives reset on album release. How the group fills is
    // the polymorphic BarFillBehavior: the concrete type is the mode, chosen at
    // import.
    [CreateAssetMenu(
        fileName = "NewBarGroup",
        menuName = "GarageBandIdle/Bar Group")]
    public class BarGroupDefinition : Definition
    {
        [SerializeField]
        private string _displayName;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Must hold for the group to show, evaluated like every other gate. None = always visible.")]
        private Condition _visibleWhen;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("How the group fills; the concrete type is the mode, like Condition.")]
        private BarFillBehavior _fillBehavior;

        [SerializeField]
        [Tooltip("Reset logic acts on this field.")]
        private ContentScope _scope;

        [SerializeField]
        [DefinitionId(typeof(BarDefinition))]
        [Tooltip("Bar ids in display order.")]
        private List<string> _barIds = new();

        public string DisplayName => _displayName;
        public Condition VisibleWhen => _visibleWhen;
        public BarFillBehavior FillBehavior => _fillBehavior;
        public ContentScope Scope => _scope;
        public IReadOnlyList<string> BarIds => _barIds;

#if UNITY_EDITOR
        // importer-only: bar group assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, Condition visibleWhen,
            BarFillBehavior fillBehavior, ContentScope scope, List<string> barIds)
        {
            SetIdentity(id);
            _displayName = displayName;
            _visibleWhen = visibleWhen;
            _fillBehavior = fillBehavior;
            _scope = scope;
            _barIds = barIds;
        }
#endif
    }
}
