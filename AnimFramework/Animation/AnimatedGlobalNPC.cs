using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.ModLoader;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Attaches AnimFramework playback to any NPC type registered with
    /// <see cref="AnimationRegistry"/>, vanilla or another mod's, without owning its ModNPC
    /// class. Only instanced for registered types, so everything else costs nothing.
    /// </summary>
    public class AnimatedGlobalNPC : GlobalNPC
    {
        public override bool InstancePerEntity
        {
            get { return true; }
        }

        private AnimationRuntime _runtime;

        public override bool AppliesToEntity(NPC entity, bool lateInstantiation)
        {
            return AnimationRegistry.IsRegistered(entity.type);
        }

        private AnimationRuntime GetRuntime(NPC npc)
        {
            AnimationSet set;
            if (_runtime == null && AnimationRegistry.TryGet(npc.type, out set))
                _runtime = new AnimationRuntime(set);
            return _runtime;
        }

        public override void PostAI(NPC npc)
        {
            AnimationController.Update(npc, GetRuntime(npc));
        }

        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            return AnimationController.PreDraw(npc, GetRuntime(npc), spriteBatch, screenPos, drawColor);
        }

        // Attack triggers for AttackType 0 (throw), 1 (shoot) and 2 (magic). Observational only:
        // the ref parameters are never touched.
        public override void TownNPCAttackProj(NPC npc, ref int projType, ref int attackDelay)
        {
            AnimationController.NotifyAttackTriggered(npc, GetRuntime(npc));
        }

        public override void TownNPCAttackMagic(NPC npc, ref float auraLightMultiplier)
        {
            AnimationController.NotifyAttackTriggered(npc, GetRuntime(npc));
        }

        // NPC-to-NPC chatter: only counts when the emote has a conversation target.
        public override int? PickEmote(NPC npc, Player closestPlayer, List<int> emoteList, WorldUIAnchor otherAnchor)
        {
            if (otherAnchor != null)
                AnimationController.NotifyTalkTriggered(npc, GetRuntime(npc));

            return base.PickEmote(npc, closestPlayer, emoteList, otherAnchor);
        }

        // MemberwiseClone would otherwise share one runtime (and its timers) across every
        // instance of the type, making them animate in lockstep.
        public override GlobalNPC Clone(NPC from, NPC to)
        {
            AnimatedGlobalNPC clone = (AnimatedGlobalNPC)base.Clone(from, to);
            clone._runtime = null;
            return clone;
        }
    }
}
