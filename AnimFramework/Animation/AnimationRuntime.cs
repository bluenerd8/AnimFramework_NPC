using System.Collections.Generic;
using System.Diagnostics;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Per-instance runtime animation state for one NPC: which state is currently playing, the
    /// player driving it, and one player per currently-active overlay.
    ///
    /// Must never be shared by reference across NPC instances of the same type — tModLoader's
    /// default MemberwiseClone would otherwise copy this reference onto every spawned instance,
    /// so all of them would animate in perfect lockstep off one shared Stopwatch. See
    /// AnimatedNPC.Clone / AnimatedGlobalNPC.Clone, which null their runtime field on clone so
    /// each spawned NPC lazily builds its own fresh instance here, while still sharing the same
    /// cached, immutable <see cref="AnimationSet"/>.
    /// </summary>
    public class AnimationRuntime
    {
        public AnimationRuntime(AnimationSet set)
        {
            Set = set;
        }

        public AnimationSet Set { get; }
        public string CurrentState { get; set; }
        public AnimationPlayer StatePlayer { get; } = new AnimationPlayer();
        public Dictionary<AnimationClip, AnimationPlayer> OverlayPlayers { get; } = new Dictionary<AnimationClip, AnimationPlayer>();
        public HashSet<AnimationClip> ActiveOverlays { get; } = new HashSet<AnimationClip>();

        /// <summary>
        /// Restarted by <see cref="AnimationController.NotifyAttackTriggered"/> each time a
        /// type-0/1/2 attack hook fires for this NPC. Not running (never restarted) until the
        /// first such hook fires — see <see cref="AnimationController"/>'s Attacking handling.
        /// </summary>
        public Stopwatch AttackSignalAge { get; } = new Stopwatch();

        /// <summary>
        /// Bumped once per <see cref="AnimationController.NotifyAttackTriggered"/> call —
        /// identifies *which* real attack-start signal is currently live, distinct from how long
        /// ago it fired (<see cref="AttackSignalAge"/>). Lets a Once-mode Attacking clip tell
        /// "still the same attack I already played for" from "a genuinely new one arrived" —
        /// see <see cref="LastConsumedAttackSignalId"/> and DetermineAttackingState.
        /// </summary>
        public int AttackSignalId { get; set; }

        /// <summary>The <see cref="AttackSignalId"/> a Once-mode Attacking clip has already played a full cycle for.</summary>
        public int LastConsumedAttackSignalId { get; set; }

        /// <summary>
        /// Bumped once per talk-start edge: either the player's dialog opening with this NPC
        /// (edge-detected in-place from the level-triggered <c>talkNPC</c> flag — see
        /// AnimationController.DetermineTalkingState) or a real
        /// <see cref="AnimationController.NotifyTalkTriggered"/> call (NPC-to-NPC emote-bubble
        /// chatter via <c>GlobalNPC.PickEmote</c>). Same "identify which signal is currently
        /// live, distinct from AttackSignalId" role as its Attacking counterpart, driving the
        /// same "play exactly once per signal" consumption via
        /// <see cref="LastConsumedTalkSignalId"/>.
        /// </summary>
        public int TalkSignalId { get; set; }

        /// <summary>The <see cref="TalkSignalId"/> a Once-mode Talk clip has already played a full cycle for.</summary>
        public int LastConsumedTalkSignalId { get; set; }

        /// <summary>
        /// Last-seen value of <c>Main.LocalPlayer.talkNPC == npc.whoAmI</c> — this flag is
        /// level-triggered (true for the whole time the player's dialog box is open), so this is
        /// what lets DetermineTalkingState detect the rising edge itself and bump
        /// <see cref="TalkSignalId"/> exactly once per conversation, rather than once per tick.
        /// </summary>
        public bool WasPlayerTalking { get; set; }

        /// <summary>Real-time clock behind the Blink timed overlay — see AnimationController.ShouldBlink.</summary>
        public Stopwatch BlinkClock { get; } = new Stopwatch();

        /// <summary>Elapsed-seconds-on-<see cref="BlinkClock"/> timestamp for this NPC's next scheduled blink.</summary>
        public double NextBlinkAt { get; set; }

        /// <summary>Real-time clock behind the SpecialIdle occasional-variant timer — see AnimationController.DetermineIdleState.</summary>
        public Stopwatch SpecialIdleClock { get; } = new Stopwatch();

        /// <summary>Elapsed-seconds-on-<see cref="SpecialIdleClock"/> timestamp for this NPC's next scheduled SpecialIdle roll.</summary>
        public double NextSpecialIdleAt { get; set; }

        /// <summary>
        /// Threshold, in completed Idle loops (see <see cref="AnimationPlayer.GetCompletedLoops"/>),
        /// for this NPC's next Blink. Distinct from <see cref="NextBlinkAt"/> above — that field
        /// belongs to the older timed-overlay Blink mechanism (seconds-based, layers on top of
        /// any state); this one belongs to the Idle-paired Blink *state* (loop-count-based,
        /// Idle-only, no override/hold-to-completion). The two are independent mechanisms a
        /// content mod can choose between — see AnimationController.DetermineIdleState. Needs no
        /// separate clock of its own: it's compared directly against
        /// <see cref="AnimationRuntime.StatePlayer"/>'s own completed-loop count, which already
        /// resets naturally whenever Idle is freshly (re-)entered.
        /// </summary>
        public int NextBlinkAtLoopCount { get; set; }
    }
}
