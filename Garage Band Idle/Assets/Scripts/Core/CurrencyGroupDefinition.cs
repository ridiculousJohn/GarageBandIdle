using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // A group of currencies sharing reset behavior (design doc section 3: run-scoped
    // vs permanent). Code acts on the behavior flags below and never on a group's
    // identity, so a new group is just a new asset with no manager changes.
    [CreateAssetMenu(
        fileName = "NewCurrencyGroup",
        menuName = "GarageBandIdle/Currency Group")]
    public class CurrencyGroupDefinition : Definition
    {
        [SerializeField]
        private string _displayName;

        [Header("Behavior")]
        [SerializeField]
        [Tooltip("An album release (prestige) resets every currency in this group to its starting value.")]
        private bool _resetsOnAlbumRelease;

        [SerializeField]
        [Tooltip("Which pool holds this group's balances: Chapter (the economy context's own pool) or " +
            "Global (the startup pool, never reset by a run operation).")]
        private CurrencyPlacement _placement;

        public string DisplayName => _displayName;
        public bool ResetsOnAlbumRelease => _resetsOnAlbumRelease;

        // Where this group's currencies live (design doc section 12, rule 12).
        // Lifetime is not declared here - it comes from who creates the pool -
        // so this field says only which pool, and boot validation refuses the
        // one combination that has no coherent reading: a global group that
        // also claims to reset on album release, since a global currency has no
        // release of its own to reset on.
        public CurrencyPlacement Placement => _placement;
    }
}
