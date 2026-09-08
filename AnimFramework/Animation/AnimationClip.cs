using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Immutable definition of one animation clip: which row of a sprite sheet it reads, how
    /// many frames, how fast, and how it loops. Shared across every NPC instance that plays it —
    /// per-instance playback progress lives in <see cref="AnimationPlayer"/>, not here.
    /// </summary>
    public class AnimationClip
    {
        /// <summary>State/overlay name this clip was declared under (e.g. "Idle", "Talk").</summary>
        public string State { get; }
        public string TexturePath { get; }
        public int FrameWidth { get; }
        public int FrameHeight { get; }
        public int FrameCount { get; }
        public int Row { get; }
        public float Fps { get; }
        public float Duration { get; }
        public LoopMode Loop { get; }
        public Vector2? Origin { get; }

        /// <summary>
        /// Resolved bounds (seconds) for how often a timed-trigger feature reading this clip
        /// should fire — e.g. the Blink overlay's own internal clock (see
        /// <c>AnimationController.ShouldBlink</c>). Meaningless for anything that isn't
        /// timer-driven; harmless to ignore. Always a valid, non-crossed range — see the
        /// resolution rule in the constructor.
        /// </summary>
        public float IntervalMin { get; }
        public float IntervalMax { get; }

        // Only used when a clip declares neither intervalMin nor intervalMax — same role as the
        // 1f fallback for Fps/Duration below: a sane default for the common "didn't think about
        // it" case, not a value any author is expected to actually want tuned.
        private const float DefaultIntervalMinSeconds = 2.5f;
        private const float DefaultIntervalMaxSeconds = 6f;

        /// <summary>
        /// Resolved bounds, in *completed loops of the currently-active Loop-mode clip*, for how
        /// often a loop-count-triggered feature should fire — currently only the Idle-paired
        /// Blink state (see <c>AnimationController.DetermineIdleState</c>). Deliberately a
        /// separate pair of fields from <see cref="IntervalMin"/>/<see cref="IntervalMax"/>
        /// rather than reusing them: those are seconds, these are a loop count — same field
        /// meaning two different units depending on which feature reads it would be a real
        /// footgun, not a simplification. Same fallback shape as everything else here though:
        /// meaningless for anything that isn't loop-count-driven; always a valid, non-crossed
        /// range — see the resolution rule in the constructor.
        /// </summary>
        public int LoopCountMin { get; }
        public int LoopCountMax { get; }

        // Default range for a loop-count-triggered feature that declares neither bound — chosen
        // so the common case (no JSON tuning at all) still gets occasional, not-every-single-loop
        // variety. Not vanilla-derived, just a reasonable out-of-the-box feel, same spirit as the
        // seconds-based defaults above.
        private const int DefaultLoopCountMin = 0;
        private const int DefaultLoopCountMax = 2;

        private Asset<Texture2D> _texture;

        public AnimationClip(string state, string texturePath, int frameWidth, int frameHeight, int frameCount,
            int row, float fps, float duration, LoopMode loop, Vector2? origin = null,
            float? intervalMin = null, float? intervalMax = null,
            int? loopCountMin = null, int? loopCountMax = null)
        {
            if (frameCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameCount), "AnimationClip requires at least 1 frame.");

            State = state;
            TexturePath = texturePath;
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            FrameCount = frameCount;
            Row = row;
            Loop = loop;
            Origin = origin;

            // fps/duration are two views of the same cycle length (see the spec's Core Timing
            // Model) — whichever the caller gave us, the other is derived here so playback math
            // downstream only ever needs Fps.
            if (fps > 0f)
            {
                Fps = fps;
                Duration = frameCount / fps;
            }
            else if (duration > 0f)
            {
                Duration = duration;
                Fps = frameCount / duration;
            }
            else
            {
                // Defensive default for bad/missing JSON (both fps and duration missing/zero)
                // rather than a divide-by-zero later in AnimationPlayer.
                Fps = 1f;
                Duration = frameCount;
            }

            // Same three-tier rule as everything else optional on a clip: undeclared row = the
            // feature this drives (e.g. Blink) never fires at all — that's decided upstream by
            // whether a clip exists for that overlay/state in the first place, not here. Given a
            // clip, both bounds declared = respected exactly (swapped if authored backwards,
            // same "never crash on bad JSON" spirit as the loop-mode/row-bounds handling in
            // SheetLayoutLoader). Exactly one bound declared = treated as a fixed, non-jittered
            // interval at that value, rather than silently blending an author's number with an
            // arbitrary framework default. Neither declared = the framework default range.
            if (intervalMin.HasValue && intervalMax.HasValue)
            {
                IntervalMin = Math.Min(intervalMin.Value, intervalMax.Value);
                IntervalMax = Math.Max(intervalMin.Value, intervalMax.Value);
            }
            else if (intervalMin.HasValue)
            {
                IntervalMin = IntervalMax = intervalMin.Value;
            }
            else if (intervalMax.HasValue)
            {
                IntervalMin = IntervalMax = intervalMax.Value;
            }
            else
            {
                IntervalMin = DefaultIntervalMinSeconds;
                IntervalMax = DefaultIntervalMaxSeconds;
            }

            // Same three-tier rule as IntervalMin/IntervalMax above, applied to the separate
            // loop-count unit instead of seconds.
            if (loopCountMin.HasValue && loopCountMax.HasValue)
            {
                LoopCountMin = Math.Min(loopCountMin.Value, loopCountMax.Value);
                LoopCountMax = Math.Max(loopCountMin.Value, loopCountMax.Value);
            }
            else if (loopCountMin.HasValue)
            {
                LoopCountMin = LoopCountMax = loopCountMin.Value;
            }
            else if (loopCountMax.HasValue)
            {
                LoopCountMin = LoopCountMax = loopCountMax.Value;
            }
            else
            {
                LoopCountMin = DefaultLoopCountMin;
                LoopCountMax = DefaultLoopCountMax;
            }
        }

        /// <summary>Source rectangle for a given zero-based frame index within this clip's row.</summary>
        public Rectangle GetSourceRect(int frameIndex)
        {
            return new Rectangle(frameIndex * FrameWidth, Row * FrameHeight, FrameWidth, FrameHeight);
        }

        /// <summary>Frame-centered origin unless an explicit per-clip origin was declared.</summary>
        public Vector2 GetOrigin()
        {
            return Origin ?? new Vector2(FrameWidth / 2f, FrameHeight / 2f);
        }

        /// <summary>
        /// Lazily requests (and caches) this clip's texture. Uses ImmediateLoad rather than the
        /// default async load — these are small, one-time-per-clip sheet requests, and a blocking
        /// load here avoids a blank/placeholder frame flashing the first time an NPC plays this
        /// clip mid-game.
        /// </summary>
        public Asset<Texture2D> GetTexture()
        {
            if (_texture == null)
                _texture = ModContent.Request<Texture2D>(TexturePath, AssetRequestMode.ImmediateLoad);
            return _texture;
        }
    }
}
