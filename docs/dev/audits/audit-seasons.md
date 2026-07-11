# PER-SEASON DEEP-PASS STATUS MATRIX â€” 19 packs (`Z:\Claude Code\ams2-career-companion\packs\f1-*`)

Legend: **(1)** weekend authoring = practice+qualifying `durationMinutes` + 4-slot `weatherSlots` on every round Â· **(2)** season `refuellingAllowed` flag Â· **(4)** ratings provenance Â· **(5)** rounds with `aiOverrides` Â· **(6)** rounds with historical `grid.starterDriverIds` Â· **(7)** placeholder rounds with/without `track.alternate` Â· **(8)** `data/history/<year>.json` Â· **(9)** era-art image.

| Year | ams2Class | Rds | (1) Weekend | (2) Refuel flag | (4) Ratings | (5) aiOverrides | (6) Grids | (7) Alt on placeholders | (8) Hist | (9) Art |
|------|-----------|-----|-------------|-----------------|-------------|-----------------|-----------|------------------------|----------|---------|
| 1967 | F-Vintage_Gen1 | 11 | **11/11 DONE** | **false (set)** | **jusk XML (imported, form dropped)** | 6/11 (jusk per-track) | 11/11 | 2/2 | yes | **1967.jpg** |
| 1969 | F-Vintage_Gen2 | 11 | 0/11 | ABSENT | f1db + driverForm; **partial aiOverrides on 3/11 rounds already present** | 3/11 | 11/11 | 4/4 | yes | none |
| 1974 | F-Retro_Gen1 | 15 | 0/15 | ABSENT | f1db + driverForm | 0 | 15/15 | 5/5 | yes | none |
| 1978 | F-Retro_Gen2 | 16 | 0/16 | ABSENT | f1db + driverForm | 0 | 16/16 | 5/5 | yes | none |
| 1985 | F-Retro_Gen3 | 16 | 0/16 | ABSENT | f1db + driverForm | 0 | 16/16 | **3/4 (1 without)** | yes | none |
| 1986 | F-Classic_Gen1 | 16 | 0/16 | ABSENT | f1db + driverForm | 0 | 16/16 | **2/3 (1 without)** | yes | none |
| 1988 | F-Classic_Gen2 | 16 | 0/16 | ABSENT | f1db + driverForm | 0 | 16/16 | **2/3 (1 without)** | yes | none |
| 1990 | F-Classic_Gen3 | 16 | 0/16 | ABSENT | f1db + driverForm | 0 | 16/16 | **2/3 (1 without)** | yes | none |
| 1991 | F-Classic_Gen4 | 16 | 0/16 | ABSENT | f1db + driverForm | 0 | 16/16 | **2/3 (1 without)** | yes | none |
| 1992 | F-Hitech_Gen1 | 16 | 0/16 | ABSENT | f1db + driverForm | 0 | 16/16 | 2/2 | yes | none |
| 1993 | F-Hitech_Gen2 | 16 | 0/16 | ABSENT | f1db + driverForm | 0 | 16/16 | 1/1 | yes | none |
| 1995 | FE-G1 | 17 | 0/17 | ABSENT | f1db + driverForm | 0 | 17/17 | 2/2 | yes | none |
| 1997 | F-V10_Gen1 | 17 | 0/17 | ABSENT | f1db + driverForm | 0 | 17/17 | 2/2 | yes | none |
| 2000 | F-V10_Gen2 | 17 | 0/17 | ABSENT | f1db + driverForm | 0 | 17/17 | 3/3 | yes | none |
| 2005 | F-V10_Gen3 | 19 | 0/19 | ABSENT | f1db + driverForm | 0 | 19/19 | 6/6 | yes | none |
| 2006 | F-V8_Gen1 | 18 | 0/18 | ABSENT | f1db + driverForm | 0 | 18/18 | 6/6 | yes | none |
| 2008 | F-V8_Gen2 | 18 | 0/18 | ABSENT | f1db + driverForm | 0 | 18/18 | **8/9 (1 without)** | yes | none |
| 2016 | F-Ultimate_Gen1 | 21 | 0/21 | ABSENT | f1db + driverForm | 0 | 21/21 | **9/10 (1 without)** | yes | none |
| 2020 | F-Ultimate | 17 | 0/17 | ABSENT | f1db + driverForm | 0 | 17/17 | 7/7 | yes | none |

Totals cross-check: 80 placeholder rounds, 73 with an alternate (matches the curated-alternates pass). All 19 history files exist (data/history covers 1967â€“2026 continuously, so carryover years are covered too).

## (3) Fuel one-tank profiles â€” `src/Companion.ViewModels/Services/FuelGuidance.cs`
`Profiles` dictionary contains ONLY `F-Vintage_Gen1` (190 L, ~58 laps, safe 55). **18 distinct ams2Class values need researched tank/laps figures:**
F-Vintage_Gen2, F-Retro_Gen1, F-Retro_Gen2, F-Retro_Gen3, F-Classic_Gen1, F-Classic_Gen2, F-Classic_Gen3, F-Classic_Gen4, F-Hitech_Gen1, F-Hitech_Gen2, FE-G1, F-V10_Gen1, F-V10_Gen2, F-V10_Gen3, F-V8_Gen1, F-V8_Gen2, F-Ultimate_Gen1, F-Ultimate.
Historical note for (2)+(3) authoring: race refuelling was legal 1994â€“2009 â†’ packs 1995/1997/2000/2005/2006/2008 should get `refuellingAllowed: true` (`--refuel`); all others false.

## (4) Custom-AI XML import candidates (verified ON-MACHINE)
`Y:\SteamLibrary\steamapps\common\Automobilista 2\UserData\CustomAIDrivers\` contains, beyond the already-imported F-Vintage_Gen1:
- **F-Vintage_Gen2.xml (jusk-authored, confirmed by content) + 6 per-track variants** (`F-Vintage_Gen2_01Kyalami/02Silverstone/03Nordschleiffe/04WatkinsGlen/05Full/06RegularF1.xml`) â†’ prime import for **f1-1969** (explains its existing partial 3/11 aiOverrides).
- **F-Classic_Gen2.xml** â€” a full 1988-grid community file ("1988 Lotus #1 - N. Piquet" etc.) â†’ import candidate for **f1-1988**.
- No other classes present; other vintage years would need XMLs sourced from OverTake/RaceDepartment (jusk's series) first.

## (9) Art resolution
`EraArtResolver.CandidateFileNames` (src/Companion.ViewModels/Services/EraArtResolver.cs): looks in `data/ams2/era-art/` for `<year>.jpg`, `<year>.png`, then era-medium fallback `telegram/fax/email.{jpg,png}`. Only `1967.jpg` exists; 18 pack years have no image and no medium fallback files exist. (Era-art is untracked by policy â€” Mike drops the files in; `UserImageResolver` layers user overrides for gallery/track/history art.)

## AUTHORING TOOLS (`tools/*.cs`, exact usage lines)
| Tool | Usage | Does |
|------|-------|------|
| author_weekend.cs | `dotnet run tools/author_weekend.cs -- <packDir> [--practice 60] [--qualifying 60] [--weather Clear,Clear,Clear,Clear] [--refuel] [--write]` | Writes per-round weekend durations + 4-slot weather + season refuellingAllowed (sim-inert; no --write = dry run) |
| author_alternates.cs | `dotnet run tools/author_alternates.cs -- <recsJson> <packsDir> <tracksJson> [--write]` | Writes curated `track.alternate` onto placeholder rounds, distance-preserving lap count |
| import_jusk_ai.cs | `dotnet run tools/import_jusk_ai.cs -- <aiXml> <packDir> [--drop-form] [--write]` | jusk Custom-AI XML â†’ drivers.json base ratings + per-round aiOverrides (grid-filtered); `--drop-form` removes f1db driverForm |
| derive_ratings.cs | `dotnet run derive_ratings.cs -- <f1db.db> <packDir> <year> [--write]` | f1db static raceSkill/qualifyingSkill into drivers.json |
| derive_form.cs | `dotnet run derive_form.cs -- <f1db.db> <packDir> <year> [--write]` | Per-round `driverForm` overlay in season.json (staging-only, sim-inert) |
| derive_history.cs | `dotnet run scratchpad/derive_history.cs -- <f1db.db> <outDir> [startYear=1967] [endYear=2026]` | Bakes shipped data/history/<year>.json from f1db |
| derive_circuits.cs | `dotnet run tools/derive_circuits.cs [f1db.db] [outDir] [startYear] [endYear]` | Downloads + WPF-normalizes f1db circuit SVGs â†’ data/ams2/circuits |
| extract_tracks.cs | `dotnet run tools/extract_tracks.cs -- "<AMS2 install dir>" [outJsonPath]` | Reads true mod-track tags from loose .trd files in the install |
| fix_mod_defaults.cs | `dotnet run tools/fix_mod_defaults.cs -- <packsDir> <tracksJson> [--write]` | Enforces "no round defaults to a mod": swaps mod default â†’ base placeholder + moves mod to alternate |

Also in tools/: project-style `Companion.ContentExtract` (vehicles/classes from .crd), `Companion.FixtureGen` (oracle fixtures), `Companion.GridInject`, `Companion.PackGen`, `Companion.StageGrid`, plus the staged `_f1db/f1db.db`.

## DEEP-PASS GAP SUMMARY (what a per-season pass must add, every pack except 1967)
1. Weekend authoring (author_weekend.cs) + correct refuellingAllowed (true for 1995â€“2008 packs) â€” 18 packs.
2. FuelGuidance profile research â€” 18 classes.
3. aiOverrides: only via community XML import (1969 + 1988 have on-machine XMLs) or future hand-tuning; f1db-derived packs have none.
4. 7 placeholder rounds still without alternates: one each in 1985, 1986, 1988, 1990, 1991, 2008, 2016 (street circuits / Bannochbrae-era limits â€” may be legitimately none).
5. Era-art: 18 pack years missing `data/ams2/era-art/<year>.jpg` (Mike-managed, untracked).
6. Grids, history data, and ratings baseline are COMPLETE everywhere â€” no work needed.