using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.ModLoader;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Attaches AnimFramework playback to any NPC type registered via <see cref="AnimationRegistry"/>
    /// — vanilla or another mod's — without needing to own that NPC's ModNPC class. Opt-in only:
    /// <see cref="AppliesToEntity"/> gates on registry membership, so unregistered NPC types are
    /// never instanced here and cost nothing.
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

        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            AnimationRuntime runtime = GetRuntime(npc);
            return AnimationController.PreDraw(npc, runtime, spriteBatch, screenPos, drawColor);
        }

        // Attacking-state trigger for AttackType 0 (throw) and 1 (shoot) — see
        // AnimationController.NotifyAttackTriggered for why this hook is safe to observe
        // without touching its ref parameters, and why melee (type 3) has no equivalent here.
        // Signature confirmed against docs.tmodloader.net's GlobalNPC reference (1.4-stable).
        public override void TownNPCAttackProj(NPC npc, ref int projType, ref int attackDelay)
        {
            AnimationController.NotifyAttackTriggered(npc, GetRuntime(npc));
        }

        // Attacking-state trigger for AttackType 2 (magic) — same caveats as TownNPCAttackProj above.
        public override void TownNPCAttackMagic(NPC npc, ref float auraLightMultiplier)
        {
            AnimationController.NotifyAttackTriggered(npc, GetRuntime(npc));
        }

        // Talk-state trigger for the NPC-to-NPC (and NPC-to-player-without-dialog-open) ambient
        // chatter case — see AnimatedNPC's copy of this override and
        // AnimationController.NotifyTalkTriggered's doc comment for the confirmed hook and the
        // otherAnchor gate.
        public override int? PickEmote(NPC npc, Player closestPlayer, List<int> emoteList, WorldUIAnchor otherAnchor)
        {
            if (otherAnchor != null)
                AnimationController.NotifyTalkTriggered(npc, GetRuntime(npc));

            return base.PickEmote(npc, closestPlayer, emoteList, otherAnchor);
        }

        // Same MemberwiseClone shared-state hazard as AnimatedNPC.Clone (see its comment) —
        // without this, every NPC instance of a registered type would share one Stopwatch.
        public override GlobalNPC Clone(NPC from, NPC to)
        {
            AnimatedGlobalNPC clone = (AnimatedGlobalNPC)base.Clone(from, to);
            clone._runtime = null;
            return clone;
        }
    }
}
