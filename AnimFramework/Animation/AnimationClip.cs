using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Immutable definition of one animation clip: which row of a sprite sheet it reads, how
    /// many frames, how fast, and how it loops. Shared by every NPC instance that plays it;
    /// per-instance progress lives in <see cref="AnimationPlayer"/>.
    /// </summary>
    public class AnimationClip
    {
        /// <summary>State/overlay name this clip was declared under (e.g. "Idle", "Blink").</summary>
        public string State { get; }
        public string TexturePath { get; }

        /// <summary>
        /// Optional sheet drawn instead of <see cref="TexturePath"/> for a town NPC's shimmered
        /// variant (<c>npc.IsShimmerVariant</c>). Must use the same layout. Null means the
        /// normal sheet is used for both.
        /// </summary>
        public string ShimmerTexturePath { get; }

        public int FrameWidth { get; }
        public int FrameHeight { get; }
        public int FrameCount { get; }
        public int Row { get; }
        public float Fps { get; }
        public float Duration { get; }
        public LoopMode Loop { get; }
        public Vector2? Origin { get; }

        /// <summary>Seconds between firings, for timer-driven features (Blink overlay, SpecialIdle).</summary>
        public float IntervalMin { get; }
        public float IntervalMax { get; }

        /// <summary>Completed loops between firings, for loop-count-driven features (IdleBlink). A different unit from the Interval pair on purpose.</summary>
        public int LoopCountMin { get; }
        public int LoopCountMax { get; }

        private const float DefaultIntervalMinSeconds = 2.5f;
        private const float DefaultIntervalMaxSeconds = 6f;
        private const int DefaultLoopCountMin = 0;
        private const int DefaultLoopCountMax = 2;

        private Asset<Texture2D> _texture;
        private Asset<Texture2D> _shimmerTexture;

        public AnimationClip(string state, string texturePath, int frameWidth, int frameHeight, int frameCount,
            int row, float fps, float duration, LoopMode loop, Vector2? origin = null,
            float? intervalMin = null, float? intervalMax = null,
            int? loopCountMin = null, int? loopCountMax = null,
            string shimmerTexturePath = null)
        {
            if (frameCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameCount), "AnimationClip requires at least 1 frame.");

            State = state;
            TexturePath = texturePath;
            ShimmerTexturePath = string.IsNullOrEmpty(shimmerTexturePath) ? null : shimmerTexturePath;
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            FrameCount = frameCount;
            Row = row;
            Loop = loop;
            Origin = origin;

            // fps and duration describe the same cycle; derive whichever wasn't given.
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
                Fps = 1f;
                Duration = frameCount;
            }

            // Range rule for both pairs: both given = used as-is (swapped if backwards), one
            // given = fixed value, neither = framework default.
            if (intervalMin.HasValue && intervalMax.HasValue)
            {
                IntervalMin = Math.Min(intervalMin.Value, intervalMax.Value);
                IntervalMax = Math.Max(intervalMin.Value, intervalMax.Value);
            }
            else if (intervalMin.HasValue || intervalMax.HasValue)
            {
                IntervalMin = IntervalMax = (intervalMin ?? intervalMax).Value;
            }
            else
            {
                IntervalMin = DefaultIntervalMinSeconds;
                IntervalMax = DefaultIntervalMaxSeconds;
            }

            if (loopCountMin.HasValue && loopCountMax.HasValue)
            {
                LoopCountMin = Math.Min(loopCountMin.Value, loopCountMax.Value);
                LoopCountMax = Math.Max(loopCountMin.Value, loopCountMax.Value);
            }
            else if (loopCountMin.HasValue || loopCountMax.HasValue)
            {
                LoopCountMin = LoopCountMax = (loopCountMin ?? loopCountMax).Value;
            }
            else
            {
                LoopCountMin = DefaultLoopCountMin;
                LoopCountMax = DefaultLoopCountMax;
            }
        }

        /// <summary>Source rectangle for a zero-based frame index within this clip's row.</summary>
        public Rectangle GetSourceRect(int frameIndex)
        {
            return new Rectangle(frameIndex * FrameWidth, Row * FrameHeight, FrameWidth, FrameHeight);
        }

        /// <summary>Frame center unless an explicit origin was declared.</summary>
        public Vector2 GetOrigin()
        {
            return Origin ?? new Vector2(FrameWidth / 2f, FrameHeight / 2f);
        }

        /// <summary>
        /// Lazily requests (and caches) the sheet. ImmediateLoad avoids a blank frame the first
        /// time a clip plays. Falls back to the normal sheet when no shimmer sheet is declared.
        /// </summary>
        public Asset<Texture2D> GetTexture(bool shimmerVariant = false)
        {
            if (shimmerVariant && ShimmerTexturePath != null)
            {
                if (_shimmerTexture == null)
                    _shimmerTexture = ModContent.Request<Texture2D>(ShimmerTexturePath, AssetRequestMode.ImmediateLoad);
                return _shimmerTexture;
            }

            if (_texture == null)
                _texture = ModContent.Request<Texture2D>(TexturePath, AssetRequestMode.ImmediateLoad);
            return _texture;
        }
    }
}
