using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Events
{
    // One opt-in event (design doc section 6.1) with its tier ladder. Event
    // behavior (baseline reset, debuffs, timers) arrives in the events slice;
    // this slice only stores the data.
    [CreateAssetMenu(
        fileName = "NewEvent",
        menuName = "GarageBandIdle/Event")]
    public class EventDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [Tooltip("Event kind from the JSON, e.g. challenge.")]
        private string _type;

        [SerializeField]
        [Tooltip("All conditions must hold for the event to appear.")]
        private List<GateCondition> _availableWhen = new();

        [SerializeField]
        [Tooltip("On entry the chapter economy resets to a fixed baseline for the event's duration.")]
        private bool _baselineReset;

        [SerializeField]
        private List<EventTier> _tiers = new();

        public string Id => _id;
        public string DisplayName => _displayName;
        public string Type => _type;
        public IReadOnlyList<GateCondition> AvailableWhen => _availableWhen;
        public bool BaselineReset => _baselineReset;
        public IReadOnlyList<EventTier> Tiers => _tiers;

#if UNITY_EDITOR
        // importer-only: event assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, string type,
            List<GateCondition> availableWhen, bool baselineReset, List<EventTier> tiers)
        {
            _id = id;
            _displayName = displayName;
            _type = type;
            _availableWhen = availableWhen;
            _baselineReset = baselineReset;
            _tiers = tiers;
        }
#endif
    }

    // One tier of an event: debuff, goal, optional timer, reward.
    [Serializable]
    public class EventTier
    {
        [SerializeField]
        private int _tier;

        [SerializeField]
        [Tooltip("Debuff effect key, e.g. automationDisabled.")]
        private string _debuffEffect;

        [Header("Goal")]
        [SerializeField]
        private string _goalCurrencyId;

        [SerializeField]
        private double _goalAmount;

        [SerializeField]
        [Tooltip("Time limit in seconds; timed tiers are the only failable ones.")]
        private double _timerSeconds;

        [SerializeField]
        private bool _failable;

        [Header("Reward")]
        [SerializeField]
        [Tooltip("Reward effect key, e.g. tapValueMultiplier.")]
        private string _rewardEffect;

        [SerializeField]
        private double _rewardValue;

        [SerializeField]
        [Tooltip("Reward scope, e.g. permanentInChapter.")]
        private string _rewardScope;

        public int Tier => _tier;
        public string DebuffEffect => _debuffEffect;
        public string GoalCurrencyId => _goalCurrencyId;
        public double GoalAmount => _goalAmount;
        public double TimerSeconds => _timerSeconds;
        public bool Failable => _failable;
        public string RewardEffect => _rewardEffect;
        public double RewardValue => _rewardValue;
        public string RewardScope => _rewardScope;

        public EventTier() { }

#if UNITY_EDITOR
        public EventTier(int tier, string debuffEffect, string goalCurrencyId, double goalAmount,
            double timerSeconds, bool failable, string rewardEffect, double rewardValue, string rewardScope)
        {
            _tier = tier;
            _debuffEffect = debuffEffect;
            _goalCurrencyId = goalCurrencyId;
            _goalAmount = goalAmount;
            _timerSeconds = timerSeconds;
            _failable = failable;
            _rewardEffect = rewardEffect;
            _rewardValue = rewardValue;
            _rewardScope = rewardScope;
        }
#endif
    }
}
