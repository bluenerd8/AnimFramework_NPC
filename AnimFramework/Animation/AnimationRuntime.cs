using System.Collections.Generic;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Per-NPC-instance animation state. Never share one across instances: both attachment
    /// classes null their reference in <c>Clone</c> so each spawned NPC builds its own, while
    /// still sharing the cached, immutable <see cref="AnimationSet"/>.
    ///
    /// All times are <see cref="AnimationClock.Now"/> readings; null means "not set yet".
    /// </summary>
    public class AnimationRuntime
    {
        public AnimationRuntime(AnimationSet set)
        {
            Set = set;
        }

        public AnimationSet Set { get; }

        /// <summary>State chosen by the last update, or null when no authored clip applies (vanilla draws instead).</summary>
        public string CurrentState { get; set; }

        /// <summary>Whether state has been resolved at least once. Lets the first draw resolve it when no update has run yet (e.g. bestiary icons).</summary>
        public bool HasUpdated { get; set; }

        public AnimationPlayer StatePlayer { get; } = new AnimationPlayer();
        public Dictionary<AnimationClip, AnimationPlayer> OverlayPlayers { get; } = new Dictionary<AnimationClip, AnimationPlayer>();
        public HashSet<AnimationClip> ActiveOverlays { get; } = new HashSet<AnimationClip>();

        // --- Attacking: pulsed by TownNPCAttackProj/TownNPCAttackMagic ---
        public double? LastAttackSignalAt { get; set; }

        /// <summary>Bumped once per attack signal, so a Once-mode clip plays exactly once per attack.</summary>
        public int AttackSignalId { get; set; }
        public int LastConsumedAttackSignalId { get; set; }

        // --- Talk: player dialog opening (edge-detected) or NPC-to-NPC emote chatter ---
        public int TalkSignalId { get; set; }
        public int LastConsumedTalkSignalId { get; set; }
        public bool WasPlayerTalking { get; set; }

        // --- Timers ---
        /// <summary>When the Blink overlay next fires.</summary>
        public double? NextBlinkAt { get; set; }

        /// <summary>When SpecialIdle next fires (shared across groups).</summary>
        public double? NextSpecialIdleAt { get; set; }

        /// <summary>Completed Idle loops before the next IdleBlink (shared across groups).</summary>
        public int NextIdleBlinkAtLoopCount { get; set; }

        // --- State groups: held briefly after the raw signal drops to avoid flicker ---
        public string LastActiveGroup { get; set; }
        public double? LastGroupSignalAt { get; set; }
    }
}
