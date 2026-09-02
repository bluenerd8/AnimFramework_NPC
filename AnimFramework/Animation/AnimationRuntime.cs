using System.Collections.Generic;
using System.Diagnostics;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Per-instance runtime animation state for one NPC: which state is currently playing, the
    /// player driving it, and one player per currently-active overlay.
    ///
    /// Must never be shared by reference across NPC instances of the same type — tModLoader's
    /// default MemberwiseClone would otherwise copy this reference onto every spawned instance,
    /// so all of them would animate in perfect lockstep off one shared Stopwatch. See
    /// AnimatedNPC.Clone / AnimatedGlobalNPC.Clone, which null their runtime field on clone so
    /// each spawned NPC lazily builds its own fresh instance here, while still sharing the same
    /// cached, immutable <see cref="AnimationSet"/>.
    /// </summary>
    public class AnimationRuntime
    {
        public AnimationRuntime(AnimationSet set)
        {
            Set = set;
        }

        public AnimationSet Set { get; }
        public string CurrentState { get; set; }
        public AnimationPlayer StatePlayer { get; } = new AnimationPlayer();
        public Dictionary<AnimationClip, AnimationPlayer> OverlayPlayers { get; } = new Dictionary<AnimationClip, AnimationPlayer>();
        public HashSet<AnimationClip> ActiveOverlays { get; } = new HashSet<AnimationClip>();

        /// <summary>
        /// Restarted by <see cref="AnimationController.NotifyAttackTriggered"/> each time a
        /// type-0/1/2 attack hook fires for this NPC. Not running (never restarted) until the
        /// first such hook fires — see <see cref="AnimationController"/>'s Attacking handling.
        /// </summary>
        public Stopwatch AttackSignalAge { get; } = new Stopwatch();

        /// <summary>Real-time clock behind the Blink timed overlay — see AnimationController.ShouldBlink.</summary>
        public Stopwatch BlinkClock { get; } = new Stopwatch();

        /// <summary>Elapsed-seconds-on-<see cref="BlinkClock"/> timestamp for this NPC's next scheduled blink.</summary>
        public double NextBlinkAt { get; set; }
    }
}
