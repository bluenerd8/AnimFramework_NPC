using System.Collections.Generic;

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
    }
}
