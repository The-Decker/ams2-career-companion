# Level 300 balance report

_2026-07-16 · produced by the `BalanceSimulationHarness` sweep (tests/Companion.Tests/Scenarios/
BalanceSimulationHarness.cs) — full synthetic 17-season careers over the REAL `packs/smgp-1` pack
(16 rounds × 34 cars) through the real creation → fold → season-rollover machinery. Every number
below was measured, not estimated._

## Method

- **200 careers**: 40 per result-profile archetype, each a complete (up to) 17-season SMGP
  campaign — ~54,000 real fold rounds total.
- **Archetypes** are RESULT profiles (where the synthetic finishes centre), not character builds —
  the character (progression v2, `dna_circuit_specialist`, durability 0.55, Normal mortality) is
  identical across all careers, so the experimental variable is career performance alone:
  | archetype | mean finish | σ | offer policy |
  |---|---:|---:|---|
  | exceptional | 1.8 | 1.4 | climbs (accepts offers) |
  | strong | 3.6 | 2.2 | climbs |
  | competent | 7.5 | 3.5 | climbs |
  | midfield | 11.5 | 4.0 | holds seat |
  | backmarker | 17.0 | 5.0 | holds seat |
- Per-round player DNFs: 3.5% mechanical, 3% accident (severity Light 50% / Medium 35% / Heavy
  15%) — the accident DNFs drive the REAL injury/fatality fold (deterministic d500 per the
  accident model), not a scripted outcome.
- Injured rounds route through the real sit-out path (`AutoSimulateRound`); season reviews sign
  the real offers; every career is reproducible from its master seed.
- Result synthesis is test scaffolding standing in for the human importing AMS2 results — the
  product sim never invents results (same stance as `FullSeasonE2ETests`).

## Results — level distributions per season checkpoint

Median level (p10–p90) after season N:

| after | backmarker | midfield | competent | strong | exceptional |
|---|---|---|---|---|---|
| S1 | 15 (12–19) | 22 (19–25) | 28 (25–31) | 37 (29–42) | 42 (36–44) |
| S3 | 35 (29–40) | 51 (44–60) | 71 (58–79) | 87 (77–97) | 103 (93–114) |
| S5 | 54 (44–59) | 78 (67–90) | 103 (88–118) | 135 (116–153) | 158 (145–178) |
| S8 | 81 (66–88) | 116 (103–135) | 145 (133–165) | 192 (174–215) | 239 (218–258) |
| S11 | 107 (92–116) | 151 (140–166) | 192 (174–214) | 245 (227–277) | **300** (285–300) |
| S14 | 132 (116–143) | 186 (165–202) | 229 (212–255) | **300** (280–300) | 300 |
| S17 | 157 (139–167) | 217 (202–235) | 270 (249–295) | 300 | 300 |

## Results — cap reach, development, attrition

| archetype | final median | ≥L100 | ≥L200 | ≥L250 | **L300** | SP earned (of 499) | championships avg | injuries avg | races missed avg | deaths | floor knock-outs |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| exceptional | L300 | 100% | 100% | 100% | **100%** | 499 | 16.80 | 1.30 | 1.63 | 2.5% | 0% |
| strong | L300 | 100% | 100% | 100% | **100%** | 499 | 8.48 | 1.20 | 1.30 | 2.5% | 0% |
| competent | L270 | 100% | 100% | 87.5% | **5%** | 448 | 0.03 | 1.23 | 1.33 | 0% | 0% |
| midfield | L217 | 100% | 92.5% | 5% | **0%** | 360 | 0.00 | 1.30 | 1.20 | 0% | 0% |
| backmarker | L157 | 97.5% | 0% | 0% | **0%** | 260 | 0.00 | 1.20 | 1.38 | 2.5% | 0% |

## Reading the numbers

1. **Level 300 is prestigious and earned, not automatic.** Only careers that contend for titles
   reach it: strong (≈8.5 championships) and exceptional (≈16.8) hit the cap 100% of the time;
   a solid points-scoring career (competent, ~P7.5 average) reaches it in only 5% of runs and
   typically finishes L249–L295 — exactly the "reachable within an exceptional career, not owed
   to everyone" contract (design: a great career can reach 300 by end of season 16).
2. **Early cap ≠ early mastery.** Exceptional careers median-cap by S11 — but the 499-SP pool is
   **season-gated** (`SeasonPool`: 499 only after season 16), so an early L300 is prestige and
   news, never an early complete build. The dual gate is doing precisely its designed job.
3. **Progression stays meaningful late.** Every archetype is still climbing at S14–S17
   (backmarker +25 levels over the last three seasons; competent +41). No XP cliff, no wall.
4. **Missed races don't break the cap.** Careers averaged ~1.2 injuries and ~1.4 missed races;
   strong/exceptional still reached L300 at 100%. Injury absence costs points and stories, not
   the mathematical possibility of the cap.
5. **Death is rare and never routine.** Under Normal mortality with a 3%-per-round accident-DNF
   profile, 1.5% of the 200 careers ended fatally (3 careers). Light accidents can never kill
   (no death band); the fatality path requires a marked heavier accident plus a deep d500 roll.
6. **No stat/archetype dominates by exploit.** The XP model rewards relative overperformance —
   backmarkers earn meaningful XP by beating weak expectations, but the win/podium bonuses keep
   absolute achievers ahead. The spread (S17: L157 → L300) is wide enough that careers feel
   different and narrow enough that nobody stalls.

## Curve verification (exact integers, unit-tested)

- Cumulative L300 threshold: **14,951 XP** (`CharacterLevelProgressionTests`,
  `VersionTwo_HasTheLockedIntegerL300Curve`).
- Boundary flips proven at 99→100, 149→150, 199→200, 249→250, 298/299/300 and `long.MaxValue`
  (no level 301 at any input; `CharacterLevelProgressionMidBoundaryTests`,
  `VersionTwo_LevelLookupHitsTheExactCapBoundaries`).
- XP at the cap banks as lifetime XP (reset economy) — never rollover
  (`CharacterDossierCapAndModifiersTests`).

## Release evidence (ledger blocker 4)

`ReleaseEvidence_RealPackCampaign_ReopensAndResimulatesByteIdentical`
(`COMPANION_BALANCE_EVIDENCE=1`) runs one exceptional-profile career through all 17 real-pack
seasons with per-season wall-clock, then proves the full 272-round campaign re-simulates
**byte-identically** and times a full-scale archive read (newsroom feed + threads + season cards)
on the memoized event spine. Results are appended below when run.

Measured 2026-07-16 (exceptional profile, seed 424242, Normal mortality):

- **All 17 seasons executed** on the real pack; per-season wall-clock 0.36–0.86 s (10.4 s total
  for the whole 272-round campaign, headless).
- Level track: L40 after S1 → L300 reached during S10 → capped thereafter (no level 301 at any
  point; the SP gate kept mastery paced to S16 regardless).
- **Re-simulation: byte-identical over 18,012 journal rows** in 0.31 s — the whole campaign
  replays exactly, injuries and XP included.
- Full-scale archive read after reopen: **0.23 s** for 1,153 rendered stories, 56 story threads,
  and 17 season cards — the memoized event spine holding at the campaign's full size (this read
  was ~5 uncached full-career recomputes per refresh before the SMGP-300 wave).
- The reopen also proved the campaign timeline reports all 17 seasons Completed.

## Reproducing

```
COMPANION_BALANCE_SIM=40 COMPANION_BALANCE_SIM_DOP=8 \
  dotnet test tests/Companion.Tests/Companion.Tests.csproj \
  --filter "FullyQualifiedName~BalanceSweep_RunsWhenConfigured"
COMPANION_BALANCE_EVIDENCE=1 \
  dotnet test tests/Companion.Tests/Companion.Tests.csproj \
  --filter "FullyQualifiedName~ReleaseEvidence"
```

Raw per-career samples (JSONL) land beside the test output or at `COMPANION_BALANCE_SIM_OUT`.

## Balance verdict and knobs

The measured pacing is **accepted for Alpha 1.0**: it satisfies the design contract (L300 by S16
for great careers; competent careers just short; the SP season-gate carrying the real endgame
pacing) and the SMGP-300 acceptance criteria (cap reachable, not universal, not fragile to missed
races, late-career progression meaningful). If later alpha feedback wants slower level pacing for
dominant careers, the correct knobs are the per-round award values in `data/rules/perks.json`
(round-award pacing is data-driven) — the L300 curve, 499-SP pool, and campaign reference are
code-pinned replay constants and must not move for existing careers.
