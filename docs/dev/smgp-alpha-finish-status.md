# SMGP alpha finish status

_Coordinator ledger · refreshed 2026-07-16 (SMGP-300 wave) by Claude (Head of Coding) · source of
truth is the current working tree plus executable evidence, not the dated roadmap text._

## Baseline

- Branch: `hub/increment-4`.
- The July 13–15 feature wave is committed and pushed; the SMGP-300 finalization wave
  (progression feedback spine, availability hardening, canon divergence, season lore, balance
  harness, docs set) lands on top of `328a33a` as the `smgp300` commit series.
- Verification on the SMGP-300 wave (2026-07-16): `dotnet build Companion.slnx` — green; full
  logic suite green at every wave commit (counts recorded per commit message; 2,633 → 2,700+
  across the wave as tests were added).

## Roadmap truth

| Roadmap prompt | Status | Current evidence |
|---|---|---|
| **#1 — CareerOver hard-stop and ending reconciliation** | **SHIPPED** | `SmgpBattleFoldDeterminismTests`, `AccidentFoldDeterminismTests`, `DeathScreenHandoffTests`, `MortalityScreensRenderTests`; the SMGP-300 wave added `PostDeathArchiveTests` (reopen → death screen, archive readable, new careers creatable) and gallery memorial badges. |
| **#2 — per-race livery parking/rotation** | **PERMANENTLY SUPERSEDED — NEVER REVIVE** | Unchanged. |
| **#3 — living flavour, reshuffle, and campaign celebration** | **SHIPPED** | Unchanged, plus the authored 17-season lore layer (`data/rules/smgp/seasons.json` + `SmgpSeasonLore`, validated by `SmgpSeasonLoreTests`) and SMGP canon divergence (`SmgpCanonDivergence`, SmgpFiction provenance). |

## SMGP-300 finalization wave (2026-07-16)

The Level-300 / 17-season / injury / game-over spine was audited end to end (10-dimension
adversarial audit) and the real gaps closed:

- **Progression feedback**: per-apply XP/level/SP announcement (`RoundProgression`), MAX-level
  state (`IsAtLevelCap`), level-milestone + Level-300 + career-completed + return-from-injury
  news/history events, persisted level-up acknowledgment.
- **Availability hardening**: Apply/Preview now hard-gate on an active injury (service layer, not
  just UI routing — `InjuryAvailabilityGateTests`); calendar carries Raced/SatOut/WillMiss; the
  finale leads on reopen of a beaten summit career.
- **Career-arc surfaces**: `CampaignTimeline()` (17 slots, locked/current/completed, lore titles),
  `InjuryHistory()` medical record with deterministic non-graphic flavour, mortality-mode label,
  season cards carry player level start/end.
- **Data-lane hardening**: pre-upgrade `VACUUM INTO` backup before the migration chain, degraded
  save-slot listing, result-provenance rows inside the fold transaction, `NewsroomEvents()`
  memoized behind a stored-state fingerprint (was ~5 full-career recomputes per applied round).
- **Balance harness**: `BalanceSimulationHarness` (env-gated) runs full synthetic 17-season
  careers over the REAL `packs/smgp-1` (16×34) across five result archetypes; the evidence run
  covers ledger blocker 4 (see below). Report: `docs/LEVEL_300_BALANCE_REPORT.md`.
- **Docs**: the eight SMGP-300 documents under `docs/` (`LEVEL_300_*`, `SMGP_17_SEASON_*`,
  `ACCIDENT_INJURY_MEDICAL_SYSTEM`, `CAREER_GAME_OVER_FLOW`, `SAVE_MIGRATION_LEVEL_300`).
- **GUI handoff**: `docs/dev/codex-gui-smgp300-brief.md` — 12 bind contracts (progression toast,
  MAX badge, active effects, campaign timeline, calendar chips, header chips, medical record,
  lore header, memorial badges, legacy-edit clear, degraded slots).

## Accepted deviations (decided positions — do not re-litigate without Mike)

1. **Replacement/substitute drivers do NOT exist.** Injured rounds auto-simulate with the player
   DNS (docs/dev/character-death-injury.md's decided model). The constructor fields one car short
   during an absence; the calendar/history say so honestly. A scoring substitute would change
   standings for existing careers unless envelope-versioned + new-career gated — if ever
   approved, it ships that way, never as a retrofit.
2. **Career-risk settings are `MortalityMode {Off, Normal, Hardcore}`.** This IS the equivalent
   setting the SMGP-300 spec allows: Off = no injuries/fatalities (the accident stream is never
   drawn — load-bearing for byte-identical replay of pre-feature careers), Normal/Hardcore differ
   in save policy, not fatality rate. There is no Reduced/Authentic rate tier and no Off-mode
   fatal→nonfatal conversion. The mode is immutable per career and now visible mid-career.
3. **The injury ladder is `none / minorInjury(1–2 races) / seasonEnding / death`.** No
   CareerEnding-alive band, no per-injury stat penalties (availability is the effect), recovery
   measured in rounds not calendar dates. Injury TYPES exist as deterministic display flavour
   only (`InjuryFlavor`).
4. **Fatigue/confidence are not separate systems.** Form (OPI adjustment), durability, and the
   aging curves carry those responsibilities; documented in `docs/LEVEL_300_SYSTEM_SPEC.md`.
5. **Legacy v0/v1 careers keep delevel-on-negative-XP** (replay contract). All three alpha modes
   create v2 profiles, which floor awards at zero.
6. **`career.app_version` records the creating version only** — no per-upgrade stamp (the
   pre-upgrade file backup covers the support case).
7. **Racing Passport stays fail-closed at creation** until its cross-thread credited-experience
   ledger ships (career-modes-alpha1.md).

## Open blockers / evidence still required

1. **~~Functional blocker~~ — RESOLVED (2026-07-15).**
2. **~~GUI blocker~~ — RESOLVED (2026-07-15).**
3. **Professional-pass blocker:** the main menu still needs the Pit Wall Command Rail acceptance
   pass and independent renders at all four required size/scale combinations. **GUI lane** — now
   joined by the SMGP-300 bind contracts (`codex-gui-smgp300-brief.md`).
4. **Release-evidence blocker:** ~~a fresh-career SMGP acceptance sweep and byte-identical
   reopen/resimulation proof~~ — the `BalanceSimulationHarness` evidence run executes a full
   real-pack 17-season career (272 rounds × 34 cars) with per-season wall-clock, a full-scale
   archive read timing, and a byte-identical `Resimulate` proof. Recorded in
   `docs/LEVEL_300_BALANCE_REPORT.md` once the current sweep completes; rerunnable any time via
   `COMPANION_BALANCE_EVIDENCE=1 dotnet test --filter FullyQualifiedName~ReleaseEvidence`.

## Exactly one next slice

**Codex GUI round 6: bind the SMGP-300 surfaces (`docs/dev/codex-gui-smgp300-brief.md`), then the
Pit Wall Command Rail acceptance pass (blocker 3).** When both are green, refresh this ledger and
cut the alpha RC.
