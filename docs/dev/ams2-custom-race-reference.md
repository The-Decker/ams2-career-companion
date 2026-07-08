# AMS2 single-player custom-race variables — reference

A map of every setting on Automobilista 2's single-player custom-race / Test-Day screens, for
authoring pack `setupGuide`/`weekend` data and the Race-Day briefing checklist. Compiled from three
independent angles (2026-07-08):

1. **Game files** (authoritative field names): the frontend menu binding IDs extracted as ASCII from
   `<install>/GUI/menu_*.bgui`, the weather vocabulary from every track's `Tracks/<t>/<t>.trd`
   `Allowed Weather` + `GUI/WeatherPhotos/` + `GUI/weathericons.bspr`, and `Support/SharedMemory/…/SharedMemory.h`.
2. **Community docs** (values/defaults/semantics), cross-checked ≥3 sources: the wiki.gg + Fandom AMS2
   wikis, the Reiza forum (incl. dev David Wright on weather), OverTake/RaceDepartment, SimRacingCockpit,
   Coach Dave Academy, and the shared Madness-engine (Project CARS 2) heritage.
3. **Our model** gap (`SeasonDefinition.cs`, `BriefingComposer.cs`).

> Value **labels** (dropdown text like "Off/Visual/Full", "Real Weather", "x25") live in the packed
> language DB and are not plain-text on disk — the field/option **keys** below are from the game files;
> the value enums are from the community sources and flagged where uncertain.

The session model is inherited from Project CARS 2 / the Madness engine:
**Practice → Qualifying → Warmup → Race (→ optional Race 2)**.

---

## 1. Sessions

Each session is independently enabled and configured (`SessionSetup-<Session>-<Field>` bindings:
`Practice1/Practice2/Qualifying1/Qualifying2/Warmup/Race1/Race2`). Per-session fields:
`Active`, `DurationType` (Time vs Laps), `Duration`/`NumLaps`, `SessionStartTime`, `DateString`,
`TimeProgression`, `WeatherProgression`, `Weather1..Weather4`, `RollingStart`, `FormationLap`,
`CoolDownLap`, `MandatoryPitStop`, `MandatoryPitStopMinTyres`, `ManualPitStop`,
`PitStopsAllowRefuelling`, `LiveTrackPResetStr`, `ScheduledFullCourseYellow`.

| Session | Enable | Length model | Notes |
|---|---|---|---|
| **Practice** | on/off | **Time only** (minutes) | Open running; own start time / time-progression / weather / LiveTrack. |
| **Qualifying** | on/off | **Time only** (minutes) — **never lap-limited** (hard game constraint) | Sets the grid. Skipping it builds the grid from AI `qualifying_skill` + your chosen start slot. |
| **Warmup** | on/off | Time only (minutes) | Optional short shakedown after quali. |
| **Race** | always on | **Laps OR Time** (time = 5-min steps, up to 24 h) | The only session that can be lap-based. |
| **Race 2** | champ/regs-only | Laps or Time | Only in Custom Championship rounds; standard Single Race is one race. |

**Start type / grid:** `StartType` Standing / Rolling (default Standing); `FormationLap` on/off
(rolling only, manually driven); player grid slot set by qualifying or chosen when quali is skipped.

---

## 2. Weather (per session)

**Up to 4 weather slots per session** (`Weather1..Weather4`, each with `-Icon`/`-String`/`-Visible`
and an "Adjust Number of Weather Slots" control). Practice, Qualifying and Race each have their own
weather + LiveTrack.

**Slot → time mapping (`WeatherProgression`):**
- **Real-time**: one slot ≈ **1 hour**; the exact change moment inside a slot is randomized (so a short
  race on real-time may never leave slot 1 — the usual "weather never changed" complaint).
- **Sync to Race**: slots spread **evenly across the session** — per-slot ≈ session ÷ #slots (4 slots
  in a 1-hour race → ~15 min each). **This is the right default for authored weather** (deterministic
  spacing). Multiplier values `2x,5x,10x,15x,20x,25x,30x,40x,50x,60x` compress the hourly schedule.
- A slot set to **Random** re-rolls a condition; multiple random slots = unpredictable changeable weather.

**Conditions** (in-game display labels; internal tokens in parentheses). ~95% confident on exact spelling:
Clear (`Weather_Clear`), Light Cloud (`Weather_LightCloud`), Medium Cloud (`Weather_MedCloud`),
Heavy Cloud (`Weather_HeavyCloud`), Overcast (`Weather_Overcast`), Foggy (`Weather_Foggy`),
Hazy (`Weather_Hazy`), Light Rain (`Weather_LightRain`), Rain (`Weather_Rainy`),
Heavy Rain, Storm (`Weather_Stormy`), Thunderstorm (`Weather_SuperStorm`), plus **Random**
(`Weather_Random`) and **Real Weather**. (Engine also has fog+rain/heavy-fog and snow/blizzard tokens;
snow is not driveable in AMS2. Per-track `Allowed Weather` + `Weather Weights` gate what a given
circuit offers and how Random rolls.)

**Real Weather:** OpenWeather live/historical, **data back to 1979-01-01 only** → **cannot be used for
1967** (or any pre-1979 season) — pre-1979 packs must use manual slots. Disables the Wet/Damp LiveTrack
presets when on.

**LiveTrack starting grip (`LiveTrackPResetStr`), per session:** Default (Progressing), Green,
Light Rubber, Medium Rubber, Heavy Rubber, Damp, Wet. Green = low grip, rubbers in over the session;
in the wet the rubbered line inverts to *more* slippery and washes away. Slicks fail in the wet.

**Temperature / wind:** no manual sliders — temperature is derived from time-of-day + weather + track
climate + date; wind is simulated but not user-set.

---

## 3. Time of day

- **Start time** (`SessionStartTime`): 00:00–23:00, **1-hour** increments, per session; plus a **date**
  (`DateString`, drives sun position/season and Real Weather).
- **Time progression** (`TimeProgression`): Off · Real-time (1x) · 2x·5x·10x·15x·20x·25x·30x·40x·50x·60x.
  **No 6x/12x/24x.** (1 h real race @ 25x ≈ a full day/night cycle.)

---

## 4. Grid / AI (`Opponents` group)

`NumOpponents`/`MaxGridSize` (total grid up to ~47, capped per track by pit/grid slots) ·
`OpponentSkill` (**70–120 %**; at X% the field spans a band; ≥80% AI also tune their own setups) ·
`OpponentAggression` (**Low/Medium/High/Max**; Max forces every car to 1.0, overriding Custom-AI files) ·
`OpponentWetSkill` · `OpponentThrottleSkill` · class/multi-class selection (up to 10 classes) ·
player start position. Per-driver AI attributes (race/qualifying/aggression/defending/stamina/
consistency/start/tyre/fuel/wet/blue/weather-pit/mistakes/forced-mistakes/reliability/scalars) come
from the Custom-AI XML, not this screen — that is what the app stages.

---

## 5. Rules (`RulesSetup` group + global Gameplay/Difficulty)

| Setting | Key | Values | Notes |
|---|---|---|---|
| Damage type | `Damage` | Off · Visual · Performance/Full | + `DamageScale` severity |
| Mechanical failures | `MechanicalFailures`/`DamageRandomFailures` | on/off (+magnitude/frequency) | AMDM: gearbox/engine/suspension failures, flatspots |
| Tyre wear | `TireWear` | Off · x1 (Authentic) · x2 · x3 · x5… | upper bound (x5 vs x7) version-uncertain |
| Fuel usage | `FuelUsage` | Off · x1 (Authentic) · multipliers | |
| **Refuelling allowed** | `PitStopsAllowRefuelling` / `PitstopRefuellingAllowed` | **on/off** | series/class-dependent; **some classes disallow it entirely regardless of the toggle** |
| Mandatory pit stop | `MandatoryPitStop` | on/off | **≥4 tyres must change** to count |
| Mandatory tyres | `MandatoryPitStopMinTyres` | count | min tyres on the stop |
| Manual pit stops | `ManualPitStops`/`ManualPitStop` | on/off | drive the pit lane vs auto |
| Pit speed limit | `PitSpeedLimitKPH` | kph | |
| Driving aids | `AllowABS`/`AllowTractionControl`/`AllowStabilityControl`/`ForceRealisticDrivingAids` | Off/Low/High · Authentic | Authentic = only aids the real car had |
| Force setups/view/gears | `ForceDefaultSetups`/`ForceInteriorView`/`ForceManualGears` | on/off | |
| Penalties | `RaceDirectorPenalties`/`Contact`/`Crash`/`Speed`/`TrackCutting`/`PitLaneWhiteLine`/`PitDriveThrough` + `TrackLimit warnings count` | on/off / strictness | master `Penalties`; can be strict or lenient |
| Flags / FCY | `Flags`/`FullCourseYellow`/`ScheduledFullCourseYellow` | on/off · 25%/50%/75%/Random | full-course yellow + safety car |
| Start | `RollingStart`/`FormationLap`/`CoolDownLap` | on/off | |

---

## 6. Track

`Track + Layout` (most venues have GP/National/short/oval variants) · `Custom Date` · `Start Time` ·
`Time/Weather Progression` · `Weather slots` · `LiveTrack` preset. Track `Max AI participants` lives in
each `Tracks/<t>/<t>.trd` and caps the grid.

---

## 7. What we model today vs. the gap

**Modelled** (`PackSessionSettings`, `SeasonDefinition.cs:183`, all display-only / sim-inert):
`Opponents`, `StartTime`, `Date`, `WeatherSlots` (round-level, one list — always `["Clear"]` in every
shipped pack), `TimeProgression`, `MandatoryPitStop` (bare bool). Race length = `PackRound.Laps`.
`PackWeekendSession` (practice/qualifying) = `Present` + `Label` only (no duration). The briefing
(`BriefingComposer.cs:20`) renders a flat 7-row checklist: Track, Class, Opponents, Laps, Date, Start
time, Weather slot(s), Time progression, Mandatory pit stop — and never reads the `weekend` structure.

**Not modelled:** per-session weather (4 slots each), session durations (practice/qualifying minutes),
qualifying-always-timed, warmup, race-by-time, **refuelling flag**, fuel/tyre/damage rates, AI skill %/
aggression, start type/formation lap, penalties/flags/FCY, LiveTrack starting grip, mandatory-tyre
nuance. All of this is **sim-inert** in our app (a manual in-game checklist), so authoring it changes
no career fold / no oracle — it only changes what the briefing tells the player to set.

---

## 8. Fuel & race length — the "ran out of fuel" problem (F-Vintage / 1967)

- **AMS2 does not reliably auto-fuel you for the race.** The starting fuel load is a **setup value the
  player owns** (Setup → Strategy / Fuel & Strategy page, also on the in-car MFD), capped at the car's
  **tank capacity**. The default is frequently under-filled, and the fuel **mix defaults to Rich**
  (inflates burn). Refuel requested at a stop is **added** to what's in the tank, not "fill to X".
- **Gotcha:** if you never change a single setup value, the pit strategy may not apply — the default can
  carry **zero refuel** so a planned stop adds nothing and you run dry. Changing any value (starting
  fuel is easiest) forces the strategy to apply.
- **F-Vintage Gen1 tank = 190 L ≈ 55–58 laps at 1× consumption** (two sources: ams2cars.info + the
  Reiza forum fuel thread). **Most full-distance 1967 GPs exceed that**: Monza 68, Kyalami/Silverstone
  80, Zandvoort/Mosport 90, Monaco 100, Watkins Glen 108 laps. So at full rich-map 1× the 190 L tank
  does **not** cover the longest real distances — a genuine (mild, known-slightly-aggressive) modelling
  quirk, but fixable at the setup/options level.
- **1967 reality:** cars started with a full tank and finished the distance on it; **no scheduled
  refuelling** (refuelling as strategy only arrived ~1982, banned 1984). So **RefuellingAllowed = No**
  for 1967 is both faithful and what AMS2 may enforce for the vintage class anyway.
- **No % race-distance scaling in a Single Race** — length is laps OR time only. Percentage scaling
  ("Race Distance Scale") exists only in **Championship** mode.
- **Companion-app guidance (authentic-first), what the briefing should tell the player:**
  1. **Fuel to the distance** (up to the 190 L cap), not brim-by-habit; if the app knows/estimates
     per-lap burn, surface a number, else "run 2–3 practice laps, read fuel-per-lap in the ICM, ×laps".
  2. If the full distance needs **> 190 L**, **do NOT recommend refuelling** — recommend **fuel-saving**:
     a leaner fuel map (ICM) + short-shift; the 190 L tank then covers the distance. (Escape hatches,
     clearly non-authentic: lower Options→Gameplay **Fuel Usage** multiplier, or shorten the race.)
  3. **Flag it per track**: a race whose laps exceed the ~55–58-lap one-tank range should show a note
     ("Full distance exceeds the ~190 L tank at 1× — fill to max + run a lean map; 1967 cars don't refuel").
