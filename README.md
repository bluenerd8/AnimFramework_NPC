# AnimFramework

Real-time, tick-rate-independent sprite animation for tModLoader NPCs. Design rationale, open
questions, and full status live in [ANIMFRAMEWORK_SPEC.md](ANIMFRAMEWORK_SPEC.md) — read that
first for *why* things are shaped this way.

## What's in this repo

Two standard tModLoader mod source folders, laid out exactly as tModLoader's own "Open Mod
Sources" would produce them, so they can be dropped straight into a real Mod Sources directory:

- **`AnimFramework/`** — the library. No gameplay content; pure engine
  (`AnimFramework/Animation/*.cs`).
- **`AnimFrameworkExample/`** — the example content mod, depending on AnimFramework via
  `modReferences`. Animates the vanilla Guide.

No `.csproj` is committed for either mod — tModLoader generates/expects one per install, and
committing a hand-guessed one risked being wrong for your setup. See the "Dev Environment
Setup" section at the bottom of the spec for how to get one.

## Current status

**Framework:** Idle, Walking, Attacking (throw/shoot/magic), Talk, SpecialIdle, IdleBlink, the
Blink overlay, and state groups (e.g. Alert) are implemented. Sitting is not wired yet (no
readable vanilla signal found), and melee attacks have no hook. See the spec's Implementation
Status for details.

**Example mod:** the Guide sheet covers all of the above plus Alert variants, but
`AnimFrameworkExample/Assets/Guide_Sheet.png` is **generated placeholder art**. Swapping in
real sprites is a PNG + JSON change, no code change.

## Quick orientation for editing

- State-machine + draw logic lives in one place — `AnimFramework/Animation/AnimationController.cs`
  — shared by both attachment paths (`AnimatedNPC` base class and `AnimatedGlobalNPC` registry
  path), so there's nothing to keep in sync between them. State is resolved once per tick
  (`PostAI`); drawing only picks the frame, timed off `AnimationClock` (real time that stops
  while the game is paused).
- Per-NPC-type data (`AnimationClip`, `AnimationSet`) is immutable and shared; per-NPC-*instance*
  playback state (`AnimationRuntime`) is rebuilt fresh on every
  `Clone()` — that's the fix for the classic MemberwiseClone shared-state bug where every
  spawned NPC of a type would otherwise animate in lockstep.
- To animate a new vanilla/other-mod NPC: add a JSON sheet layout + PNG to your content mod,
  then one `AnimationRegistry.Register(NPCID.Whatever, () => SheetLayoutLoader.Load(Mod,
  "Assets/Whatever.json"))` call in a `ModSystem.PostSetupContent()` — see
  `AnimFrameworkExample/GuideAnimationSetup.cs`. Extra state logic goes through
  `AnimationController.RegisterStateDetector(npcType, ...)` /
  `RegisterStateGroupDetector(npcType, ...)`, scoped to that NPC type. Use the `AnimStates`
  constants for state names.
- To build a brand-new NPC from scratch instead, inherit `AnimatedNPC` and implement
  `BuildAnimationSet()`.
