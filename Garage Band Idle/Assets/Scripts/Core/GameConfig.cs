using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // The global tuning knobs (design doc 12.9): a small settings asset the
    // real game references once and tests build inline. Authored values, never
    // state - nothing here is serialized into a save.
    [CreateAssetMenu(menuName = "Garage Band Idle/Game Config")]
    public class GameConfig : ScriptableObject
    {
        // The ceiling of the tick's game_speed clamp (section 9). Authored
        // carriers multiply freely; the sole consumer clamps to [1, this].
        public double maxGameSpeed = 4;

        // The idle thresholds (section 9): seconds, not multipliers, which is
        // why they live here and not in the stat vocabulary. Away time under
        // the minimum claims nothing; away time over the cap pays the cap.
        // The authored numbers are placeholders until tuning cares.
        public double minimumAwaySeconds = 180;
        public double idleCapSeconds = 14400;

        // Encore's two knobs (section 9), seconds like the thresholds above and
        // placeholders in the same way. The cap bounds REMAINING time, so a
        // repeatable grant runs into it rather than accumulating past it.
        public double encoreAdSeconds = 14400;   // one rewarded ad's extension
        public double encoreCapSeconds = 86400;  // the most remaining Encore a record may hold

        // The Pass's idle cap (section 9), a placeholder like the thresholds
        // above. Require refuses one below the base cap: a malformed Pass cap
        // must fail at boot, never shrink a paid claim.
        public double backstagePassIdleCapSeconds = 28800;

        // The Roadie bundles the store sells (section 9): counts, since Roadies
        // are counted, and placeholders until tuning cares. Require refuses a
        // nonpositive one - a store that has already reported success must
        // never meet a grant that throws.
        public int roadieBundleSmall = 1;
        public int roadieBundleMedium = 5;
        public int roadieBundleLarge = 15;

        // The tick cadence (section 9): the session ticks ONCE with the whole
        // accumulation when pending crosses this. Smoothness comes from
        // interpolation, so the interval only bounds the latency of an
        // autonomous change; zero would restore the per-frame ticking it exists
        // to remove.
        public double tickIntervalSeconds = 0.25;

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
            if (double.IsNaN(config.minimumAwaySeconds) || double.IsInfinity(config.minimumAwaySeconds) || config.minimumAwaySeconds < 0)
                throw new InvalidOperationException(
                    $"GameConfig: minimumAwaySeconds {config.minimumAwaySeconds} is not a finite nonnegative value.");
            if (double.IsNaN(config.idleCapSeconds) || double.IsInfinity(config.idleCapSeconds) || config.idleCapSeconds < 0)
                throw new InvalidOperationException(
                    $"GameConfig: idleCapSeconds {config.idleCapSeconds} is not a finite nonnegative value.");
            if (double.IsNaN(config.encoreAdSeconds) || double.IsInfinity(config.encoreAdSeconds)
                || config.encoreAdSeconds <= 0)
                throw new InvalidOperationException(
                    $"GameConfig: encoreAdSeconds {config.encoreAdSeconds} is not a finite positive value.");
            if (double.IsNaN(config.encoreCapSeconds) || double.IsInfinity(config.encoreCapSeconds)
                || config.encoreCapSeconds <= 0)
                throw new InvalidOperationException(
                    $"GameConfig: encoreCapSeconds {config.encoreCapSeconds} is not a finite positive value.");
            if (config.encoreCapSeconds < config.encoreAdSeconds)
                throw new InvalidOperationException(
                    $"GameConfig: encoreCapSeconds {config.encoreCapSeconds} is below encoreAdSeconds {config.encoreAdSeconds} - a cap under one ad's grant would clamp every ad short.");
            if (double.IsNaN(config.backstagePassIdleCapSeconds) || double.IsInfinity(config.backstagePassIdleCapSeconds)
                || config.backstagePassIdleCapSeconds < config.idleCapSeconds)
                throw new InvalidOperationException(
                    $"GameConfig: backstagePassIdleCapSeconds {config.backstagePassIdleCapSeconds} is not a finite value of at least idleCapSeconds {config.idleCapSeconds}.");
            RequirePositiveCount(config.roadieBundleSmall, nameof(config.roadieBundleSmall));
            RequirePositiveCount(config.roadieBundleMedium, nameof(config.roadieBundleMedium));
            RequirePositiveCount(config.roadieBundleLarge, nameof(config.roadieBundleLarge));
            if (double.IsNaN(config.tickIntervalSeconds) || double.IsInfinity(config.tickIntervalSeconds)
                || config.tickIntervalSeconds <= 0)
                throw new InvalidOperationException(
                    $"GameConfig: tickIntervalSeconds {config.tickIntervalSeconds} is not a finite positive value.");
        }

        // The three bundles take one rule, so they ask it in one place: a
        // bundle is a count of Roadies, and a nonpositive one is a purchase
        // that delivers nothing.
        private static void RequirePositiveCount(int value, string name)
        {
            if (value <= 0)
                throw new InvalidOperationException($"GameConfig: {name} {value} is not a positive count.");
        }
    }
}
