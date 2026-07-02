# Research Digest — AMS2 Career Companion

Distilled from the 9-agent research/design pass (2026-07-02). Verbatim detail, uncertainties and all
source URLs are in `research-workflow-output.json` (content under `.result.research` and `.result.designs`).
Facts verified against primary sources (Reiza forum spec threads, OverTake.gg, GitHub source code) as of
AMS2 **v1.6.9.8** (June 2026).

---

## 1. Custom AI Drivers (the grid-generation target)

- **Location (game install, NOT Documents):** `<Steam>\steamapps\common\Automobilista 2\UserData\CustomAIDrivers\`
  (create if missing). One **XML** file per vehicle class, filename = internal class name, case-sensitive
  (e.g. `F-Vintage_Gen1.xml`, `F-Classic_Gen2.xml`).
- **Format:** root `<custom_ai_drivers>`; each entry `<driver livery_name="...">` — `livery_name` is an XML
  **attribute**, case-sensitive, must exactly match the livery display name in the vehicle-select screen.
  All stats are child elements:
  - `name` (free text), `country` (3-letter code)
  - Floats 0.0–1.0 (0.5 = realistic average): `race_skill`, `qualifying_skill`, `aggression`, `defending`,
    `stamina`, `consistency`, `start_reactions`, `wet_skill`, `tyre_management`, `fuel_management` (ovals only),
    `blue_flag_conceding`, `weather_tyre_changes`, `avoidance_of_mistakes`, `avoidance_of_forced_mistakes`,
    `vehicle_reliability` (may exceed 0–1; >0.6 ≈ reliable; mean-minutes-between-failures mapping in Reiza thread)
  - **v1.6.9.8 additions:** `weight_scalar`, `power_scalar`, `drag_scalar` (~0.900–1.100, BoP-style —
    **also affects the PLAYER when driving that livery**) and `setup_downforce`, `setup_downforce_randomness`.
- **Semantics:** `race_skill` is compressed around the in-game Opponent Skill slider (at 90%: 1.0→~95%, 0.0→~85%).
  `aggression` scales with the Opponent Aggression setting; at Max it's forced to 1.0 for everyone.
  Low `consistency` = more lap-time variance. Omitted fields keep the stock driver's defaults; only listed
  liveries are overridden.
- **Per-track overrides (v1.3.4.0+):** duplicate a `<driver>` with `tracks="Track_Id1,Track_Id2"` attribute
  (internal track ids, e.g. `Monza_1971`, `Spa_Francorchamps_1993`, `Azure_Circuit_2021`); present fields
  override, missing ones inherit.
- **Livery binding:** skin packs = DDS + override XML under
  `Documents\Automobilista 2\Vehicles\Textures\CustomLiveries\Overrides\` with
  `<LIVERY_OVERRIDE LIVERY="51" NAME="..." BASELIVERY="Default">`. The `NAME` attribute defines the display
  name the custom-AI `livery_name` must match exactly. Livery slot IDs discoverable via `-showLiveryIDs`
  launch option. Gotchas: case sensitivity, unescaped `&`, special chars (ã); write UTF-8.
- **Custom grid mechanic:** set AI opponent count ≤ number of overridden drivers. Single-player only.
- **Machine-readable libraries to bundle:** `github.com/FitzHastings/AMS2CustomDriversUtil` (Apache-2.0) —
  `library/vehicles/ams2_vehicles_1.5.6.3.xml` (class name + xml_name + livery names) and
  `library/tracks/ams2_tracks_1.5.6.3.xml`. NOTE: predates v1.6 renames/content — re-extract from a current
  install on the desktop.
- **Why not built-in championship mode:** can't hand-pick AI entry lists (random fill, duplicate liveries),
  no custom points systems, no mid-season roster changes, replacement-driver points bug, no constructors
  standings, max 4 saves, grid capped by smallest-grid track.

## 2. Class → real season map (post-v1.6.9 renames) + content

| AMS2 class | Real era | Notes / licensed cars |
|---|---|---|
| F-Vintage Gen1 | 1966–67 | ≈Lotus 49 / Repco Brabham (fictional models) |
| F-Vintage Gen2 | 1969–70 | + Brabham BT26A, Lotus 49C |
| F-Retro Gen1 | 1974–75 | + Lotus 72E, McLaren M23, Brabham BT44 |
| F-Retro Gen2 | 1978–80 | + Lotus 79, BT46B fan car, BT49 (BRL DLC) |
| F-Retro Gen3 | 1983 | + MP4/1C, BT52 (BRL DLC) |
| F-Classic Gen1 | 1986 | + Lotus 98T (BRL DLC) |
| F-Classic Gen2 | 1988 | M1=top NA, M2=mid NA, M3=turbo; + MP4/4 (BRL DLC) |
| F-Classic Gen3 | 1990 | M1 V12≈Ferrari 641, M2 V8≈B190, M3 V10≈FW13B; + MP4/5B |
| F-Classic Gen4 | 1991 | M1 V12≈643, M2 V10≈FW14, M3 V8≈B191; + MP4/6 |
| F-HiTech Gen1/Gen2 | 1992 / 1993 | Formula HiTech DLC; active aids; + MP4/7A, MP4/8 |
| Formula Edge (ex F-V12) | 1995 | M1 V12 / M2 V10 / M3 V8 engine split |
| F-V10 Gen1 / Gen2 / Gen3 | 1997–98 / 2000–01 / 2005 | Gen3 new in 1.6.9.8, + Renault R25, Bridgestone/Michelin variants |
| F-V8 Gen1 / Gen2 / Gen3 (ex F-Reiza) | 2006 / 2008 / 2011–13 | + Renault R26, R28 |
| F-Hybrid Gen1 / Gen2 (ex F-Ultimate G1) / Gen3 (ex G2) | 2016 / 2019 / 2022 | |

- **Junior ladder:** Karts (5 variants), F-Vee Gen1/Gen2, F-Trainer (+Advanced), F-Junior (Lotus 22, 1962),
  F-Inter, F-3 (Dallara F301/F309), F-Dirt.
- **F-USA:** Gen1=1995 CART, Gen2=1998, Gen3=2000 (Racin' USA Pt2; Lola/Reynard/Swift, 4 engine makes,
  Road/Short Oval/Speedway configs each), F-USA 2023 (Pt3).
- **Historic F1 layouts (key ones):** Adelaide 1988, Montreal 1988/1991, Kyalami 1976, Imola 1972/1988/2001/2005,
  Interlagos 1976/1991/1993, Jacarepaguá 1988, Österreichring 1974/1977 + A1-Ring 2001, Monaco («Azure Circuit»),
  Suzuka («Kansai» GP/Classic), Spa 1970/1993/2005, Monza 1971 (+10k banked)/1991/2005, Silverstone 1975/1991/2001,
  Hockenheim 1977/1988/2001, Nordschleife 1971 (+Süd/Gesamt), Bathurst 1983, Estoril («Cascais») 1988, Jerez 1988,
  Barcelona 1991/1999/2001, Indianapolis 2001, Buenos Aires (7 variants). 62 locations / 210 layouts / 44 historic.
- **Missing venues needing `fallbackTracks`:** Zandvoort, Anderstorp, Dijon, Detroit/Phoenix/Dallas/Vegas,
  Magny-Cours, Mexico, Melbourne.
- **Season-calendar consensus:** near-period-correct calendars possible for 1967, 1969/70, 1974, 1978, 1983,
  1986, 1988, 1990–1993, 1995, 1997, 2001, 2005, 2006, 2008, 2016, 2019, 2022+.
- **Non-open-wheel for bonus series:** Stock Car Brasil (1979→2024), Stock USA G1–G3 (NASCAR-style), Super V8,
  GT1 (97/05), GTE/GT3/GT4, Group C, LMDh 2023, LMP1/2 2005, Group A (1992 DTM), M1 Procar, Copa Truck, karts, RX.
- **Season-guide sources:** OverTake "Ultimate AMS2 F1 Season Guide" (news id 567, revamped 2026); Reiza forum
  threads 32281 (F1 season track lists), 32150 (historic circuit guide); ams2cars.info; automobilista2.wiki.gg.

## 3. Result capture (Phase 2; manual entry is v1)

- **Shared memory (primary plan):** enable Options→System→Shared Memory = "Project CARS 2"; memory-mapped file
  **`$pcars2$`**, struct version 14 (`CREST2-AMS2` SharedMemory.h). `mSequenceNumber` is odd mid-write (spin
  until even). `mNumParticipants` + `mParticipantInfo[64]` (`mName[64]` — **custom AI names DO propagate**,
  `mRacePosition`, `mLapsCompleted`), parallel arrays: `mRaceStates`
  (FINISHED/DISQUALIFIED/RETIRED/DNF), `mFastestLapTimes`, `mCarNames`, `mCarClassNames`, `mNationalities`.
  Session: `mSessionState` (SESSION_RACE etc.), `mTrackLocation`, `mTrackVariation`, `mLapsInEvent`.
  **End-of-race recipe** (proven by `diegocbarboza/AMS2_SessionLogger`): poll 1–10 Hz, snapshot every valid frame,
  commit the last one when `mRaceState` → RACESTATE_INVALID after SESSION_RACE (classification wipes on exit to menu).
  C# marshalling reference: **CrewChiefV4** (open source). .NET: `MemoryMappedFile.OpenExisting("$pcars2$")`.
- **Second Monitor (alternative):** v9.39.0, `gitlab.com/winzarten/SecondMonitor`. Enable Settings→Reports →
  per-session export + "Also Export .Json File" → JSON per session in `Documents\SecondMonitor\Reports`.
  Drivers[] entries: `DriverLongName`, `FinishingPosition(InClass)`, `InitialPosition`, `CarName`, `TotalLaps`,
  `Laps[]` (lap + sector times), `IsPlayer`, `Finished`, `FinishStatus` + `TrackInfo`, `SessionType`, `Simulator`.
  Schema is app-internal/unversioned. Two community trackers already import it.
- **No native offline results export exists** in AMS2 (Reiza threads 18219, 30902). CREST2 = HTTP bridge over the
  same shared memory (localhost:8180), stale since 2024 — skip.

## 4. Historical data + era scoring rules

- **f1db** (`github.com/f1db/f1db`, **CC BY 4.0** — redistribution OK with attribution): 1950–present, updated
  per race. Release assets incl. `f1db-sqlite.zip` (~15.5 MB), JSON, CSV. Schema v6.4.0
  (`src/schema/current/single/f1db.schema.json`). Season entrants: Season→entrants→constructors→
  {chassis, engines, tyres, drivers(rounds, roundsText, testDriver)} — the only open source with true entry lists.
  RaceResult has `sharedCar` flag, `reasonRetired`, `positionText`, points; per-season official
  driver/constructor standings = **free test oracle for the points engine**. Circuit SVGs included.
- **jolpica-f1** (Ergast successor, `api.jolpi.ca/ergast/f1/`): CC BY-NC-SA — online lookups only, no entry lists.
- **Other series:** ChampCarStats.com (CART/IndyCar, HTML), nascaR.data (GitHub, direct CSVs), racingsportscars.com
  + wsrp.cz (Group C/WSC), touringcars.net (DTM), Wikipedia wikitables as the practical fallback → hand-curated packs.
- **Points tables:** 1950–59: 8-6-4-3-2 + 1 FL (FL split fractionally on ties — 1954 British GP = 7×1/7 pt);
  1960: 8-6-4-3-2-1; 1961–90: 9-6-4-3-2-1; 1991–2002: 10-6-4-3-2-1; 2003–09: 10-8-6-5-4-3-2-1;
  2010+: 25-18-15-12-10-8-6-4-2-1 (+FL top-10 2019–24; abolished 2025). Sprints 2021: 3-2-1; 2022+: 8..1.
  2014 double-points finale.
- **Best-N dropped scores (drivers):** 1950–53 best 4; 1954–57 best 5; 1958 best 6; 1959 best 5; 1960 best 6;
  1961–62 best 5; 1963–65 best 6; 1966 best 5. **Split-half seasons 1967–80** (best N from each half):
  1967: 5of6+4of5 · 1968: 5of6+5of6 · 1969: 5of6+4of5 · 1970: 6of7+5of6 · 1971: 5of6+4of5 · 1972: 5of6+5of6 ·
  1973–74: 7of8+6of7 · 1975: 6of7+6of7 · 1976: 7of8+7of8 · 1977: 8of9+7of8 · 1978: 7of8+7of8 · 1979: 4of7+4of8 ·
  1980: 5of7+5of7. 1981–90: best 11. 1991+: all count. (Verify each pre-1991 season against f1db in the oracle suite —
  Wikipedia list-article vs season-article discrepancies exist for 1959/1962.)
- **Shared drives:** 1950–57 points split equally; 1958+ shared drives score zero. f1db flags via `sharedCar`.
- **Half points (<75% distance, ≥2 laps) — exactly six races:** 1975 Spanish, 1975 Austrian, 1984 Monaco,
  1991 Australian, 2009 Malaysian, 2021 Belgian. 2022+ sliding scale: <2 laps: 0 · 2laps–25%: 6-4-3-2-1 ·
  25–50%: 13-10-8-6-5-4-3-2-1 · 50–75%: 19-14-12-10-8-6-4-3-2-1 · ≥75%: full.
- **Aggregate-time races (need manual result entry):** 1959 German (AVUS heats); red-flag aggregates: 1978 Austrian,
  1978 Italian, 1981 French, 1982 Detroit, 1987 Mexican, 1989 San Marino, 1992 French, 1994 San Marino, 1994 Japanese.
- **Indy 500:** WC round 1950–1960, drivers' championship only, entrants outside the season entry list
  (engine must allow per-race guest entries).
- **Constructors:** exists only from 1958; 1958–79 only the highest-placed car per constructor scores; all cars
  from ~1980 (verify 1979 exactly); FL point never counted for constructors in the 1950s.
- **Edge flags:** fractional points, excluded-driver-results-stand (1997 Schumacher), countback tiebreaks (wins→places).

## 5. Ecosystem & competitors

- **Season packs (OverTake.gg, mostly IMG collective / AFry):** F1 1967, 1969, 1975 (id 41981), 1978/79 (41320),
  1983, 1985/86, 1988, 1990 (64765 + AI 53488), **1991 (38880, ~25.6k downloads — flagship)**, 1992, 1993, 1995 (53478),
  1996 (80579), 1997, 2000, 2012, 2021, 2022+. Typical pack: liveries per class model + previews + helmets + suits +
  custom AI XML + a `.bat` "XML Selector" that swaps round-scenario rosters (our per-round generation replaces this).
- **Keystone tools:** NAMeS real-driver XMLs (50989/60634 — multiclass grids need custom-AI files for EVERY class
  present); AMS2CustomDriversUtil (61336); AMS2 Content Manager (`OpenSimTools/AMS2CM`); AMS2 Skinning Project Discord.
- **Competitors:** **Rewind GP** (82303, closest — historical F1 careers, shared-mem auto-capture, season packs +
  pack creator; F1-only, no economy) · **Race Pace** (81848, living multi-discipline career, not historical) ·
  For the Win (63157, stalled 2024) · Racing Life (55558, discontinued) · abesto/ams2-career (web, manual, shallow) ·
  Second Monitor championships (no career) · "The Paddock/Apex Racing Manager" (itch.io — proves the
  write-XML→shared-mem→career pipeline) · built-in championship (limits above).
- **Our open gaps:** era-correct points engines, constructors+drivers standings, team finances/bankruptcy (nobody
  does economy), round-by-round entry lists, first-class manual entry, pack format referencing existing skin packs,
  all-era coverage, single lightweight exe. **Reiza official career mode targeted end of 2026 — ship before it.**

## 6. Design proposals digest (full text in the JSON under `.result.designs`)

- **Career sim:** deterministic PCG32 streams keyed `H(masterSeed, subsystem, year, round, entityId)`; append-only
  journal `{seq, phase, entity, delta, cause}` powers the news feed and a "why?" inspector; save = snapshot +
  journal + verbatim raw results (re-simulate from round 1 is a menu item). Overperformance index:
  `OPI = 0.8·OPI + 0.2·(expectedFinish − actualFinish)`, DNF-cause aware; offer scoring
  `w1·rep + w2·OPI + w3·exp − w4·salary − w5·ageRisk` with team-archetype weights; pay-driver seats; canon-vs-living
  world dial; Budget Units currency (top team ≈ 100 BU/season) with era display skins + inflation rescale;
  crisis ladder (warn → sell seat → skip rounds → fold/rescue); aging curves era-shifted; player pace anchor from
  results vs known generated AI values → difficulty slider recommendation; Hardcore Aging via player scalars (opt-in).
- **Architecture:** WPF + .NET LTS single exe; Core/Data/Ams2/App/Tests split; SQLite per career (WAL, migrations,
  raw payload blobs); JSON packs immutable + pinned (hash) into career DB; pointsSystem as pure data (pointsTable,
  fastestLap.splitOnTie, bestN whole/splitSeason segments, sharedDrivePolicy, halfPointsRule, constructors
  exists/bestCarOnly/excludedRounds, tiebreak chain); rational arithmetic; f1db oracle CI suite.
- **UX:** two-state home (Briefing/Enter Result); briefing = check-once panel with exact in-game strings + copy
  buttons + staged-XML file watcher; result entry = single input stream (car number or 2–3 surname letters vs
  unplaced drivers, `me⏎` for player, F8 DNF phase with bulk-confirm + one-letter reasons, single-key DSQ/penalty,
  Ctrl+Z, drag fallback) — ~80s for 26 cars; confirm screen = points + animated standings delta + one headline;
  standings with gross vs counted points + Wikipedia-style round matrix + rules chip; era-styled offer letters
  (telegram/fax/email); new-career wizard with content verification and proceed-anyway; season readiness score;
  headlines from seeded template bank; minimal-narrative toggle; startup <1s; offline-first.
