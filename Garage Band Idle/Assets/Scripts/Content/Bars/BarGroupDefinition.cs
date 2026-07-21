using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // How a bar group distributes its fill currency — a closed, code-defined
    // set: the bar system has one fill behavior per mode. The chapter JSON
    // spells these "perBar" (etc.).
    // Explicit values: the numbers are the serialization contract, and zero is
    // reserved for the uninitialized state (see ContentScope). Append with new
    // values only.
    public enum BarFillMode
    {
        None = 0,

        // player-directed: each bar accrues its own progress and the player
        // chooses which bar the fill currency pours into
        PerBar = 1,
    }

    // How fill currency moves from the shared pool into the target bar - the
    // trigger, orthogonal to BarFillMode's bookkeeping. A closed, code-defined
    // set: one transfer behavior per mode. The chapter JSON spells these
    // "continuous" (etc.). Future modes (tap a fixed chunk, dump the pool) are
    // appended values, not new systems.
    // Explicit values: the numbers are the serialization contract, and zero is
    // reserved for the uninitialized state. Append with new values only.
    public enum BarFillDelivery
    {
        None = 0,

        // accrued fill currency streams through the pool into the group's
        // active bar as it arrives; the pool only holds a balance when no bar
        // is selected
        Continuous = 1,
    }

    // An ordered group of fillable bars that reveals as one unit (Learn Covers).
    // Reveal runs through the flag registry like all content; the group's scope
    // drives reset on album release. Fill behavior arrives with the bars slice.
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

        [SerializeField]
        private BarFillMode _fillMode;

        [SerializeField]
        private BarFillDelivery _delivery;

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
        public BarFillMode FillMode => _fillMode;
        public BarFillDelivery Delivery => _delivery;
        public ContentScope Scope => _scope;
        public IReadOnlyList<string> BarIds => _barIds;

#if UNITY_EDITOR
        // importer-only: bar group assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, string revealFlagId,
            BarFillMode fillMode, BarFillDelivery delivery, ContentScope scope, List<string> barIds)
        {
            _id = id;
            _displayName = displayName;
            _revealFlagId = revealFlagId;
            _fillMode = fillMode;
            _delivery = delivery;
            _scope = scope;
            _barIds = barIds;
        }
#endif
    }
}
