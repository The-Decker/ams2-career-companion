# AMS2 Career Companion — agent guide (Codex)

Windows desktop app (WPF, .NET 10, single self-contained exe) that runs historical career seasons
around Automobilista 2 single-player custom races. **`PLAN.md`** is the founding product vision
(2026-07-02) — still the scope north star, though the built state has moved well past it (career
hub, character system, SMGP replica mode). The durable design specs are in `docs/dev/`
(`career-hub-design.md`, `character-system.md`, `smgp-design.md`, the audits, `season-coverage.md`).
Superseded planning docs live in `docs/archive/`. Verified AMS2/f1db reference tables:
`docs/research/RESEARCH.md`.

## ⚠ YOUR LANE (Codex) — read `CODEX-1967-BRIEF.md` first

Two agents share this repo. **Claude = SMGP mode only. You (Codex) = the 1967 F1 era.** To avoid
clobbering each other:

- **Work in your OWN git worktree + branch `era/1967`** — NOT the same working directory Claude is
  editing. `git worktree add "Z:\Claude Code\ams2-worktrees\era-1967" -b era/1967 hub/increment-4`.
- **You own:** `packs/f1-1967/**`, `data/rules/news/1960s.json`, `data/history/1967.json`, 1967
  docs/research. Start from `docs/dev/season-coverage.md` to find 1967's real gaps.
- **Do NOT touch (Claude's / shared):** `data/rules/smgp/**`, `src/**/Smgp/**`,
  `src/Companion.Ams2/Skins/**`, `SMGP-CONTINUE.md`; the points/standings engine and
  `src/Companion.Core/News/**` are **data-only for you** (flag Mike if you truly need a code change);
  `MEMORY.md`/`CLAUDE.md`/`AGENTS.md`/`PLAN.md` are coordinate-don't-clobber.

Full details, guardrails, and the mission are in **`CODEX-1967-BRIEF.md`**.

## Locked directions (do not re-litigate)

- **SMGP is a separate career entity** from the semi-historical F1 careers — its own mode, not a
  pack mixed into the historical gallery (`docs/dev/smgp-design.md`). In SMGP, **A. Senna is always
  OP** (Madonna #1, the top of the grid) — never nerfed or dropped. *(Claude's lane — for context.)*
- The v1 result-entry model is **manual-first**; the sim is **deterministic and replay-verified** —
  new fold rows are envelope-versioned + per-career gated, pack/grid changes affect new careers
  only, and the **f1db oracle is never touched**.

## Build & test

- Solution: `Companion.slnx` (the .NET 10 XML solution format — there is no `.sln`).
- `dotnet build` / `dotnet test Companion.slnx` from the repo root. Tests are xunit in
  `tests/Companion.Tests`; the RenderHarness is `tests/Companion.RenderHarness.Tests`.
- The oracle suite replays f1db season fixtures (`tests/Companion.Tests/Fixtures/f1db/*.json`)
  through the points engine and asserts equality with official standings — **77/77, never touched**.
  News changes must pass `NewsCorpusGuardTests` (era-banned vocabulary, token/pool resolution).

## Layout

- `src/Companion.Core` — domain, points/standings engine, career sim, pack loading. **No I/O, no WPF, no DB.**
- `src/Companion.Data` — SQLite persistence (one DB per career), migrations, replay.
- `src/Companion.Ams2` — Steam detect, custom-AI XML writer + backup/restore, class/livery/track
  library (`data/ams2/*.json`), preflight validation, skin overrides.
- `src/Companion.App` — WPF shell (MVVM, CommunityToolkit.Mvvm). `src/Companion.ViewModels` — the VMs.
- `data/ams2/` — machine-extracted content libraries with provenance; refreshable, never compiled in.
- `packs/` — the season packs (`f1-1967`, `f1-1988` = SMGP base, etc.).

## Machine specifics (Mike's desktop)

- AMS2 install: `Y:\SteamLibrary\steamapps\common\Automobilista 2` — custom AI files go in
  `UserData\CustomAIDrivers\<VehicleClass>.xml`; **never overwrite without a timestamped backup**.
- `Vehicles\*\*.crd` / `Tracks\*\*.trd` in the install are plain XML.

## Conventions

- Season packs + fixtures serialize with `Companion.Core.Json.CoreJson.Options` (camelCase, enums as
  camelCase strings, `Rational` as `"3"`/`"1/7"` strings).
- Scoring quirks (half/double points, Indy exclusion, shortened-race tables) are round-level DATA
  (`PointsFactor`, `CountsForConstructors`, `AlternateRaceTableId`) — never hard-coded era logic.
