# Pipeline to v0.4.0

Written 2026-07-03 for Mike's return. Current shipped state: **v0.3.3**, suite **977/977**,
8 season packs bundled, the full career loop (wizard → briefing → stage grid → result entry →
confirm → standings → season review → sign & continue) working and user-verified.

## What v0.4.0 IS

**Career Hub v1 — the immersive shell** (the biggest visible leap, per career-hub-vision.md),
delivered on top of complete content and a hardened suite. The minimal loop is never buried;
depth is opt-in. Design already synthesized in `docs/dev/career-hub-design.md`.

## The 3 things that unblock the build (yours to decide/confirm on return)

1. **The 5 hub design questions** (top of `docs/dev/career-hub-design.md`) — these gate Stage 2:
   - Q1 tab reveal (progressive unlock vs always-present)
   - Q2 the "Why?" inspector scope (first-class clickable-everywhere vs contained) — *biggest fork*
   - Q3 era-skin depth for v1 (full telegram/fax/email now vs later)
   - Q4 tear-off "own windows" (News/Scrapbook pop-out vs single window)
   - Q5 first minigame (Setup Gamble vs Media Moment)
2. **Ctrl+Z after mech/acc** — a quick real-keyboard confirm (the one thing I couldn't inject
   headlessly; the fix is sound but unproven by a physical chord).
3. **In-game per-class grid cap** (optional) — if AMS2 caps a class's grid below the track's
   AI number, that integer folds in as a final `min()` on grid sizes.

## Pipeline stages

| Stage | Scope | Needs you? | Status |
|---|---|---|---|
| **0 · Hygiene** | Harden 2 known test-infra flakes (below); fold in any bugs from your camping-trip testing | No | Ready |
| **1 · Content** | ~~Remaining F1 fleet (1974/78/91/95/2000)~~ ✅ **DONE (v0.3.4, 13 packs)**. Still open: season-readiness score in the wizard; apply the grid-cap number if you have it | No | Partly done |
| **2 · Hub Increment 1** | The hub shell (re-home the loop verbatim → tab rail) + News feed over the journal + EraTheme swap | **Q1–Q5** | Blocked on you |
| **3 · Hub Increment 2** | Team + Career/History lens tabs + the "Why?" inspector (read-only over existing state) | Q2 | After Stage 2 |
| **4 · Hub Increment 3** | First minigame (Setup Gamble or Media Moment) + Contracts-as-era-documents | Q5 | After Stage 3 |
| **Gate** | You playtest the hub end-to-end → tag **v0.4.0** | Yes | — |

Each stage ships additively behind the existing `ICareerSession` seam, keeps the loop and the
sacred keyboard-grammar/keystroke-budget tests untouched, and gets a version bump + publish
(0.3.x continues for Stage 0/1 patches; 0.4.0 is tagged at the Gate).

## Known flakes to harden in Stage 0 (not product bugs — both pass on retry)

1. **SQLite parallel-disposal race** — `Companion.Tests.ViewModels.EraSignAndContinueTests`
   intermittently throws `ObjectDisposedException` on a `SQLitePCL.sqlite3` handle under
   parallel execution. Cause: `Microsoft.Data.Sqlite` connection pooling + rapid temp-DB
   open/close across parallel tests. Fix options: `Pooling=False` on the test connection
   string, or `SqliteConnection.ClearAllPools()` in the temp-DB fixture teardown before file
   delete, or a non-parallel `[Collection]` for the Data/session tests. Production
   `CareerDatabase.Open` should keep pooling — the fix belongs in the test fixture.
2. **Render-harness STA timing** — one `Companion.RenderHarness.Tests` case flaked once
   (deferred dispatcher op not drained before the assert). Fix: pump the dispatcher to idle
   (a `DispatcherFrame` wait / `DoEvents`-style helper) before assertions, or serialize the
   harness tests.

## What stays as-is (deferred past v0.4.0, per PLAN.md phases)

- **Phase 2:** shared-memory auto-capture (`docs/dev/auto-capture.md` is specced; needs a real
  race — your Gate 3), Second Monitor import, the full team-ledger economy + bankruptcy ladder.
- **Phase 3:** Owner-Driver tycoon mode, rivalries, hardcore aging.
- **Phase 4:** pack-creator GUI, non-F1 series (CART/Group C/GT/DTM), junior ladder, guest drives.

## Fast resume checklist (for when you're back)

1. Skim `docs/dev/career-hub-design.md`, answer Q1–Q5 (a sentence each is enough).
2. Confirm Ctrl+Z-after-mech/acc works in v0.3.3.
3. (Optional) drop me the per-class grid cap.
4. Say go — I run Stage 0 + Stage 1 autonomously, then build the hub Stage 2→4 to your answers.

## Resume prompt — paste this into a NEW thread (fill the blanks)

> Resume the AMS2 Career Companion project in `Z:\Claude Code\ams2-career-companion` — it's a
> git repo and everything is committed (last tag v0.3.3, suite 977/977). Read
> `PIPELINE-0.4.0.md`, `ROADMAP.md`, and `docs/dev/career-hub-design.md` to get oriented, then
> continue toward v0.4.0.
>
> My answers to the 5 hub design questions:
> - Q1 tab reveal: ______
> - Q2 "Why?" inspector scope: ______
> - Q3 era-skin depth for v1: ______
> - Q4 tear-off "own windows": ______
> - Q5 first minigame: ______
>
> Confirmations: Ctrl+Z after mech/acc → [works / still broken]; per-class grid cap → [number /
> none found]; bugs I found while testing → [list / none].
>
> Plan: run Stage 0 (flake hardening) + Stage 1 (remaining fleet + wizard readiness score)
> autonomously, then build hub Increments 1–3 to my answers, version-bumping + publishing each
> stage and tagging v0.4.0 at the gate.

(Even a bare "resume the AMS2 Career Companion in Z:\Claude Code\ams2-career-companion, read
PIPELINE-0.4.0.md and continue" works — the machine facts auto-load from memory and the repo
docs carry the rest. The blanks above just save a round-trip.)
