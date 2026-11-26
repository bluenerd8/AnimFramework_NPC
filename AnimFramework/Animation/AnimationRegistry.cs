using System;
using System.Collections.Generic;
using Terraria.ModLoader;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Opt-in lookup from NPC type to its <see cref="AnimationSet"/>, for animating NPCs you
    /// don't own the class of (vanilla or another mod's). Content mods call
    /// <see cref="Register"/> from their own <c>PostSetupContent</c>. Unregistered types are
    /// never touched by <see cref="AnimatedGlobalNPC"/>.
    /// </summary>
    public static class AnimationRegistry
    {
        private sealed class Entry
        {
            public Func<AnimationSet> Builder;
            public AnimationSet Set;
            public bool Attempted;
        }

        private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();

        /// <summary>
        /// Registers a set builder for an NPC type. The builder runs the first time that NPC is
        /// updated or drawn, and its result is cached. If the builder throws, the error is logged
        /// once and that NPC falls back to vanilla drawing.
        ///
        /// Registering a type that's already registered replaces the earlier entry and logs a
        /// warning, so a clash between two content mods shows up in client.log.
        /// </summary>
        public static void Register(int npcType, Func<AnimationSet> setBuilder)
        {
            if (setBuilder == null)
                throw new ArgumentNullException(nameof(setBuilder));

            if (Entries.ContainsKey(npcType))
                Log.Warn($"AnimFramework: NPC type {npcType} was registered more than once; the latest registration replaces the earlier one.");

            Entries[npcType] = new Entry { Builder = setBuilder };
        }

        public static void Unregister(int npcType)
        {
            Entries.Remove(npcType);
        }

        public static bool IsRegistered(int npcType)
        {
            return Entries.ContainsKey(npcType);
        }

        /// <summary>Gets the built set, building it on first call. Returns false if unregistered or if building failed.</summary>
        public static bool TryGet(int npcType, out AnimationSet set)
        {
            set = null;
            Entry entry;
            if (!Entries.TryGetValue(npcType, out entry))
                return false;

            if (!entry.Attempted)
            {
                entry.Attempted = true;
                try
                {
                    entry.Set = entry.Builder();
                }
                catch (Exception ex)
                {
                    Log.Error($"AnimFramework: building the animation set for NPC type {npcType} failed; that NPC will use vanilla drawing.", ex);
                }
            }

            set = entry.Set;
            return set != null;
        }

        internal static void Clear()
        {
            Entries.Clear();
        }

        private static log4net.ILog Log
        {
            get { return ModContent.GetInstance<global::AnimFramework.AnimFramework>().Logger; }
        }
    }
}
