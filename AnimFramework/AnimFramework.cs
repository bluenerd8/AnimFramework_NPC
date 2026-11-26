using AnimFramework.Animation;
using Terraria.ModLoader;

namespace AnimFramework
{
    /// <summary>
    /// Library mod: no gameplay content of its own. Content mods reference it with
    /// <c>modReferences = AnimFramework</c> in their build.txt, then register their NPCs through
    /// <see cref="AnimationRegistry"/> or inherit <see cref="AnimatedNPC"/>.
    /// </summary>
    public class AnimFramework : Mod
    {
        // Static state holds delegates and assets from content mods, so it has to be cleared
        // for tModLoader to fully unload those assemblies on Build + Reload.
        public override void Unload()
        {
            AnimationRegistry.Clear();
            AnimationController.ClearDetectors();
            AnimatedNPC.ClearSetCache();
            AnimationClock.Reset();
        }
    }
}
