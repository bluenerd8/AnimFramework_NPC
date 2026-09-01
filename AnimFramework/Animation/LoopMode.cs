namespace AnimFramework.Animation
{
    /// <summary>
    /// Playback behavior for an <see cref="AnimationClip"/> once it reaches its last frame.
    /// Chosen per-clip by the content mod author — the framework never predicts which mode a
    /// given animation "should" use (see ANIMFRAMEWORK_SPEC.md's "Loop Modes" section).
    /// </summary>
    public enum LoopMode
    {
        /// <summary>Wraps back to frame 0 and continues forever.</summary>
        Loop,

        /// <summary>Plays forward to the last frame, then backward to the first, repeating.</summary>
        PingPong,

        /// <summary>Plays through all frames once, then holds on the last frame until reset (IsFinished flips true).</summary>
        Once
    }
}
