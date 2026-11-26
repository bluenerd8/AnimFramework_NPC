using System.Diagnostics;
using Terraria;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Framework-wide real-time clock. Advances smoothly between game ticks (so animation isn't
    /// capped to the 60 Hz logic rate) but stops while the game is paused, so NPCs freeze and
    /// interval timers don't run down during autopause.
    ///
    /// Every timestamp in the framework (playback start, signal ages, next-blink times) is a
    /// reading of <see cref="Now"/>, so pausing this one clock pauses everything consistently.
    /// </summary>
    public static class AnimationClock
    {
        private static readonly Stopwatch Clock = new Stopwatch();

        /// <summary>Seconds of unpaused time since the clock first started.</summary>
        public static double Now
        {
            get { return Clock.Elapsed.TotalSeconds; }
        }

        /// <summary>Starts or stops the clock to match <c>Main.gamePaused</c>. Cheap; called every update and draw.</summary>
        internal static void Sync()
        {
            bool paused = Main.gamePaused;
            if (paused && Clock.IsRunning)
                Clock.Stop();
            else if (!paused && !Clock.IsRunning)
                Clock.Start();
        }

        internal static void Reset()
        {
            Clock.Reset();
        }
    }
}
