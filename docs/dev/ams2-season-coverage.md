# AMS2 open-wheel classes ↔ F1 seasons — coverage & buildable map

_Research pass 2026-07-06 (workflow `wf_567629aa`), verified against Mike's actual install._

AMS2's open-wheel classes are a near-chronological ladder through F1 history: each "generation"
of a class corresponds to a specific technical era, and Mike's install has community NAMeS/skin
rosters pinning **one real F1 season per class**. **The class's default year label and the
installed roster's real year often disagree** — so a pack must trust the installed NAMeS roster,
not the class label, and bind each entry to the exact `livery_name` string in the XML (which,
unlike the `f1-1988` reference, usually omits the year + driver and often uses licensing-safe
pseudonyms like "Scuderia Forza" for Ferrari).

## Coverage table

| AMS2 class | F1 era | Real year | Status | NAMeS file · livery pattern |
|---|---|---|---|---|
| F-Junior | early-60s feeder | — | ❌ not F1 / no roster | — |
| F-Vintage_Gen1 | 1966–67 | 1967 | ✅ built | `f1-1967` |
| F-Vintage_Gen2 | 1964–70 | 1969 | ✅ built (roster is 1969) | `f1-1969` · `Lotus-Ford Cosworth #1 G. Hill` |
| F-Retro_Gen1 | ~1972–75 | 1974 | ✅ built | `f1-1974` |
| F-Retro_Gen2 | ~1975–78 | 1978 | ✅ built | `f1-1978` |
| **F-Retro_Gen3** | turbo peak | **1985** | 🔨 **buildable** | `F-Retro_Gen3.xml` · `Marlboro McLaren International #1` (sponsor-team + #N, real driver in `<name>`), 30 slots |
| F-Classic_Gen1 | 1986 | 1986 | ✅ built | `f1-1986` |
| F-Classic_Gen2 | 1988 | 1988 | ✅ built (reference pattern) | `1988 Lotus #1 - N. Piquet` |
| F-Classic_Gen3 | 1986–90 | 1990 | ✅ built (roster is 1990) | `f1-1990` |
| F-Classic_Gen4 | 1991 | 1991 | ✅ built | `f1-1991` |
| F-Hitech_Gen1 | 1992 | 1992 | ✅ built | `f1-1992` |
| F-Hitech_Gen2 | 1993 | 1993 | ✅ built | `f1-1993` |
| FE-G1 (Formula Edge) | ~1995 | 1995 | ✅ built | `f1-1995` |
| F-V10_Gen1 | 1996–97 | 1997 | ✅ built | `f1-1997` |
| F-V10_Gen2 | 2000–02 | 2000 | ✅ built | `f1-2000` |
| **F-V10_Gen3** | V10 swansong | **2005** | 🔨 **buildable** | `F-V10_Gen3.xml` · `Scuderia Forza #1` (pseudo-teams) + chassis `McLaren MP4/20 #9`, 20 |
| **F-V8_Gen1** | V8 formula | **2006** | 🔨 **buildable** | `F-V8_Gen1.xml` · `Scuderia Forza #5`, `Renault R26 #1`, 22 |
| **F-V8_Gen2** | V8 formula | **2008** | 🔨 **buildable** | `F-V8_Gen2.xml` · pseudo-teams + real chassis `McLaren MP4/23 #22`, 22 |
| F-Ultimate_Gen1 (Hybrid) | 2019–21 | **2016** (installed) | 🔨 buildable as 2016 | `F-Ultimate_Gen1.xml` · pseudo-teams, 20 |
| F-Ultimate_Gen2 (Hybrid) | 2022–24 | **2023** (installed) | 🔨 buildable as 2023 | `F-Ultimate_Gen2.xml` · "Real Drivers" NAMeS, real 20-car grid, pseudo-team tokens |

## Net-new buildable seasons (priority order)

1. **1985** — F-Retro_Gen3 — 30 drivers — real sponsor-team liveries, cleanest bind. **Strongest.**
2. **2008** — F-V8_Gen2 — 22 — mixed pseudo-team + real chassis names.
3. **2006** — F-V8_Gen1 — 22 — pseudo-team names.
4. **2005** — F-V10_Gen3 — 20 — pseudo-team names.
5. **2023** — F-Ultimate_Gen2 — 20 — real drivers, pseudo-team tokens, but **AMS2 lacks 2023's
   newest circuits (Jeddah/Miami/Vegas/Qatar)** → heavy placeholder substitution.

## Gaps — not buildable now, and why

- **F-Junior** — Formula Junior isn't an F1 championship; no f1db grid to bind.
- **A strict 2019 or 2022 pack** — the installed Ultimate NAMeS files hold 2016 / 2023 grids, not
  2019 / 2022. Would need different NAMeS files.
- **A strict 1983 pack** — F-Retro_Gen3's installed roster is 1985, not the class's 1983 label.
- **Anything past 2023** — Mike lacks the newest skinpacks (F-Hybrid Gen3, 2024+).
- **Intra-band extra years** (e.g. 1987, 1989, 2007) — each class has only one installed NAMeS
  file pinning one year; the extra years have f1db fixtures but no installed roster.

## Tooling status (unblocked 2026-07-06)

`tools/Companion.PackGen` needs the raw **f1db SQLite** (`race`/`circuit`/`grand_prix`/`driver`/
`constructor`/`season_entrant_driver`). It was absent on disk; the public **f1db v2026.9.1
`f1db-sqlite.zip`** (CC BY 4.0) was downloaded and its schema verified against PackGen's queries —
now at `tools/_f1db/f1db.db` (gitignored). PackGen currently hardcodes only 1967/1969/1988
`SeasonConfigs`; the other 10 packs were agent-generated per-season. Building the 5 new seasons
means, per season: map each f1db circuit → an era-correct AMS2 track (placeholder-substitute where
AMS2 lacks the layout, exactly like `f1-1988`), and bind each `entries[].ams2LiveryName` to the
**exact** `livery_name` string in that class's NAMeS XML (year/driver usually omitted; pseudo-team
tokens for the modern classes).
