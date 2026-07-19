# SMGP completion audit (mission SMGP-COMPLETE-001, Phase Zero)

_Baseline recorded 2026-07-18, Claude (Head of Coding). Branch `hub/increment-4` @ `fc724a7`,
tree clean. This audit is grounded in executable evidence (test suites, shipped RCs, the
ledgers), not estimates. Statuses per the mission: `VERIFIED_COMPLETE` /
`IMPLEMENTED_NEEDS_VALIDATION` / `PARTIAL` / `MISSING` / `BROKEN` / `BLOCKED_EXTERNAL`._

## Verified baseline

- Branch: `hub/increment-4`; last commit at audit start: `fc724a7` (Passport nationality).
- Build: `dotnet build Companion.slnx` — clean (0 errors; pre-existing nullable/analyzer
  warnings only).
- Logic suite: `dotnet test tests/Companion.Tests` — **2,881 passed, 0 failed**.
- Render harness: `dotnet test tests/Companion.RenderHarness.Tests` — **246 passed, 0 failed**.
- f1db oracle: 77/77 inside the logic suite, untouched.
- Suite flake note: 1–2 SQLite-open tests can flake under the parallel run and pass isolated
  (documented in PROJECT.md; not a defect).
- Publish: `dotnet publish src/Companion.App -c Release` — clean; RC deployed to `dist/` (boot
  verified) as of the 11:55 build + subsequent backend-only commits (card re-badge, nationality,
  rail renders).

## The one open release blocker (owner item)

- **Pit Wall Command Rail acceptance pass.** Independent renders at all four required
  combinations (Dark/Light × 1.00/1.50 UI scale) are GREEN in
  `StartViewCommandRailRenderTests`, and the four review frames sit in
  `scratchpad/review-frames/` (gitignored) for Mike's sign-off. Everything else in this audit is
  the evidence and hardening around the release gate.

## Feature/system matrix (SMGP module)

| System | Status | Evidence / next action |
|---|---|---|
| 17-season world data (smgp-1 pack, lore, eras, identities) | VERIFIED_COMPLETE | `packs/smgp-1` + `data/rules/smgp/seasons.json`; `SmgpSeasonLoreTests`; balance harness runs the real pack 16×34. |
| Season structure & cap (no Season 18) | VERIFIED_COMPLETE | `CampaignProgressionPlan.CreateSmgp` + the Season-18 guard tests. |
| Core loop (briefing → grid → apply → standings) | VERIFIED_COMPLETE | 2,881-test suite incl. fold/determinism coverage; resim byte-identical. |
| Player progression (L300, XP curve, SP, mastery, DNA, cap) | VERIFIED_COMPLETE | `character-progression-v2` suite; MAX-level state + persisted ack; no Level 301 anywhere (grep-clean). |
| Skill tree / reset / respec | VERIFIED_COMPLETE | `MasterySkillGraph/Catalog`, `SkillResetSessionTests`, `SkillPlanSessionTests`. |
| Death/injury/mortality + Game Over | VERIFIED_COMPLETE | Backend + screens + `MortalityScreensRenderTests`, `PostDeathArchiveTests`, `InjuryAvailabilityGateTests`; mortality-mode label on the dossier. |
| Sit-out/auto-sim with DNS honesty | VERIFIED_COMPLETE | `AutoSimFoldTests`, calendar Raced/SatOut/WillMiss chips. |
| CareerOver hard-stop (floor knock-out) | VERIFIED_COMPLETE | Fold-entry guard + tests. |
| Rival ladder / clean seat-swap / promotion-demotion | VERIFIED_COMPLETE | `SmgpPromotionScreenTests`, two-phase offer fold seam, `SmgpCareerBeats` suite. |
| DNQ field (seeded, per-season re-roll) | VERIFIED_COMPLETE | `SmgpDnqField`, `SmgpMultiSeasonDnqTests`. |
| Paddock + live stats + rival readouts | VERIFIED_COMPLETE | `SmgpLiveStats`, Paddock render tests. |
| Dispatches + world stories | VERIFIED_COMPLETE | `SmgpDispatchCorpus`, `SmgpWorldStories` suites. |
| Newsroom (event spine, desks, composer, dedupe) | VERIFIED_COMPLETE | `NewsroomCorpus/Composer/EditorialSelection` suites; memoized projection; 241 templates / 54 events. |
| History archive + encyclopedia + divergence | VERIFIED_COMPLETE | `HistoryArchiveData`, `HistoryEntityIndex`, `CareerDivergence` suites. |
| Campaign timeline strip (SMGP arc + Dynasty previews + FJ prologue) | VERIFIED_COMPLETE | `CampaignTimelineStrip` + `CampaignTimelineRenderTests`. |
| SMGP finale + flawless celebration | VERIFIED_COMPLETE | `SmgpFinaleViewModel/RenderTests`, `SmgpRules.CampaignFlawless`. |
| Canon divergence vs almanac | VERIFIED_COMPLETE | `SmgpCanonDivergence` + SmgpFiction provenance. |
| AMS2 staging (custom AI writer, backup/restore, preflight) | VERIFIED_COMPLETE | `GridStager`, `CustomAiBackup`, staging-contract tests; never overwrite without timestamped backup. |
| Skins (season manager, activator, grid preview) | VERIFIED_COMPLETE | `SkinSeasonManager`, `RoundLiveryActivator`, Skins render tests. |
| Mod ownership vault (anti-RCM) | VERIFIED_COMPLETE | `ModOwnership` + 13 tests + Skins tab banner. |
| Era theming (docs, newsroom, SFX) | VERIFIED_COMPLETE | Era dictionaries + `SetEraSkin` + `EraThemingRenderTests`, `SettingsAudioRenderTests`. |
| Screens (wizard, hub, dossier, standings, news, history, review, settings, start) | VERIFIED_COMPLETE | 246 render tests; SMGP-300 GUI round 6 at `dfc6b9b`. |
| **17-season consolidated content validator** | **VERIFIED_COMPLETE** | `tests/Companion.Tests/Smgp/SmgpWorldCompletenessTests.cs` — 4 facts over the tracked sources: pack structure (24 teams/34 drivers/16 rounds, resolvable refs, Senna pinned), 17 lore entries, 24 logos+banners, 34 portraits+grid-cars, all decodable. |
| **Full-career golden-path automation** | **VERIFIED_COMPLETE** | `BalanceSimulationHarness` (200-career sweep, report exists) + the ReleaseEvidence 17-season real-pack run with byte-identical resim, green 2026-07-18. |
| **Publish + fresh-install validation gate** | **VERIFIED_COMPLETE** | `docs/SMGP_TEST_EVIDENCE.md`: publish clean, content verified (rules/era-art/grid-cars/pack/WAVs), boot from foreign cwd, dist deploy 14:10 boot-verified. |
| **User guide** | **VERIFIED_COMPLETE** | `docs/SMGP_USER_GUIDE.md`. |
| **Balance report** | **VERIFIED_COMPLETE** | `docs/SMGP_BALANCE_REPORT.md` + `docs/LEVEL_300_BALANCE_REPORT.md` (200-career sweep, cap rates, attrition) + the release-evidence run green 2026-07-18. |
| **Test evidence + handoff docs** | **VERIFIED_COMPLETE** | `docs/SMGP_TEST_EVIDENCE.md` + `docs/SMGP_K3_HANDOFF.md`. |

## Decided positions (recorded, not gaps — do not re-litigate without Mike)

1. **No tycoon economy inside SMGP.** The mission's Tycoon sections map to the Dynasty mode
   (`grandPrixDynasty`), which owns the economy (`docs/dev/dynasty-tycoon-economy.md`, slices
   0-9 shipped, GUI landed). SMGP is the rival/seat-swap career; the economy is a separate mode
   by the locked three-mode contract (career-modes-alpha1.md). SMGP's "Team Ledger" needs do
   not exist; the Dynasty Team Ledger is shipped for Dynasty careers.
2. **Racing Passport is pure racing** (2026-07-18 decision) — isolation tested in
   `RacingPassportTests` (no economy/XP/SMGP state, no rollover).
3. **Replacement/substitute drivers do not exist** in SMGP; injured rounds auto-simulate with
   the player DNS (decided model; calendar says so honestly).
4. **Career-risk = `MortalityMode {Off, Normal, Hardcore}`**; Off draws no accident stream
   (byte-identical replay of pre-feature careers).
5. **Per-race livery rotation is permanently superseded** (AMS2 loads liveries once at launch).
6. **A. Senna is always the OP benchmark** — never nerfed or dropped.

## Gap-driven loop order (from the matrix)

1. Consolidated 17-season content validator (machine-readable, fails on missing data/assets).
2. `docs/SMGP_BALANCE_REPORT.md` from the evidence harness (fixed seeds, measured outcomes).
3. Publish + fresh-install validation, recorded in `docs/SMGP_TEST_EVIDENCE.md`.
4. `docs/SMGP_USER_GUIDE.md`, `docs/SMGP_K3_HANDOFF.md`.
5. Release RC cut after Mike's rail sign-off; final completion report.
