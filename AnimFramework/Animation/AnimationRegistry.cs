using System;
using System.Collections.Generic;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Opt-in lookup table from NPC type to its <see cref="AnimationSet"/>. Content mods call
    /// <see cref="Register"/> for exactly the NPC types they own (in their own PostSetupContent),
    /// mirroring standard cross-mod compatibility patterns (Mod.Call()-style APIs, DI container
    /// registration) — the framework never scans installed mods or guesses. NPC types not
    /// present here cost nothing and are left untouched by <see cref="AnimatedGlobalNPC"/>.
    /// </summary>
    public static class AnimationRegistry
    {
        // Lazy: Register() itself is just storing a delegate; the (possibly JSON/texture
        // loading) set builder only actually runs the first time that NPC type is looked up,
        // and the result is cached from then on.
        private static readonly Dictionary<int, Lazy<AnimationSet>> Builders = new Dictionary<int, Lazy<AnimationSet>>();

        /// <summary>
        /// Registers an animation set builder for the given NPC type (a vanilla NPCID.* constant,
        /// or another mod's exposed type). If two content mods register the same NPC type, the
        /// most recently registered one wins — silently, by design of the underlying Dictionary.
        /// Mod authors should be aware of this rather than treating it as a bug.
        /// </summary>
        public static void Register(int npcType, Func<AnimationSet> setBuilder)
        {
            Builders[npcType] = new Lazy<AnimationSet>(setBuilder);
        }

        public static void Unregister(int npcType)
        {
            Builders.Remove(npcType);
        }

        public static bool IsRegistered(int npcType)
        {
            return Builders.ContainsKey(npcType);
        }

        public static bool TryGet(int npcType, out AnimationSet set)
        {
            Lazy<AnimationSet> lazy;
            if (Builders.TryGetValue(npcType, out lazy))
            {
                set = lazy.Value;
                return true;
            }

            set = null;
            return false;
        }
    }
}
