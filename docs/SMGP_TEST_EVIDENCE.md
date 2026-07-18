# SMGP test evidence (mission SMGP-COMPLETE-001)

_2026-07-18 · Claude (Head of Coding) · branch `hub/increment-4`. Every result below is a
command and its outcome, not a claim. The release gate is reproducible end to end._

## Build + suite

| Command | Result |
|---|---|
| `dotnet build Companion.slnx` | Clean: 0 errors (pre-existing nullable/analyzer warnings only). |
| `dotnet test tests/Companion.Tests` | **2,881 passed / 0 failed.** Note: 1–2 SQLite-open tests can flake under the parallel run and pass isolated (documented; seen today on `F11967NewsFeedIntegrationTests`, pass isolated). |
| `dotnet test tests/Companion.RenderHarness.Tests` | **246 passed / 0 failed.** |
| f1db oracle (`F1DbOracleTests`, inside the logic suite) | **77/77.** |
| `dotnet test tests/Companion.Tests --filter FullyQualifiedName~SmgpWorldCompletenessTests` | **4/4** — consolidated 17-season validator: pack (24 teams / 34 drivers / 16 rounds, resolvable refs, Senna pinned), 17 lore entries, 24 logos + banners, 34 portraits + grid cars, all decodable from tracked sources. |
| `dotnet test tests/Companion.Tests --filter FullyQualifiedName~RacingPassportTests` | **12/12** — pure-racing isolation: no economy/XP/SMGP state, no rollover, one pinned season. |
| `dotnet test tests/Companion.Tests --filter FullyQualifiedName~NoEmDashGuardTests` | **3/3** — the em-dash rule holds over XAML + JSON content + C# literals. |

## Determinism + release evidence

| Command | Result |
|---|---|
| `COMPANION_BALANCE_EVIDENCE=1 dotnet test --filter FullyQualifiedName~ReleaseEvidence` | **Green (2026-07-18).** Full exceptional-profile 17-season real-pack career (272 rounds × 34 cars) with per-season wall-clock + **byte-identical Resimulate** proof. |
| Balance sweep evidence | `docs/LEVEL_300_BALANCE_REPORT.md` (200 careers × 17 seasons; cap rates, attrition, injury rates) + `docs/SMGP_BALANCE_REPORT.md` (module verdict). |

## Publish + fresh-install validation (2026-07-18, 14:08 build)

| Check | Result |
|---|---|
| `dotnet publish src/Companion.App -c Release` | Clean (the stale-obj .baml error is a known incremental-build artifact; `rm -rf src/Companion.App/obj/{Debug,Release}` fixes it — clean publish after). |
| Publish output content | `data/rules/era-themes.json` ✓, `data/ams2/era-art/{telegram,fax,email}.jpg` ✓, `data/ams2/grid-cars/` 34 ✓, `packs/smgp-1/` 5 files ✓, `Assets/Audio/Sfx/` 21 WAVs ✓. |
| Boot from foreign cwd (`cd /tmp`, launch publish exe) | **Pass** — process alive at 12s, main window up, resources resolved from the publish dir (no repo-relative path). |
| Deploy to dist (backup `.old-*` + swap, boot verify) | **Pass** — dist exe 14:10 boot-verified. |

## Manual acceptance (owner)

- **Pit Wall Command Rail:** four independent renders (Dark/Light × 1.00/1.50 UI scale) green
  in `StartViewCommandRailRenderTests`; review frames at `scratchpad/review-frames/`
  (gitignored) for Mike's sign-off. Last open item before the SMGP-1.0-alpha RC is cut.
