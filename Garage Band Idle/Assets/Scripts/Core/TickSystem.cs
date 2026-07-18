using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Fixed-interval economy tick driven by real elapsed time (DateTime.UtcNow
    // deltas), not frame time, so simulated time stays correct regardless of
    // frame rate and lines up with the offline-earnings math later.
    public class TickSystem : MonoBehaviour
    {
        // fires once per elapsed tick with the tick length in seconds
        public event Action<double> Ticked;

        private const double TickInterval = 0.1;

        // long gaps (suspend, editor pause) are the offline-earnings path in a later
        // slice; until then they are clamped rather than simulated as live ticks
        private const double MaxCatchUpSeconds = 5.0;

        private DateTime _lastUpdateUtc;
        private double _accumulator;

        private void OnEnable()
        {
            _lastUpdateUtc = DateTime.UtcNow;
        }

        private void Update()
        {
            var now = DateTime.UtcNow;
            double delta = (now - _lastUpdateUtc).TotalSeconds;
            _lastUpdateUtc = now;

            // the wall clock can move backwards (NTP sync, manual change); never tick negative time
            if (delta < 0)
                delta = 0;
            if (delta > MaxCatchUpSeconds)
                delta = MaxCatchUpSeconds;

            _accumulator += delta;
            while (_accumulator >= TickInterval)
            {
                _accumulator -= TickInterval;
                Ticked?.Invoke(TickInterval);
            }
        }
    }
}
