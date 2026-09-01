using Terraria.ModLoader;

namespace AnimFramework
{
    /// <summary>
    /// Library mod — zero gameplay content, zero NPC-specific data (see ANIMFRAMEWORK_SPEC.md's
    /// "Project Goal" section). Everything here lives under AnimFramework.Animation. Content
    /// mods depend on this via modReferences in their own build.txt and call
    /// AnimFramework.Animation.AnimationRegistry.Register(...) for the NPC types they own.
    /// </summary>
    public class AnimFramework : Mod
    {
    }
}
