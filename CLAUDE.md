# AMS2 Career Companion

Windows desktop app (WPF, .NET 10, single self-contained exe) that runs historical career seasons
around Automobilista 2 single-player custom races. **PLAN.md is the approved product plan and the
single source of truth**; docs/research/RESEARCH.md holds the verified AMS2/f1db reference tables.
Four product decisions are locked (see HANDOFF.md) — do not re-litigate them.

## Build & test

- Solution: `Companion.slnx` (the .NET 10 XML solution format — there is no `.sln`).
- `dotnet build` / `dotnet test` from the repo root. Tests are xunit in `tests/Companion.Tests`.
- The oracle suite replays f1db season fixtures (`tests/Companion.Tests/Fixtures/f1db/*.json`)
  through the points engine and asserts equality with official standings. Regenerate fixtures with
  `tools/Companion.FixtureGen <f1db.db> <catalog.json> <outDir>` (f1db SQLite release, CC BY 4.0).

## Layout

- `src/Companion.Core` — domain, points/standings engine, career sim, pack loading. **No I/O, no WPF, no DB.**
  Points rules are pure data (`data/rules/f1-points-systems.json`), exact rational arithmetic
  (`Companion.Core.Numerics.Rational`), engine output carries gross vs counted points + dropped results.
- `src/Companion.Data` — SQLite persistence (one DB per career), migrations.
- `src/Companion.Ams2` — Steam detect, custom-AI XML writer + backup/restore, class/livery/track
  library (`data/ams2/*.json`, extracted from the local install), preflight validation.
- `src/Companion.App` — WPF shell (MVVM, CommunityToolkit.Mvvm).
- `data/ams2/` — machine-extracted content libraries (classes/vehicles/tracks/liveries) with
  provenance; refreshable data, never compiled in. Regenerate vehicles.json + classes.json with
  `dotnet run --project tools/Companion.ContentExtract -- "<AMS2 install>" data/ams2` — it parses
  every `Vehicles\*\*.crd` and resolves the install's genuine duplicate .crd basenames (the
  dir-named copy wins; `Ams2ContentLibrary.Load` applies the same rule as a backstop).

## Machine specifics (Mike's desktop)

- AMS2 install: `Y:\SteamLibrary\steamapps\common\Automobilista 2` — custom AI files go in
  `UserData\CustomAIDrivers\<VehicleClass>.xml`. That folder contains the user's live community
  NAMeS files: **never overwrite without a timestamped backup** (the app's staging contract).
- `Vehicles\*\*.crd` / `Tracks\*\*.trd` in the install are plain XML (class names, years,
  per-track `Max AI participants`).

## Conventions

- Season packs and fixtures serialize with `Companion.Core.Json.CoreJson.Options`
  (camelCase, enums as camelCase strings, `Rational` as `"3"`/`"1/7"` strings).
- Scoring quirks (half points, double points, Indy constructors exclusion, shortened-race tables)
  are round-level data (`PointsFactor`, `CountsForConstructors`, `AlternateRaceTableId`) — never
  hard-coded era logic in the engine.
