using System;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Per-instance playback head for a single <see cref="AnimationClip"/>, timed off
    /// <see cref="AnimationClock"/> rather than Terraria's 60-tick counter. Everything here is
    /// computed from elapsed time, so state can be read from the update tick even while the NPC
    /// is off-screen and never drawn.
    /// </summary>
    public class AnimationPlayer
    {
        private AnimationClip _clip;
        private double _startTime;

        public AnimationClip Clip
        {
            get { return _clip; }
        }

        /// <summary>
        /// True once a Once-mode clip has played all its frames (and always for a single-frame
        /// clip). Never true for Loop or PingPong clips.
        /// </summary>
        public bool IsFinished
        {
            get
            {
                if (_clip == null)
                    return false;
                if (_clip.FrameCount == 1)
                    return true;
                return _clip.Loop == LoopMode.Once && CyclePosition() >= _clip.FrameCount;
            }
        }

        /// <summary>Starts the clip from frame 0. Always restarts, even for the same clip; call only on a state change.</summary>
        public void Play(AnimationClip clip)
        {
            _clip = clip;
            Reset();
        }

        public void Reset()
        {
            _startTime = AnimationClock.Now;
        }

        /// <summary>Fractional frames elapsed since <see cref="Play"/>.</summary>
        private double CyclePosition()
        {
            return (AnimationClock.Now - _startTime) * _clip.Fps;
        }

        public int GetCurrentFrame()
        {
            if (_clip == null || _clip.FrameCount <= 1)
                return 0;

            int frameCount = _clip.FrameCount;
            int t = (int)CyclePosition();

            switch (_clip.Loop)
            {
                case LoopMode.Once:
                    return Math.Min(t, frameCount - 1);

                case LoopMode.PingPong:
                    {
                        int length = (frameCount - 1) * 2;
                        int p = t % length;
                        return p < frameCount ? p : length - p;
                    }

                case LoopMode.Loop:
                default:
                    return t % frameCount;
            }
        }

        /// <summary>Full loops completed since <see cref="Play"/>. Always 0 for non-Loop clips.</summary>
        public int GetCompletedLoops()
        {
            if (_clip == null || _clip.FrameCount <= 0 || _clip.Loop != LoopMode.Loop)
                return 0;

            return (int)CyclePosition() / _clip.FrameCount;
        }
    }
}
