using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // One currency (design doc section 3). Currencies are data: CurrencyManager
    // keys balances by id and discovers definitions at load, so a new currency
    // is just a new asset with no manager changes. A fill currency OWNS its
    // engagement-earn config (a later chapter's fill currency is a new asset,
    // not a chapter field).
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
        [DefinitionId(typeof(CurrencyGroupDefinition))]
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

        [Header("Engagement Earn")]
        [SerializeField]
        private EngagementEarnConfig _earn = new();

        public string Id => _id;
        public string DisplayName => _displayName;
        public string GroupId => _groupId;
        public string Symbol => _symbol;
        public int MaxDecimals => _maxDecimals;
        public double StartingValue => _startingValue;
        public EngagementEarnConfig Earn => _earn;

#if UNITY_EDITOR
        // importer-only: currencies a chapter JSON declares (e.g. rehearsal) are
        // generated like any other content; hand-authored ones are left alone
        public void EditorInitialize(string id, string displayName, string groupId, EngagementEarnConfig earn)
        {
            _id = id;
            _displayName = displayName;
            _groupId = groupId;
            _earn = earn ?? new EngagementEarnConfig();
        }
#endif
    }

    // Engagement earn (design doc section 3): a fill currency owns its earn
    // config — a passive tick plus Jam taps, gated by a reveal flag, never
    // Cash. Unset means the currency has no engagement earn (cash, fans,
    // records). Earn values without a reveal flag can never activate — the
    // importer refuses that state and boot validation reports it.
    [Serializable]
    public class EngagementEarnConfig
    {
        [SerializeField]
        [Tooltip("Flag that activates the earn (the single reveal registry). Empty = no engagement earn.")]
        private string _revealFlagId;

        [SerializeField]
        private double _perSec;

        [SerializeField]
        [Tooltip("Yield per Jam tap (engagement, never Cash).")]
        private double _perTap;

        public string RevealFlagId => _revealFlagId;
        public double PerSec => _perSec;
        public double PerTap => _perTap;

        public bool Configured => !string.IsNullOrEmpty(_revealFlagId) || _perSec != 0 || _perTap != 0;

        public EngagementEarnConfig() { }

#if UNITY_EDITOR
        public EngagementEarnConfig(string revealFlagId, double perSec, double perTap)
        {
            _revealFlagId = revealFlagId;
            _perSec = perSec;
            _perTap = perTap;
        }
#endif
    }
}
