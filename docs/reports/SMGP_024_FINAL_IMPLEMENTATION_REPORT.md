# SMGP-024 Final Implementation Report

Mission: SMGP-024, complete 24-team canon lock + driver-swap alignment.
Branch: hub/increment-4. Date: 2026-07-18. Status: COMPLETE.

## Executive summary

SMGP now has one authoritative canonical identity registry (24 teams, 24
permanent cars, 17 permanent engine specifications, 17 seasons, 408 derived
team-season identity combinations), an executable lock over all 408
combinations, and a complete lore layer written against it: 24 car dossiers,
17 engine dossiers, and 408 team-season history capsules. The winter seat
reshuffle no longer contradicts any driver-facing surface: paddock, skins,
dossier, milestone dispatches, world feed, newsroom attribution, and all
34 driver bios / 24 team histories / rival quotes follow the driver's
current team or frame season-1 seats as origin, never as present fact.

## Initial problems found (audit: docs/audits/SMGP_24_TEAM_CANON_AUDIT.md)

1. No car/engine identity existed anywhere; the only machine registry
   (car-specs.json) carried the real-world "MP4/5B / Honda V10" row for
   Iris and Azalea and four generic "Type G3-Mx" rows for the other teams,
   shown live on dossier MACHINE blocks (C1 canon violation).
2. The winter standings reshuffle (shipped design) moved drivers, but the
   paddock keyed car art by driver id, and 34/34 bios asserted season-1
   seats, colors, tiers, teammates, and car numbers as present-tense fact
   (Mike's screenshot: Delvaux at Azalea with Cool lore).
3. Milestone dispatches filled {team} with the CURRENT team even for
   past-season beats; world-feed and newsroom winner teams for past
   seasons resolved from the pinned season-1 pack.
4. One user-facing real-world term ("out of F1 SMGP") in the dispatch corpus.
5. No alias enforcement for LOTUS->IRIS / AZELIA->AZALEA existed (and, the
   scan confirmed, zero occurrences of those spellings exist in SMGP scope;
   every Lotus hit in the repo is real-F1 content, untouched by policy).

## Canonical data implemented

- `data/rules/smgp/canon.json` (smgp-24-v1): 24 teams (id, displayName,
  carId/carDisplayName, engineId/engineDisplayName, maxPowerHp, season
  span, identityLocked, aliases), 17 engines, mode "smgp".
- `src/Companion.Core/Smgp/SmgpCanon.cs`: loader/model, Validate()
  (counts, uniqueness, alias collisions, season span, suffix detection,
  car-id convention), NormalizeTeamName, SeasonIdentity (the derived 408).
- Consumption: CareerRulesData wiring; CarSpecCatalog.WithSmgpCanon
  (canonical machine/engine always win for canon team ids; bars/hp from
  the vehicle rows; real-F1 careers untouched); the paddock machine dossier.

## Driver-swap alignment (the triggering bug)

| Surface | Fix | Pin |
|---|---|---|
| Paddock car art | Car art via GridCarArtKeyForLivery (pre-reshuffle livery map) | SmgpMultiSeasonDnqTests |
| Skins car art | Same mapping, driver id only a non-SMGP fallback | SeasonTwo_SkinsRows_* |
| Dossier team line / PlayerCarSpec | Live SMGP seat wins over the boundary-updated fold | existing suite |
| Milestone dispatches {team} | Beat-time team (per-round / season-start seat) | MilestoneDispatches_NameTheBeatTimeTeam |
| World feed / newsroom past-season teams | Stored envelope ConstructorId (fold-time truth) | SeasonThree_WorldFeed_* |
| 34 bios + quotes | Time-stable rewrite: personality present, seats as origin | profile suites |
| 24 team histories + rival quotes | Same origin/era framing | profile suites |

## Lore implemented

- 24 car dossiers + 17 engine dossiers (~20k words): distinct naming logic
  per car, character, 3-paragraph histories, quotes; the permanent-name
  tradition explained in-world; VAPOR DN keeps no architecture; shared
  engines carry one manufacturer story (LIZZIE 24 V8, VAPOR DNPQ V8,
  LORRY 32 V8, RAM V12) with team integration in the car dossiers.
- 408 team-season capsules (17 files x 24, ~38k words): the base
  universe's own arc with tracked multi-season threads (Moreau's pace-car
  law to the Charter, Bullets-Dardan war to the handshake, Serga's
  whip-round, Blanche's champagne, Zeroforce's toast), no champion ever
  declared, the player never present.
- Surfaces: paddock team card MACHINE section + THE SEVENTEEN SEASONS arc.
  Reveal rule (owner, 2026-07-19): a season's capsule unlocks only after the
  career completes that season, so the 17-season story is never spoiled at
  career start; the full 408 become reachable as the campaign plays out.

## Files added

- data/rules/smgp/canon.json; car-dossiers.json; engine-dossiers.json;
  capsules/s01..s17.json
- src/Companion.Core/Smgp/SmgpCanon.cs; SmgpCarDossiers.cs;
  SmgpEngineDossiers.cs; SmgpSeasonCapsules.cs
- tests: SmgpCanonLockTests, SmgpCarDossiersTests, SmgpEngineDossiersTests,
  SmgpSeasonCapsulesTests, SmgpMachineDossierTests
- docs: audits/SMGP_24_TEAM_CANON_AUDIT.md, content/SMGP_CANON_ROSTER.md,
  migrations/SMGP_024_ALIAS_MIGRATION_NOTES.md, this report

## Files modified

- src: CarSpecCatalog (canon overlay), CareerRulesData (four catalogs),
  ICareerSession (SmgpMachineDossier, SeasonArc), CareerSessionService
  (machine/arc population, dispatch beat teams, world-round ConstructorId,
  PlayerTeamName/PlayerCarSpec seat-first), CareerSessionService.Newsroom
  (winner ConstructorId), SkinsViewModel (physical-car art),
  PaddockView.xaml (MACHINE + arc sections), both csproj copy rules.
- data/rules/smgp: driver-profiles.json, team-profiles.json,
  rival-quotes.json (time-stable rewrite), dispatches.json (F1 wording).
- docs/PROJECT.md, tools/author_smgp.cs (22 SEGA-base + 2 = 24 wording).

## Migrations

None required. Zero alias occurrences exist in SMGP scope; persisted state
stores ids, not display names; the pinned pack blob is sha256-verified and
must never be rewritten. Policy and rationale:
docs/migrations/SMGP_024_ALIAS_MIGRATION_NOTES.md.

## Import/export

Contract documented in the same notes: stable ids win, snapshots validate,
`SmgpCanon.NormalizeTeamName` is the single normalization primitive, and
spelling differences never create teams.

## Test coverage

- 408 identity combinations + structural canon lock (SmgpCanonLockTests, 414).
- Registry structural validation (SmgpCanon.Validate, empty on shipped data).
- Car-spec overlay kills the Honda leak, real-F1 untouched (CarSpecTests).
- Dossier loaders + canon cross-checks (13 tests).
- Capsule completeness (408, all fields), distinctness (no duplicate bodies),
  no champion declarations, VAPOR DN architecture guard (5 tests).
- Alignment: promotion-scenario dispatch teams, season-3 world feed vs
  folded ConstructorIds, skins physical car (3 tests).
- Real-session dossier wiring: all 24 team cards + arcs (1 test).
- Render: MACHINE section + arc render (PaddockRenderTests).

## Verification

- Build: `dotnet build src/Companion.App --nologo -v q`, 0 errors.
- Logic suite: `dotnet test tests/Companion.Tests --nologo -v q`,
  3329 passed / 0 failed.
- Render suite: `dotnet test tests/Companion.RenderHarness.Tests --nologo -v q`,
  246 passed / 0 failed.
- Known intermittent pre-existing flake: parallel test classes collide on
  pooled SQLite connections (ObjectDisposedException inside
  CareerDatabase.Open). Unrelated to this mission (different test fails each
  run, stack never touches mission code); suites pass on re-run. Tracked as
  test-infrastructure debt, not a release blocker.

## Remaining known limitations (precise)

1. The History tab has no dedicated per-season capsule view; the capsules
   reveal through the 24 paddock team cards' SEVENTEEN SEASONS arc as the
   career completes each season (owner rule: no future spoilers). A
   History-tab surface would be additive UI, not missing content.
   Not release-blocking.
2. Season lore long-form prose (seasons.json) keeps canon driver/team
   pairings literal BY DESIGN (season-1 opening canon per the narrative
   bible). It is framed as opening canon wherever surfaced; no further
   rewrite is planned. Not a defect; documented in the audit §4.3.
3. packs/smgp-1/pack.json keeps its "22 teams" install note (hash-pinned
   blob; see migration notes). The 22+2=24 clarification lives in
   docs/PROJECT.md and docs/content/SMGP_CANON_ROSTER.md.
4. The dossier quotes in news templates were NOT rewritten to carry car
   names; news resolves team identity from live state, which is correct by
   construction. Car/engine names appear where the dossier surfaces them.

## Intentionally deferred work

- Car/engine dossier pages as standalone encyclopedia entries (the paddock
  MACHINE section carries the full text today).
- A History-tab season capsule browser (limitation 1).
- Import-side alias normalization code (no importer accepts display-name
  team keys today; the contract is documented).

All three are additive presentations, not canon or alignment gaps.
