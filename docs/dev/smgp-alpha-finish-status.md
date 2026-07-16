# SMGP alpha finish status

_Coordinator ledger · refreshed 2026-07-16 by Claude (Head of Coding) · source of truth is the current working tree plus executable evidence, not the dated roadmap text._

## Baseline

- Branch: `hub/increment-4`.
- The July 13–15 feature wave (progression v2 implementation, Safe Stage & Launch, unified News +
  History archive, background music, session intros, track banners, SMGP grid-car refresh) is now
  **committed and pushed** (2026-07-16). The earlier preserve-in-place freeze is lifted; normal
  commit discipline applies again.
- Baseline verification on that snapshot (2026-07-16):
  - `dotnet build Companion.slnx --no-restore` — **green**, 0 warnings / 0 errors.
  - `dotnet test Companion.slnx --no-restore --no-build` — **green**, 2,554 logic tests + 172 RenderHarness tests.

## Roadmap truth

| Roadmap prompt | Status | Current evidence |
|---|---|---|
| **#1 — CareerOver hard-stop and ending reconciliation** | **SHIPPED** | `SmgpBattleFoldDeterminismTests` proves the Level-D floor terminal gate and reopen behavior; `AccidentFoldDeterminismTests` proves a fatal career refuses later rounds and replays byte-identically; `DeathScreenHandoffTests` and `MortalityScreensRenderTests` cover the mortality and fired-ending routes. |
| **#2 — per-race livery parking/rotation** | **PERMANENTLY SUPERSEDED — NEVER REVIVE** | AMS2 loads a model's active liveries once at launch. `RoundLiveryActivator` therefore keeps the fixed authored SMGP livery set active; the seeded DNQ field is display/fold data and must not trigger per-race skin rotation. |
| **#3 — living flavour, reshuffle, and campaign celebration** | **SHIPPED** | `SmgpPitCrewAdviceTests`, `SmgpRivalQuotesTests`, `SmgpSeasonVarietyTests`, `SmgpMultiSeasonDnqTests`, `SmgpRulesTests`, `SmgpFinaleScreenTests`, and `SmgpFinaleRenderTests` cover the data corpora, season variety/standings reshuffle, `CampaignFlawless`, and the 17-season finale. |

## Shipped alpha surface

- Death, injury, sit-out, Normal restore, Hardcore permadeath, and the SMGP floor-knockout screens are present and render-tested.
- SMGP `CareerOver` blocks further manual result entry, auto-simulation, and rollover.
- The fixed authored livery set, seeded dynamic DNQ display, clean seat ownership, promotion/decline/demotion, season reshuffle, and 17-season finale are implemented.
- Level 300, the 499 Skill Point lifetime budget, Racing DNA, mastery skills, atomic skill plans, resets, and replay coverage are present in the current tree.
- The existing AMS2 staging path already owns preflight, timestamped backups, force gating, fixed-livery activation, base-game fallback, and staged-file external-change watching.

## Open blockers / evidence still required

1. **~~Functional blocker~~ — RESOLVED (2026-07-15):** `Ams2Launcher` owns `steam://run/1066890` behind the machine seam; `Ams2LauncherTests` prove the exact shell URI, the actionable no-throw failure path, and the safe no-OS default.
2. **~~GUI blocker~~ — RESOLVED (2026-07-15):** `BriefingViewModel`/`BriefingView` carry the combined **Stage & Launch** primary action plus the complete Ready / Changed Externally / Reapply Required / Staging Failed / Launch Failed state language; suite green over it.
3. **Professional-pass blocker:** the main menu still needs the Pit Wall Command Rail acceptance pass and independent renders at all four required size/scale combinations.
4. **Release-evidence blocker:** a fresh-career SMGP acceptance sweep and byte-identical reopen/resimulation proof must be recorded after the functional/UI slices are green.

## Exactly one next slice

**Pit Wall Command Rail acceptance pass — GUI lane only (blocker 3).**

Render and accept the main menu at all four required size/scale combinations, without touching
`src/Companion.Core/**`, `src/Companion.ViewModels/**`, `src/Companion.Data/**`, or
`src/Companion.Ams2/**`. When it is green, the single next slice becomes the release-evidence
sweep (blocker 4: fresh-career SMGP acceptance run plus byte-identical reopen/resimulation proof),
then refresh this ledger.
