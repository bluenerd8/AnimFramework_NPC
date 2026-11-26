using System.Collections.Generic;

namespace AnimFramework.Animation
{
    /// <summary>
    /// All clips for one NPC type: named states (mutually exclusive, e.g. "Idle") and overlays
    /// (drawn on top of the current state, e.g. "Blink"). Built once per type and shared.
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
