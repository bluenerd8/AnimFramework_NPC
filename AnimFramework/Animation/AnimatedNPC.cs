using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Base class for a brand-new NPC built from scratch with AnimFramework-driven animation.
    /// To attach animation to an NPC you don't own the class of (a vanilla NPC, or another
    /// mod's), use <see cref="AnimationRegistry"/> + <see cref="AnimatedGlobalNPC"/> instead —
    /// see the Modularity section of ANIMFRAMEWORK_SPEC.md for why both paths are kept.
    /// </summary>
    public abstract class AnimatedNPC : ModNPC
    {
        // Keyed by concrete subclass so the (possibly JSON-loading) animation set is only ever
        // built once per NPC *type*, no matter how many instances get spawned.
        private static readonly Dictionary<Type, AnimationSet> SetCache = new Dictionary<Type, AnimationSet>();

        private AnimationRuntime _runtime;

        /// <summary>Builds this NPC type's animation set. Called once and cached.</summary>
        protected abstract AnimationSet BuildAnimationSet();

        private AnimationRuntime Runtime
        {
            get
            {
                if (_runtime == null)
                {
                    Type type = GetType();
                    AnimationSet set;
                    if (!SetCache.TryGetValue(type, out set))
                    {
                        set = BuildAnimationSet();
                        SetCache[type] = set;
                    }
                    _runtime = new AnimationRuntime(set);
                }
                return _runtime;
            }
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            return AnimationController.PreDraw(NPC, Runtime, spriteBatch, screenPos, drawColor);
        }

        // Without this, tModLoader's default MemberwiseClone would copy our _runtime reference
        // as-is onto every spawned instance of this NPC type, so all of them would share one
        // Stopwatch and animate in lockstep. Nulling it here forces each clone to lazily build
        // its own fresh AnimationRuntime (still sharing the cached, immutable AnimationSet).
        public override ModNPC Clone(NPC newEntity)
        {
            AnimatedNPC clone = (AnimatedNPC)base.Clone(newEntity);
            clone._runtime = null;
            return clone;
        }
    }
}
