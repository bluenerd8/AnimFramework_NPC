using AnimFramework.Animation;
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
        public override void PostSetupContent()
        {
            AnimationRegistry.Register(NPCID.Guide, BuildGuideAnimationSet);
        }

        private AnimationSet BuildGuideAnimationSet()
        {
            return SheetLayoutLoader.Load(Mod, "Assets/Guide_Sheet.json");
        }
    }
}
