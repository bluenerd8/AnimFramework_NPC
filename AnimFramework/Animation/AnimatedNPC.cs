using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.ModLoader;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Base class for a new NPC with AnimFramework-driven animation. For NPCs you don't own the
    /// class of, use <see cref="AnimationRegistry"/> instead.
    ///
    /// If you override <c>PostAI</c>, <c>PreDraw</c>, <c>TownNPCAttackProj</c>,
    /// <c>TownNPCAttackMagic</c>, <c>PickEmote</c> or <c>Clone</c>, call the base method or
    /// the animation stops updating.
    /// </summary>
    public abstract class AnimatedNPC : ModNPC
    {
        // One set per concrete subclass, however many instances spawn. A null value means the
        // build failed (already logged); that type then uses vanilla drawing.
        private static readonly Dictionary<Type, AnimationSet> SetCache = new Dictionary<Type, AnimationSet>();

        private AnimationRuntime _runtime;

        /// <summary>Builds this NPC type's animation set. Called once per type and cached.</summary>
        protected abstract AnimationSet BuildAnimationSet();

        private AnimationRuntime Runtime
        {
            get
            {
                if (_runtime == null)
                {
                    AnimationSet set = GetOrBuildSet();
                    if (set != null)
                        _runtime = new AnimationRuntime(set);
                }
                return _runtime;
            }
        }

        private AnimationSet GetOrBuildSet()
        {
            Type type = GetType();
            AnimationSet set;
            if (SetCache.TryGetValue(type, out set))
                return set;

            try
            {
                set = BuildAnimationSet();
            }
            catch (Exception ex)
            {
                Mod.Logger.Error($"AnimFramework: building the animation set for {type.Name} failed; it will use vanilla drawing.", ex);
                set = null;
            }

            SetCache[type] = set;
            return set;
        }

        internal static void ClearSetCache()
        {
            SetCache.Clear();
        }

        public override void PostAI()
        {
            AnimationController.Update(NPC, Runtime);
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            return AnimationController.PreDraw(NPC, Runtime, spriteBatch, screenPos, drawColor);
        }

        // Same attack and chatter triggers as AnimatedGlobalNPC. The ModNPC signatures drop the
        // leading NPC parameter.
        public override void TownNPCAttackProj(ref int projType, ref int attackDelay)
        {
            AnimationController.NotifyAttackTriggered(NPC, Runtime);
        }

        public override void TownNPCAttackMagic(ref float auraLightMultiplier)
        {
            AnimationController.NotifyAttackTriggered(NPC, Runtime);
        }

        public override int? PickEmote(Player closestPlayer, List<int> emoteList, WorldUIAnchor otherAnchor)
        {
            if (otherAnchor != null)
                AnimationController.NotifyTalkTriggered(NPC, Runtime);

            return base.PickEmote(closestPlayer, emoteList, otherAnchor);
        }

        // MemberwiseClone would otherwise share one runtime across every spawned instance.
        public override ModNPC Clone(NPC newEntity)
        {
            AnimatedNPC clone = (AnimatedNPC)base.Clone(newEntity);
            clone._runtime = null;
            return clone;
        }
    }
}
