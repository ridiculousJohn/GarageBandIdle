using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // An ordered group of fillable bars that reveals as one unit (Learn Covers).
    // Reveal runs through the flag registry like all content; the group's scope
    // drives reset on album release. How the group fills is the polymorphic
    // BarFillBehavior: the concrete type is the mode, chosen at import.
    [CreateAssetMenu(
        fileName = "NewBarGroup",
        menuName = "GarageBandIdle/Bar Group")]
    public class BarGroupDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [Tooltip("Flag that reveals the group (the single reveal registry).")]
        private string _revealFlagId;

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

        public string Id => _id;
        public string DisplayName => _displayName;
        public string RevealFlagId => _revealFlagId;
        public BarFillBehavior FillBehavior => _fillBehavior;
        public ContentScope Scope => _scope;
        public IReadOnlyList<string> BarIds => _barIds;

#if UNITY_EDITOR
        // importer-only: bar group assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, string revealFlagId,
            BarFillBehavior fillBehavior, ContentScope scope, List<string> barIds)
        {
            _id = id;
            _displayName = displayName;
            _revealFlagId = revealFlagId;
            _fillBehavior = fillBehavior;
            _scope = scope;
            _barIds = barIds;
        }
#endif
    }
}
