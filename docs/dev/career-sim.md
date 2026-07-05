# Career sim v1 (M5 contract — draft)

Synthesized from PLAN.md + the research design digest (RESEARCH.md §6). Everything here is
deterministic, journaled, and replayable: given the same master seed and the same imported
race results, re-simulating from round 1 reproduces a byte-identical journal.

## Determinism

- **RNG:** PCG32, one instance per named stream. Stream seed =
  `SplitMix64(Fnv1a64(subsystem + "|" + year + "|" + round + "|" + entityId) XOR masterSeed)`.
  FNV-1a over UTF-8 bytes — never `string.GetHashCode` (not stable across runs/versions).
- **Streams** (subsystem names): `offers`, `aging`, `retirement`, `form`, `events`,
  `headlines`, `tier-drift`. One stream per (subsystem, year, round, entity) — consuming
  numbers for driver A never shifts driver B's sequence.
- **Journal:** append-only rows `{seq, utc, seasonId, round, phase, entity, deltaJson, cause}`
  (already in Data schema v1). The sim NEVER reads its own prior output except through
  explicit state snapshots; every state change is a journal row; the news feed and "why?"
  inspector render journal rows directly.
- **Replay contract:** sim state = fold(journal). Re-simulate = wipe derived state, replay
  raw results through the same code + seed. Tested byte-identical in CI.

## Player model

- **Pace anchor:** after each round, compare the player's finish against the expected finish
  of their car (see OPI below) for the known generated AI ratings. A rolling calibration
  (EWMA, α=0.3) estimates "player skill as a rating" → recommends the in-game Opponent Skill
  slider for the next round (research: race_skill compresses around the slider; at 90%:
  1.0→~95%, 0.0→~85%). Recommendation shown in the briefing, never auto-applied.
- **Reputation** 0–100, moves on: finishing vs expectation, beating the teammate,
  season-end championship position. Era-scaled gains (a podium in a tier-5 car >> tier-1).
- **OPI (overperformance index):** `OPI ← 0.8·OPI + 0.2·(expectedFinish − actualFinish)`,
  DNF-cause aware: mechanical DNFs score as `expectedFinish` (no blame), driver-error DNFs
  as `gridSize` (full blame). `expectedFinish` = rank of the seat's car+driver strength
  among the round's grid (car rating from team tier + scalars, driver rating from pack).

## Teams & economy (v1 scope — tier drift only, full ledger is Phase 2)

- **Budget tiers 1–5** drive: generated AI strength spread (era-authored scalar bands, e.g.
  tier 5 → power_scalar 1.02, tier 1 → 0.96 — data per pack), the player's own car scalars,
  salary bands for offers, expectations for OPI.
- **Tier drift** at season end: constructors position vs tier expectation moves tier ±1
  (stream `tier-drift`, small probability weighting, never jumps 2). Journaled with cause.

## Season-end pipeline (strict order, one journal row each)

1. Final standings computed (engine) → championship journal rows.
2. Reputation/OPI final updates.
3. Aging: each AI driver's ratings drift along era-shifted age curves (peak ~28–32 in the
   60s, later in modern eras; stream `aging`). Small deterministic noise.
4. Retirements: canon retirements fire on schedule (pack/lineage data); non-canon drivers
   retire on age+performance hazard (stream `retirement`). Seeded foreshadowing: a driver
   retiring next season gets a "considering their future" headline this season.
5. Seat market: teams rank available drivers (rating, rep, age, pay-driver budget weight —
   era-correct: pay seats are first-class); vacancies fill deterministically (stream `offers`).
6. Player offers: every team scores the player
   `w1·rep + w2·OPI + w3·experience − w4·salaryAsk − w5·ageRisk` with team-archetype weights
   (works/privateer/minnow authored per pack); top N (tier-gated) become offer letters.
7. Tier drift (above), then era transition check (M6: lineage carryover to the next pack).

## News/headlines

Template bank keyed by journal phase+cause (data file, era-flavored variants); selection via
stream `headlines`. v1: one headline per race + season-end digest. Minimal-narrative toggle
suppresses all but championship-critical items.

## Persistence (Data schema v2 — next migration)

`driver_state` (ratings drift, age, rep), `team_state` (tier, lineage), `player_state`
(rep, OPI, pace anchor), `offer` (season, team, terms, accepted), all keyed to season. Raw
pack data stays pinned; state rows reference pack ids + lineage ids (`team.lotus`,
`driver.j_clark`).

## Out of scope for v1 (Phase 2/3)

Full team ledger/sponsors/bankruptcy ladder, mid-season driver market, rumor system,
living-world divergence dial, Owner-Driver mode.
