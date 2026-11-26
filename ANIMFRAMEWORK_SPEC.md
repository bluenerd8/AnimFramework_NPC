# AnimFramework — tModLoader High-FPS Animation Library

## Project Goal
A reusable tModLoader library mod that provides real-time (delta-time), tick-rate-independent
sprite animation for NPCs — starting with vanilla compatibility (binding to existing vanilla
NPC states/signals), with custom/modded NPC behavior explicitly deferred to a future pass.

Two-mod structure:
- **AnimFramework** (library) — zero gameplay content, zero NPC-specific data. Pure engine:
  clip/player classes, JSON loader, attachment mechanisms (base class + registry).
- **Content mods** (e.g. "AnimFramework Example") — depend on AnimFramework via
  `modReferences`, own all actual sprite sheets, JSON layout files, and one `Register()` call
  per NPC they animate.

The library never contains a single NPC-specific number. Anything specific to how the Guide
(or any NPC) actually animates lives entirely in that NPC's own content mod.

---

## Core Timing Model

- Animation playback is driven by `AnimationClock` (real elapsed time that stops while the game
  is paused), not Terraria's 60-tick `frameCounter` — this is the "bypass" that makes uncapped/off-tick-rate animation possible,
  and it only matters because frame data (below) is fully declarable, not hardcoded.
- **Per-row (per-animation) speed declaration, not per-NPC.** Idle, Walking, Attacking etc.
  each declare their own pace independently — a single global FPS for the whole NPC was
  explicitly rejected (it forces all animations to share one pace and fights frame count to
  fake speed differences).
- **Each row may declare either `fps` OR `duration` (loop time in seconds) — whichever is the
  natural way to think about that specific animation:**
  - `fps` is the default/primary path — matches how spriting tools (Aseprite etc.) and artists
    naturally think, and is frame-count-independent (adding a frame doesn't change per-frame
    speed, just total cycle length).
  - `duration` is for animations where a fixed real-world rhythm is what matters, independent
    of frame count — e.g. "this idle breathes once every 2 seconds" regardless of whether it's
    drawn with 4 or 12 frames, or "5 frames over 0.5s" to get a smoother double-loop-per-second
    feel without drawing extra frames. Confirmed as a first-class use case, not just an
    "override" bolted onto fps.
  - Whichever one is declared, the loader derives the other (`fps = frameCount / duration`,
    `duration = frameCount / fps`).
- **Per-frame duration (uneven hold times within one row) is explicitly deferred** — not
  needed for anything currently specified (idle/walk/attack/sit/talk). Schema should be able
  to absorb it later as an optional field without breaking the uniform case.
- **Static, not velocity-linked** — Walking animation fps does not scale with the NPC's actual
  `NPC.velocity` magnitude. Decided to keep this simple; flagged as a possible future
  enhancement if a walk cycle ever visibly desyncs from apparent movement speed.

## Loop Modes (per clip, modder's discretion — not predicted by the framework)
- **`Loop`** — wraps to frame 0 forever. Used for continuous animations (Idle, Walking) and for
  attack types where vanilla's own attack signal is a sustained "still attacking" hold rather
  than a pulse (e.g. Dryad's Ward, a channeled/looping cast).
- **`PingPong`** — plays forward then backward.
- **`Once`** — plays through all frames once, then holds/freezes on the last frame
  (`IsFinished` flips true) until state changes. Used where the vanilla attack signal is more
  pulse-like or where the animation is meant to play through once and hold a pose (e.g. Guide:
  points bow, holds static pose briefly, vanilla fires the arrow via its own logic).
  - Explicitly **not** tied to matching vanilla's internal `AttackTime` — the freeze/hold
    duration is self-contained and declared independently. Not knowing vanilla's exact
    `AttackTime` value for a given NPC is not a blocker.
  - Exit-from-Attacking logic only checks `IsFinished` for `Once`-mode clips; `Loop`-mode
    attacks exit purely off vanilla's own attack-active signal dropping.

Frame-count-per-loop-mode is **not** a hard rule tied together — they're independent fields.
(Vanilla convention observed: throw/shoot/melee commonly use 4 frames, Guide/Traveling
Merchant are a named 5-frame exception for shooting, magic commonly uses 2 frames — useful as
a default assumption per attack type when authoring, not something to hardcode in the
framework.)

---

## Sheet Authoring Convention

- **Rigid row-per-animation layout**: one sprite sheet, each row = one animation state, frames
  stacked left-to-right within a row, frame 0 at column 0. Chosen deliberately to match
  existing Terraria spriting community conventions (vertical row stacking is standard) for
  familiarity — even though the framework doesn't technically require it.
- **Declared via external JSON file** (not XML — chosen for simplicity, native
  `System.Text.Json` support, and parity with common animation-tool export formats), sitting
  alongside the sheet's PNG as a bundled mod asset.
- **Declarable per-clip origin/pivot**, defaulting to frame-centered when not specified — for
  asymmetrical sprites (off-center weapon swings, capes, uneven padding) where a naive centered
  origin would cause visible drift/misalignment against the hitbox as frames change.
  - Per-frame (not just per-clip) origin override identified as a possible future escape hatch
    if a specific animation's asymmetry changes frame-to-frame, but not needed for v1.
- **Declarable per-clip timer interval** (`intervalMin`/`intervalMax`, seconds), for timed
  overlays like Blink — see `ANIMFRAMEWORK_NEW_FEATURES.md` §4. Same three-tier fallback as the
  rest of this section: no clip authored → the feature never fires at all (decided upstream by
  whether the clip exists); clip authored with neither field → framework default range; one or
  both declared → the author's numbers win (one alone = a fixed, non-jittered interval; both
  swapped automatically, with a load-time warning, if authored backwards).
- **Declarable per-row frame size override** (`frameWidth`/`frameHeight`), falling back to the
  sheet-wide default when unset — see `ANIMFRAMEWORK_NEW_FEATURES.md` §9. Lets one state (e.g.
  a Walking row whose art needs extra width for a wide-swinging cape/sleeve) use a bigger frame
  without forcing every other row in the same sheet to pay for that width too. `frameWidth` is
  always safe to vary per row; a non-default `frameHeight` requires the sheet's PNG to be
  physically laid out to match, since row position is computed as `Row * FrameHeight` — not
  validated, on the author to get right.
- **Declarable sheet-wide default origin** (`originX`/`originY` at the sheet level, alongside
  `frameWidth`/`frameHeight`), used by any row that doesn't declare its own — see
  `ANIMFRAMEWORK_NEW_FEATURES.md` §9. Useful when most/all rows in a sheet share the same
  non-center pivot, so it doesn't need repeating on every row. Same "both X and Y required
  together" rule as the per-row origin; resolution order is row → sheet → computed frame-center.
- **Declarable per-clip loop-count interval** (`loopCountMin`/`loopCountMax`, completed loops —
  a different unit from the seconds-based `intervalMin`/`intervalMax` above, kept as a separate
  pair rather than overloading those), for loop-count-triggered features like the Idle-paired
  Blink state — see `ANIMFRAMEWORK_NEW_FEATURES.md` §11. Same three-tier fallback shape as
  `intervalMin`/`intervalMax`, applied to loops instead of seconds.

### JSON Schema (conceptual shape)
```json
{
  "texturePath": "AnimFrameworkExample/Assets/Guide_Sheet",
  "frameWidth": 64,
  "frameHeight": 64,
  "rows": [
    { "state": "Idle",        "row": 0, "frameCount": 8,  "fps": 8,  "loop": "Loop" },
    { "state": "SpecialIdle", "row": 1, "frameCount": 12, "fps": 8,  "loop": "Loop" },
    { "state": "Walking",     "row": 2, "frameCount": 8,  "fps": 12, "loop": "Loop",
      "frameWidth": 96, "frameHeight": 64, "originX": 48, "originY": 56 },
    { "state": "Attacking",   "row": 3, "frameCount": 5,  "duration": 0.5, "loop": "Once" },
    { "state": "Sitting",     "row": 4, "frameCount": 4,  "fps": 4,  "loop": "PingPong" },
    { "state": "Talk",        "row": 5, "frameCount": 2,  "fps": 3,  "loop": "Once" }
  ],
  "overlays": [
    { "state": "Blink", "row": 6, "frameCount": 3, "fps": 12, "loop": "Once",
      "intervalMin": 2, "intervalMax": 5 }
  ]
}
```
(Optional sheet-level `"shimmerTexturePath"` points at a same-sized sheet drawn for the town
NPC's shimmered variant; omit it to use the normal sheet for both. `fps` vs `duration` per row is either/or; loader derives whichever wasn't given. Frame count
is always required — it's what tells the reader how many columns to step through in that row.
`intervalMin`/`intervalMax` are optional and only meaningful for timed-trigger overlays like
Blink — omit both for the framework default, declare either or both to override it. `frameWidth`/
`frameHeight`/`originX`/`originY` on a row, shown here on Walking, are all optional overrides —
every other row above omits them and just inherits the sheet-level `frameWidth`/`frameHeight`
and the computed frame-center origin, exactly as before this schema addition.)

---

## Baseline Animation Set (current scope)

**True states** (mutually exclusive, one active at a time, drive `CurrentState`):
1. Idle
2. SpecialIdle (occasional variant idle — no vanilla-native signal, framework's own
   randomized-interval logic; **implemented**, reusing the Blink overlay's interval mechanism —
   see Implementation Status)
3. Walking
4. Attacking (single, non-directional attack animation for now — directional attack variants
   like up/forward/down explicitly deferred; noted that since states are just string keys, this
   is additive later, e.g. `Attacking_Up`, without reworking the controller)
5. Sitting
6. Talk — **revised from overlay to a true state.** Originally spec'd as a single static
   mouth-open frame layered on top of whatever state was active (see the superseded "Resolved"
   note this replaces, below). Revisited: an overlay mark can't express a full bodily talking
   animation (a hand gesture, a distinct stance) without the underlying pose fighting it, and
   what prompted rethinking this in the first place was realizing "Talk" was originally meant to
   cover vanilla's ambient NPC-to-NPC chat behavior (random emote-bubble chatter, a real system —
   `ModEmoteBubble`/`EmoteBubbleLoader`, confirmed via docs.tmodloader.net), not just the
   player-has-dialog-open case — a case worth a full animation, not a static mark. Now a full
   clip like any other state, author-chosen `Loop` or `Once` like every other row — no
   special-casing beyond the trigger check itself.
   - **Superseded note** (kept for history): "layered on top of the current state's frame as a
     second draw call (not a frame swap/replace) — matches the existing overlay list mechanism."
     This was `AnimationController.DrawOverlays`'s original design; Talk no longer uses it (the
     `ShouldDrawOverlay` "Talk" case was removed). The timed-overlay Blink mechanism (§ below)
     still exists and is independent of this.
7. IdleBlink (state name `"IdleBlink"`; was `"Blink"` before 2026-11-26, renamed so it no
   longer shares a name with the Blink overlay) — a *second*, independent mechanism from the
   timed Blink overlay (see `ANIMFRAMEWORK_NEW_FEATURES.md` §4 vs §11): a real sibling state to Idle,
   triggered by completed Idle loops rather than elapsed time, with no hold-to-completion
   override (resumed velocity cuts it off immediately via the Walking gate, same as any other
   idle-family variant that doesn't ask for priority). Optional and Idle-only by design — for
   this project's actual needs, blinking during Walking/Attacking/etc. wasn't wanted, so the
   overlay mechanism's cross-state reach wasn't worth its per-state alignment cost here. The two
   mechanisms coexist; a content mod picks whichever fits a given NPC.

**State groups** (not a true state themselves — a prefix modifier over Walking/Idle/IdleBlink/
SpecialIdle only; see `ANIMFRAMEWORK_NEW_FEATURES.md` §12): a group detector registered for that
NPC type with `RegisterStateGroupDetector(npcType, detector)` picks an active prefix (e.g. `"Alert"`), and each of those four names is looked up as
`{prefix}{Name}` first, falling back to the bare name whenever a grouped flavor isn't authored.
Attacking/Talk/Sitting are never grouped — a threat being nearby doesn't change how Guide
attacks or talks, only how he stands/walks/blinks/varies when he isn't doing either. Alert is
the first (and currently only) registered group, covering `AlertIdle`/`AlertWalking`/
`AlertIdleBlink`/`AlertSpecialIdle`, all derived from `NPCID.Sets.DangerDetectRange[Type]`-based
proximity to hostile NPCs.

**Explicitly deferred to later passes:**
- Triggered/one-shot secondary effect animations (e.g. directional attack "gust" effect) —
  conceptually distinct from the Talk overlay: event-triggered (fires on state transition, e.g.
  attack start) rather than continuously active, needs its own position offset (not centered on
  NPC.Center) and direction-flipping, likely `Once` loop mode. Deferred until core
  controller + authoring is solid.
- Per-frame duration arrays.
- Velocity-linked animation speed.
- Directional attack variants.

---

## Vanilla Compatibility — Confirmed API Findings

Binding to vanilla signals rather than inventing parallel detection, where a real vanilla
signal exists:

- **Walking** — `NPC.velocity != 0`. Direct, reliable, vanilla sets this itself.
- **Talk** (now a state, not an overlay — see Baseline Animation Set above) — two triggers, both
  edge-detected so a `Once`-mode Talk clip plays exactly once per conversation start and then
  falls back to whatever state would otherwise apply (Idle/Walking/etc.), even while the
  underlying condition is still true:
  - `Main.LocalPlayer.talkNPC == NPC.whoAmI` for the player-has-this-NPC's-dialog-open case.
    Direct, reliable; level-triggered for the whole dialog-open duration, so
    `AnimationController.DetermineTalkingState` edge-detects the rising edge itself
    (`AnimationRuntime.WasPlayerTalking`) rather than re-firing every tick.
  - `GlobalNPC.PickEmote`'s `otherAnchor` parameter (confirmed via docs.tmodloader.net: "A
    WorldUIAnchor instance indicating the emote conversation target; use this to access the NPC
    or Player this NPC is communicating with") for vanilla's ambient NPC-to-NPC emote-bubble
    chatter (`ModEmoteBubble`/`EmoteBubbleLoader`) — the case previously flagged here as not yet
    wired. Gated on `otherAnchor != null` so a solo mood emote (rain, biome, etc.) doesn't
    falsely trigger Talk. See `AnimationController.NotifyTalkTriggered`.
  A `Loop`-mode Talk clip keeps the original level-triggered behavior (held for as long as the
  player's dialog is open) for authors who prefer that — see `DetermineTalkingState`.
- **Sitting** — vanilla has native town-NPC sitting (added 1.4.4, bench/chair interactions),
  but `NPC` exposes no readable "is sitting" field: the earlier `NPC.sitting.isSitting` guess
  didn't compile, and the only related member is the `SitDown(...)` placement helper. **Not
  wired**; needs in-game testing to find a reliable signal (see Implementation Status).
- **Idle / SpecialIdle** — no vanilla-native equivalent concept; stays framework-original logic.
  Idle is effectively the fallback default when no other state applies.
- **Attacking** — confirmed via `NPCID.Sets` fields and hooks, **not uniform across all NPCs**:
  - `NPCID.Sets.AttackType[Type]`: **-1** = NPC does not attack at all (no Attacking state
    needed/exists for these); **0** = throwing; **1** = shooting; **2** = magic; **3** = melee.
  - `NPCID.Sets.AttackTime[Type]`: ticks (60/sec) — vanilla's own "how long the attack sequence
    runs" value. Real per-NPC values NOT confirmed from memory/search (only found other
    modders' example/tutorial numbers, e.g. 90 or 45 ticks for custom example NPCs — these are
    NOT vanilla Guide's actual number). Confirmed as not a blocker: `Once`-mode clips are
    self-timed and don't need to reference this value at all.
  - `NPCID.Sets.AttackFrameCount[Type]`: vanilla's own frame count for that NPC's attack.
  - `NPCID.Sets.DangerDetectRange[Type]`: detection radius for engaging enemies — closest thing
    to an "alert range," but it's a detection radius, not a separate pre-attack animation state.
    No dedicated "alert" boolean field found.
  - `NPCID.Sets.AttackAverageChance[Type]`: denominator for attack-attempt chance (aggressionish).
  - **For attack types 0/1/2 (throw/shoot/magic)**: a documented, confirmed hook family tied to
    projectile creation — determines projectile type/delay, and a specific hook for "this NPC
    has already created a projectile and will create more as part of one attack" (multi-shot
    attacks). This is the reliable "attack is happening" trigger point for these three types.
    There's also a dedicated hook for customizing how a shooting-type NPC's weapon/bow is drawn
    — flagged as possibly the cleaner attach point for arm/bow-out visuals rather than folding
    it into the body row sheet.
  - **For attack type 3 (melee)**: no matching documented hook found. Genuine unknown — needs
    direct empirical testing (spawn a melee-attacking town NPC, log field/hook behavior) before
    building assumptions for this category. Deliberately left unwired in code (see
    `ANIMFRAMEWORK_NEW_FEATURES.md` §5).
  - `TownNPCAttackProj(NPC npc, ref int projType, ref int attackDelay)` (types 0/1) and
    `TownNPCAttackMagic(NPC npc, ref float auraLightMultiplier)` (type 2) — **confirmed directly
    against `docs.tmodloader.net`'s GlobalNPC class reference** (1.4-stable), now wired in
    `AnimationController`/`AnimatedGlobalNPC`/`AnimatedNPC`. The matching `ModNPC`-side overrides
    (same names, no leading `NPC npc` parameter) are unverified by that same source and should
    still be checked at first Build+Reload.

**Observed vanilla frame-count-by-attack-type convention** (useful default assumption per type,
not a hard framework rule):
- Throwing / Shooting / Melee: 4 attacking frames baseline.
- Magic: 2 frames (consistent with a channeled/held cast, though frame count alone doesn't
  prove looping vs. one-shot — confirmed via the Dryad's actual persistent "Dryad's Ward" effect
  as a real sustained/loop-appropriate example).
- Guide and Traveling Merchant: named exception, 5 frames for shooting (not the 4-frame
  baseline).
- Shooting has a further special case: an alternate "arm raised" frame swapped in when aiming
  straight ahead, on top of the normal sequence.

"Single anim" in this project's usage = plays the full declared frame sequence once and holds
(`Once` mode) — **not** a single static frame. (Single static frame = the Talk overlay case,
`FrameCount: 1`, a separate concept.)

**Hooks confirmed directly against docs.tmodloader.net while implementing the POC** (see
Implementation Status): `ModNPC.PreDraw(SpriteBatch, Vector2 screenPos, Color drawColor)`,
`ModNPC.FindFrame(int frameHeight)`, `GlobalNPC.InstancePerEntity`, `GlobalNPC.AppliesToEntity
(TEntity, bool lateInstantiation)`, `GlobalNPC.Clone(TEntity from, TEntity to)`,
`GlobalNPC.PreDraw(NPC, SpriteBatch, Vector2, Color)`. Note `FindFrame` — vanilla's own
animation hook, called once per fixed-60Hz logic tick — was deliberately **not** used for
playback here: it would cap real-time smoothness back down to tick rate on any client whose
render rate outpaces the logic tick, defeating the framework's whole premise. Instead, state
is resolved once per tick in `PostAI` (cheap, and keeps signals flowing while off-screen), and
`PreDraw` (called once per actual render) picks the frame from real elapsed time and draws it,
with the NPC's own draw fully overridden (`return false`) rather than relying on
`npc.frame`/`FindFrame`.

---

## Modularity / Activation Model

- **Not global by default.** `AnimatedGlobalNPC.AppliesToEntity` checks `AnimationRegistry`
  and only attaches real per-instance state to NPC types that were explicitly registered —
  unregistered NPCs cost nothing and are untouched.
- **Two attachment paths, kept both:**
  1. **Base-class** (`AnimatedNPC : ModNPC`) — for building a new NPC from scratch. Simpler,
     used for your own POC NPC.
  2. **Registry** (`AnimationRegistry` + `AnimatedGlobalNPC`) — for attaching animations to
     *any* NPC (vanilla NPC via `NPCID.*` constant, or another mod's NPC via their exposed
     type), without needing inheritance. Required for compatibility/expansion-style content
     mods like "AnimFramework Example."
- **Explicit registration only — no mod detection.** No scanning `ModLoader.Mods`, no
  reflection-based discovery of what's installed. A content mod depends on the library via
  `modReferences` in `build.txt` and calls `AnimationRegistry.Register(...)` for exactly the
  NPC type(s) it owns, in its own `PostSetupContent()`. This mirrors standard cross-mod
  compatibility patterns both within tModLoader (`Mod.Call()`-style APIs, e.g. Boss Checklist)
  and generally (DI container registration, WordPress `add_action` hooks) — the generic core
  stays a dumb lookup table; specific behavior announces itself into that table.
- **Resolved**: two content mods registering the same NPC type is last-registered-wins, by
  virtue of the underlying `Dictionary` overwrite in `AnimationRegistry.Register` — documented
  explicitly in that method's XML doc comment rather than left as a silent surprise.

---

## Fallback / Validation Needs

Distinguish between two categories:
- **Computed values from good data never need fallback** — frame/loop math (division, modulo)
  is correct by construction once inputs are valid.
- **Anything depending on hand-authored JSON needs defensive handling**, since that's where
  human mistakes actually occur:
  - Missing/zero fps or duration — handled: `AnimationClip`'s constructor falls back to a safe
    default (1 fps) rather than dividing by zero.
  - Unrecognized loop mode string (typo) — handled: `SheetLayoutLoader` logs a warning and
    defaults to `Loop` rather than throwing during deserialization.
  - Declared row index beyond the actual sheet's pixel height, or a `frameCount` wider than the
    sheet's actual pixel width — both handled: `SheetLayoutLoader` bounds-checks each row
    against the loaded texture's real dimensions on both axes, logs an error, and skips that row
    rather than silently reading garbage/out-of-bounds pixels. The width check is against the
    sheet's actual width, not any other row's frame count — rows are free to use fewer frames
    than the sheet's widest row (e.g. a 2-frame Attacking row sharing a sheet with an 8-frame
    Walking row).
  - Missing required state (e.g. no `Idle` row) — handled by design: `AnimationController.PreDraw`
    falls back to vanilla drawing (`return true`) whenever the current state has no registered
    clip.
  - Missing/bad texture path — **resolved as loud**: `SheetLayoutLoader.Load` throws at load
    time if `texturePath` is missing, and `ModContent.Request` throws its own error if the path
    doesn't resolve to a real asset — both surface immediately as a load-time error rather than
    leaving an invisible NPC with no clue why.

---

## Implementation Status (as of 2026-11-26)

C# targeting tModLoader. Hook signatures were checked against docs.tmodloader.net, but treat the
first `Build + Reload` after any change as the real verification.

**Library (`AnimFramework/`):**
- `AnimFramework.cs`: library mod shell. `Unload()` clears all framework statics (registry,
  detectors, `AnimatedNPC` set cache, clock) so content-mod assemblies can unload on reload.
- `Animation/AnimationClock.cs`: framework-wide real-time clock (`Stopwatch`-backed) that
  stops while `Main.gamePaused`. Every timestamp in the framework reads `AnimationClock.Now`.
- `Animation/AnimStates.cs`: name constants (`AnimStates.Idle`, `Walking`, `Attacking`, `Talk`,
  `Sitting`, `SpecialIdle`, `IdleBlink`; `AnimOverlays.Blink`).
- `Animation/LoopMode.cs`: `Loop` / `PingPong` / `Once`.
- `Animation/AnimationClip.cs`: immutable clip definition (row, frames, fps/duration, loop mode,
  origin, interval and loop-count ranges, optional shimmer-variant sheet).
- `Animation/AnimationPlayer.cs`: per-instance playback head. Frame index, `IsFinished` and
  completed-loop count are all computed from elapsed clock time, so they're valid from the
  update tick even for NPCs that aren't being drawn.
- `Animation/AnimationSet.cs`: states dictionary + overlays list for one NPC type.
- `Animation/AnimationRuntime.cs`: per-instance mutable state. Rebuilt per instance via the
  `Clone()` overrides.
- `Animation/AnimationController.cs`: shared state machine and drawing. `Update()` resolves state
  once per tick (from `PostAI`); `PreDraw()` only draws (and resolves once itself if no update
  has run yet, e.g. bestiary icons). Draws with `npc.GetAlpha(drawColor)` and the shimmer sheet
  when `npc.IsShimmerVariant`. Public API: `RegisterStateDetector(npcType, detector)` and
  `RegisterStateGroupDetector(npcType, detector)`, both scoped per NPC type.
- `Animation/AnimatedNPC.cs`: base class for new NPCs (attachment path 1). Per-type cached set;
  a throwing `BuildAnimationSet()` is logged once and that type falls back to vanilla drawing.
- `Animation/AnimationRegistry.cs`: NPC type → set builder (attachment path 2). Builder runs on
  first use and is cached; a throwing builder is logged once and that NPC falls back to vanilla
  drawing. Re-registering a type logs a warning.
- `Animation/AnimatedGlobalNPC.cs`: `GlobalNPC`, `InstancePerEntity`, applies only to registered
  types. `PostAI`, `PreDraw`, attack and emote hooks all delegate to `AnimationController`.
- `Animation/SheetLayoutLoader.cs`: JSON schema + loader (see Sheet Authoring Convention).
  Bad rows are logged and skipped; out-of-bounds rows are rejected; an optional
  `shimmerTexturePath` must match the main sheet's pixel size or it's ignored with a warning.

**Example content mod (`AnimFrameworkExample/`):**
- `GuideAnimationSetup.cs`: registers `NPCID.Guide` through the registry and an "Alert" state
  group (hostile within `DangerDetectRange`) scoped to the Guide only.
- `Assets/Guide_Sheet.json` + `.png`: 10 rows: Idle, Walking, Attacking, SpecialIdle, Talk,
  IdleBlink, and Alert flavors of Idle/Walking/IdleBlink/SpecialIdle. The PNG is
  **generated placeholder art**; swap it (and adjust the JSON) without touching engine code.

**Known gaps:**
- **Sitting** is not wired. `NPC` exposes no readable "is sitting" field (only the `SitDown(...)`
  placement helper; the earlier `npc.sitting.isSitting` guess didn't compile). Needs in-game
  testing to find a signal.
- **Melee attacks** (AttackType 3) have no confirmed hook, so they never enter Attacking.
- **Falling/Airborne**: no confirmed vanilla signal; easy to add later as a per-type detector.
- **Vanilla draw extras**: returning `false` from `PreDraw` skips vanilla's own draw. Check
  in-game whether extras such as party hats still appear.
- **Pause and the bestiary**: the clock stops on pause, so bestiary icons opened during
  singleplayer autopause won't animate.
- Triggered secondary effects (attack-gust style) and per-frame origins remain out of scope.

---

## Rollout Plan
1. Ship as proof of concept first: **one NPC, one state (Idle only)**, small frame count.
   **Resolved**: the POC targets the real vanilla Guide (`NPCID.Guide`) via the Registry +
   `AnimatedGlobalNPC` attachment path, not a from-scratch lookalike NPC via the base class —
   chosen over the base-class route because "AnimFramework Example" is explicitly a
   compatibility/expansion-style content mod per the Modularity section, and proving the
   registry path end-to-end (including vanilla-NPC coexistence) was judged more valuable than
   deferring that risk to a later pass. Confirm real-time playback and rendering work correctly
   in-game before adding Walking/Attacking/Sitting/Talk.
2. Expand to the full baseline set (Idle, SpecialIdle, Walking, Attacking, Sitting, Talk
   overlay) once the core loop is confirmed working.
3. Publish the library mod (AnimFramework) plus one minimal single-NPC content mod (Guide
   Expanded Animations) as the first public example, so the "modular, multi-mod" design claim
   is demonstrated in practice, not just asserted.
4. Later passes (explicitly out of scope for now): directional attacks, triggered secondary
   effects, per-frame timing, velocity-linked animation speed.

---

## Dev Environment Setup (for reference)
1. Install tModLoader via Steam; enable Developer Mode; use Workshop → Open Mod Sources.
2. Install a C# IDE — Visual Studio Community (recommended, best debugging) or VS Code + C#
   extension. Install the .NET SDK if not bundled.
3. Point tModLoader's Mod Sources folder at (or copy/symlink into it) this repo's
   `AnimFramework/` and `AnimFrameworkExample/` folders — they're laid out as standard
   tModLoader mod source folders (`build.txt` + namespace-matching `.cs` files) but were
   authored outside a live tModLoader install, so **no `.csproj` is committed**. Opening/building
   them once through tModLoader's Mod Sources UI (or its "Build" command) generates the
   per-mod `.csproj` tModLoader itself expects; if that doesn't happen automatically for a
   folder it didn't create, use tModLoader's "Create Mod" wizard against an empty temp
   folder and copy the generated `.csproj` in as a starting point.
4. `build.txt` in both mods already sets `displayName`/`author`/`version`/`side`;
   `AnimFrameworkExample/build.txt` already has `modReferences = AnimFramework`.
5. Build + Reload AnimFramework first, then AnimFrameworkExample, to catch compile errors
   early per mod.
6. Enable both mods, test in-world with an actual Guide NPC present (and with multiple Guides
   spawned via multiplayer/`cheatsheet`-style tools if available, to catch any instance-sharing
   regression — the exact bug `AnimatedNPC.Clone`/`AnimatedGlobalNPC.Clone` are meant to
   prevent).

Note: tModLoader's exact UI wording/menu locations should be confirmed against current official
docs (`docs.tmodloader.net`) rather than trusted from memory here, since the interface shifts
across versions.
