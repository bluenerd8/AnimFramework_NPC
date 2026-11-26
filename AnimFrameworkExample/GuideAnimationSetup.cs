using AnimFramework.Animation;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AnimFrameworkExample
{
    /// <summary>
    /// Registers the vanilla Guide's animation set through the registry path (we don't own the
    /// Guide's class), plus an "Alert" state group for when hostiles are nearby.
    /// </summary>
    public class GuideAnimationSetup : ModSystem
    {
        // Vanilla's fallback when NPCID.Sets.DangerDetectRange is left at -1.
        private const int DefaultDangerDetectRange = 200;

        public override void PostSetupContent()
        {
            AnimationRegistry.Register(NPCID.Guide, BuildGuideAnimationSet);

            // Scoped to the Guide: other mods' animated NPCs are unaffected. The framework picks
            // AlertWalking/AlertIdle/AlertIdleBlink/AlertSpecialIdle when authored, else the bare state.
            AnimationController.RegisterStateGroupDetector(NPCID.Guide, DetectAlertGroup);
        }

        private AnimationSet BuildGuideAnimationSet()
        {
            return SheetLayoutLoader.Load(Mod, "Assets/Guide_Sheet.json");
        }

        private static string DetectAlertGroup(NPC npc)
        {
            int range = NPCID.Sets.DangerDetectRange[npc.type];
            if (range <= 0)
                range = DefaultDangerDetectRange;

            return IsHostileNearby(npc, range) ? "Alert" : null;
        }

        /// <summary>
        /// Heuristic for "something dangerous is nearby": an active, non-friendly NPC that deals
        /// damage, within range. A visual cue only, not a guarantee vanilla will engage it.
        /// </summary>
        private static bool IsHostileNearby(NPC npc, int range)
        {
            float rangeSq = (float)range * range;

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC other = Main.npc[i];
                if (!other.active || other.friendly || other.damage <= 0)
                    continue;

                if (Vector2.DistanceSquared(npc.Center, other.Center) <= rangeSq)
                    return true;
            }

            return false;
        }
    }
}
