using System.Collections.Generic;

namespace AnimFramework.Animation
{
    /// <summary>
    /// The full set of animation clips for one NPC type: named states (mutually exclusive,
    /// keyed by state name e.g. "Idle") plus overlays (drawn on top of whatever state is
    /// active, e.g. "Talk"). Built once per NPC type and shared/cached by whichever attachment
    /// path (AnimatedNPC or AnimationRegistry) constructed it.
    /// </summary>
    public class AnimationSet
    {
        public Dictionary<string, AnimationClip> States { get; }
        public List<AnimationClip> Overlays { get; }

        public AnimationSet(Dictionary<string, AnimationClip> states, List<AnimationClip> overlays)
        {
            States = states ?? new Dictionary<string, AnimationClip>();
            Overlays = overlays ?? new List<AnimationClip>();
        }
    }
}
