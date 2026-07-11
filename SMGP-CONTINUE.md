# CONTINUE SMGP — resume prompt (paste into a new session)

Resume the **AMS2 Career Companion** SMGP work (`Z:\Claude Code\ams2-career-companion`, WPF /
.NET 10, single self-contained exe, branch **`hub/increment-4`**). **DIRECTION (Mike, locked in
`docs/dev/smgp-design.md`): SMGP-ONLY until the Super Monaco GP replica mode is truly polished.**

## First: orient (do this before touching anything)
1. **Read memory**: `MEMORY.md` → `ams2-hub-build-progress.md` (TOP blocks = current, read the first
   3-4) → `ams2-next-content-arc.md` → `mike-build-maximally.md` → `ams2-mclaren-skin-pipeline.md`.
2. **Verify the repo**: `git log --oneline -8`, `git status`, **`git stash list`** (there is a
   stashed WIP — see thread B). Treat every path/line below as a hint to confirm; this repo moves fast.
3. **Discipline (non-negotiable)**: keep the full suite + RenderHarness green (`dotnet test
   Companion.slnx`); **the f1db oracle is 77/77, NEVER touched**; **byte-identical replay is a LOCKED
   invariant** — a sim/fold change that breaks `ReplayService.Resimulate` self-consistency does NOT
   ship; sim/fold rows are envelope-versioned + per-career gated; pack/grid changes affect NEW careers
   only; commit + push per slice; republish when the app is CLOSED (a background watcher pattern —
   `until Get-Process AMS2CareerCompanion is gone; then cp publish exe → dist`, backup-first, sync ONLY
   changed data, NEVER wholesale-copy dist). No `gh` CLI — PRs via the cached git cred + `Z:\Claude
   Code\open-pr.ps1`. ⚠ A **babysit/auto-finalize pipeline** is live on this repo+memory (it commits,
   pushes, republishes, and writes memory blocks on its own) — **verify HEAD before assuming state.**

## What is DONE and shipped (head `f277a95`)
- **THREAD B — SMGP CLEAN SEAT-SWAP — SHIPPED** (`f277a95`, RC deployed to dist 11:32): the player is
  their OWN distinct driver (`RoundGridResolver.SyntheticPlayerDriverId`), no cascade, `AiSeatOverrides`
  always empty; NEW smgp careers only. Fixed the byte-identical-replay BLOCKER in
  `ReplayService.SeasonPlayerDriverId` (clean-swap career scores under the synthetic id for the whole
  season, don't walk the grid to the authored AI). Removed dead `OccupantOf`/`ChallengerHomeSeat`. A
  3-lens verify workflow caught + I fixed two DISPLAY regressions (player win credited to the benched
  AI; standings showed the raw id) via a `PlayerDisplayName()` seam. Suite 1799 + 46 green, replay
  invariant solid. (A lost title defense now drops the "Ceara takes Madonna" flourish — clean model,
  Mike's choice.) ⚠ `stash@{0}` still lingers (drop denied by the guard); its content IS in `f277a95`.
- **Item B — SMGP "What Really Happened" almanac** (`ff62523` + render pin `805027d`): per-race
  fictional history unlocked on the History tab once you finish a race. `SmgpWhatReallyHappened` (Core)
  + `data/rules/smgp/what-really-happened.json` (16 venues) + `ICareerSession.SmgpWorldHistory()` +
  `HistoryViewModel.SmgpWorld`/`HistoryView`. Tests green (loader + all-16 drift guard + render pin).
- **Item A — per-race skin activation → FIXED FULL SET** (`af9fadb` then `65a59d3`):
  `Companion.Ams2.Skins.RoundLiveryActivator` + `LiveryOverrideWriter.Liveries/Activate/Deactivate`.
  `65a59d3` changed staging from a per-round ROTATION to activating the FIXED FULL SET
  (`ApplyRound(packLiveries, packLiveries, ...)` — activate every inactive SMGP livery, park none).

## THREAD A — SKINS still defaulting in-game (the LIVE issue Mike is testing)
**Symptom:** ~6 cars pool-fill with stock drivers in AMS2 (Gino Mandelli / Ronaldo Moreira / Canio
Leone / Nilton Pereira / Ivanti Castelli / Jean Alarie); the 6 SMGP second-cars (Blume / Gould /
Alfven / Nono / Arai / Rampal) are absent. **Root cause (understood):** AMS2 loads a car model's
custom liveries ONCE at LAUNCH, only the active (numeric-slot) ones; the OLD per-race rotation
switched skins on AFTER launch → not in AMS2's loaded pool → pool-fill, restart-every-round.
**Fix shipped (`65a59d3`, deployed to dist 10:52):** activate the fixed full set once → stable pool.
**⚠ Mike uses RCM (a mod manager, NOT a skin tool) — it re-applies its own slot config on launch and
UNDOES the app's activation.** Two more facts: AMS2's per-MODEL livery cap looks like ~10 (the mod
uses slots 51-60 + `X..` placeholders for overflow), and **g3m4 has 15 liveries so ~5 can't fit at
once** (a hard AMS2 limit); the old code even assigned slots >60 (61-64) which AMS2 won't load.
**AWAITING MIKE'S TEST:** stage a round in the app → **fully quit AMS2** → **relaunch AMS2 DIRECTLY
(not via RCM)** → load the race. If the fitting skins finally stick → SOLVED (accept ~5 g3m4 cars on
base-game). If not → deeper AMS2 behavior; reconsider.
**Also open (Mike's latest screenshot):** the New-career **content-verification** step warns **4
liveries won't bind** — `Iris #33 B. Salgado` + `Azalea #34 M. Larssen` (the McLaren `mclaren_mp45b`
mod cars) and `Millions #5 N. Jones` + `Millions #6 G. Alberti` (g3m3 pair). So the McLaren mod + the
g3m3 skins aren't fully detected in the install (`GridPreflight`/livery scan). Fold into the skin work.
**Mike's DIRECTION (deferred, validate the fix first):** wants the app to OWN the mods — bundle
`C:\Users\KOBRA\Downloads\SMGP SKINS V1.rar` (extracts to **1.1 GB** of DDS!) + `Z:\Claude
Code\mclaren-mp45b-skin\McLaren MP45B - SMGP Iris & Azalea (by KobraFleetworks) v1.2.zip`, delete the
RCM mod, and re-install from inside the app with a correct fixed slot config + AI-name merge. I pushed
back: bundling won't change AMS2's cap/launch-cache (an engine limit), and 1.1 GB is heavy — so PROVE
the fixed-set fix on the existing install + a DIRECT launch first; build the bundled installer only if
Mike still wants full ownership after it works. Archive layout: SMGP → `Automobilista 2/Vehicles/
Textures/CustomLiveries/Overrides/formula_classic_g3m1..4/` + `UserData/CustomAIDrivers/
F-Classic_Gen3.xml`; McLaren → `Overrides/mclaren_mp45b/` + `OPTIONAL .../F-Classic_Gen3_ADD-these-2-
drivers.xml` (merge, don't overwrite).

## THREAD B — SMGP CLEAN SEAT-SWAP — ✅ SHIPPED (`f277a95`, RC deployed 11:32)
Done this session. The blocker (3 byte-identical-replay failures) was `ReplayService.SeasonPlayerDriverId`
resolving the livery's AUTHORED AI instead of the synthetic id → season-end reputation lookup missed the
player (`championshipPosition` null on replay). Fix: a clean-swap career scores under the synthetic id for
the whole season, return it straight. Removed dead `OccupantOf`/`ChallengerHomeSeat`. A 3-lens verify
workflow found ZERO determinism siblings and TWO display regressions (player win credited to the benched
AI; standings showed the raw `driver.player-entrant`) — fixed via a `CareerSessionService.PlayerDisplayName()`
seam (`CharacterName() ?? (IsDistinctDriverPlayer ? "You" : null)`), routed through the grid card, skin
crib, standings/review identity, race-news winner/leader, and the season digest. Regression test added.
Suite 1799 + 46 green. Mike accepted the clean model dropping the "Ceara takes Madonna" flourish. NEW smgp
career needed to see it. ⚠ `stash@{0}` still lingers (drop was guard-denied) — content is in `f277a95`.

## Suggested order next session
1. **Read Mike's skin test result** (THREAD A) — the LIVE open issue; it decides everything about the skin
   fix. (Stage → fully quit AMS2 → relaunch DIRECT, not via RCM → do the fitting skins stick?) Cannot be
   run for Mike — it needs him in-game.
2. If the fixed-set works → optionally build the app-owned mod installer (Mike's direction) + fix the 4
   non-binding liveries (McLaren + g3m3). If not → dig into AMS2's per-model cap / launch behavior.
3. Item C (per-team car-specs card, waiting on Mike's numbers), item D (SMGP news Phase 2).
⚠ pre-existing SUITE FLAKINESS under parallel xunit (random unrelated tests fail ~1 in 3 full runs, all
green in isolation / on a clean run) — NOT from recent changes.
