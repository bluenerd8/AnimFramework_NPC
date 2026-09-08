using AnimFramework.Animation;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GuideExpandedAnimations
{
    /// <summary>
    /// Registers the vanilla Guide's AnimFramework animation set via the Registry path (not the
    /// AnimatedNPC base class) — NPCID.Guide is a vanilla NPC we don't own the class of. See the
    /// Modularity section of ANIMFRAMEWORK_SPEC.md for why this content mod uses this path.
    /// </summary>
    public class GuideAnimationSetup : ModSystem
    {
        // Default vanilla uses when NPCID.Sets.DangerDetectRange[Type] is left at its own -1
        // sentinel — confirmed against docs.tmodloader.net's NPCID.Sets reference: "distance, in
        // pixels, that a town NPC will check for enemies to attack... defaults to -1, which uses
        // a default of 200 pixels."
        private const int DefaultDangerDetectRange = 200;

        public override void PostSetupContent()
        {
            AnimationRegistry.Register(NPCID.Guide, BuildGuideAnimationSet);

            // Basic Alert state, Idle-only for now (see ANIMFRAMEWORK_NEW_FEATURES.md's Alert
            // notes): a real competing state, not an Idle-family variant like Blink — registered
            // via the detector list so it ranks above Walking/Idle in DetermineState's order
            // (same slot Falling-style checks were designed for), but still below
            // Attacking/Talk/Sitting, which is exactly right — an NPC actually mid-attack should
            // show Attacking, not Alert. No AlertWalking authored yet, so the detector itself
            // steps aside (returns null) whenever the NPC has velocity, deferring to ordinary
            // Walking rather than freezing a stationary Alert pose on a moving NPC.
            AnimationController.RegisterStateDetector(DetectAlert);
        }

        private AnimationSet BuildGuideAnimationSet()
        {
            return SheetLayoutLoader.Load(Mod, "Assets/Guide_Sheet.json");
        }

        private static string DetectAlert(NPC npc)
        {
            if (npc.velocity != Vector2.Zero)
                return null; // no AlertWalking yet — stay out of Walking's way entirely

            int range = NPCID.Sets.DangerDetectRange[npc.type];
            if (range <= 0)
                range = DefaultDangerDetectRange;

            return IsHostileNearby(npc, range) ? "AlertIdle" : null;
        }

        /// <summary>
        /// Heuristic, not a confirmed single vanilla API: "hostile" here means active, not
        /// friendly, and able to actually deal damage — the same rough shape vanilla's own
        /// engagement logic almost certainly checks internally, but unlike DangerDetectRange
        /// itself this exact combination isn't something docs.tmodloader.net spells out field by
        /// field. Good enough for a visual "is something dangerous nearby" cue; not something to
        /// treat as an authoritative "vanilla will attack this" signal.
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
