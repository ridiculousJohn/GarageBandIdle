using System;

namespace RidiculousGaming.GarageBandIdle
{
    // The one time source (design doc 12.9): driver-owned, advanced at the top
    // of every driver entry point before any game code runs, and read by
    // everything else instead of Time.* or DateTime. Suspension is entered and
    // left through lifecycle hooks rather than frames, so a resume reading a
    // clock last advanced in Update would compute a zero idle window from the
    // stale time and then tick the whole suspended gap as live production, past
    // the idle fraction, cap, and threshold. The values are RAW - no clamping,
    // no game_speed - because every scaling belongs in the sim's normal means.
    public sealed class GameClock
    {
        // Advances at EVERY entry point: the session's sample is diffed against
        // this, so a suspended gap is visible to the idle path as real time.
        public DateTime RealTimeUtc { get; private set; }

        // This frame's live delta, and zero at a lifecycle resample - the one
        // pause-sensitive line the game reads.
        public double DeltaSeconds { get; private set; }

        // Accumulated DeltaSeconds. Suspension is real time, never game time.
        public double GameTimeSeconds { get; private set; }

        public GameClock(DateTime nowUtc) => RealTimeUtc = nowUtc;

        public void Frame(DateTime nowUtc, double deltaSeconds)
        {
            RealTimeUtc = nowUtc;
            DeltaSeconds = deltaSeconds;
            GameTimeSeconds += deltaSeconds;
        }

        // Pause, resume, and quit: real time moves over the gap while game time
        // holds, and the frame's delta is zero because no frame elapsed.
        public void Resample(DateTime nowUtc)
        {
            RealTimeUtc = nowUtc;
            DeltaSeconds = 0;
        }
    }
}
