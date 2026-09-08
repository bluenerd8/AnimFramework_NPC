# AnimFramework — New Features & Design Additions

Companion to `ANIMFRAMEWORK_SPEC.md`. That doc is the full project spec; this one isolates just
the features/decisions layered on *after* the original core engine (clip/player/timing/JSON/
registry/attachment) was already designed and written. Useful as a focused diff when picking
this project back up elsewhere.

---

## 1. State vs. Overlay vs. Timed-Event Taxonomy

The single biggest structural addition: recognizing that not everything animatable is a
"state," and conflating the categories causes real design mistakes (e.g. almost treating Blink
or Talk as competing states when they're not mutually exclusive with anything).

Three distinct categories:

| Category | Definition | Confirmed examples | Trigger source |
|---|---|---|---|
| **State** | Mutually exclusive, held in `CurrentState`, drives the base clip | Idle, SpecialIdle, Walking, Attacking, Sitting | External vanilla signal (Walking, Sitting, Attacking) or framework-original logic (SpecialIdle) |
| **Condition overlay** | Layered on top of active state, on exactly when an external fact is true | Talk | Vanilla flag check (`Main.LocalPlayer.talkNPC == NPC.whoAmI`) |
| **Timed overlay** | Layered, fires periodically on an internal clock | Blink | Framework-original timer logic |

Practical effect: an NPC can be simultaneously Idle + Talking + mid-Blink — none of these
compete for `CurrentState`, only the true "state" ones do.

---

## 2. Extensible State Detection (Detector List Pattern)

**Problem this solves:** `DetermineState()` was a single monolithic method — adding one new
check (e.g. Falling) meant a subclass had to override the *whole method* and re-implement all
existing Idle/Walking/Attacking/Sitting logic just to slot in one more `if`.

**Rejected alternatives, and why:**
- **A `ModConfig` toggle** — wrong audience. Toggles suit end-user on/off switches; this is a
  content-mod *author* who needs to add a line of code, not a settings menu entry.
- **A separate "expanded states" add-on mod** — too much packaging overhead for a couple of
  small optional helpers. Reserved as the right call only if this later grows into something
  genuinely large and independently versioned.

**Chosen design:** a detector list living in the core library itself, opt-in via registration,
inert unless used:

```csharp
public delegate string StateDetector(NPC npc);
public static void RegisterStateDetector(StateDetector detector);
```

A content mod enables a new state in one line, from its own setup code:

```csharp
AnimationController.RegisterStateDetector(npc => npc.velocity.Y > 2f ? "Falling" : null);
```

**Status: implemented**, in `AnimationController.cs` — the shared state/draw logic layer both
attachment paths (`AnimatedNPC`, `AnimatedGlobalNPC`) already delegated to. `AnimationController`
was promoted from `internal` to `public` so content mods in another assembly can reach
`RegisterStateDetector`; every other member on it stays `internal` (same-assembly-only), so the
public surface this exposes is exactly that one method.

Detection order in `DetermineState()`, most-confident-signal-first:
1. Attacking (edge-triggered off the attack hooks — see §5 below)
2. Sitting (`NPC.sitting.isSitting`)
3. Registered detectors, in registration order — first non-null result *with a matching clip on
   this NPC* wins
4. Walking (`NPC.velocity != Vector2.Zero`)
5. Idle (final fallback)

Detectors run *before* Walking specifically so a check like the Falling example above can
distinguish itself from Walking's broad "any nonzero velocity" condition, without having to
out-rank Attacking/Sitting to do it.

---

## 3. Confirmed Graceful Degradation (tested, not just theorized)

Directly observed in testing: an NPC missing a given state's animation "plays the next best
animation" rather than breaking. Two layers make this true by design:

1. **Detector fires (built-in or registered), but no clip registered for that state name** —
   discarded via a `set.States.ContainsKey(result)` check; falls through to remaining checks /
   default logic. A state existing in *code* never forces every NPC to support it.
2. **`CurrentState` somehow points at an unregistered clip anyway** — `PreDraw`'s
   `_players.TryGetValue` returns `true` on failure, deferring to vanilla's own drawing for
   that frame. No crash, no garbled sprite.

Net effect: an NPC authored with only Idle+Walking, and one with the full state set, both work
correctly side by side. Partial authoring is safe by construction — this is the property every
addition below (detector list, Attacking, Sitting, Blink) was specifically built to preserve:
each one is a no-op for any NPC that doesn't opt in by authoring the matching clip.

---

## 4. Blink (Timed Overlay)

Confirmed to exist in vanilla's own NPC sprite sheet layout (a dedicated blink frame alongside
idle/walk/talk/sit/attack, per community frame-layout documentation). Categorized as a **timed
overlay** (see taxonomy above) — same layering/draw mechanism as Talk, but time-driven
(periodic, internal-clock-based) rather than condition-driven (Talk's vanilla-flag check).

"More fluid" blink = simply a multi-frame `Once`-mode overlay clip (closing → closed → opening)
instead of one static frame — no new mechanism required, just richer authored art using
existing overlay machinery.

**Status: implemented**, in `AnimationController.ShouldBlink` + two new fields on
`AnimationRuntime` (`BlinkClock`, `NextBlinkAt`):
- Each NPC instance rolls its own randomized interval so multiple NPCs sharing a Blink clip
  don't blink in lockstep. The bounds are **declarable per-clip**, not a framework constant —
  `AnimationClip.IntervalMin`/`IntervalMax`, fed by optional `intervalMin`/`intervalMax` JSON
  fields on the row (see `ANIMFRAMEWORK_SPEC.md`'s Sheet Authoring Convention section for the
  full fallback rule): no Blink clip authored → nothing fires, full stop; clip authored with
  neither field → framework default (2.5–6s, not vanilla-confirmed, just a reasonable
  out-of-the-box feel); one or both declared → the author's numbers win. This mirrors
  `Fps`/`Duration`/`Origin`'s existing "optional, sane default when unset" shape exactly, rather
  than inventing a new convention for one more optional field.
- Nothing Blink-specific about the mechanism: `IntervalMin`/`IntervalMax` live on the generic
  `AnimationClip`, described as meaningful only to whatever timed-trigger feature reads them —
  SpecialIdle's still-unwritten random-chance-after-N-ticks logic (§ below) can reuse the exact
  same two fields instead of inventing its own interval schema when it's written.
- A single-frame Blink clip is visible for exactly one draw call (`AnimationPlayer` marks a
  1-frame clip finished immediately); a multi-frame `Once`-mode clip plays its full
  closing/closed/opening sequence per firing via the same `IsFinished` mechanics every other
  `Once`-mode clip already uses.
- Entirely opt-in per NPC: `ShouldDrawOverlay`'s `"Blink"` case only ever runs for NPCs whose
  `AnimationSet.Overlays` actually contains a clip declared with `state: "Blink"` — an NPC
  authored without one costs nothing here, same as every other optional piece.

---

## 5. Attacking (types 0/1/2 — melee explicitly unhandled)

Wired to the vanilla `TownNPCAttackProj` (throw/shoot, AttackType 0/1) and `TownNPCAttackMagic`
(AttackType 2) hooks, overridden on both attachment paths purely to *observe* — the ref
parameters are never touched, so this can't change vanilla's actual attack behavior even if a
hook fires in an unexpected case.

**Design (see `AnimationController.DetermineAttackingState`):**
- **Entry** is edge-triggered: a hook firing restarts `AnimationRuntime.AttackSignalAge`
  (a `Stopwatch`), and `DetermineState` enters Attacking on the next frame that stopwatch is
  running and within a short buffer window.
- **Exit** differs by the clip's own declared loop mode, matching the spec's Loop Modes design
  exactly:
  - **`Once`-mode** Attacking clips hold until they finish on their own (`IsFinished`) —
    self-timed, independent of vanilla's `AttackTime`, per the spec's explicit "not tied to
    matching vanilla's internal AttackTime" decision.
  - **`Loop`-mode** (channeled) Attacking clips have no confirmed continuous "still attacking"
    vanilla signal — the trigger hooks fire only at discrete attack-decision points, not every
    tick. A buffer (`AttackSignalBufferSeconds`) treats repeated hook firings as "still
    attacking" and lets the state lapse shortly after they stop, sized from
    `NPCID.Sets.AttackTime[npc.type]` — vanilla's own declared attack-sequence length in ticks
    (a confirmed public field; decompiled 1.4.0.5 source shows vanilla using this exact value as
    an attack countdown internally: `this.ai[1] = (float) NPCID.Sets.AttackTime[this.type]`) —
    rather than an arbitrary constant. Flagged as a best-effort proxy either way, since it's a
    different timing layer from vanilla's actual internal state; worth revisiting if a real
    continuous signal is ever confirmed.
  - (Ruled out: vanilla's `NPC.ai[0]` does hold a numeric attack-state constant continuously
    while attacking — confirmed present in decompiled 1.4.0.5 source (10/12/14/15 per
    AttackType 0/1/2/3) — which would make a much cleaner continuous "still attacking" signal
    than the buffer above. Deliberately not used: these are undocumented private AI values, not
    part of any stable modding API, unconfirmed for the actual target version, and exactly what
    the `TownNPCAttackProj`/`TownNPCAttackMagic` hooks exist to let mods avoid depending on.)
- **Melee (AttackType 3) is deliberately never wired.** No confirmed hook exists for it (see
  `ANIMFRAMEWORK_SPEC.md`'s Vanilla Compatibility section — still a genuine unknown pending
  direct empirical testing). An NPC with a melee AttackType simply never receives an attack
  signal, so it falls through Attacking exactly as if no clip were authored for it at all — same
  degradation path as every other optional piece, not a special case.
- **Hook names/signatures confirmed**: `TownNPCAttackProj(NPC npc, ref int projType, ref int
  attackDelay)` and `TownNPCAttackMagic(NPC npc, ref float auraLightMultiplier)` — verified
  directly against docs.tmodloader.net's GlobalNPC class reference (1.4-stable), not just
  memory/paraphrase. The ModNPC-side overrides (same names, minus the leading `NPC npc`
  parameter, matching the existing `PreDraw` convention in this codebase) are unverified by the
  same source and should still be checked at first Build+Reload.

---

## 6. Sitting

Wired to `NPC.sitting.isSitting` (vanilla's own town-NPC sitting struct, added 1.4.4). Not
directly confirmed for 1.4.4, but decompiled 1.4.0.5 source confirms the identical pattern
already exists on `Player` (`public PlayerSittingHelper sitting;`, read as `sitting.isSitting`)
— town-NPC chair-sitting, added later, very likely mirrors that existing field/shape rather
than inventing a new one. Flagged in-code in `AnimationController.DetermineSittingState` as
still worth a Build+Reload check in case the exact name/type differs.

---

## 7. Falling / Airborne (deferred, open question — explicitly not tackled this pass)

Still **not added**, by deliberate choice, for the same reasons as before:

- **No confirmed vanilla signal yet**, unlike Walking (`NPC.velocity`), Sitting (`NPC.sitting`),
  or Attacking (the confirmed-if-unverified hook family). Would need the same kind of
  verification pass as the melee-attack unknown before committing to an approach.
- **Vanilla's own frame-layout convention groups jump and fall into one shared animation
  section**, per community frame-layout reference — suggesting a single `Airborne` state may be
  truer to vanilla's own visual convention than separate `Falling`/`Jumping` states. Worth
  deciding this before splitting them, not after.

Once a detection approach is confirmed, this is now a genuine one-line addition via the
detector-list pattern (§2) — no framework changes required, e.g.:

```csharp
AnimationController.RegisterStateDetector(npc => npc.velocity.Y > 2f ? "Falling" : null);
```

**Status: open question, no code, no confirmed vanilla signal.**

---

## 8. Declarable Per-Clip Origin

For asymmetrical sprites (off-center weapon swings, capes, inconsistent padding) where a
naive centered origin (`FrameWidth/2, FrameHeight/2`) causes visible drift against the hitbox
across frames.

**Status: implemented.** `AnimationClip.Origin` (optional `Vector2?`, defaults to
frame-centered via `GetOrigin()` when unset) and the matching `originX`/`originY` fields in
`SheetLayoutLoader`'s row schema. Per-frame (rather than per-clip) origin override remains a
possible future escalation, not needed for v1.

---

## 9. Per-Row Frame Size Override & Sheet-Level Origin Default

Sheet authoring had two remaining rigidities on top of §8's per-clip origin: `frameWidth`/
`frameHeight` were sheet-wide only (every row forced to share one cell size, even a row whose
art genuinely needs to be bigger — e.g. a long-sleeved character whose sleeve only extends
during Walking), and origin was per-row only with no way to declare a shared non-center default
once for a sheet where most/all rows are consistently off-center.

**Status: implemented.** Both are optional, fully backward-compatible additions to
`SheetLayoutLoader`'s schema — a sheet declaring neither behaves identically to before this
change.

- `SheetRowDefinition.FrameWidth`/`FrameHeight` (nullable): a row-level override, resolved
  against the sheet default via `SheetLayoutLoader.ResolveDimension` (row value if `> 0`, else
  sheet default; a declared-but-invalid `<= 0` override degrades to the sheet default with a
  load-time warning rather than crashing or skipping the row).
- `SheetDefinition.OriginX`/`OriginY` (nullable): a sheet-wide default origin, resolved via
  `SheetLayoutLoader.ResolveOrigin`'s three-tier cascade — row's own origin, if both X and Y are
  declared, wins; else the sheet's default, if both are declared there; else `null`, so
  `AnimationClip.GetOrigin()`'s computed frame-center fallback still applies exactly as before.

**Fragility worth stating plainly, not burying:** `FrameWidth` is always safe to vary per row —
each row's horizontal frame math (`frameIndex * FrameWidth` in `AnimationClip.GetSourceRect`) is
self-contained and doesn't touch any other row. `FrameHeight` is not. Rows are stacked
vertically as `Row * FrameHeight` — an index times that clip's own height — so a row declaring a
taller `FrameHeight` than the sheet default only reads the correct pixels if the sheet's PNG was
physically laid out to match. This is not validated or protected against; it's on the author to
arrange the sheet correctly when using a non-default `FrameHeight`. A future, more robust fix
would replace the index-based `Row` with an explicit per-row vertical pixel offset — not
attempted here, kept as a possible follow-up.

---

## 10. Resource Pack Differentiation (positioning clarity, not a code feature)

Confirmed via research: vanilla resource packs are locked to **exact original canvas
dimensions and frame grid** per NPC — same sheet size, same row layout, same frame count as
the file being replaced, varying per-NPC (not a universal number) and with zero ability to add
frames, rows, or resize. This is a hard engine-level constraint resource packs cannot lift.

Useful to keep in mind for how this project is described to others: AnimFramework isn't "a
fancier resource pack" — it structurally can do what resource packs cannot (new frame counts,
new sheet sizes, new rows/states per NPC), because frame data is read from the mod's own
declared JSON rather than hardcoded engine tables.

---

## 11. Idle-Paired Blink (Loop-Count-Triggered State)

A second, *different* mechanism sharing the name "Blink" with §4's timed overlay — worth being
explicit that these are two independent, coexisting options, not one feature replacing the
other. Motivation: for this project's actual authoring needs, blinking is conventionally an
Idle-only detail (eyes stay open during Walking/Attacking/etc. in most pixel-art games), so the
timed overlay's real selling point — one clip works across every state — wasn't buying anything
in practice, while its per-state alignment cost (an overlay's fixed origin has to line up
against whatever base pose is currently showing) was a real cost. A full duplicate Idle pose
with eyes closed, authored as its own row, sidesteps that entirely.

**Status: implemented**, in `AnimationController.DetermineIdleState`, `AnimationPlayer`, and new
`AnimationClip`/`AnimationRuntime` fields:

- **A real sibling state to Idle** (`AnimationSet.States["Blink"]`), not a layered mark — same
  full-clip authoring as any other state, sharing Idle's texture/frame-size conventions per the
  §9-style per-row overrides if it ever needs to differ.
- **Triggered by completed Idle loops, not elapsed time** — deliberately different from every
  other timer in this framework (Blink-overlay, SpecialIdle), specifically to guarantee the
  switch always lands on a clean loop boundary instead of an arbitrary mid-cycle moment, which a
  seconds-based timer can't promise. `AnimationPlayer.GetCompletedLoops()` derives this from the
  same elapsed-time/Fps math `GetCurrentFrame()` already uses — no separate clock, and it
  naturally resets to 0 whenever Idle is freshly (re-)entered, which is exactly the right
  semantic (can't have "completed 3 Idle loops" if the NPC spent that time Walking instead).
- **Bounds are a separate declarable pair, `loopCountMin`/`loopCountMax`** (`AnimationClip`,
  fallback default 0–2 loops) — kept distinct from `intervalMin`/`intervalMax` on purpose, since
  those are seconds; reusing the same fields for two different units depending on which feature
  reads them would be a real footgun, not a simplification.
- **No hold-to-completion override, unlike SpecialIdle** — though the two behave more alike now
  than they used to. SpecialIdle's `HoldInProgressSpecialIdle` still outranks the Walking gate in
  `DetermineState` so a triggered gesture (or its `AlertSpecialIdle` flavor) survives being
  interrupted by e.g. Talk/Attacking, but it's now velocity-gated: real walking (`npc.velocity !=
  Vector2.Zero`) cuts it off immediately, same as Blink, rather than holding it to completion
  regardless of movement — see that method's own doc comment in `AnimationController.cs` for why
  the earlier "hold across any nonzero-velocity blip" behavior let a SpecialIdle gesture
  accidentally keep playing over an NPC that had already started walking. Blink never had this
  carve-out at all: it lives entirely inside `DetermineIdleState`, ranked *below* the Walking
  gate, so resumed velocity cuts it off immediately with zero special-casing — a deliberate
  simplicity trade-off, not an oversight.
- **Known footgun, documented rather than enforced (same precedent as SpecialIdle's own
  Loop-mode caveat, see `HoldInProgressSpecialIdle`'s doc comment in `AnimationController.cs`):**
  Blink must be authored `Once`. If declared `Loop`, `IsFinished`
  never goes true (`AnimationPlayer.GetCurrentFrame`), so `DetermineIdleState`'s
  still-mid-playthrough check never releases it — the NPC would visibly get stuck in Blink
  forever. Not runtime-validated, same as the equivalent SpecialIdle risk.
- **Open question, not yet resolved:** min-loop-count of 0 (the framework default's floor) means
  two Blinks can fire back-to-back with zero Idle loops between them. Estimated acceptable at the
  chosen default range (0–2: roughly 1-in-9 chance of one immediate repeat, 1-in-27 of two) and
  deliberately left unguarded for now — verify how it actually reads in-game before adding any
  anti-clustering logic. If it does look repetitive, the fix is a semantic one (redefine the
  bounds as "guaranteed 1 loop, plus `min`–`max` additional" so zero-gap repeats are ruled out by
  construction) rather than tracking consecutive-fire counts at runtime.

---

## 12. Alert and the State-Group Pattern

§2's `RegisterStateDetector` example (`npc.velocity.Y > 2f ? "Falling" : null`) was always
hypothetical — Alert is the first content mod to actually use the detector list, and it started
as exactly that: a plain detector returning `"AlertIdle"` directly, with the detector itself
hand-checking `npc.velocity` to stay out of Walking's way. That worked for an Idle-only slice,
but didn't scale to "Alert also has its own Walking/Blink/SpecialIdle" — those live in
completely different resolution paths (`DetermineIdleState`'s hardcoded name checks, and Blink's
own per-instance runtime timers) that a plain `NPC -> string` detector has no way to reach.

**Status: implemented as a new, distinct mechanism — the state-group pattern —
not a bigger plain detector.** A *group* detector (`AnimationController.RegisterStateGroupDetector`,
same `StateDetector` delegate shape, different list and meaning) returns a prefix (`"Alert"`) or
null, rather than a full state name. `DetermineState`, the Walking gate, and `DetermineIdleState`
all resolve `Walking`/`Idle`/`Blink`/`SpecialIdle` through a new
`AnimationController.TryResolveGroupedClip` helper: try `prefix + baseName` first, fall back to
the bare name. `Attacking`/`Talk`/`Sitting` are deliberately never grouped — they're ranked above
group resolution entirely, so an NPC already mid-attack always shows Attacking, never an Alert
pose, regardless of how "alert" it also is.

- **`GuideExpandedAnimations/GuideAnimationSetup.cs`'s detector shrank, not grew**, once this
  landed: `DetectAlertGroup` is just "is a threat nearby → `\"Alert\"` : `null`" — no velocity
  branching. The engine's own Walking-gate/`DetermineIdleState` generalization now decides
  `AlertWalking` vs `AlertIdle` vs `AlertBlink` vs `AlertSpecialIdle` vs their bare equivalents,
  generically, for any future group the same way.
- **Detection is still `NPCID.Sets.DangerDetectRange[Type]`-based** (confirmed against
  docs.tmodloader.net: pixel radius a town NPC uses to engage enemies, `-1` sentinel meaning
  vanilla's own 200px default) plus a proximity scan over `Main.npc` for anything active,
  non-friendly, and damaging — a reasonable heuristic, not a single confirmed vanilla field the
  way `DangerDetectRange` itself is.
- **Once-mode hold-to-completion (`HoldInProgressSpecialIdle`, Blink's own "still mid-playthrough"
  check) now matches by suffix** (`EndsWith("SpecialIdle")` / `EndsWith("Blink")`) instead of the
  literal bare name, so a grouped flavor like `AlertSpecialIdle` gets the identical
  hold-to-completion guarantee without either check needing to know which groups exist or
  re-derive the current tick's prefix — a Once-mode clip that already started just finishes what
  it started, group or no group.
- **Deliberate simplification: Blink/SpecialIdle timers are shared across groups, not doubled per
  group.** One `NextBlinkAtLoopCount`, one `SpecialIdleClock` — whichever flavor happens to be
  active when the shared timer fires is what gets shown. Switching groups mid-idle-session
  already naturally resets the loop counter regardless (any state-name change calls
  `AnimationPlayer.Play`), so nothing extra was needed to make this consistent.
- **Known cost, not yet addressed:** group detectors run once per render frame per animated NPC
  that reaches this point in `DetermineState`, not once per logic tick — same draw-time-only
  characteristic as the plain detector list and everything else in this framework. Fine for one
  NPC; worth throttling (e.g. only rescanning every few frames) if this pattern gets reused
  across many animated NPCs at once.
- **Flicker at the detector's own boundary — addressed, not just flagged.** A raw group signal
  (e.g. Alert's `DangerDetectRange` check) can legitimately flip on/off rapidly for a genuinely
  plausible reason, not just a theoretical edge case: an NPC that skirmishes crosses that
  boundary back and forth as part of normal behavior, not as a rare fluke. `DetermineState` now
  resolves the group through `AnimationController.ResolveGroupPrefix` rather than the raw
  detector poll directly — it holds the last active group for `GroupSignalBufferSeconds` (1.5s,
  a flat framework constant, not derived from anything vanilla) after the raw signal stops being
  reported, restarting the hold every tick the signal is live again. This is what actually
  prevents `DetermineIdleState`'s "fresh idle session" check (`CurrentState != idleName`) from
  re-rolling the Blink threshold on every boundary crossing, and prevents Walking/Idle/Blink/
  SpecialIdle from visibly flickering between grouped and bare flavors. `AnimationRuntime` gained
  `LastActiveGroup`/`GroupSignalAge` to support this — same "age since last seen" shape as
  `AttackSignalAge`, but restarted every live tick rather than on discrete trigger events, since
  group detectors are polled continuously rather than pulsed.

---

## Current Code Status (recap)

As of this doc, `AnimationController.cs` implements: Idle, Walking, Sitting, Attacking
(types 0/1/2; melee unhandled by design), the detector-list extension point, and the Blink
timed overlay, on top of the already-solid core engine (`AnimationClip`, `AnimationPlayer`,
`AnimatedNPC`, `AnimationRegistry`, `AnimatedGlobalNPC`, `SheetLayoutLoader`) and the
already-resolved Talk overlay / per-clip origin.

**Still open:**
1. SpecialIdle — framework-original random-chance-after-N-ticks logic, not implemented.
2. Falling/Airborne (§7) — no confirmed vanilla signal; deliberately deferred.
3. Attacking/Sitting hook and field names need verification at first real Build+Reload (flagged
   in-code at each call site).
4. `GuideExpandedAnimations`'s sample sheet (`Guide_Sheet.json`) is still Idle-only (POC scope)
   — extending it to exercise Walking/Attacking/Sitting/Blink would be a good first real-game
   verification pass once a live tModLoader install is available, and would incidentally be the
   first live test of the graceful-degradation contract (§3) as clips get added incrementally.
