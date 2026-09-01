# AnimFramework

Real-time, tick-rate-independent sprite animation for tModLoader NPCs. Design rationale, open
questions, and full status live in [ANIMFRAMEWORK_SPEC.md](ANIMFRAMEWORK_SPEC.md) — read that
first for *why* things are shaped this way.

## What's in this repo

Two standard tModLoader mod source folders, laid out exactly as tModLoader's own "Open Mod
Sources" would produce them, so they can be dropped straight into a real Mod Sources directory:

- **`AnimFramework/`** — the library. No gameplay content; pure engine
  (`AnimFramework/Animation/*.cs`).
- **`GuideExpandedAnimations/`** — the example content mod (current POC), depending on
  AnimFramework via `modReferences`. Registers the vanilla Guide's Idle animation.

No `.csproj` is committed for either mod — tModLoader generates/expects one per install, and
committing a hand-guessed one risked being wrong for your setup. See the "Dev Environment
Setup" section at the bottom of the spec for how to get one.

## Current status: POC, Idle only

Per the spec's rollout plan, this is step 1: one NPC (the real vanilla Guide, via the
registry/`AnimatedGlobalNPC` attachment path), one state (Idle), to prove the real-time
rendering pipeline before expanding to Walking/Attacking/Sitting/Talk. The Idle sprite sheet
(`GuideExpandedAnimations/Assets/Guide_Sheet.png`) is a **generated placeholder** — a tinted
silhouette, not real art — so swapping in real Guide sprites is just a file + JSON row change,
no code change.

**Not yet built or verified against a real tModLoader install.** Key API surfaces (ModNPC/
GlobalNPC hook signatures, `Mod.GetFileBytes`, `ReLogic.Content.Asset<T>`) were checked against
docs.tmodloader.net while writing this, but the very first `Build + Reload` should still be
expected to surface small fixes.

**Deliberately not wired yet** (see spec's Implementation Status / Known Gaps): Attacking,
Sitting, SpecialIdle. Each has a `TODO` comment in `AnimationController.DetermineState` marking
exactly what vanilla signal it's waiting on.

## Quick orientation for editing

- State-machine + draw logic lives in one place — `AnimFramework/Animation/AnimationController.cs`
  — shared by both attachment paths (`AnimatedNPC` base class and `AnimatedGlobalNPC` registry
  path) so there's nothing to keep in sync between them.
- Per-NPC-type data (`AnimationClip`, `AnimationSet`) is immutable and shared; per-NPC-*instance*
  playback state (`AnimationRuntime`, wrapping a `Stopwatch`) is rebuilt fresh on every
  `Clone()` — that's the fix for the classic MemberwiseClone shared-state bug where every
  spawned NPC of a type would otherwise animate in lockstep.
- To animate a new vanilla/other-mod NPC: add a JSON sheet layout + PNG to your content mod,
  then one `AnimationRegistry.Register(NPCID.Whatever, () => SheetLayoutLoader.Load(Mod,
  "Assets/Whatever.json"))` call in a `ModSystem.PostSetupContent()` — see
  `GuideExpandedAnimations/GuideAnimationSetup.cs`.
- To build a brand-new NPC from scratch instead, inherit `AnimatedNPC` and implement
  `BuildAnimationSet()`.
