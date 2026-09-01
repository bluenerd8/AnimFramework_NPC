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

        private Asset<Texture2D> _texture;

        public AnimationClip(string state, string texturePath, int frameWidth, int frameHeight, int frameCount,
            int row, float fps, float duration, LoopMode loop, Vector2? origin = null)
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
