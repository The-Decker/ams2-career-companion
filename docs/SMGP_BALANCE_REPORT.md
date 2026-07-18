# SMGP balance report (mission SMGP-COMPLETE-001)

_2026-07-18 · Claude (Head of Coding). The measured evidence for the SMGP module's 17-season
balance, all produced by the deterministic harnesses over the REAL `packs/smgp-1` pack. Nothing
here is estimated; every number traces to a runnable suite._

## Method and where the numbers live

- **Career sweep:** `tests/Companion.Tests/Scenarios/BalanceSimulationHarness.cs` — 200 complete
  synthetic careers (40 per result archetype: exceptional / strong / competent / midfield /
  backmarker), each a full 17-season SMGP campaign (~54,000 real fold rounds) through the real
  creation → fold → rollover machinery, Normal mortality, real accident/injury fold, real sit-out
  path, real offer signing. Full method + tables: **`docs/LEVEL_300_BALANCE_REPORT.md`**.
- **Release evidence:** `ReleaseEvidence_RealPackCampaign_ReopensAndResimulatesByteIdentical`
  (env `COMPANION_BALANCE_EVIDENCE=1`) — one full exceptional-profile 17-season career (272
  rounds × 34 cars) with per-season wall-clock and a **byte-identical Resimulate** proof.
  Re-run today (2026-07-18): **green**.
- **Curve integers:** `CharacterLevelProgression*Tests` pin the exact L300 curve (14,951 XP
  cumulative threshold; boundary flips at 99/149/199/249/298/299/300; no Level 301 at any input).

## Progression: measured outcomes

| Archetype (mean finish) | Final median level | L300 rate | SP earned (of 499) |
|---|---:|---:|---:|
| exceptional (1.8) | L300 | 100% | 499 |
| strong (3.6) | L300 | 100% | 499 |
| competent (7.5) | L270 | 5% | 448 |
| midfield (11.5) | L217 | 0% | 360 |
| backmarker (17.0) | L157 | 0% | 260 |

- **The cap is earned, never owed.** Only title-contending careers (≈8.5+ championships) cap at
  100%; a solid points career reaches L249–L295. That is the designed contract.
- **Early cap ≠ early mastery.** The 499-SP pool is season-gated (full release only at the
  season-16 review), so an early L300 is prestige, not an early complete build.
- **No cliff, no wall.** Every archetype still climbs at S14–S17.
- **Injuries cost stories, not math.** ~1.2 injuries / ~1.4 missed races per career on average;
  top archetypes still cap at 100%.
- **Death is rare.** 1.5% of 200 careers ended fatally under a 3%-per-round accident-DNF
  profile; Light accidents can never kill; no floor knock-outs in the sweep.

## Field, grid, and calendar integrity

- **Seeded per-round DNQ field**, re-rolled per season from season 2, is deterministic per career
  and replay-verified (`SmgpDnqField`, `SmgpMultiSeasonDnqTests`).
- **The campaign terminates at 17.** No Season 18 is constructible (`CreateSmgp` pins 17
  ordinal seasons; the guard tests the boundary).
- **Grid composition is stable:** 24 teams / 34 drivers / 16 rounds, validated machine-readably
  by `SmgpWorldCompletenessTests` (2026-07-18, 4/4 green) — pack structure, lore, logos, banners,
  portraits, grid cars.
- **Senna is the permanent benchmark** and stays OP by design (locked direction, validated in
  the roster).

## Economy stance

SMGP has **no owner economy by contract** (three-mode separation; the economy is the Dynasty
mode, shipped separately). SMGP balance is therefore judged on progression, injury, DNQ, and
calendar integrity only — all green above. There is no money loop to spiral, by design, so no
bankruptcy tuning exists here to audit.

## Verdict

The 17-season module is **balanced for release**: progression is prestigious-but-reachable,
attrition is rare and honest, the field is deterministic, the arc terminates, and the whole
campaign replays byte-identically. Reproduce any number here with
`COMPANION_BALANCE_EVIDENCE=1 dotnet test --filter FullyQualifiedName~ReleaseEvidence` (release
gate) or the full archetype sweep in `BalanceSimulationHarness.cs`.
