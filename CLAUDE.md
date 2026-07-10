# AMS2 Career Companion

Windows desktop app (WPF, .NET 10, single self-contained exe) that runs historical career seasons
around Automobilista 2 single-player custom races. **`PLAN.md` is the founding product vision**
(2026-07-02) — still the scope north star, though the built state has moved well past it (career
hub, character system, SMGP replica mode). The **living** progress log — branch/RC state, what
shipped, what's next — is the auto-memory (`MEMORY.md` → `ams2-hub-build-progress.md`); the durable
design specs are in `docs/dev/` (`career-hub-design.md`, `character-system.md`, `smgp-design.md`,
the audits). Superseded planning docs are in `docs/archive/`. The single current resume prompt is
`NEXT-SESSION-megaprompt.md`. Verified AMS2/f1db reference tables: `docs/research/RESEARCH.md`.

## Locked directions (do not re-litigate)

- **SMGP is a separate career entity** from the semi-historical F1 careers — its own mode, not a
  pack mixed into the historical gallery (see `docs/dev/smgp-design.md`). In SMGP, **A. Senna is
  always OP** (Madonna #1, the top of the grid) — the benchmark to beat, never nerfed or dropped.
- The v1 result-entry model is **manual-first** (fast keyboard entry); the sim is **deterministic
  and replay-verified** — new fold rows are envelope-versioned + per-career gated, pack/grid
  changes affect new careers only, and the **f1db oracle is never touched**.

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
