# AMS2 Career Companion — agent guide (Codex)

Windows desktop app (WPF, .NET 10, single self-contained exe) that runs historical career seasons
around Automobilista 2 single-player custom races. **`PLAN.md`** is the founding product vision
(2026-07-02) — still the scope north star, though the built state has moved well past it (career
hub, character system, SMGP replica mode). **`docs/PROJECT.md` is the master onboarding guide — the
whole project in one place; read it first.** The durable design specs are in `docs/dev/`
(`character-rpg-rework.md`, `smgp-finish-roadmap.md`, `career-hub-design.md`, `character-system.md`,
`smgp-design.md`, the audits). Superseded planning docs live in `docs/archive/`. Verified AMS2/f1db
reference tables: `docs/research/RESEARCH.md`.

## ⚠ YOUR LANE (Codex) — dual-role handoff, read your charter first

While Claude (the usual Head of Coding) is out until it resets **Wednesday 2026-07-16**, Codex holds
BOTH roles, run as **two parallel instances** against a strict lane boundary. Read your charter:

- **Head of Coding** — charter **`docs/dev/codex-head-of-coding.md`**. Owns `src/Companion.Core/**`,
  `src/Companion.ViewModels/**`, `src/Companion.Data/**`, `src/Companion.Ams2/**`, `data/rules/**`,
  `tests/**` (except the render stand-ins). **Held temporarily** — when Claude resets it resumes Head
  of Coding and this instance reverts to GUI-only.
- **Head of GUI** — charter **`docs/dev/codex-head-of-gui.md`**. Owns `src/Companion.App/**`
  (Views/Themes/Converters/Assets) + `tests/Companion.RenderHarness.Tests`. **Permanent** — stays Head
  of GUI after Claude returns.
- **Strict, load-bearing boundary:** the coding instance NEVER edits `src/Companion.App/**`; the GUI
  instance NEVER edits Core/ViewModels/Data/tests (except its own render stand-in). This is what lets
  two agents share the repo without clobbering. `MEMORY.md`/`CLAUDE.md`/`AGENTS.md`/`PLAN.md` are
  coordinate-don't-clobber.
- **Current priority (both):** the character/RPG rework (`docs/dev/character-rpg-rework.md` — coding
  ships the Slice-0 stub bind contract FIRST so GUI can bind names), then the death/injury screens
  (`docs/dev/codex-gui-round5-brief.md`) and the SMGP finish queue (`docs/dev/smgp-finish-roadmap.md`).
- The **f1db oracle is sacred (77/77, never touched)**; the determinism/replay contract in
  `docs/PROJECT.md` §3 is non-negotiable. News changes must pass `NewsCorpusGuardTests`.

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
- **The em-dash `"—"` (U+2014) is banned from everything user-visible** (owner rule, 2026-07-17):
  XAML, JSON content (`data/rules`, `packs`, `data/history`, `data/ams2`), and C# string literals.
  In prose it becomes a comma, headings/labels take a colon, empty-value glyphs a plain hyphen.
  Allowed only in code comments (users never see those). Enforced by
  `tests/Companion.Tests/Guards/NoEmDashGuardTests.cs` — it must stay green.
