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
        // Attacking clips don't use this buffer at all — see below.
        private const double MinAttackSignalBufferSeconds = 0.2;

        private static double AttackSignalBufferSeconds(NPC npc)
        {
            int ticks = NPCID.Sets.AttackTime[npc.type];
            return Math.Max(MinAttackSignalBufferSeconds, ticks / 60.0);
        }

        /// <summary>
        /// Default state-transition logic. Baseline set per ANIMFRAMEWORK_SPEC.md is Idle,
        /// SpecialIdle, Walking, Attacking, Sitting. SpecialIdle has no vanilla signal to bind to
        /// (framework-original random-chance logic, not yet written) and stays unwired; the rest
        /// are live. Every check here degrades the same way: no clip authored for that state on
        /// this NPC → skip it, never force it — see ANIMFRAMEWORK_NEW_FEATURES.md section 3.
        /// </summary>
        private static string DetermineState(NPC npc, AnimationRuntime runtime)
        {
            AnimationSet set = runtime.Set;

            string state = DetermineAttackingState(npc, runtime, set);
            if (state != null)
                return state;

            state = DetermineSittingState(npc, set);
            if (state != null)
                return state;

            state = RunRegisteredDetectors(npc, set);
            if (state != null)
                return state;

            if (npc.velocity != Vector2.Zero && set.States.ContainsKey("Walking"))
                return "Walking";

            return set.States.ContainsKey("Idle") ? "Idle" : null;
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

            if (runtime.CurrentState == "Attacking" && clip.Loop == LoopMode.Once)
                return runtime.StatePlayer.IsFinished ? null : "Attacking";

            return signalLive ? "Attacking" : null;
        }

        /// <summary>
        /// NPC.sitting.isSitting — vanilla's own town-NPC sitting struct (bench/chair
        /// interactions, added 1.4.4). Not directly confirmed against 1.4.4 source, but
        /// `Player.sitting` is confirmed (decompiled 1.4.0.5 source: `public PlayerSittingHelper
        /// sitting;`, read as `sitting.isSitting`) — town-NPC chair-sitting, added later, is very
        /// likely the same field/shape mirrored onto NPC rather than a new pattern. Still worth
        /// verifying at first Build+Reload (see ANIMFRAMEWORK_SPEC.md's Vanilla Compatibility
        /// section) in case the field or type name differs.
        /// </summary>
        private static string DetermineSittingState(NPC npc, AnimationSet set)
        {
            if (npc.sitting.isSitting && set.States.ContainsKey("Sitting"))
                return "Sitting";
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

        // Blink's own randomized-interval jitter (see RollNextBlinkDelay) — the bounds
        // themselves come from the clip (AnimationClip.IntervalMin/Max, declarable per-overlay
        // via the JSON's intervalMin/intervalMax, defaulting when unset — see
        // ANIMFRAMEWORK_NEW_FEATURES.md section 4), not from a framework-wide constant.
        private static readonly Random _blinkJitter = new Random();

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
        /// state's frame as a second draw call, not a replacement of it — matches the "layered"
        /// resolution of the Talk overlay's replace-vs-layer open question in the spec.
        /// </summary>
        private static bool ShouldDrawOverlay(NPC npc, AnimationRuntime runtime, AnimationClip overlay)
        {
            switch (overlay.State)
            {
                case "Talk":
                    return Main.LocalPlayer.talkNPC == npc.whoAmI;
                case "Blink":
                    return ShouldBlink(runtime, overlay);
                default:
                    return false; // unrecognized overlay name — no framework-known trigger yet
            }
        }

        /// <summary>
        /// Condition overlay vs. timed overlay (see the taxonomy in
        /// ANIMFRAMEWORK_NEW_FEATURES.md section 1): Talk above is condition-driven (an external
        /// fact is checked each frame); Blink is time-driven — it fires on its own internal
        /// clock, independent of anything vanilla exposes. A multi-frame Once-mode Blink clip
        /// naturally plays through once per firing (closing → closed → opening) via the same
        /// IsFinished mechanics every other Once-mode clip uses; a single-frame Blink clip
        /// finishes (and so is visible for) exactly one draw call, which is a legitimate, if
        /// blunt, authoring choice.
        /// </summary>
        private static bool ShouldBlink(AnimationRuntime runtime, AnimationClip overlay)
        {
            if (!runtime.BlinkClock.IsRunning)
            {
                runtime.BlinkClock.Start();
                runtime.NextBlinkAt = RollNextBlinkDelay(overlay);
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
            runtime.NextBlinkAt = now + RollNextBlinkDelay(overlay);
            return true;
        }

        private static double RollNextBlinkDelay(AnimationClip overlay)
        {
            // IntervalMin == IntervalMax (author declared a fixed interval, or declared neither
            // and got the framework default range) still works fine here — NextDouble() * 0 is
            // just 0, so the roll collapses to that single value with no special-casing needed.
            return overlay.IntervalMin + _blinkJitter.NextDouble() * (overlay.IntervalMax - overlay.IntervalMin);
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
