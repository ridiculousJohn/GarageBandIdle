using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // One currency (design doc section 3). Currencies are data: CurrencyManager
    // keys balances by id and discovers definitions at load, so a new currency
    // is just a new asset with no manager changes.
    [CreateAssetMenu(
        fileName = "NewCurrency",
        menuName = "GarageBandIdle/Currency")]
    public class CurrencyDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id used as the balance key. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [Tooltip("Id of the CurrencyGroupDefinition this currency belongs to.")]
        private string _groupId;

        [Header("Formatting Hints")]
        [SerializeField]
        [Tooltip("Prefix shown before the value, e.g. \"$\" for Cash.")]
        private string _symbol;

        [SerializeField]
        [Tooltip("Maximum decimal places shown when the value is small enough to display in full.")]
        [Range(0, 3)]
        private int _maxDecimals;

        [Header("Economy")]
        [SerializeField]
        private double _startingValue;

        public string Id => _id;
        public string DisplayName => _displayName;
        public string GroupId => _groupId;
        public string Symbol => _symbol;
        public int MaxDecimals => _maxDecimals;
        public double StartingValue => _startingValue;
    }
}
