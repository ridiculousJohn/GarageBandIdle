using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // A group of currencies sharing reset behavior (design doc section 3: run-scoped
    // vs permanent). Code acts on the behavior flags below and never on a group's
    // identity, so a new group is just a new asset with no manager changes.
    [CreateAssetMenu(
        fileName = "NewCurrencyGroup",
        menuName = "GarageBandIdle/Currency Group")]
    public class CurrencyGroupDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id referenced by a currency's Group Id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [Header("Behavior")]
        [SerializeField]
        [Tooltip("An album release (prestige) resets every currency in this group to its starting value.")]
        private bool _resetsOnAlbumRelease;

        public string Id => _id;
        public string DisplayName => _displayName;
        public bool ResetsOnAlbumRelease => _resetsOnAlbumRelease;
    }
}
