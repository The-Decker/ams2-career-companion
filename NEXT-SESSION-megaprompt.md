# NEXT SESSION — AMS2 Career Companion megaprompt

Resume the AMS2 Career Companion (`Z:\Claude Code\ams2-career-companion` — WPF/.NET 10, single
self-contained exe). **READ MEMORY FIRST**, in order: `MEMORY.md`, then `ams2-hub-build-progress.md`
(the TOP "2026-07-08 SESSION" section is the latest), `ams2-career-companion-machine.md`,
`mike-build-maximally.md`, `no-fantasy-packs.md`, `opening-github-prs.md`, `ams2-no-commit-era-art.md`.
Branch `hub/increment-4`. **Confirm green FIRST:** `dotnet test Companion.slnx` (main ~1522 + render 28;
oracle 77/77). Head/RC as of this handoff: **`e562ad7`, RC `dist/AMS2CareerCompanion.exe` = 0.6.0+e562ad7**
(verify with git log). The render harness has a known STA-contamination flake in the FULL slnx run
(intermittent, several tests) — **re-run `tests/Companion.RenderHarness.Tests` ALONE to confirm green
(28)**; that is the source of truth, the parallel-run failures are not real.

Mike wants MAX building, minimal stopping (1.0 = the alpha). He verifies in-game / in-app (Claude can't
see AMS2) — give PRECISE things to check. Republish the RC only when the app is CLOSED.

## WHAT THE LAST BIG ARC DELIVERED (2026-07-08, all CONFIRMED working in-app by Mike unless noted)
A 12-slice assets/content/ratings arc, each green + determinism-safe + pushed:
1. **Year-pic 16:9 gallery** — career cards show the year image as the 16:9 hero (`StartView.xaml`).
2. **Shared user-image asset system** — `UserImageResolver` (folder+key+resolver) + per-career "Set
   card image…" picker (`RecentCareer.CustomImagePath`, carry-forward `Touch`, `SetCustomImage` seam).
3. **Global text-scale** — a root `LayoutTransform` (`AppUiScale`) scales the WHOLE UI incl. tear-off
   windows (was body-text-only). `App.ApplyAppearance`.
4. **⭐ RATINGS PHASE 3 (form-reactive fold)** — the sim FOLD reacts to per-race `DriverForm` (a hot
   rival shifts the player's expected finish/OPI/pace anchor). Gated by `PlayerCareerState.FormAware`
   (seeded true at creation via `CareerCreationRequest.FormAware`; wizard sets it), carried forward via
   record `with`. `RoundGridResolver.Resolve(applyWeekendForm)` overlays form on AI seats AFTER the cap,
   EXCLUDING the player, mirroring `GridStager.Nudge`. Fold passes `previous.Player.FormAware`; display
   expected-finish/slider pass `CurrentFormAware()`; STAGING stays form-off (GridStager applies it → no
   double-apply). Existing careers (FormAware absent→false) + oracle byte-identical. Gates
   `FormReactiveFoldDeterminismTests` (ON) + `FormFoldDeterminismTests` (OFF). **Adversarially reviewed
   clean.**
5. **Per-track thumbnails** — `data/ams2/track-art/<trackId>.jpg` on the briefing.
6. **Real historical results in History** — `tools/derive_history.cs` bakes 60 `data/history/<year>.json`
   (1967-2026, real champions + full per-race classified results, CC BY 4.0). `HistoricalSeason` model +
   `HistoricalSeasonStore` + `ICareerSession.HistoricalSeason(int)`. Read-only reference — fold never
   scores it.
7. **Data-grounded season summary** ("Senna took the 1988 title with 8 wins, 3 pts ahead of Prost…" —
   counted from results, no hallucination).
8. **Story images on History** — `KeyedAssetImageConverter` + `data/ams2/history-art/<year>.jpg`.
9. **⭐ CIRCUIT MAPS PER RACE** — f1db ships per-layout circuit SVGs (schema v6.4.0). `tools/derive_circuits.cs`
   downloads all **140** referenced layouts, extracts the path `d`, and **NORMALIZES it into WPF path
   mini-language** (WPF's `Geometry.Parse` needs the separators SVG's compact notation omits) →
   `data/ams2/circuits/<layoutId>.json`. `derive_history.cs` bakes each round's `circuit`
   {layoutId,name,place,type,direction,lengthKm,turns,history}. `CircuitGeometryConverter` (App,
   cached+frozen) renders via a `Viewbox<Path>`. Shown on the **briefing/race-setup**. Render test proves
   all 140 parse into non-empty geometries.
10. **HISTORY REVEAL MODEL** — each race is a spoiler-free **RACE PREVIEW** (circuit map + track detail)
    until the player races it (round ≤ `card.RoundsApplied` → `IsRevealed`), then a **HISTORICAL
    DOCUMENT** (winner + full grid). Champions/summary sealed until `IsSeasonComplete`. History leads with
    a prominent **NEXT RACE preview**.
11. **Circuit polish** — data-grounded circuit HISTORY blurbs on the previews; briefing circuit panel
    reformatted (venue heading + accent stats, no duplicated name via `CircuitCaptions.Compose(includeName:false)`);
    shortened the reveal note.
12. **Season-pick card grid** — the New-career season pick is now a GRID of year-pic cards (like the
    gallery), no version/path, accent-ring selection (`DiscoveredPack.Title`).

## NEXT WORK (Mike's open asks + noted deferrals — build maximally, determinism-safe)
- **Circuit map on the RESULT-ENTRY screen + the per-round HISTORICAL DOCUMENTS** (currently the circuit
  map shows on the briefing preview + History previews + next-race; add it to result-entry and to the
  revealed history documents too). Mike explicitly floated this.
- **Circuit carryover accuracy** — circuits are keyed by (seasonYear, round) via the history data, exact
  when playing a pack's REAL year but mismatched on a CARRYOVER year (same car, next year → different real
  circuits). Refine: bake `circuitLayoutId` into the PACK rounds (carryover-stable) OR add an AMS2
  Track.Id → circuit map. Note this to Mike.
- **More tear-off windows** (History/Standings/results pop-out) — reuse the `NewsWindow` pattern +
  `WindowPlacementSettings` + the `AppUiScale` transform (already added to tear-offs).
- **Results/Standings 4K polish** (#8, open-ended — audit fixed px in `ResultEntryView/StandingsView/
  ConfirmView/MainWindow/Theme.xaml`; do WITH Mike's eyes on it — hard to verify blind).
- **Wizard "type a custom livery" field** → `CareerCreationRequest.PlayerLiveryName` (player-as-own-entrant
  already handles a non-pack livery; this makes it reachable).
- **Deeper era/reference narrative content** (#4) — keep FACTUAL/data-grounded (per-race authored prose
  risks hallucination). The circuit-history blurbs are the model.
- **Card-size tuning** — season-pick cards are 248px (~4/row); gallery cards 288px. Mike may want them
  bigger/smaller.
- Long-standing: canonicalize 1988+1967 to one-driver-per-seat (needs synthetic-pack test rewrites);
  more V8 seasons if Mike installs the rosters; 2023 needs missing AMS2 tracks.

## HARD CONSTRAINTS (never break)
- **DETERMINISM:** sim = fold(journal), byte-replayable. `FormAware` (Ratings Phase 3) is the per-career
  form-reactive gate — seeded at creation, carried via record `with`. ANYTHING touching the fold needs a
  Resimulate gate + oracle 77/77 + adversarial review. Staging/skins/history/circuits are sim-inert.
- **NO FANTASY packs** (only faithful single historical seasons; a real-grid year CARRYOVER is fine).
- **Never commit user art** — `data/ams2/{era-art,track-art,history-art}/*.jpg|png` are Mike's, stay
  UNTRACKED ([[ams2-no-commit-era-art]]); the READMEs in those folders ARE committed. Stage specific
  files, never `git add -A`.
- **Never print git-credential tokens** (helper `Z:\Claude Code\open-pr.ps1`; no gh CLI; PR self-merge to
  main is sandbox-blocked — Mike merges PR #4).
- pack/`drivers.json`/`season.json` files are **CRLF + no-BOM**; `data/rules`/`data/history` are LF+no-BOM.
  Surgical edits only. `HubViewModel.cs` is CRLF (multi-line Edit old_strings need `\r\n` care).
- **Republish the RC only when the app is CLOSED** (test-rename `dist/*.exe` first; back up the old exe as
  `dist/*.exe.old-*`). `dotnet publish src/Companion.App/Companion.App.csproj -c Release -o dist`. Data
  globs (history/circuits/art) copy into `dist/data` automatically. Sync `dist/data` + `dist/packs` after
  a DATA-only change.
- Render harness **full-slnx STA flake** — re-run render alone to confirm 28 green.

## KEY FACTS / PATHS
- f1db: `tools/_f1db/f1db.db` (v2026.9.1, CC BY 4.0). sqlite3.exe:
  `C:\Users\KOBRA\AppData\Local\Microsoft\WinGet\Packages\SQLite.SQLite_Microsoft.Winget.Source_8wekyb3d8bbwe\sqlite3.exe`.
  Circuit tables: `circuit`(id,name,place_name,type,direction,previous_names,total_races_held),
  `circuit_layout`(id,circuit_id,length,turns); `race`(year,round,circuit_id,circuit_layout_id).
  Circuit SVGs (NOT in f1db releases): `github.com/f1db/f1db` → `src/assets/circuits/{white,white-outline,
  black,black-outline}/<circuit-layout-id>.svg` (single `<path d>`, 500×500).
- **Tools** (`.NET 10` file-based, `#:package Microsoft.Data.Sqlite@10.0.9`, run via `dotnet run tools/X.cs -- args`):
  `tools/derive_history.cs` (real results + per-round circuit + circuit history), `tools/derive_circuits.cs`
  (download+normalize circuit SVGs). Ratings tools `derive_ratings.cs`/`derive_form.cs` were in scratchpad.
- **Data:** `data/history/*.json` (60, real results + circuit info), `data/ams2/circuits/*.json` (140,
  WPF path geometry), `data/ams2/{era-art,track-art,history-art}/` (untracked user art + committed READMEs).
- **Circuit rendering:** `CircuitGeometryConverter.LoadFrom(dir, layoutId)` parses the normalized path via
  `Geometry.Parse` (App/Converters.cs). Keyed by `circuit_layout_id`. `CircuitCaptions.Compose(circuit,
  includeName)` in `HistoricalSeason.cs`.
- **Ratings Phase 3:** `PlayerCareerState.FormAware`; `RoundGridResolver.Resolve(...,applyWeekendForm)` +
  `ApplyWeekendForm`; `CareerSessionService.CurrentFormAware()`; `ReplayService.ResolvePlayerGrid(...,
  applyWeekendForm)`. Gates in `tests/Companion.Tests/Data/Form*FoldDeterminismTests.cs`.
- **History reveal:** `HistoryViewModel` (`NextRacePreview`, `HistoricalSeasonViewModel(season,
  roundsApplied, isSeasonComplete)`, `HistoricalRoundViewModel(round, isRevealed)` + `Circuit*`).
- 19 bundled packs: f1-1967,69,74,78,85,86,88,90,91,92,93,95,97,2000,2005,2006,2008,2016,2020.
- AMS2 install `Y:\SteamLibrary\steamapps\common\Automobilista 2`. Mike verifies in-game.
- Determinism-gate patterns: `tests/Companion.Tests/Data/*FoldDeterminismTests.cs`.
