# NEXT SESSION — AMS2 Career Companion: per-session weekend/weather/refuelling/fuel model + 1967 authoring

Resume the AMS2 Career Companion (`Z:\Claude Code\ams2-career-companion` — WPF/.NET 10, single
self-contained exe). **READ MEMORY FIRST**, in order: `MEMORY.md`, then `ams2-hub-build-progress.md`
(the TOP two sections are the latest — the per-season-deep-pass pivot + 1967-from-jusk rebuild, and the
circuit/tear-off/own-entrant work), `ams2-career-companion-machine.md`, `mike-build-maximally.md`,
`no-fantasy-packs.md`, `opening-github-prs.md`, `ams2-no-commit-era-art.md`. Then **READ
`docs/dev/ams2-custom-race-reference.md`** — the full AMS2 custom-race variable research this task is
built on (esp. §1 Sessions, §2 Weather, §5 Rules/refuelling, §8 Fuel).

Branch `hub/increment-4`. Head as of this handoff: **`f367b7a`** (verify with `git log`). RC exe
`dist/AMS2CareerCompanion.exe` = `0.6.0+e66ac4b`; `dist/packs/f1-1967` already synced with the jusk
rebuild. **Confirm green FIRST:** `dotnet test Companion.slnx` (main **1527** + render **33**; oracle
77/77). Render harness has a known STA-contamination flake in the full slnx run — re-run
`tests/Companion.RenderHarness.Tests` ALONE to confirm 33.

Mike wants MAX building, minimal stopping (1.0 = the alpha). He verifies in-game / in-app — give PRECISE
things to check. Republish the RC only when the app is CLOSED; for a DATA-only pack change, sync
`dist/packs/f1-1967` (copy the changed files) rather than rebuilding the exe.

## THE GOAL

Model AMS2's **per-session** custom-race setup faithfully and section the Race-Day briefing by session,
then author it for 1967. Concretely, Mike's ask:
- When setting up **Practice, Qualifying, and Race**, show the **weather sectioned into 4 slots** for
  EACH session (AMS2 has up to 4 weather slots per session, each independent — confirmed from the game's
  menu binding IDs `Weather1..Weather4`).
- **Practice** = a representative 1967 practice length (**default 60 min** unless Mike says otherwise —
  1967 had no separate practice/quali, grid came from practice times over Thu–Sat).
- **Qualifying** = **60 min** (AMS2 qualifying is ALWAYS time-limited, never lap-based — a hard game
  constraint, so this is exactly right).
- **Race** = laps, as now (`PackRound.Laps`).
- Show whether the season's rules **allow refuelling** (Yes/No). For **1967 = No** (cars started full
  and finished on one tank; refuelling as strategy only arrived ~1982; AMS2 may block it for the
  vintage class anyway).
- **Fuel guidance** (Mike hit an actual run-out-of-fuel race): the F-Vintage Gen1 tank is **190 L ≈
  55–58 laps at 1× consumption**, and most 1967 GPs are longer (Monza 68, Kyalami/Silverstone 80,
  Zandvoort/Mosport 90, Monaco 100, Watkins 108). AMS2 makes the player set fuel in the setup (max =
  tank); the default is often under-filled. So the briefing must tell the player how to not run dry.

**CRITICAL: this is entirely SIM-INERT.** The `setupGuide`/`weekend` block is a manual in-game
checklist, NOT a fold input (verified: `BriefingComposer` reads only `setupGuide.session` + `round.Laps`
for DISPLAY; the fold never reads weather/durations/refuelling). So this changes **no career fold and no
oracle** — no determinism gate needed. The only hard requirement is **existing packs keep loading**
(make every new field nullable/optional) and the reference-pack + weekend-authoring tests stay green.

## BUILD

### 1. Model — `src/Companion.Core/Packs/SeasonDefinition.cs` (all additive/nullable)
- **`PackWeekendSession`** (currently `Present` + `Label`, used by Practice/Qualifying): add
  - `DurationMinutes` (int?, nullable) — timed length.
  - `WeatherSlots` (IReadOnlyList<string>?, up to 4) — this session's weather, AMS2 display labels
    ("Clear", "Light Cloud", "Rain", …; see reference §2 for the vocabulary).
- **`PackWeekendRace`** (currently `Id`/`Label`/`PointsTable`/`GridFrom`): add `WeatherSlots`
  (IReadOnlyList<string>?, up to 4). Race length stays `PackRound.Laps` (no change).
- **`SeasonDefinition`**: add `RefuellingAllowed` (bool?, nullable — null = "unknown/not shown").
  Season-wide (Mike: "for each season").
- Keep the existing round-level `PackSessionSettings.WeatherSlots` for back-compat; the new per-session
  weather SUPERSEDES it for display when present, else fall back to the round-level list.
- Update `docs/dev/season-pack-format.md` to document the new fields.
- Validation (`PackStructuralValidator`): weather slots ≤ 4; durations > 0 when present. Additive; no
  new required fields (so the other 18 packs validate unchanged).

### 2. Briefing — `BriefingComposer` + `BriefingViewModel` + `BriefingView.xaml`
Section the checklist by session instead of the flat 9-row list. Target layout (still a tickable manual
checklist in AMS2 screen order, with the "N of M set" progress + Copy-summary intact):
- **Track / Class / Opponents** (unchanged, top).
- **PRACTICE** — Duration (min) · Weather slot 1–4.
- **QUALIFYING** — Duration (60 min) · Weather slot 1–4.
- **RACE** — Laps · Weather slot 1–4 · Date · Start time · Time progression.
- **RULES** — Mandatory pit stop · **Refuelling: Yes/No** (from `SeasonDefinition.RefuellingAllowed`;
  hide the row when null).
- **FUEL** — guidance (see §4). Not a tick row; an advisory line/panel like `DifficultyRecommendation`.
- Read per-session weather from the new `weekend` session fields, falling back to
  `setupGuide.session.weatherSlots` when a session has none (so un-migrated packs still render).
- The briefing currently NEVER reads `round.Weekend` — this is the change that surfaces it.
- Add a render-harness test (mirror the existing briefing render tests) proving the sectioned layout +
  the refuelling row + fuel note render.

### 3. Author 1967 — `packs/f1-1967/season.json`
For every round's `weekend`:
- `practice`: `{ present:true, label:"Practice", durationMinutes:60, weatherSlots:["Clear","Clear","Clear","Clear"] }`
- `qualifying`: `{ present:true, label:"Qualifying", durationMinutes:60, weatherSlots:["Clear",×4] }`
- `races[0]`: `{ id:"race", label:"Grand Prix", weatherSlots:["Clear",×4] }`
- Season: `refuellingAllowed:false`.
- Weather stays **Clear ×4** for now (Mike asked for the 4-slot STRUCTURE; real per-race 1967 weather —
  which GPs were wet — is a later pass, and Real Weather can't be used pre-1979).
- **Author via a tool, not by hand** (11 rounds × nested edits): either extend `tools/import_jusk_ai.cs`
  or add a small `tools/author_weekend.cs` that sets these on each round + the season flag, writing with
  the same 2-space-indent + CRLF + UTF8-no-BOM contract (`import_jusk_ai.cs`'s `WriteJson` does exactly
  this — reuse it). Then sync `dist/packs/f1-1967`.

### 4. Fuel guidance (from research §8)
- Add a per-round fuel note to the briefing. The app can't read exact consumption (packed in `.bff`), so
  keep it honest + qualitative, driven by lap count vs the one-tank range:
  - Constant: F-Vintage one-tank ≈ **58 laps** at 1× (190 L). Make it a small config (per-class ideally;
    a constant is fine for 1967).
  - If `round.Laps` ≤ ~55: *"⛽ One tank covers the distance — fill to the race length in the setup
    (Setup → Fuel). 1967 cars don't refuel."*
  - If `round.Laps` > ~55: *"⛽ N laps exceeds the ~190 L tank at full consumption — fill to max and run
    a leaner fuel map (ICM) + short-shift, or lower Options→Gameplay Fuel Usage. 1967 cars don't
    refuel."*
- Also surface the **AMS2 gotcha** once (a coach-mark or the fuel note): *"AMS2 makes you set the fuel
  load in the car setup (it doesn't auto-fill enough); change at least one setup value or the pit
  strategy won't apply."*
- Do NOT recommend enabling refuelling for 1967 (non-authentic + may be class-blocked).

## CONSTRAINTS
- **Sim-inert** — no fold/oracle change. Still run the FULL suite + `ReferencePackTests` (validates every
  bundled pack loads) + `WeekendAuthoringTests` (asserts every bundled round = practice + qualifying +
  exactly one "Grand Prix"; adding fields keeps the shape, but UPDATE that test if it asserts exact
  session object contents).
- Pack files are **CRLF + no-BOM**; `tools/import_jusk_ai.cs` round-trips season.json to 2-space+CRLF
  (which matches the sibling packs like 1988 — a consistency win). Reuse that writer or do surgical edits.
- `HubViewModel.cs` is CRLF (multi-line Edit old_strings need `\r\n` care).
- Never commit user art (`data/ams2/{era,track,history}-art/*.jpg`); stage specific files, never `git add -A`.
- No `gh` CLI / PR self-merge (sandbox-blocked) — Mike merges PR #4.

## VERIFY IN-GAME (Mike)
- New 1967 career → Race-Day briefing shows **Practice / Qualifying / Race sections, each with 4 weather
  slots**; Qualifying **60 min**; a **Refuelling: No** line; and a **fuel note** (with a warning on the
  long tracks — Monza/Monaco/Watkins/etc.).
- In AMS2's custom-race screen the 4 weather slots per session + the 60-min sessions match; you can fill
  fuel to ~190 L in Setup → Fuel; the fuel note matches what actually happens.

## OPEN DECISIONS (confirm with Mike)
- **Practice duration** — 60 min proposed; Mike may want the real multi-hour 1967 total or another value.
- **A recommended Fuel Usage multiplier** for 1967 (a hands-off "set Fuel Usage to x0.7" alternative to
  fuel-saving) — offer it or not?
- **Real per-race 1967 weather** (which GPs were wet) — deferred; Clear ×4 for now.
- **Roll this out to the other 18 packs** after 1967 proves the pattern (each needs its own durations +
  refuelling flag + per-class one-tank lap figure), or keep it 1967-only for now.

## WHAT'S ALREADY DONE (this arc, on hub/increment-4, all green + pushed)
Season-pick 4-col grid `5897bf9`; circuit map on result-entry `c13bf40`; tear-off lens windows
`aed80c4`; circuit carryover fix `83d6a80`; own-entrant custom-livery wizard field `e66ac4b`; recovered
derive tools `5844435`; **1967 rebuilt from jusk's XML** `c798260` (`tools/import_jusk_ai.cs`); AMS2
custom-race reference doc `f367b7a`. Suite 1527 + render 33; oracle 77/77.
