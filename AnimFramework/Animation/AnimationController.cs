using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace AnimFramework.Animation
{
    /// <summary>A single extra state-detection check — see <see cref="AnimationController.RegisterStateDetector"/>.</summary>
    public delegate string StateDetector(NPC npc);

    /// <summary>
    /// Shared state-machine + draw logic used by both attachment paths (the AnimatedNPC base
    /// class and the AnimationRegistry/AnimatedGlobalNPC registry path), so the two don't
    /// duplicate this logic against each other.
    ///
    /// Public surface is intentionally narrow — just <see cref="RegisterStateDetector"/>, the
    /// one thing content mods need to reach from outside this library assembly. Everything else
    /// here is `internal`, called only by the two attachment classes in this same assembly.
    /// </summary>
    public static class AnimationController
    {
        // --- Detector-list pattern (see ANIMFRAMEWORK_NEW_FEATURES.md section 2) ---
        //
        // DetermineState() used to be one monolithic method — adding a single new state check
        // meant overriding the whole thing. This list lets a content mod register one extra
        // check from its own setup code, composing with everything below rather than replacing
        // it. A detector is pure opt-in: registering none (the default) costs nothing and
        // changes nothing about existing behavior.
        private static readonly List<StateDetector> _detectors = new List<StateDetector>();

        /// <summary>
        /// Registers an additional state-detection check. Run after the framework's own
        /// vanilla-bound checks (Attacking, Sitting) and before the Walking/Idle fallback — so a
        /// registered detector can distinguish a case Walking's broad "velocity != 0" check
        /// would otherwise swallow (e.g. Falling), without needing to out-rank the confirmed
        /// signals. Detectors run in registration order; the first to return a non-null state
        /// name *that the current NPC actually has a clip for* wins — same "unregistered clip
        /// falls through, never crashes" contract as every other detection path here (see
        /// ANIMFRAMEWORK_NEW_FEATURES.md section 3).
        /// </summary>
        public static void RegisterStateDetector(StateDetector detector)
        {
            if (detector != null)
                _detectors.Add(detector);
        }

        internal static bool PreDraw(NPC npc, AnimationRuntime runtime, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            // No registered set (or nothing built for this NPC yet) — fall back to vanilla
            // drawing rather than drawing nothing. See spec's Fallback/Validation Needs section.
            if (runtime == null || runtime.Set == null)
                return true;

            string state = DetermineState(npc, runtime);
            AnimationClip clip;
            if (state == null || !runtime.Set.States.TryGetValue(state, out clip))
                return true; // current state has no registered clip — fall back to vanilla

            if (runtime.CurrentState != state)
            {
                runtime.CurrentState = state;
                runtime.StatePlayer.Play(clip);
            }

            DrawClip(npc, clip, runtime.StatePlayer, spriteBatch, screenPos, drawColor);
            DrawOverlays(npc, runtime, spriteBatch, screenPos, drawColor);

            return false; // we drew the NPC ourselves; suppress vanilla's own draw
        }

        /// <summary>
        /// Called by both attachment classes' <c>TownNPCAttackProj</c>/<c>TownNPCAttackMagic</c>
        /// overrides whenever vanilla is about to run an attack of type 0 (throw), 1 (shoot), or
        /// 2 (magic) for this NPC — see "Vanilla Compatibility" in ANIMFRAMEWORK_SPEC.md. Purely
        /// observational: never touches the hooks' ref parameters, so it can't change vanilla's
        /// actual attack behavior even if the hook fires in a case we didn't expect.
        ///
        /// Type 3 (melee) has no confirmed hook and is deliberately never wired here — an NPC
        /// with a melee AttackType simply never gets an attack signal, so DetermineState falls
        /// through past Attacking for it exactly as if no Attacking clip were authored at all.
        /// </summary>
        internal static void NotifyAttackTriggered(NPC npc, AnimationRuntime runtime)
        {
            if (runtime == null)
                return;

            runtime.AttackSignalAge.Restart();
            runtime.AttackSignalId++;
        }

        // Loop-mode Attacking clips (channeled attacks) have no confirmed continuous "still
        // attacking" vanilla signal — the trigger hooks only fire at discrete attack-decision
        // points, not every tick. This buffer treats repeated firings as "still attacking" and
        // lets the state lapse shortly after they stop. Sized from
        // NPCID.Sets.AttackTime[npc.type] — vanilla's own declared attack-sequence length in
        // ticks (confirmed field; used by vanilla itself as an attack-duration countdown) —
        // rather than an arbitrary constant, with a small floor for NPCs that declare none.
        // This is a *different* timing layer from the spec's "not tied to AttackTime" rule for
        // Once-mode clips below: that rule is about a clip's own per-frame playback timing;
        // this is about how long the higher-level Attacking *state* stays entered. Once-mode
        // Attacking clips only use this buffer to gate initial entry, never to re-affirm/hold —
        // see DetermineAttackingState's AttackSignalId handling for why that distinction matters.
        private const double MinAttackSignalBufferSeconds = 0.2;

        private static double AttackSignalBufferSeconds(NPC npc)
        {
            int ticks = NPCID.Sets.AttackTime[npc.type];
            return Math.Max(MinAttackSignalBufferSeconds, ticks / 60.0);
        }

        /// <summary>
        /// Default state-transition logic. Baseline set per ANIMFRAMEWORK_SPEC.md is Idle,
        /// SpecialIdle, Walking, Attacking, Sitting — all five are live now. Every check here
        /// degrades the same way: no clip authored for that state on this NPC → skip it, never
        /// force it — see ANIMFRAMEWORK_NEW_FEATURES.md section 3.
        /// </summary>
        private static string DetermineState(NPC npc, AnimationRuntime runtime)
        {
            AnimationSet set = runtime.Set;

            string state = DetermineAttackingState(npc, runtime, set);
            if (state != null)
                return state;

            state = DetermineTalkingState(npc, set);
            if (state != null)
                return state;

            state = DetermineSittingState(npc, set);
            if (state != null)
                return state;

            state = RunRegisteredDetectors(npc, set);
            if (state != null)
                return state;

            // A mid-playthrough Once-mode SpecialIdle is held to completion before anything
            // below (including Walking) can reclaim the state — see this method's own doc
            // comment for why this can't live inside DetermineIdleState like the rest of
            // SpecialIdle's logic does.
            state = HoldInProgressSpecialIdle(runtime, set);
            if (state != null)
                return state;

            if (npc.velocity != Vector2.Zero && set.States.ContainsKey("Walking"))
                return "Walking";

            return DetermineIdleState(runtime, set);
        }

        /// <summary>
        /// Keeps a Once-mode SpecialIdle clip playing to completion even if velocity blips
        /// nonzero mid-clip — vanilla town NPC AI's wander velocity is erratic tick-to-tick, and
        /// without this a single stray tick of nonzero velocity mid-playthrough would hand
        /// priority to Walking (checked directly in DetermineState, above DetermineIdleState)
        /// and cut the clip off before <c>IsFinished</c>. Same "hold until finished" contract
        /// Attacking's Once-mode clips get (see DetermineAttackingState) — checked here, ahead
        /// of the Walking gate, rather than where the rest of SpecialIdle's logic lives in
        /// DetermineIdleState, specifically so it can out-rank that gate.
        ///
        /// Scoped to Loop == Once only: <c>AnimationPlayer.IsFinished</c> never goes true for
        /// Loop-mode clips (see AnimationPlayer.GetCurrentFrame), so applying this hold to a
        /// Loop-mode SpecialIdle would trap the NPC in it forever. Loop-mode SpecialIdle has no
        /// defined play-duration semantics yet — not attempted here; author SpecialIdle as Once
        /// until that's designed.
        /// </summary>
        private static string HoldInProgressSpecialIdle(AnimationRuntime runtime, AnimationSet set)
        {
            if (runtime.CurrentState != "SpecialIdle" || runtime.StatePlayer.IsFinished)
                return null;

            AnimationClip specialClip;
            if (set.States.TryGetValue("SpecialIdle", out specialClip) && specialClip.Loop == LoopMode.Once)
                return "SpecialIdle";

            return null;
        }

        /// <summary>
        /// Idle, with the occasional SpecialIdle variant mixed in — framework-original logic,
        /// no vanilla signal to bind to (per the spec's Baseline Animation Set). Reuses the same
        /// randomized-interval mechanism as the Blink overlay (<see cref="RollNextInterval"/>,
        /// <see cref="AnimationClip.IntervalMin"/>/<c>IntervalMax</c>) but applied to a true
        /// state instead of an overlay: while otherwise idle, wait out a freshly-rolled interval
        /// on a real-time clock, then switch in for one play-through. The in-progress hold for a
        /// live SpecialIdle playthrough is handled upstream, in DetermineState — see
        /// HoldInProgressSpecialIdle — so this method only ever sees SpecialIdle either not yet
        /// started or already finished.
        /// </summary>
        private static string DetermineIdleState(AnimationRuntime runtime, AnimationSet set)
        {
            if (!set.States.ContainsKey("Idle"))
                return null;

            AnimationClip specialClip;
            if (!set.States.TryGetValue("SpecialIdle", out specialClip))
                return "Idle"; // not authored for this NPC — plain Idle only

            if (!runtime.SpecialIdleClock.IsRunning)
            {
                runtime.SpecialIdleClock.Start();
                runtime.NextSpecialIdleAt = RollNextInterval(specialClip);
            }

            if (runtime.SpecialIdleClock.Elapsed.TotalSeconds < runtime.NextSpecialIdleAt)
                return "Idle";

            // Window elapsed — fire once, then roll a fresh interval so multiple NPCs sharing
            // this clip don't all play their SpecialIdle in lockstep (same reasoning as Blink).
            runtime.NextSpecialIdleAt = runtime.SpecialIdleClock.Elapsed.TotalSeconds + RollNextInterval(specialClip);
            return "SpecialIdle";
        }

        /// <summary>
        /// Attacking takes top priority when live — an NPC mid-attack shouldn't visibly revert
        /// to Walking/Idle just because it also has velocity. Entry is edge-triggered off
        /// <see cref="NotifyAttackTriggered"/>; exit differs by loop mode exactly as designed in
        /// the spec's Loop Modes section: Once-mode clips hold until they finish on their own
        /// (self-timed, independent of vanilla's AttackTime); Loop-mode clips exit once trigger
        /// pulses stop arriving (see <see cref="AttackSignalBufferSeconds"/>).
        /// </summary>
        private static string DetermineAttackingState(NPC npc, AnimationRuntime runtime, AnimationSet set)
        {
            AnimationClip clip;
            if (!set.States.TryGetValue("Attacking", out clip))
                return null; // not authored for this NPC — nothing to trigger into

            bool signalLive = runtime.AttackSignalAge.IsRunning
                && runtime.AttackSignalAge.Elapsed.TotalSeconds <= AttackSignalBufferSeconds(npc);

            if (clip.Loop == LoopMode.Once)
            {
                // Once-mode: consume each attack-start signal for exactly one playthrough, never
                // re-enter off a signal already consumed. Bug this replaces: the buffer window
                // above is sized for Loop-mode re-affirmation and is very often longer than a
                // short punch/point-and-shoot clip's own duration (its floor alone, 0.2s, already
                // outlasts e.g. a 0.1s clip) — checking signalLive here as originally written
                // meant a single real attack could replay the clip several times before the
                // buffer itself finally expired. AttackSignalId (bumped once per real
                // NotifyAttackTriggered call) is what actually prevents that now.
                if (runtime.CurrentState == "Attacking" && !runtime.StatePlayer.IsFinished)
                    return "Attacking"; // still mid-playthrough

                if (signalLive && runtime.AttackSignalId != runtime.LastConsumedAttackSignalId)
                {
                    runtime.LastConsumedAttackSignalId = runtime.AttackSignalId;
                    return "Attacking";
                }

                return null;
            }

            // Loop-mode (channeled attacks): continuous re-affirmation is exactly what we want —
            // stay Attacking for as long as trigger pulses keep arriving, exit shortly after they
            // stop (AttackSignalBufferSeconds).
            return signalLive ? "Attacking" : null;
        }

        /// <summary>
        /// Talk — a real mutually-exclusive state, not an overlay. Revised from the original
        /// spec's overlay-based design: a small mark layered on top of whatever pose was active
        /// worked for a simple mouth-flap, but couldn't express a full bodily talking animation
        /// (a gesture, a distinct stance) without the underlying pose fighting it. As a state, a
        /// content mod's Talk clip is a full sprite like any other, author-chosen Loop or Once
        /// like every other row — no special-casing here beyond the trigger check itself.
        /// Trigger: <c>Main.LocalPlayer.talkNPC == npc.whoAmI</c> — confirmed, but only covers
        /// the player-has-this-NPC's-dialog-open case. The other case (vanilla's ambient
        /// NPC-to-NPC "emote bubble" chatter, via <c>ModEmoteBubble</c>/<c>EmoteBubbleLoader</c>
        /// — confirmed to exist, per docs.tmodloader.net) is NOT wired here yet: the exact way
        /// to read "does this NPC currently have an active emote bubble" from PreDraw isn't
        /// confirmed, and guessing it would repeat the exact mistake Sitting's first attempt
        /// made. Ranked above Sitting/registered detectors/Walking — a player mid-dialog with
        /// this NPC shouldn't flicker back to Walking/Idle off residual velocity.
        /// </summary>
        private static string DetermineTalkingState(NPC npc, AnimationSet set)
        {
            if (set.States.ContainsKey("Talk") && Main.LocalPlayer.talkNPC == npc.whoAmI)
                return "Talk";
            return null;
        }

        /// <summary>
        /// TODO: Sitting is not wired yet. The previous attempt guessed `npc.sitting.isSitting`
        /// by analogy with the confirmed `Player.sitting` field — that guess does not compile
        /// (`NPC` has no such member). Re-checked directly against docs.tmodloader.net (both the
        /// `stable` and `1.4-preview` NPC class references): the only sitting-related member
        /// exposed on `NPC` at all is `SitDown(Point anchorTilePosition, out int direction, out
        /// Vector2 bottom)`, which is a helper vanilla uses to *compute where to place* a sitting
        /// NPC — not a state you can read to check "is this NPC currently sitting". No public
        /// field/property for that was found. Needs empirical testing (spawn a town NPC on a
        /// bench, log its fields) rather than another guess — see ANIMFRAMEWORK_SPEC.md's
        /// Vanilla Compatibility section.
        /// </summary>
        private static string DetermineSittingState(NPC npc, AnimationSet set)
        {
            return null;
        }

        private static string RunRegisteredDetectors(NPC npc, AnimationSet set)
        {
            for (int i = 0; i < _detectors.Count; i++)
            {
                string result = _detectors[i](npc);
                if (result != null && set.States.ContainsKey(result))
                    return result;
            }
            return null;
        }

        // Shared randomized-interval jitter for every timed feature reading a clip's
        // IntervalMin/Max (declarable per-row/overlay via the JSON's intervalMin/intervalMax,
        // defaulting when unset — see ANIMFRAMEWORK_NEW_FEATURES.md section 4): Blink below, and
        // DetermineIdleState's SpecialIdle timer above. One shared Random rather than one per
        // feature — same roll math either way, no reason to duplicate the RNG instance.
        private static readonly Random _intervalJitter = new Random();

        private static void DrawOverlays(NPC npc, AnimationRuntime runtime, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            foreach (AnimationClip overlay in runtime.Set.Overlays)
            {
                bool shouldDraw = ShouldDrawOverlay(npc, runtime, overlay);
                bool wasActive = runtime.ActiveOverlays.Contains(overlay);

                if (!shouldDraw)
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

                if (!wasActive)
                {
                    runtime.ActiveOverlays.Add(overlay);
                    player.Play(overlay); // fresh start each time the overlay becomes active
                }

                DrawClip(npc, overlay, player, spriteBatch, screenPos, drawColor);
            }
        }

        /// <summary>
        /// Whether a given overlay should currently be drawn. Layered on top of the current
        /// state's frame as a second draw call, not a replacement of it. Talk used to be a case
        /// here (a small mark, condition-driven off the same talkNPC check DetermineTalkingState
        /// uses above) — moved to a real state instead, see that method's doc comment for why.
        /// </summary>
        private static bool ShouldDrawOverlay(NPC npc, AnimationRuntime runtime, AnimationClip overlay)
        {
            switch (overlay.State)
            {
                case "Blink":
                    return ShouldBlink(runtime, overlay);
                default:
                    return false; // unrecognized overlay name — no framework-known trigger yet
            }
        }

        /// <summary>
        /// Timed overlay (see the taxonomy in ANIMFRAMEWORK_NEW_FEATURES.md section 1): fires on
        /// its own internal clock, independent of anything vanilla exposes. A multi-frame
        /// Once-mode Blink clip naturally plays through once per firing (closing → closed →
        /// opening) via the same IsFinished mechanics every other Once-mode clip uses; a
        /// single-frame Blink clip
        /// finishes (and so is visible for) exactly one draw call, which is a legitimate, if
        /// blunt, authoring choice.
        /// </summary>
        private static bool ShouldBlink(AnimationRuntime runtime, AnimationClip overlay)
        {
            if (!runtime.BlinkClock.IsRunning)
            {
                runtime.BlinkClock.Start();
                runtime.NextBlinkAt = RollNextInterval(overlay);
            }

            AnimationPlayer player;
            bool midBlink = runtime.ActiveOverlays.Contains(overlay)
                && runtime.OverlayPlayers.TryGetValue(overlay, out player)
                && !player.IsFinished;
            if (midBlink)
                return true; // keep drawing until this play-through completes

            double now = runtime.BlinkClock.Elapsed.TotalSeconds;
            if (now < runtime.NextBlinkAt)
                return false;

            // Window elapsed — fire once, then roll a fresh randomized interval so multiple NPCs
            // sharing this clip don't all blink in lockstep.
            runtime.NextBlinkAt = now + RollNextInterval(overlay);
            return true;
        }

        private static double RollNextInterval(AnimationClip clip)
        {
            // IntervalMin == IntervalMax (author declared a fixed interval, or declared neither
            // and got the framework default range) still works fine here — NextDouble() * 0 is
            // just 0, so the roll collapses to that single value with no special-casing needed.
            return clip.IntervalMin + _intervalJitter.NextDouble() * (clip.IntervalMax - clip.IntervalMin);
        }

        private static void DrawClip(NPC npc, AnimationClip clip, AnimationPlayer player, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            var texture = clip.GetTexture();
            if (!texture.IsLoaded)
                return;

            Rectangle source = clip.GetSourceRect(player.GetCurrentFrame());
            Vector2 origin = clip.GetOrigin();
            SpriteEffects effects = npc.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Vector2 drawPosition = npc.Center - screenPos + new Vector2(0f, npc.gfxOffY);

            spriteBatch.Draw(texture.Value, drawPosition, source, drawColor, npc.rotation, origin, npc.scale, effects, 0f);
        }
    }
}
