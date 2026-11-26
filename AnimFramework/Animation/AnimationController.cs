using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace AnimFramework.Animation
{
    /// <summary>Returns a state name (or group prefix) for this NPC this tick, or null for none.</summary>
    public delegate string StateDetector(NPC npc);

    /// <summary>
    /// Shared state-machine and draw logic for both attachment paths (<see cref="AnimatedNPC"/>
    /// and <see cref="AnimationRegistry"/>/<see cref="AnimatedGlobalNPC"/>).
    ///
    /// State is resolved once per game tick in <see cref="Update"/> (called from PostAI), and
    /// drawing only picks the frame. That keeps the per-tick logic cheap at high FPS and keeps
    /// signals flowing while an NPC is off-screen.
    ///
    /// Resolution priority, highest first:
    /// Attacking, Talk, Sitting (not wired yet), registered detectors, an in-progress Once-mode
    /// SpecialIdle, Walking, then Idle / IdleBlink / SpecialIdle. Any state without an authored
    /// clip is skipped, and if nothing applies vanilla draws the NPC.
    /// </summary>
    public static class AnimationController
    {
        // ---------------------------------------------------------------------------------
        // Detector registration (per NPC type)
        // ---------------------------------------------------------------------------------

        private static readonly Dictionary<int, List<StateDetector>> Detectors = new Dictionary<int, List<StateDetector>>();
        private static readonly Dictionary<int, List<StateDetector>> GroupDetectors = new Dictionary<int, List<StateDetector>>();

        /// <summary>
        /// Adds a state check for one NPC type. Runs after Attacking/Talk/Sitting and before the
        /// Walking/Idle fallback. The first detector to return a state this NPC has a clip for
        /// wins. Example: <c>npc =&gt; npc.velocity.Y &gt; 2f ? "Falling" : null</c>.
        /// </summary>
        public static void RegisterStateDetector(int npcType, StateDetector detector)
        {
            Add(Detectors, npcType, detector);
        }

        /// <summary>
        /// Adds a state-group selector for one NPC type. It returns a prefix (e.g. "Alert") that
        /// applies to Walking/Idle/IdleBlink/SpecialIdle, so "Alert" tries "AlertWalking" before
        /// "Walking". Return null or empty for no group. The first non-empty result wins.
        /// Attacking/Talk/Sitting are never grouped.
        /// </summary>
        public static void RegisterStateGroupDetector(int npcType, StateDetector detector)
        {
            Add(GroupDetectors, npcType, detector);
        }

        private static void Add(Dictionary<int, List<StateDetector>> map, int npcType, StateDetector detector)
        {
            if (detector == null)
                return;

            List<StateDetector> list;
            if (!map.TryGetValue(npcType, out list))
            {
                list = new List<StateDetector>();
                map[npcType] = list;
            }
            list.Add(detector);
        }

        internal static void ClearDetectors()
        {
            Detectors.Clear();
            GroupDetectors.Clear();
        }

        // ---------------------------------------------------------------------------------
        // Entry points (called by the attachment classes)
        // ---------------------------------------------------------------------------------

        /// <summary>Resolves this tick's state and restarts playback on a state change. Called from PostAI.</summary>
        internal static void Update(NPC npc, AnimationRuntime runtime)
        {
            if (runtime == null || runtime.Set == null)
                return;

            AnimationClock.Sync();
            runtime.HasUpdated = true;

            string state = DetermineState(npc, runtime);
            AnimationClip clip;
            if (state == null || !runtime.Set.States.TryGetValue(state, out clip))
            {
                runtime.CurrentState = null; // nothing authored applies: vanilla draws this NPC
                return;
            }

            if (runtime.CurrentState != state)
            {
                runtime.CurrentState = state;
                runtime.StatePlayer.Play(clip);
            }
        }

        /// <summary>Draws the current state and any active overlays. Returns false when it drew the NPC (suppresses vanilla).</summary>
        internal static bool PreDraw(NPC npc, AnimationRuntime runtime, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (runtime == null || runtime.Set == null)
                return true;

            AnimationClock.Sync();

            // Bestiary icons and freshly spawned NPCs can be drawn before any PostAI has run.
            if (!runtime.HasUpdated)
                Update(npc, runtime);

            AnimationClip clip;
            if (runtime.CurrentState == null || !runtime.Set.States.TryGetValue(runtime.CurrentState, out clip))
                return true;

            Color color = npc.GetAlpha(drawColor);
            DrawClip(npc, clip, runtime.StatePlayer, spriteBatch, screenPos, color);
            DrawOverlays(npc, runtime, spriteBatch, screenPos, color);
            return false;
        }

        /// <summary>
        /// Attack signal from TownNPCAttackProj/TownNPCAttackMagic (AttackType 0/1/2). Melee
        /// (type 3) has no hook, so those NPCs never enter Attacking. Observational only.
        /// </summary>
        internal static void NotifyAttackTriggered(NPC npc, AnimationRuntime runtime)
        {
            if (runtime == null)
                return;

            runtime.LastAttackSignalAt = AnimationClock.Now;
            runtime.AttackSignalId++;
        }

        /// <summary>
        /// Talk signal from PickEmote when the emote has a conversation target (NPC-to-NPC
        /// chatter). Solo mood emotes don't count. Observational only.
        /// </summary>
        internal static void NotifyTalkTriggered(NPC npc, AnimationRuntime runtime)
        {
            if (runtime == null)
                return;

            runtime.TalkSignalId++;
        }

        // ---------------------------------------------------------------------------------
        // State resolution
        // ---------------------------------------------------------------------------------

        private static string DetermineState(NPC npc, AnimationRuntime runtime)
        {
            AnimationSet set = runtime.Set;

            string state = DetermineAttackingState(npc, runtime, set)
                ?? DetermineTalkingState(npc, runtime, set)
                ?? DetermineSittingState(npc, set)
                ?? RunDetectors(npc, set)
                ?? HoldInProgressSpecialIdle(npc, runtime, set);
            if (state != null)
                return state;

            string groupPrefix = ResolveGroupPrefix(npc, runtime);

            string walkName;
            AnimationClip walkClip;
            if (npc.velocity != Vector2.Zero && TryResolveGroupedClip(set, groupPrefix, AnimStates.Walking, out walkName, out walkClip))
                return walkName;

            return DetermineIdleState(runtime, set, groupPrefix);
        }

        // Once-mode clips hold until finished. Loop-mode (channeled) clips stay active while
        // attack pulses keep arriving, then lapse after this buffer. The buffer is sized from
        // vanilla's own NPCID.Sets.AttackTime.
        private const double MinAttackSignalBufferSeconds = 0.2;

        private static double AttackSignalBufferSeconds(NPC npc)
        {
            return Math.Max(MinAttackSignalBufferSeconds, NPCID.Sets.AttackTime[npc.type] / 60.0);
        }

        private static string DetermineAttackingState(NPC npc, AnimationRuntime runtime, AnimationSet set)
        {
            AnimationClip clip;
            if (!set.States.TryGetValue(AnimStates.Attacking, out clip))
                return null;

            bool signalLive = runtime.LastAttackSignalAt.HasValue
                && AnimationClock.Now - runtime.LastAttackSignalAt.Value <= AttackSignalBufferSeconds(npc);

            if (clip.Loop != LoopMode.Once)
                return signalLive ? AnimStates.Attacking : null;

            if (runtime.CurrentState == AnimStates.Attacking && !runtime.StatePlayer.IsFinished)
                return AnimStates.Attacking;

            // One playthrough per attack signal. The buffer can outlast a short clip, so the
            // signal id stops a single attack from replaying it.
            if (signalLive && runtime.AttackSignalId != runtime.LastConsumedAttackSignalId)
            {
                runtime.LastConsumedAttackSignalId = runtime.AttackSignalId;
                return AnimStates.Attacking;
            }

            return null;
        }

        /// <summary>
        /// Talk is a full state, not an overlay. Triggered when the local player opens this NPC's
        /// dialog (rising edge of talkNPC) or by NPC-to-NPC chatter. Once-mode plays one
        /// playthrough per trigger. Loop-mode holds for as long as the player's dialog is open.
        /// </summary>
        private static string DetermineTalkingState(NPC npc, AnimationRuntime runtime, AnimationSet set)
        {
            AnimationClip clip;
            if (!set.States.TryGetValue(AnimStates.Talk, out clip))
                return null;

            bool playerTalking = Main.LocalPlayer.talkNPC == npc.whoAmI;
            if (playerTalking && !runtime.WasPlayerTalking)
                runtime.TalkSignalId++;
            runtime.WasPlayerTalking = playerTalking;

            if (clip.Loop != LoopMode.Once)
                return playerTalking ? AnimStates.Talk : null;

            if (runtime.CurrentState == AnimStates.Talk && !runtime.StatePlayer.IsFinished)
                return AnimStates.Talk;

            if (runtime.TalkSignalId != runtime.LastConsumedTalkSignalId)
            {
                runtime.LastConsumedTalkSignalId = runtime.TalkSignalId;
                return AnimStates.Talk;
            }

            return null;
        }

        /// <summary>
        /// TODO: not wired. <c>NPC</c> exposes no readable "is sitting" field (only the
        /// <c>SitDown(...)</c> placement helper), so this needs in-game testing to find a reliable
        /// signal.
        /// </summary>
        private static string DetermineSittingState(NPC npc, AnimationSet set)
        {
            return null;
        }

        private static string RunDetectors(NPC npc, AnimationSet set)
        {
            List<StateDetector> list;
            if (!Detectors.TryGetValue(npc.type, out list))
                return null;

            for (int i = 0; i < list.Count; i++)
            {
                string result = list[i](npc);
                if (result != null && set.States.ContainsKey(result))
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Lets a Once-mode SpecialIdle (bare or grouped) play to the end instead of dropping
        /// back to Idle on the next tick. Any movement cuts it off immediately.
        /// </summary>
        private static string HoldInProgressSpecialIdle(NPC npc, AnimationRuntime runtime, AnimationSet set)
        {
            if (npc.velocity != Vector2.Zero)
                return null;

            return IsOnceClipInProgress(runtime, set, AnimStates.SpecialIdle) ? runtime.CurrentState : null;
        }

        /// <summary>True when the current state ends with <paramref name="baseState"/> (so grouped flavors match), is Once-mode, and hasn't finished.</summary>
        private static bool IsOnceClipInProgress(AnimationRuntime runtime, AnimationSet set, string baseState)
        {
            if (runtime.CurrentState == null || !runtime.CurrentState.EndsWith(baseState) || runtime.StatePlayer.IsFinished)
                return false;

            AnimationClip clip;
            return set.States.TryGetValue(runtime.CurrentState, out clip) && clip.Loop == LoopMode.Once;
        }

        /// <summary>
        /// Idle, with IdleBlink after a randomized number of completed Idle loops, and SpecialIdle
        /// on a randomized timer. Each is tried in the active group's flavor first, then bare.
        /// Velocity is already zero when this runs.
        /// </summary>
        private static string DetermineIdleState(AnimationRuntime runtime, AnimationSet set, string groupPrefix)
        {
            if (IsOnceClipInProgress(runtime, set, AnimStates.IdleBlink))
                return runtime.CurrentState;

            string idleName;
            AnimationClip idleClip;
            if (!TryResolveGroupedClip(set, groupPrefix, AnimStates.Idle, out idleName, out idleClip))
                return null;

            string blinkName;
            AnimationClip blinkClip;
            if (TryResolveGroupedClip(set, groupPrefix, AnimStates.IdleBlink, out blinkName, out blinkClip))
            {
                if (runtime.CurrentState != idleName)
                    runtime.NextIdleBlinkAtLoopCount = RollNextLoopCount(blinkClip); // Idle starts fresh this tick
                else if (runtime.StatePlayer.GetCompletedLoops() >= runtime.NextIdleBlinkAtLoopCount)
                    return blinkName;
            }

            string specialName;
            AnimationClip specialClip;
            if (!TryResolveGroupedClip(set, groupPrefix, AnimStates.SpecialIdle, out specialName, out specialClip))
                return idleName;

            double now = AnimationClock.Now;
            if (!runtime.NextSpecialIdleAt.HasValue)
                runtime.NextSpecialIdleAt = now + RollNextInterval(specialClip);

            if (now < runtime.NextSpecialIdleAt.Value)
                return idleName;

            runtime.NextSpecialIdleAt = now + RollNextInterval(specialClip);
            return specialName;
        }

        // ---------------------------------------------------------------------------------
        // State groups
        // ---------------------------------------------------------------------------------

        // Group detectors are polled every tick, so an NPC hovering at a range boundary can flip
        // the raw signal on and off. Holding the last group this long after it drops smooths that.
        private const double GroupSignalBufferSeconds = 1.5;

        private static string ResolveGroupPrefix(NPC npc, AnimationRuntime runtime)
        {
            double now = AnimationClock.Now;
            string raw = RunGroupDetectors(npc);

            if (!string.IsNullOrEmpty(raw))
            {
                runtime.LastActiveGroup = raw;
                runtime.LastGroupSignalAt = now;
                return raw;
            }

            bool held = runtime.LastGroupSignalAt.HasValue
                && now - runtime.LastGroupSignalAt.Value <= GroupSignalBufferSeconds
                && !string.IsNullOrEmpty(runtime.LastActiveGroup);
            return held ? runtime.LastActiveGroup : string.Empty;
        }

        private static string RunGroupDetectors(NPC npc)
        {
            List<StateDetector> list;
            if (!GroupDetectors.TryGetValue(npc.type, out list))
                return null;

            for (int i = 0; i < list.Count; i++)
            {
                string result = list[i](npc);
                if (!string.IsNullOrEmpty(result))
                    return result;
            }
            return null;
        }

        /// <summary>Prefers <c>prefix + baseName</c> when authored, otherwise the bare <paramref name="baseName"/>.</summary>
        private static bool TryResolveGroupedClip(AnimationSet set, string prefix, string baseName, out string resolvedName, out AnimationClip clip)
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                string groupedName = AnimStates.Grouped(prefix, baseName);
                if (set.States.TryGetValue(groupedName, out clip))
                {
                    resolvedName = groupedName;
                    return true;
                }
            }

            if (set.States.TryGetValue(baseName, out clip))
            {
                resolvedName = baseName;
                return true;
            }

            resolvedName = null;
            clip = null;
            return false;
        }

        // ---------------------------------------------------------------------------------
        // Overlays
        // ---------------------------------------------------------------------------------

        private static void DrawOverlays(NPC npc, AnimationRuntime runtime, SpriteBatch spriteBatch, Vector2 screenPos, Color color)
        {
            foreach (AnimationClip overlay in runtime.Set.Overlays)
            {
                if (!ShouldDrawOverlay(runtime, overlay))
                {
                    runtime.ActiveOverlays.Remove(overlay);
                    continue;
                }

                AnimationPlayer player;
                if (!runtime.OverlayPlayers.TryGetValue(overlay, out player))
                {
                    player = new AnimationPlayer();
                    runtime.OverlayPlayers[overlay] = player;
                }

                if (runtime.ActiveOverlays.Add(overlay))
                    player.Play(overlay); // just became active: start from frame 0

                DrawClip(npc, overlay, player, spriteBatch, screenPos, color);
            }
        }

        private static bool ShouldDrawOverlay(AnimationRuntime runtime, AnimationClip overlay)
        {
            switch (overlay.State)
            {
                case AnimOverlays.Blink:
                    return ShouldBlink(runtime, overlay);
                default:
                    return false; // no framework trigger for this overlay name
            }
        }

        /// <summary>Blink overlay: fires on a randomized timer and plays through once per firing.</summary>
        private static bool ShouldBlink(AnimationRuntime runtime, AnimationClip overlay)
        {
            double now = AnimationClock.Now;
            if (!runtime.NextBlinkAt.HasValue)
                runtime.NextBlinkAt = now + RollNextInterval(overlay);

            AnimationPlayer player;
            if (runtime.ActiveOverlays.Contains(overlay)
                && runtime.OverlayPlayers.TryGetValue(overlay, out player)
                && !player.IsFinished)
                return true;

            if (now < runtime.NextBlinkAt.Value)
                return false;

            runtime.NextBlinkAt = now + RollNextInterval(overlay);
            return true;
        }

        // ---------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------

        // Per-instance rolls keep NPCs sharing a clip from blinking/varying in lockstep.
        private static readonly Random Jitter = new Random();

        private static double RollNextInterval(AnimationClip clip)
        {
            return clip.IntervalMin + Jitter.NextDouble() * (clip.IntervalMax - clip.IntervalMin);
        }

        private static int RollNextLoopCount(AnimationClip clip)
        {
            return Jitter.Next(clip.LoopCountMin, clip.LoopCountMax + 1); // inclusive upper bound
        }

        private static void DrawClip(NPC npc, AnimationClip clip, AnimationPlayer player, SpriteBatch spriteBatch, Vector2 screenPos, Color color)
        {
            var texture = clip.GetTexture(npc.IsShimmerVariant);
            if (!texture.IsLoaded)
                return;

            Rectangle source = clip.GetSourceRect(player.GetCurrentFrame());
            Vector2 origin = clip.GetOrigin();
            SpriteEffects effects = npc.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Vector2 drawPosition = npc.Center - screenPos + new Vector2(0f, npc.gfxOffY);

            spriteBatch.Draw(texture.Value, drawPosition, source, color, npc.rotation, origin, npc.scale, effects, 0f);
        }
    }
}
