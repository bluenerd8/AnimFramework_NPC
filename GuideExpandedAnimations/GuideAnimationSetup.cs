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

            // Alert state group (see ANIMFRAMEWORK_NEW_FEATURES.md's Alert notes): registered via
            // RegisterStateGroupDetector, not the plain detector list — this just reports whether
            // Alert is active ("Alert" or null), and AnimationController's own Walking-gate/
            // DetermineIdleState resolution decides which of AlertWalking/AlertIdle/AlertBlink/
            // AlertSpecialIdle actually applies (falling back to the bare state whenever a
            // grouped flavor isn't authored). No velocity branching needed here at all — that's
            // exactly the point of the group mechanism over the old ad hoc detector.
            AnimationController.RegisterStateGroupDetector(DetectAlertGroup);
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
