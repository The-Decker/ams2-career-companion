# Per-circuit FUN FACTS â€” investigation + implementation plan

## (a) f1db schema â€” what can ground MORE facts (`tools/_f1db/f1db.db`)

Verified live against the DB (sqlite3 CLI is on PATH). Relevant tables/columns:

- **`circuit`** â€” `id, name, full_name, previous_names, type (RACE/STREET), direction, place_name, country_id, length, turns, total_races_held` (all-time count â€” NOT era-capped)
- **`circuit_layout`** â€” `id (e.g. monza-3), circuit_id, effective, length, turns` (per-configuration length/turns â†’ layout evolution)
- **`race`** â€” `id, year, round, date, grand_prix_id, official_name, circuit_id, circuit_layout_id, circuit_type, direction, course_length, turns, laps, distance, drivers_championship_decider (BOOL), constructors_championship_decider (BOOL), qualifying_format`
- **`race_data`** (`type` discriminator; relevant types `RACE_RESULT`, `FASTEST_LAP`, `STARTING_GRID_POSITION`) â€” per RACE_RESULT row: `position_number, position_text, driver_id, constructor_id, engine_manufacturer_id, tyre_manufacturer_id, race_reason_retired, race_pole_position (BOOL), race_grid_position_number, race_positions_gained, race_pit_stops, race_fastest_lap (BOOL), race_grand_slam (BOOL)`; per FASTEST_LAP row: `fastest_lap_time, fastest_lap_time_millis, fastest_lap_lap`
- **`driver`** â€” `name, nationality_country_id` (â†’ home-win counts vs `circuit.country_id`), plus career totals (not needed)
- **`grand_prix`** â€” `name, full_name, total_races_held`
- **Coverage floors verified:** `FASTEST_LAP` rows and `race_grid_position_number` both exist from **1950** â†’ every template below is safe for all 60 shipped years (1967â€“2026).

All candidate queries were executed and return sane values (Monza as-of-1967: most wins Moss/Fangio 3 each, 11 winners in 18 GPs, 5 title deciders, layout monza-3 lap record Clark 1:28.500, Fangio 5 poles, Ferrari 6 wins, winner from as far back as P9, 4 home wins, 3 layouts 5.75â€“10 km).

## (b) Where the blurb comes from + current data shapes

- **`tools/derive_history.cs` is the generator** (NOT derive_circuits.cs). Its `CircuitHistory()` helper (line 156) composes the one-sentence blurb from `circuit.previous_names + place_name + total_races_held + MIN/MAX(race.year)` and writes it as `rounds[].circuit.history` in **`data/history/<year>.json`**. Round circuit object shape: `{layoutId, name, place, type, direction, lengthKm, turns, history}`.
- **`tools/derive_circuits.cs`** only bakes SVG map geometry â†’ `data/ams2/circuits/<layoutId>.json` = `{source, w, h, paths[]}`. Leave it alone.
- **App plumbing:** `src/Companion.ViewModels/Services/HistoricalSeason.cs` â†’ `HistoricalCircuit` record (`History` at line 80), loaded by `HistoricalSeasonStore`. `CareerSessionService.SeasonSchedule()` (line ~690) projects it into `SeasonScheduleEntry` (`ICareerSession.cs` line 221, `CircuitHistory` line 246) â†’ `CalendarViewModel` round VM (line 140) â†’ **`CalendarView.xaml` expander line 109** (History TextBlock under the map). **`BriefingViewModel`** (lines 227â€“231) pulls the same `HistoricalCircuit` for the race-setup circuit panel; HistoryView uses it too.
- **Spoiler bug worth fixing in the same pass:** the current blurb is NOT era-capped â€” the 1967 file says Kyalami "hosted 20 GPs between 1967 and 1993" (verified in `data/history/1967.json`). The new facts must be capped; recommend capping the blurb's count/span the same way.

## (c) Proposed design

**Data shape:** add `"facts": ["â€¦", "â€¦"]` to `rounds[].circuit` in `data/history/<year>.json` â†’ `HistoricalCircuit.Facts : IReadOnlyList<string> = []`. Additive + camelCase â†’ old files deserialize fine (empty list). Facts live in the per-year history file (NOT the geometry file) because they are **era-capped per season**: every aggregation uses `WHERE r.year < seasonYear` â€” "coming into this season" â€” so nothing from the current season or the future ever leaks (fixes the anachronism class above by construction).

**Fact templates â€” 10, all pure f1db aggregations, never invented** (venue = `circuit_id`, except lap record = `circuit_layout_id`; skip a template when its query returns no rows/NULL; ties joined "A / B (3 each)"):

1. **First GP + count:** "First Grand Prix here: 1950 â€” 17 World Championship GPs held coming into this season." Debut special case (`MIN(year) == seasonYear`, e.g. Kyalami/Mosport/Bugatti in 1967): "Hosts its first World Championship Grand Prix this season."
2. **Most wins:** "Most wins here: Stirling Moss / Juan Manuel Fangio (3 each)." â€” `RACE_RESULT position_number=1 GROUP BY driver_id`
3. **Most poles:** "Most pole positions: Juan Manuel Fangio (5)." â€” `race_pole_position=1`
4. **Top constructor:** "Most successful constructor: Ferrari (6 wins)." â€” group winners by `constructor_id`
5. **Winner variety:** "11 different winners in 18 Grands Prix." â€” `COUNT(DISTINCT winner driver_id)` vs `COUNT(DISTINCT race id)`
6. **Lap record (this layout):** "Race lap record on this layout: 1:28.500 â€” Jim Clark (1967)." â€” `MIN(fastest_lap_time_millis)` on `circuit_layout_id`, display `fastest_lap_time`
7. **Pole conversion:** "6 of 18 races here were won from pole." â€” winner rows with `race_grid_position_number = 1`
8. **Deepest winning grid slot:** "Furthest back a winner has started: P9 (John Surtees, 1967)." â€” `MAX(race_grid_position_number)` among winners
9. **Title deciders:** "The drivers' championship has been decided here 5 times." â€” `race.drivers_championship_decider = 1`
10. **Home wins:** "Home-crowd wins: 4." â€” winner `driver.nationality_country_id = circuit.country_id`

(Optional 11th: "3 layout configurations used here, 5.75â€“10 km" â€” `COUNT(DISTINCT circuit_layout_id)` + `MIN/MAX(course_length)` from `race`.)

## Implementation steps (megaprompt-ready)

1. **`tools/derive_history.cs`:** add `static JsonArray CircuitFacts(conn, circuitId, layoutId, seasonYear, cache)` beside `CircuitHistory()`; cache per `(circuitId, seasonYear)` (lap record per `(layoutId, seasonYear)`), like the existing `yearSpans` dictionary. Emit at line ~132: `circuit["facts"] = CircuitFacts(...)`. Order = template list above; emit only non-empty; cap at ~6 per round to keep cards tight. Also era-cap the existing `history` sentence (`COUNT(*)/MIN/MAX WHERE year < seasonYear` instead of `total_races_held` â€” flag to Mike, recommended yes).
2. **Regenerate:** `dotnet run tools/derive_history.cs -- tools/_f1db/f1db.db data/history 1967 2026` (60 files; a few short strings per round â€” negligible size).
3. **`src/Companion.ViewModels/Services/HistoricalSeason.cs`:** add `public IReadOnlyList<string> Facts { get; init; } = [];` to `HistoricalCircuit`.
4. **`src/Companion.ViewModels/Services/ICareerSession.cs`:** add `CircuitFacts : IReadOnlyList<string> = []` to `SeasonScheduleEntry`; fill in `CareerSessionService.SeasonSchedule()` (~line 713) from `circuit?.Facts ?? []`.
5. **`src/Companion.ViewModels/Hub/CalendarViewModel.cs`:** round VM gets `CircuitFacts` + `HasCircuitFacts` (copy at line ~82).
6. **`src/Companion.App/Views/CalendarView.xaml`:** in the expander, under the `CircuitHistory` TextBlock (line 109), add a faint "FUN FACTS" mini-header + `ItemsControl` bullet list (`"â€¢  "` Run + wrapping TextBlock, `Style=Body`), collapsed via `HasCircuitFacts`.
7. **Briefing (second surface, same data free):** `BriefingViewModel` add `CircuitFacts` observable set at line ~231 from `circuit?.Facts`; matching bullet list in `BriefingView.xaml`'s circuit panel. HistoryView could reuse later â€” defer.
8. **Tests:** deserialization round-trip incl. missing-`facts` back-compat (`HistoricalSeasonStore`); `SeasonSchedule()` projection carries facts; Calendar VM exposes them; derive-tool logic is a dev tool (no shipped tests needed, but a 1967 spot-check against known values â€” Fangio 5 Monza poles â€” is cheap). Determinism untouched: reference data only, sim/fold never reads it (same contract as `History`).
9. **Hard rule enforcement:** every fact string is composed from a COUNT/MIN/MAX/GROUP-BY query result â€” no free text beyond the fixed template scaffolding; no editorializing ("famous", "tragic" etc. are banned by construction).

Key files: `Z:\Claude Code\ams2-career-companion\tools\derive_history.cs`, `Z:\Claude Code\ams2-career-companion\src\Companion.ViewModels\Services\HistoricalSeason.cs`, `Z:\Claude Code\ams2-career-companion\src\Companion.ViewModels\Services\ICareerSession.cs`, `Z:\Claude Code\ams2-career-companion\src\Companion.ViewModels\Services\CareerSessionService.cs`, `Z:\Claude Code\ams2-career-companion\src\Companion.ViewModels\Hub\CalendarViewModel.cs`, `Z:\Claude Code\ams2-career-companion\src\Companion.App\Views\CalendarView.xaml`, `Z:\Claude Code\ams2-career-companion\src\Companion.ViewModels\Briefing\BriefingViewModel.cs`, `Z:\Claude Code\ams2-career-companion\data\history\<year>.json`, `Z:\Claude Code\ams2-career-companion\tools\_f1db\f1db.db`.