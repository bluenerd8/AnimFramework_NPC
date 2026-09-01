using System;
using System.Diagnostics;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Per-instance playback head for a single <see cref="AnimationClip"/>. Driven by a
    /// real-time <see cref="Stopwatch"/> rather than Terraria's 60-tick frame counter, so
    /// playback stays smooth independent of tick rate / uncapped FPS — the framework's core
    /// "bypass" (see the Core Timing Model section of ANIMFRAMEWORK_SPEC.md).
    /// </summary>
    public class AnimationPlayer
    {
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private AnimationClip _clip;

        public AnimationClip Clip
        {
            get { return _clip; }
        }

        public bool IsFinished { get; private set; }

        /// <summary>
        /// Starts playing the given clip from frame 0. Always restarts, even if this is the
        /// same clip as before — callers are expected to only call this on an actual
        /// state-entry edge (see AnimationController), not on every frame the state is active.
        /// </summary>
        public void Play(AnimationClip clip)
        {
            _clip = clip;
            Reset();
        }

        public void Reset()
        {
            _stopwatch.Restart();
            IsFinished = false;
        }

        /// <summary>Current frame index for the active clip, given elapsed real time.</summary>
        public int GetCurrentFrame()
        {
            if (_clip == null || _clip.FrameCount <= 0)
                return 0;

            int frameCount = _clip.FrameCount;
            if (frameCount == 1)
            {
                IsFinished = true;
                return 0;
            }

            double cyclePos = _stopwatch.Elapsed.TotalSeconds * _clip.Fps; // fractional frames elapsed

            switch (_clip.Loop)
            {
                case LoopMode.Once:
                    if (cyclePos >= frameCount - 1)
                    {
                        IsFinished = true;
                        return frameCount - 1;
                    }
                    IsFinished = false;
                    return (int)cyclePos;

                case LoopMode.PingPong:
                    {
                        int pingPongLength = Math.Max(1, (frameCount - 1) * 2);
                        int t = (int)cyclePos % pingPongLength;
                        IsFinished = false;
                        return t < frameCount ? t : pingPongLength - t;
                    }

                case LoopMode.Loop:
                default:
                    IsFinished = false;
                    return (int)cyclePos % frameCount;
            }
        }
    }
}
