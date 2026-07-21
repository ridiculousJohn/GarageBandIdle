using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Events
{
    // One opt-in event (design doc section 6.1) with its tier ladder.
    // Availability and tier goals are the shared Condition type — the same
    // evaluator the capstone and sections use. Event behavior (baseline reset,
    // debuffs, timers) arrives in the events slice; this slice stores the data.
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

        [SerializeReference]
        [Tooltip("Must hold for the event to appear.")]
        private Condition _availableWhen;

        [SerializeField]
        [Tooltip("On entry the chapter economy resets to a fixed baseline for the event's duration.")]
        private bool _baselineReset;

        [SerializeField]
        private List<EventTier> _tiers = new();

        public string Id => _id;
        public string DisplayName => _displayName;
        public string Type => _type;
        public Condition AvailableWhen => _availableWhen;
        public bool BaselineReset => _baselineReset;
        public IReadOnlyList<EventTier> Tiers => _tiers;

#if UNITY_EDITOR
        // importer-only: event assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, string type,
            Condition availableWhen, bool baselineReset, List<EventTier> tiers)
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

        [SerializeReference]
        [Tooltip("Reaching this wins the tier, e.g. a currency condition.")]
        private Condition _goal;

        [SerializeField]
        [Tooltip("Time limit in seconds; timed tiers are the only failable ones.")]
        private double _timerSeconds;

        [SerializeField]
        private bool _failable;

        [SerializeField]
        [DefinitionId(typeof(RewardDefinition))]
        [Tooltip("Reward pool id applied on tier success (RewardManager).")]
        private string _rewardId;

        public int Tier => _tier;
        public string DebuffEffect => _debuffEffect;
        public Condition Goal => _goal;
        public double TimerSeconds => _timerSeconds;
        public bool Failable => _failable;
        public string RewardId => _rewardId;

        public EventTier() { }

#if UNITY_EDITOR
        public EventTier(int tier, string debuffEffect, Condition goal,
            double timerSeconds, bool failable, string rewardId)
        {
            _tier = tier;
            _debuffEffect = debuffEffect;
            _goal = goal;
            _timerSeconds = timerSeconds;
            _failable = failable;
            _rewardId = rewardId;
        }
#endif
    }
}
