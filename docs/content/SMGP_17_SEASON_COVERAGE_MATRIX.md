# SMGP 17-season coverage matrix

_Mode-separation finalization mission. Per-season narrative coverage: what exists today (verified)
vs the mission floor (expansion targets). Season identities from `data/rules/smgp/seasons.json`
(17 authored entries, validated by `SmgpSeasonLoreTests`); the engine coverage from the newsroom
corpora; the arc coverage from StoryThreads + SMGP beats. "Floor" = the mission's minimum per
season (dossier, opening feature, retrospective, 8-12 archive entries, 12-20 reactive templates,
4+ arcs, 6+ features)._

## Reading the matrix

- **Identity** = the season's authored title/era/context/arcs (all 17: COMPLETE).
- **Reactive news** = season-aware templates that fire on real results (era-voiced newsroom +
  SMGP packs; they inherit the season's era by construction).
- **Arcs** = continuing multi-stage stories the engines can run in the season.
- **Opening/Retrospective** = season-opening feature + closing retrospective framing (lore
  headers + 036-retrospectives pack).

## Act I — The Iron Circus

| # | Title | Identity | Reactive news | Arcs | Opening/Retrospective |
|---|---|---|---|---|---|
| 1 | The Tenth Summer | COMPLETE | era-voiced core + smgp packs (035/038/smgp.json) | rivalry, rookie, underdog, championship | lore header + retrospective pack |
| 2 | The Protest Year | COMPLETE | same engine, era variants | + contract, team crisis | same |
| 3 | The Wet Season | COMPLETE | same, wet-weather variants weighted | + injury/safety | same |
| 4 | The Closing Door Opens | COMPLETE | same | + contract, comeback | same |

## Act II — The Horsepower War

| # | Title | Identity | Reactive news | Arcs | Opening/Retrospective |
|---|---|---|---|---|---|
| 5 | The Horsepower Spring | COMPLETE | same engine, era variants | + reliability/technical | same |
| 6 | The Temple Wars | COMPLETE | same | + teammate, championship | same |
| 7 | The Spending War | COMPLETE | same | + contract, team crisis | same |
| 8 | The Boiling Point | COMPLETE | same | + injury/safety, teammate | same |
| 9 | The Reckoning | COMPLETE | same | + championship, team crisis | same |

## Act III — The Safety Reckoning

| # | Title | Identity | Reactive news | Arcs | Opening/Retrospective |
|---|---|---|---|---|---|
| 10 | The Charter Season | COMPLETE | same engine, era variants | + injury/safety, rookie | same |
| 11 | The Craftsman's Year | COMPLETE | same | + technical, comeback | same |
| 12 | The Frost Blooms | COMPLETE | same | + underdog, comeback | same |
| 13 | The Veterans' Autumn | COMPLETE | same | + veteran, retirement | same |

## Act IV — The Golden Circus

| # | Title | Identity | Reactive news | Arcs | Opening/Retrospective |
|---|---|---|---|---|---|
| 14 | The Jewel Formula | COMPLETE | same engine, era variants | + championship, rivalry | same |
| 15 | The Insurgent's Last Climb | COMPLETE | same | + underdog, championship | same |
| 16 | The Silver Jubilee | COMPLETE | same | + record/milestone, veteran | same |
| 17 | The Crown of Crowns | COMPLETE | same + finale framing | + championship, retirement, finale | same + finale |

## Expansion targets (the mission floor, tracked in the audit)

The floor is a per-season AUTHORING program, not an engine gap. The engines (newsroom, beats,
threads, lore, retrospectives) already run in all 17 seasons with era-appropriate voices. The
program below deepens per-season authored material in batches:

1. **Batch A (Act I, seasons 1-4):** season dossiers (from `seasons.json` context/arcs) +
   season-specific reactive template variants + 4 arc seeds per season.
2. **Batch B (Act II, seasons 5-9):** same, with the reliability/spending-war weighting.
3. **Batch C (Act III, seasons 10-13):** same, with the safety-reckoning register (restraint).
4. **Batch D (Act IV, seasons 14-17):** same, with the golden-circus spectacle + finale framing.

Each batch ships with its corpus guard + validator coverage (the consolidated validator's lore
section extends to assert the new per-season packs), keeping the byte-identical replay contract
(display-only, never a fold input).
