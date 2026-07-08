# NEXT SESSION — AMS2 Career Companion megaprompt

Resume the AMS2 Career Companion (`Z:\Claude Code\ams2-career-companion` — WPF/.NET 10, single
self-contained exe). **READ MEMORY FIRST**, in order: `MEMORY.md`, then `ams2-hub-build-progress.md`
(full state — the OVERNIGHT MISSION section at the top is the latest), `ams2-career-companion-machine.md`,
`mike-build-maximally.md`, `no-fantasy-packs.md`, `opening-github-prs.md`, `ams2-no-commit-era-art.md`.
Branch `hub/increment-4`. **Confirm green FIRST:** `dotnet test Companion.slnx` (main ~1497 + render 24;
oracle 77/77). Head/RC as of this handoff: **`849f35e`, RC `dist/AMS2CareerCompanion.exe` = 0.6.0+849f35e**
(verify with git log). The render harness has a known SQLite parallel-disposal flake in the full slnx run —
re-run `tests/Companion.Tests` alone to confirm green.

## WHAT THE LAST BIG ARC DELIVERED (all CONFIRMED working in-game by Mike, all determinism-safe)
- **Systematic f1db ratings pipeline** (STATIC raceSkill/qualifyingSkill across all 19 packs + a sim-inert
  per-race FORM overlay in season.json `driverForm`). Tools: `scratchpad/derive_ratings.cs`,
  `scratchpad/derive_form.cs` (.NET 10 file-based, `#:package Microsoft.Data.Sqlite@10.0.9`).
- **DNQ grid-seating**: a player on a car that DNQ'd a round is still seated (CapToGridSize drops the
  slowest peer to hold AMS2's hardcoded grid size).
- **Bubble-car skin graft** (`Companion.Ams2.Skins.BubbleCarGraft`): a 1988 Coloni/Eurobrun on a DNQ round
  gets its skin grafted into the slowest same-model peer's slot so it's selectable in-game. THE root-cause
  fix was `IsLiveActiveFile` — the livery scanner reads ALL override files incl. per-round VARIANTS, so a
  bubble car looked "already active" and the graft/smart-binding skipped it; now only the live `<model>.xml`
  counts.
- **Skin finalize — zero stock**: staged AI file names EVERY live-active livery (`RoundGridResolver.Resolve`
  gained `capToGridSize`; staging resolves the full uncapped field to cover the cap-dropped/displaced cars).
- **Player-as-own-entrant** (`RoundGridResolver.SyntheticPlayerDriverId`): a livery matching no pack entry
  seats the player as their own synthetic entrant → custom skins work + career never dead-ends.
- **News breadth**: ~700 new period-voiced variants across the 6 era corpora (`data/rules/news/*.json`).
- **Skin-module UX**: button is "Set up this race in AMS2" / "Overwrite anyway (backup first)"; the banner
  surfaces the whys + the player-car pick.

## NEXT WORK (Mike approved ALL of this for the overnight; do in this order, determinism-gated)
1. **RATINGS PHASE 3 — sim reacts to per-race form (the big one; touches the FOLD):** today `season.json`
   `driverForm` (round→driver→{raceSkill,qualifyingSkill} additive deltas) is STAGING-ONLY/sim-inert
   (`SeasonDefinition.DriverForm`, read only by `GridStager.Build`). Phase 3 makes the SIM react: the player's
   expected finish / OPI should shift when a RIVAL is hot that weekend. Plan: bump the round envelope
   (`RoundResultEnvelope`, currently v5) with the round's form (or read `pack.Season.DriverForm[round]` in the
   fold), fold it into `PaceAnchorMath`/field-strength so expected-finish moves; **Resimulate-gate it +
   keep the oracle byte-identical** (the oracle uses f1db fixtures with no driverForm → unaffected; a career
   WITH form must re-sim identically). This is the RISKIEST surface — do it carefully with a determinism gate
   BEFORE touching anything, and adversarial-review the fold change.
3. **WIZARD "type a custom livery" field (additive UI):** makes player-as-own-entrant reachable for
   non-standard skins. New-career wizard seat step: an optional text field → `CareerCreationRequest.PlayerLiveryName`
   = whatever the user types; the resolver already seats a synthetic entrant for a non-pack livery. Render-test it.
4. **MORE SEASONS in-game:** validate the skin injection on Mike's other installed community packs (1985 etc.)
   as he installs them; build any clean single-year packs (NO FANTASY). 2023 needs missing AMS2 tracks.

## ⭐ BIG NEXT PHASE — ASSETS, HISTORICAL CONTENT, UI POLISH & ACCESSIBILITY
Mike (2026-07-08, wrapping the ratings/skins arc): "preparing for the next phases, adding lots of content
and finalizing the seasons. I want a LOT of places to add assets so the app feels alive and historical —
but you still play your own seasons." He is actively creating art assets. This is a broad content + UI
phase; a codebase-scoping workflow (wf_cf400a48) mapped the seams — SEE ITS RESULT / the enriched notes
below for exact file:line before building. Build it determinism-safe (nearly all of this is presentation /
data / assets → sim-inert; the only sim-touching risk is if historical results get wired into scoring —
they must NOT: real history is REFERENCE content shown alongside the player's own career results).

REQUIREMENTS (Mike's list — all approved):
1. **Full 16:9 campaign art — the YEAR PIC is the selector.** The career-selection gallery icons are too
   small to show the full 16:9 image. ⭐ Mike (added): "when selecting your career, i want the YEAR PICS to
   be what is selected" — the season/year image should be the HERO of each career card (the big thing you
   see and pick by), not a tiny letterbox strip. Redesign the gallery card to a proper full-quality 16:9
   hero image driven by the career's year (via EraArtResolver), don't crop it.
2. **A general "upload high-quality images" system.** Lots of places to add art. Design ONE reusable
   user-asset convention (folder + key + resolver-with-fallback, mirroring the existing EraArtResolver /
   untracked era-art pattern) + ideally an in-app image picker, shared by items 1, 6, 7 below. Constraint:
   user art stays UNTRACKED (never git-commit it — see [[ams2-no-commit-era-art]]).
3. **Multiple / tear-off windows.** "See different windows." Extend the existing tear-off News window
   pattern so more views (History, Standings, a track/reference viewer, results) can pop out to their own
   window — good for multi-monitor.
4. **Historical references in-game (learn real F1).** Educational reference content so the player can learn
   what REALLY happened even when their career diverged. Real drivers/teams/season narratives.
5. **Full real historical race results in the History section.** Show the actual historical results of each
   race from history, ALONGSIDE the player's own career results — clearly separated ("what really happened"
   vs "your season"). NOTE: f1db (tools/_f1db/f1db.db) is a DEV tool, NOT shipped with the app — real
   historical results must be BAKED into pack/data (e.g. extend the pack, or a new shipped data file the
   History tab reads) at build time via a derivation tool (like derive_ratings.cs). Determinism: this is
   read-only reference data the FOLD never scores.
6. **Images on historical stories.** Attach art to the historical reference/story content (per-race or
   per-event image slots via the item-2 asset system).
7. **Per-track thumbnail sections.** Upload a track-layout image per track (keyed by track id from
   data/ams2/tracks.json; user-managed untracked folder like era-art). Show the track thumbnail on the
   briefing / round / history.
8. **Polished, higher-fidelity results layouts.** Make ResultEntry / Standings / round-result views look
   nicer on higher-quality + LARGE monitors — responsive spacing/typography, no fixed sizes that break on
   high-DPI/4K, higher visual polish (the tactile/therapeutic feel Mike values).
9. **Text-size setting for large monitors.** A settings option (a global text-scale factor / root FontSize)
   so text scales up on big monitors. Accessibility.
10. **Full optimization** for all the above (image loading/caching — don't load full-res into tiny thumbs;
    virtualized lists; responsive layout perf).
Suggested build order: item 2 (the shared asset system) FIRST since 1/6/7 depend on it → 1 (16:9 gallery) →
9 (text-size setting, small + high-value) → 8 (results polish) → 7 (track thumbs) → 5 (historical results
data + tool) → 4/6 (reference content + story images) → 3 (more tear-off windows). Each a green,
render-tested slice. Fan out with workflows where parallelizable (per-view polish, per-era reference copy).

### SCOPING SEAMS (from workflow wf_cf400a48 — precise file:line; read the code before building)
- **(1) Gallery 16:9:** `src/Companion.App/Views/StartView.xaml` — career ListBox L113-176, card DataTemplate
  L130-174; the image band is a `Grid Height="88"` L144-153 inside a `Border Width="288"` L132 with
  `Image Stretch="UniformToFill"` + a HARD-CODED `Border.Clip RectangleGeometry Rect="0,0,288,88"` L151. The
  288×88 band ≈3.27:1 letterbox → a 16:9 source loses ~46% vertically. FIX: band Height 88→**162** (288×9/16)
  + update the clip Rect, OR better drop the fixed clip and aspect-lock via a converter/Viewbox so it scales
  without magic numbers (288 is hard-coded in TWO places). Raise `ListBox MaxHeight="420"` L115 for taller
  cards. Converters `EraAccent`/`EraLabel`/`EraImage` in `src/Companion.App/Converters/Converters.cs`
  (registered Theme.xaml L57-60); `EraImageConverter` L217-254 loads from `EraArtDirectory =
  AppContext.BaseDirectory/data/ams2/era-art` (beside exe), full-res, OnLoad+IgnoreImageCache+Freeze (swaps
  hot-reload on Start `Refresh()`). The card binds the WHOLE `RecentCareer` (no path) so a converter can read
  a new per-career image field. Year source = `EraArtResolver.YearForEntry` (SeasonYear else name regex).
- **(2) Asset upload system:** resolver pattern = `src/Companion.ViewModels/Services/EraArtResolver.cs`
  (`CandidateFileNames(year)` most-specific-first, `Resolve(dir,year)`). Per-career override: add nullable
  `CustomImagePath` to `RecentCareer` (`src/Companion.ViewModels/Services/RecentCareers.cs` L18) mirroring the
  `SeasonYear` back-compat pattern (non-required, JSON default), thread through `Touch(...)`. NOTE: no in-app
  file-picker today (era-art is folder-drop, untracked). Build a shared `data/ams2/<kind>-art/` convention +
  an optional WPF OpenFileDialog picker in `StartView.xaml.cs`.
- **(5/4/6) History + real results + references:** History view `src/Companion.App/Views/HistoryView.xaml`(+.cs),
  VM `src/Companion.ViewModels/Hub/HistoryViewModel.cs` (CareerTimeline/ReadFeed/ArchivedArticles). ⭐ Real
  historical results ALREADY EXIST as `tests/Companion.Tests/Fixtures/f1db/*.json` (1950-2026,
  position-by-position, f1db-derived CC BY 4.0) — f1db.db itself is NOT shipped. SHIP them: bake a trimmed
  projection into `data/history/*.json`, add a `CareerRulesData` loader field, render a "what really happened"
  panel SEPARATE from the player's career results (fold never scores it → sim-inert; keep the CC BY 4.0
  attribution). Reference/educational copy → `data/history/references.json` (author via a workflow, per era).
  Story images → item-2 asset system keyed per race/event.
- **(7) Track thumbnails:** `Ams2ContentLibrary.Tracks` keyed by track Id (`src/Companion.Ams2/ContentLibrary/
  Ams2ContentLibrary.cs:32-49`). `BriefingView.xaml` (L6-17) is the SOLE view referencing a track/venue (no
  schedule view) — add a track-image slot at its top mirroring StartView's clipped image, via a new
  track-art resolver keyed by track id, user-managed untracked `data/ams2/track-art/<trackId>.jpg`.
- **(8) Results polish + (9) text size + (3) windows:** ⭐ Text-scale infra PARTLY EXISTS —
  `src/Companion.ViewModels/Settings/AppSettings.cs` already has `FontScalePercent`, and `App.xaml.cs:80`
  computes MainWindow base FontSize = `14.0 * FontScalePercent/100`. GAP: `Themes/Theme.xaml` sizes are FIXED
  (Body=14 L31 is the only scalable one; H1=26 L68, H2=19 L72, Faint=12) so they don't scale → rebind them to
  the scalable base; AND a MainWindow-only FontSize does NOT reach tear-off windows → set FontSize per window.
  `WindowPlacementSettings` record + `ClampTo` already exist (the tear-off News window precedent) → reuse for
  more tear-off windows (History/Standings/results/track viewer) and to persist their placement. Results
  views to polish: `src/Companion.App/Views/ResultEntryView.xaml`, `StandingsView.xaml`, `ConfirmView.xaml`,
  `MainWindow.xaml` + `Theme.xaml` (audit fixed px that break on high-DPI/4K).

## HARD CONSTRAINTS (never break)
- **DETERMINISM:** sim = fold(journal), byte-replayable. Pack-data changes → existing careers pin old bytes
  (safe). The f1db oracle replays fixtures, not packs. Anything touching the FOLD needs a Resimulate gate +
  oracle 77/77. Staging/skins are sim-inert. Run the full suite + oracle after every change.
- **NO FANTASY packs** (only faithful single historical seasons; a real-grid year CARRYOVER is fine).
- **Never commit `data/ams2/era-art/*`** (Mike manages the art; stage specific files, never `git add -A`).
- **Never print git-credential tokens** (helper `Z:\Claude Code\open-pr.ps1`; no gh CLI; PR self-merge to
  main is sandbox-blocked — Mike merges).
- `drivers.json`/`season.json`/pack files are **CRLF + no-BOM**; data/rules/news files ended up LF+no-BOM (fine).
  Surgical edits only (textual token replacement for drivers.json; a top-level `driverForm` insert for
  season.json) — keep the whitespace-stripped diff minimal.
- **Republish the RC only when the app is CLOSED** (locks the exe; test-rename `dist/*.exe` first). After a
  DATA change sync ALL of `dist/data` + `dist/packs`. After a CODE change republish + back up the old exe.
- **Community override files are MALFORMED** (stray `-->`); the skin writers edit them LINE-BASED, never
  re-serialize. AMS2 caps F-Classic_Gen2 at 26 liveries (Mike's in-game count) → bubble cars must be
  SWAPPED into the pool, not all-active.
- Mike verifies in-game (Claude can't see AMS2) — give PRECISE tests. Mike wants MAX building + workflows,
  minimal stopping; 1.0 = the alpha.

## KEY FACTS / PATHS
- f1db: `tools/_f1db/f1db.db` (v2026.9.1, CC BY 4.0). sqlite3:
  `C:\Users\KOBRA\AppData\Local\Microsoft\WinGet\Packages\SQLite.SQLite_Microsoft.Winget.Source_8wekyb3d8bbwe\sqlite3.exe`.
  Tables: `race`(year,round,id,grand_prix_id), `race_data`(race_id,type,driver_id,position_number/text,...)
  type in QUALIFYING_RESULT/RACE_RESULT/STARTING_GRID_POSITION/PRE_QUALIFYING_RESULT; `season_driver_standing`
  (year,driver_id,points DECIMAL). Pack id `driver.<snake>` → f1db `<snake→hyphen>` (+ alias carlos-sainz→
  carlos-sainz-jr). python is a dead Store stub — use sqlite3 + .NET.
- AMS2 install `Y:\SteamLibrary\steamapps\common\Automobilista 2`. Custom-AI: `UserData\CustomAIDrivers\<Class>.xml`.
  Skins: `Vehicles\Textures\CustomLiveries\Overrides\<model>\<model>.xml` (live) + `<model>_<Round>.xml` variants.
  F-Classic_Gen2 = 4 models: formula_classic_g2m1/g2m2/g2m3 + mclaren_mp44; g2m2 = the bubble-car model
  (shared chassis). The overtake 1988 pack's scenario `.bat` rotates variants per round; the app replicates it
  (`ApplyScenarioForRound`).
- 19 bundled packs: f1-1967,69,74,78,85,86,88,90,91,92,93,95,97,2000,2005,2006,2008,2016,2020.
- Determinism gate patterns: `tests/Companion.Tests/Data/*FoldDeterminismTests.cs` (Character/GridSelection/
  Form/OwnEntrant) — mirror these for phase 3.
