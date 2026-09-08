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
    /// Public surface is intentionally narrow — <see cref="RegisterStateDetector"/> and
    /// <see cref="RegisterStateGroupDetector"/> are the only things content mods need to reach
    /// from outside this library assembly. Everything else here is `internal`, called only by
    /// the two attachment classes in this same assembly.
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

        // --- State-group pattern (see ANIMFRAMEWORK_NEW_FEATURES.md's Alert notes) ---
        //
        // Distinct from the plain detector list above: a plain StateDetector returns a full
        // override state name to jump to directly (e.g. "Falling"). A group detector instead
        // returns a *prefix* (e.g. "Alert") applied only to Walking/Idle/Blink/SpecialIdle — the
        // "idle-family" cluster that has no vanilla-bound signal of its own. Attacking/Talk/
        // Sitting are never grouped; a plain detector's direct override still wins outright over
        // group resolution, same priority order as before this existed. Reuses the same
        // StateDetector delegate shape (NPC -> string) since the signature need is identical,
        // even though the returned string means something different in each list.
        private static readonly List<StateDetector> _groupDetectors = new List<StateDetector>();

        /// <summary>
        /// Registers a state-group selector: given the NPC, return the prefix to apply to
        /// Walking/Idle/Blink/SpecialIdle lookups this tick (e.g. "Alert" tries "AlertWalking"
        /// before falling back to plain "Walking"), or null/empty for no group. Detectors run in
        /// registration order; the first non-null, non-empty result wins outright — unlike the
        /// plain detector list, there's no "does a clip exist for this" check here, because a
        /// missing grouped clip degrades gracefully per-lookup anyway (see
        /// <see cref="TryResolveGroupedClip"/>), so a group can be "active" even for an NPC that
        /// hasn't authored a single grouped clip — it just silently behaves identically to no
        /// group being active in that case.
        /// </summary>
        public static void RegisterStateGroupDetector(StateDetector detector)
        {
            if (detector != null)
                _groupDetectors.Add(detector);
        }

        private static string RunRegisteredGroupDetectors(NPC npc)
        {
            for (int i = 0; i < _groupDetectors.Count; i++)
            {
                string result = _groupDetectors[i](npc);
                if (!string.IsNullOrEmpty(result))
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Resolves <paramref name="baseName"/> against an active group prefix, preferring the
        /// grouped flavor (<c>prefix + baseName</c>) when both the prefix is non-empty and that
        /// exact clip is authored, falling back to the bare <paramref name="baseName"/>
        /// otherwise. Same graceful-degradation spirit as every other optional piece here — an
        /// NPC can author only some grouped flavors (or none) and everything still resolves
        /// correctly to whatever's actually available.
        /// </summary>
        private static bool TryResolveGroupedClip(AnimationSet set, string prefix, string baseName, out string resolvedName, out AnimationClip clip)
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                string groupedName = prefix + baseName;
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

        /// <summary>
        /// Called by both attachment classes' <c>PickEmote</c> overrides whenever vanilla is
        /// about to have this NPC pick an emote bubble to show — confirmed against
        /// docs.tmodloader.net's GlobalNPC reference: "Allows you to change the emote that the
        /// NPC will pick... <c>otherAnchor</c> - A WorldUIAnchor instance indicating the emote
        /// conversation target; use this to access the NPC or Player this NPC is communicating
        /// with." That's the NPC-to-NPC (and NPC-to-player-without-dialog-open) ambient chatter
        /// case flagged as unwired in ANIMFRAMEWORK_SPEC.md's Vanilla Compatibility section — the
        /// two attachment classes only forward here when <c>otherAnchor</c> is non-null, i.e.
        /// this is an actual conversation rather than a solo mood emote (rain, biome, etc.), so a
        /// bored/lonely emote doesn't falsely trigger the Talk animation.
        ///
        /// Purely observational, same contract as NotifyAttackTriggered: never touches the
        /// hook's own return value, so it can't change which emote vanilla actually picks.
        /// </summary>
        internal static void NotifyTalkTriggered(NPC npc, AnimationRuntime runtime)
        {
            if (runtime == null)
                return;

            runtime.TalkSignalId++;
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

        // Group detectors are polled fresh every tick (see RunRegisteredGroupDetectors) rather
        // than pulsed like an attack trigger, so an NPC skirmishing back and forth across a
        // detector's own boundary (e.g. Alert's DangerDetectRange) can flip the raw signal on and
        // off rapidly with no single "attack sequence length" to size a buffer from the way
        // AttackSignalBufferSeconds does. A flat constant instead — long enough to smooth a
        // normal skirmish's back-and-forth, short enough that a genuinely-over threat still drops
        // Alert promptly. Not derived from anything vanilla; revisit if it ever needs to be
        // per-group instead of framework-wide.
        private const double GroupSignalBufferSeconds = 1.5;

        /// <summary>
        /// Resolves this tick's effective group prefix from the raw detector poll
        /// (<see cref="RunRegisteredGroupDetectors"/>), holding the last active group for
        /// <see cref="GroupSignalBufferSeconds"/> after it stops being reported rather than
        /// dropping it the instant the raw signal lapses — this is what actually prevents the
        /// erratic on/off flicker a boundary-crossing NPC would otherwise cause in Walking-gate/
        /// DetermineIdleState's grouped lookups. <see cref="AnimationRuntime.GroupSignalAge"/> is
        /// restarted every tick the raw signal is live, so it only starts counting up once the
        /// signal actually stops — once it exceeds the buffer, the group truly clears.
        /// </summary>
        private static string ResolveGroupPrefix(NPC npc, AnimationRuntime runtime)
        {
            string rawGroup = RunRegisteredGroupDetectors(npc);

            if (!string.IsNullOrEmpty(rawGroup))
            {
                runtime.LastActiveGroup = rawGroup;
                runtime.GroupSignalAge.Restart();
                return rawGroup;
            }

            bool bufferLive = runtime.GroupSignalAge.IsRunning
                && runtime.GroupSignalAge.Elapsed.TotalSeconds <= GroupSignalBufferSeconds;
            if (bufferLive && !string.IsNullOrEmpty(runtime.LastActiveGroup))
                return runtime.LastActiveGroup;

            return string.Empty;
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

            state = DetermineTalkingState(npc, runtime, set);
            if (state != null)
                return state;

            state = DetermineSittingState(npc, set);
            if (state != null)
                return state;

            state = RunRegisteredDetectors(npc, set);
            if (state != null)
                return state;

            // A mid-playthrough Once-mode SpecialIdle (of either the bare or a grouped flavor —
            // see HoldInProgressSpecialIdle) is held to completion before anything below
            // (including Walking) can reclaim the state — see this method's own doc comment for
            // why this can't live inside DetermineIdleState like the rest of SpecialIdle's logic
            // does.
            state = HoldInProgressSpecialIdle(runtime, set);
            if (state != null)
                return state;

            // State group resolution (see ANIMFRAMEWORK_NEW_FEATURES.md's Alert notes): a
            // registered group detector picks an active prefix (e.g. "Alert") applied to
            // Walking/Idle/Blink/SpecialIdle only — Attacking/Talk/Sitting above are never
            // grouped, and override detectors above already won outright if they fired. Empty
            // prefix (no group active, or none registered) behaves identically to before this
            // feature existed. Goes through ResolveGroupPrefix rather than the raw detector poll
            // directly, so a brief signal gap (e.g. a boundary-crossing skirmish) doesn't flicker
            // the group on and off — see GroupSignalBufferSeconds.
            string groupPrefix = ResolveGroupPrefix(npc, runtime);

            string walkName;
            AnimationClip walkClip;
            if (npc.velocity != Vector2.Zero && TryResolveGroupedClip(set, groupPrefix, "Walking", out walkName, out walkClip))
                return walkName;

            return DetermineIdleState(runtime, set, groupPrefix);
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
        /// Matches by suffix (<c>EndsWith("SpecialIdle")</c>) rather than the literal name
        /// "SpecialIdle", so a grouped flavor (e.g. "AlertSpecialIdle") gets the exact same
        /// hold-to-completion guarantee without this method needing to know which groups exist
        /// or what the *current* tick's group resolves to — once a Once-mode SpecialIdle-family
        /// clip has started, it finishes what it started even if the group that picked it
        /// flickers away mid-playthrough.
        ///
        /// Scoped to Loop == Once only: <c>AnimationPlayer.IsFinished</c> never goes true for
        /// Loop-mode clips (see AnimationPlayer.GetCurrentFrame), so applying this hold to a
        /// Loop-mode SpecialIdle would trap the NPC in it forever. Loop-mode SpecialIdle has no
        /// defined play-duration semantics yet — not attempted here; author SpecialIdle as Once
        /// until that's designed.
        /// </summary>
        private static string HoldInProgressSpecialIdle(AnimationRuntime runtime, AnimationSet set)
        {
            if (runtime.CurrentState == null || !runtime.CurrentState.EndsWith("SpecialIdle") || runtime.StatePlayer.IsFinished)
                return null;

            AnimationClip specialClip;
            if (set.States.TryGetValue(runtime.CurrentState, out specialClip) && specialClip.Loop == LoopMode.Once)
                return runtime.CurrentState;

            return null;
        }

        /// <summary>
        /// Idle, with the occasional SpecialIdle and/or Blink variant mixed in — framework-
        /// original logic, no vanilla signal to bind to (per the spec's Baseline Animation Set).
        /// Group-aware: <paramref name="groupPrefix"/> (from a registered state-group detector,
        /// e.g. "Alert") is tried first for each of Idle/Blink/SpecialIdle via
        /// <see cref="TryResolveGroupedClip"/>, falling back to the bare name — an NPC can author
        /// only some grouped flavors (or none at all, or no groups registered) and this still
        /// resolves correctly.
        ///
        /// SpecialIdle reuses the same randomized-interval mechanism as the timed-overlay Blink
        /// (<see cref="RollNextInterval"/>, <see cref="AnimationClip.IntervalMin"/>/
        /// <c>IntervalMax</c>) but applied to a true state instead of an overlay: while otherwise
        /// idle, wait out a freshly-rolled interval on a real-time clock, then switch in for one
        /// play-through. The in-progress hold for a live SpecialIdle playthrough is handled
        /// upstream, in DetermineState — see HoldInProgressSpecialIdle — so this method only ever
        /// sees SpecialIdle either not yet started or already finished. Its timer
        /// (<see cref="AnimationRuntime.SpecialIdleClock"/>) is shared across every group rather
        /// than one per group — whichever flavor is active when it fires is what gets shown.
        ///
        /// Blink here is a *different*, Idle-paired mechanism from the timed overlay of the same
        /// name (see ShouldBlink) — a real sibling state to Idle rather than a layered mark, with
        /// no upstream hold-to-completion carve-out. Deliberately scoped this way: unlike
        /// SpecialIdle, Blink has no priority over the Walking gate in DetermineState, which is
        /// ranked above this method — so resumed velocity cuts Blink off immediately, with zero
        /// special-casing needed here to make that happen. Triggered by completed Idle loops
        /// (<see cref="AnimationPlayer.GetCompletedLoops"/>) rather than elapsed time, so the
        /// switch always lands on a clean loop boundary instead of an arbitrary mid-cycle moment.
        /// Its "still mid-playthrough" check matches by suffix (<c>EndsWith("Blink")</c>), same
        /// reasoning as HoldInProgressSpecialIdle's suffix match — a started Blink-family
        /// playthrough finishes what it started regardless of the current tick's group.
        /// </summary>
        private static string DetermineIdleState(AnimationRuntime runtime, AnimationSet set, string groupPrefix)
        {
            if (runtime.CurrentState != null && runtime.CurrentState.EndsWith("Blink") && !runtime.StatePlayer.IsFinished)
                return runtime.CurrentState; // still mid-playthrough (velocity is already confirmed 0 this tick)

            string idleName;
            AnimationClip idleClip;
            if (!TryResolveGroupedClip(set, groupPrefix, "Idle", out idleName, out idleClip))
                return null; // not authored (grouped or bare) for this NPC

            string blinkName;
            AnimationClip blinkClip;
            if (TryResolveGroupedClip(set, groupPrefix, "Blink", out blinkName, out blinkClip))
            {
                if (runtime.CurrentState != idleName)
                {
                    // Fresh idle session starting this frame — this flavor of Idle hasn't
                    // actually played a single frame yet as of this call (the transition into it
                    // happens after DetermineState returns, in PreDraw), so StatePlayer's
                    // completed-loop count still belongs to whatever state was active before this
                    // and isn't meaningful yet. Roll this session's threshold and start idling.
                    runtime.NextBlinkAtLoopCount = RollNextLoopCount(blinkClip);
                }
                else if (runtime.StatePlayer.GetCompletedLoops() >= runtime.NextBlinkAtLoopCount)
                {
                    return blinkName;
                }
            }

            string specialName;
            AnimationClip specialClip;
            if (!TryResolveGroupedClip(set, groupPrefix, "SpecialIdle", out specialName, out specialClip))
                return idleName; // not authored (grouped or bare) — this flavor of Idle only

            if (!runtime.SpecialIdleClock.IsRunning)
            {
                runtime.SpecialIdleClock.Start();
                runtime.NextSpecialIdleAt = RollNextInterval(specialClip);
            }

            if (runtime.SpecialIdleClock.Elapsed.TotalSeconds < runtime.NextSpecialIdleAt)
                return idleName;

            // Window elapsed — fire once, then roll a fresh interval so multiple NPCs sharing
            // this clip don't all play their SpecialIdle in lockstep (same reasoning as Blink).
            runtime.NextSpecialIdleAt = runtime.SpecialIdleClock.Elapsed.TotalSeconds + RollNextInterval(specialClip);
            return specialName;
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
        /// like every other row.
        ///
        /// Two triggers feed the same <see cref="AnimationRuntime.TalkSignalId"/> signal, each
        /// bumping it once per conversation *start* rather than once per tick:
        /// - Player dialog: <c>Main.LocalPlayer.talkNPC == npc.whoAmI</c> is level-triggered
        ///   (true for the whole dialog-open duration), so this method edge-detects it itself via
        ///   <see cref="AnimationRuntime.WasPlayerTalking"/> and bumps the signal only on the
        ///   false→true transition.
        /// - NPC-to-NPC ambient chatter: <see cref="NotifyTalkTriggered"/>, called from the two
        ///   attachment classes' <c>PickEmote</c> override — see that method's doc comment for
        ///   why this is the confirmed hook (docs.tmodloader.net) rather than a guess.
        ///
        /// A Once-mode Talk clip then plays exactly one play-through per signal — same
        /// "consume this signal id, hold current playthrough to completion" contract as
        /// DetermineAttackingState's Once-mode branch — and falls through to whatever state
        /// would otherwise apply (Idle/Walking/etc.) once finished, even if the player's dialog
        /// box is still open. A Loop-mode Talk clip keeps the original level-triggered behavior
        /// (held for as long as the player's dialog is open) for authors who prefer that.
        ///
        /// Ranked above Sitting/registered detectors/Walking — a player mid-dialog with this NPC
        /// shouldn't flicker back to Walking/Idle off residual velocity while Talk is playing.
        /// </summary>
        private static string DetermineTalkingState(NPC npc, AnimationRuntime runtime, AnimationSet set)
        {
            AnimationClip clip;
            if (!set.States.TryGetValue("Talk", out clip))
                return null; // not authored for this NPC — nothing to trigger into

            bool playerTalking = Main.LocalPlayer.talkNPC == npc.whoAmI;
            if (playerTalking && !runtime.WasPlayerTalking)
                runtime.TalkSignalId++; // rising edge: dialog just opened with this NPC
            runtime.WasPlayerTalking = playerTalking;

            if (clip.Loop != LoopMode.Once)
                return playerTalking ? "Talk" : null; // legacy Loop-mode behavior: held open-dialog-long

            if (runtime.CurrentState == "Talk" && !runtime.StatePlayer.IsFinished)
                return "Talk"; // still mid-playthrough

            if (runtime.TalkSignalId != runtime.LastConsumedTalkSignalId)
            {
                runtime.LastConsumedTalkSignalId = runtime.TalkSignalId;
                return "Talk";
            }

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

        /// <summary>
        /// Rolls how many completed Idle loops until the next Blink — the loop-count analogue of
        /// <see cref="RollNextInterval"/>, reusing the same shared <see cref="_intervalJitter"/>
        /// instance rather than a second RNG. <c>Random.Next(min, max)</c>'s upper bound is
        /// exclusive, so <c>LoopCountMax + 1</c> makes it inclusive, matching
        /// <see cref="RollNextInterval"/>'s inclusive-both-ends contract; <c>LoopCountMin ==
        /// LoopCountMax</c> (fixed value, or declared neither and got the default range) still
        /// works fine — <c>Next(n, n + 1)</c> always returns exactly <c>n</c>.
        /// </summary>
        private static int RollNextLoopCount(AnimationClip clip)
        {
            return _intervalJitter.Next(clip.LoopCountMin, clip.LoopCountMax + 1);
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
