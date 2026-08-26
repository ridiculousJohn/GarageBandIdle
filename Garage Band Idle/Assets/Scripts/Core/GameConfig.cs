using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // The global tuning knobs (design doc 12.9): a small settings asset the
    // real game references once and tests build inline. Authored values, never
    // state - nothing here is serialized into a save. minimumAwaySeconds and
    // idleCapSeconds join it with their consumer, the idle claim.
    [CreateAssetMenu(menuName = "Garage Band Idle/Game Config")]
    public class GameConfig : ScriptableObject
    {
        // The ceiling of the tick's game_speed clamp (section 9). Authored
        // carriers multiply freely; the sole consumer clamps to [1, this].
        public double maxGameSpeed = 4;

        // Fail-loud at the consumers (requirement 7): the tick for direct use,
        // the session at construction. A bad ceiling would silently clamp the
        // clamp, so it throws instead; sub-1 is refused because the clamp's
        // floor is 1, and a ceiling under the floor is not a range.
        public static void Require(GameConfig config)
        {
            if (config == null)
                throw new InvalidOperationException("GameConfig: the consumer was handed no config asset.");
            if (double.IsNaN(config.maxGameSpeed) || double.IsInfinity(config.maxGameSpeed) || config.maxGameSpeed < 1)
                throw new InvalidOperationException(
                    $"GameConfig: maxGameSpeed {config.maxGameSpeed} is not a finite value of at least 1.");
        }
    }
}
