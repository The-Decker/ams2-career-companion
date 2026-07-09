# NEXT SESSION — AMS2 Career Companion: fun facts → per-season deep passes (parallel) → whole-app responsiveness

Resume the AMS2 Career Companion (`Z:\Claude Code\ams2-career-companion` — WPF/.NET 10, single
self-contained exe). **READ MEMORY FIRST**, in order: `MEMORY.md`, then `ams2-hub-build-progress.md`
(TOP section = latest), `ams2-alternate-tracks.md`, `ams2-career-companion-machine.md`,
`mike-build-maximally.md`, `no-fantasy-packs.md`, `opening-github-prs.md`, `ams2-no-commit-era-art.md`.
Then **READ the three grounding audits** this megaprompt is built on — they carry the per-file detail
this document only summarizes:

- `docs/dev/audits/audit-funfacts.md` — f1db schema, verified fact queries, exact implementation steps.
- `docs/dev/audits/audit-seasons.md` — the 19-pack deep-pass status matrix + tool usage lines.
- `docs/dev/audits/audit-responsive.md` — per-view responsiveness worklist with line numbers.

## STATE (verify with `git log` first)

Branch `hub/increment-4`, head as of this handoff **`c0c4345`** (pushed; PR #4 open — Mike merges).
RC `dist/AMS2CareerCompanion.exe` = **`0.6.0+c0c4345`**. Suite **1579** main + **38** render green
(render harness is serialized — `DisableTestParallelization` — and NO LONGER flaky; a clean full
`dotnet test Companion.slnx` passes in one shot); oracle 77/77. Recent: sectioned per-session
weekend/weather/fuel briefing + 1967 authored; alternate-track system complete (opt-in
install-verified tick, 73/80 placeholder rounds covered); Calendar hub tab with expandable
original-circuit cards + resizable PhotoWindow; circuit maps auto-level (PCA in
`CircuitGeometryConverter`).

Mike's context: he may be on a fresh usage week (he restarts when consumed) — **commit each slice as
it lands** so nothing is lost mid-mission. He has invoked **ultracode** before and may again: mission
2 is designed to fan seasons out in parallel. **All 19 season-picker artworks are uploaded** at
`dist/data/ams2/era-art/<year>.jpg` (beside the exe, where `EraArtResolver` reads; publishes never
delete them; NEVER commit them — era-art is untracked by policy).

---

## MISSION 1 — Circuit FUN FACTS + the era-cap spoiler fix (one slice, do this first)

Follow `docs/dev/audits/audit-funfacts.md` §(c) + implementation steps — it is exact. Summary:

- **Spoiler bug (fix in the same pass):** `tools/derive_history.cs` `CircuitHistory()` uses all-time
  `total_races_held` + full year span, so a 1967 card says Kyalami "hosted 20 GPs between 1967 and
  1993" — leaks the future. Era-cap it: `COUNT/MIN/MAX WHERE race.year < seasonYear`.
- **Facts:** add `CircuitFacts(conn, circuitId, layoutId, seasonYear, cache)` to `derive_history.cs`;
  emit `rounds[].circuit.facts: string[]` (≤6 per round, skip empty). The 10 verified templates (all
  pure f1db aggregations, era-capped `WHERE year < seasonYear`, ties joined "A / B (3 each)", NO
  invented text): first-GP+count (debut special case), most wins, most poles, top constructor,
  winner variety, layout lap record, pole-conversion count, deepest winning grid slot, title
  deciders, home wins. All queries were executed against `tools/_f1db/f1db.db` and return sane
  values (Monza-as-of-1967 spot checks in the audit).
- **Regenerate:** `dotnet run tools/derive_history.cs -- tools/_f1db/f1db.db data/history 1967 2026`.
- **Plumb:** `HistoricalCircuit.Facts` (default `[]`, back-compat) → `SeasonScheduleEntry.CircuitFacts`
  → `CalendarRoundViewModel` → a faint "FUN FACTS" bullet list in the CalendarView expander under the
  History text; same list in the BriefingView circuit panel (`BriefingViewModel` already pulls the
  same `HistoricalCircuit`). Sim-inert (reference data; fold never reads it — same contract as
  `History`).
- **Tests:** missing-`facts` back-compat via `HistoricalSeasonStore`; `SeasonSchedule()` carries
  facts; Calendar VM exposes them; a 1967 spot-check (e.g. Fangio 5 Monza poles). Mirror a render
  test off `CalendarRenderTests`.
- Commit + sync `dist/data/history` (data) + republish the exe (code change) when the app is CLOSED.

## MISSION 2 — PER-SEASON DEEP PASSES (the headline; ultracode-parallel or year-by-year)

`docs/dev/audits/audit-seasons.md` has the full matrix. 1967 is the finished template. **Everything
here is sim-inert display/staging data except the jusk imports (which change NEW careers only — same
contract as the 1967 import).** What every other pack lacks:

**Per-pack checklist (18 packs):**
1. **Weekend authoring** — `dotnet run tools/author_weekend.cs -- packs/f1-<year> [--refuel] --write`
   (practice 60 / qualifying 60 / Clear×4 defaults). **`--refuel` for 1995, 1997, 2000, 2005, 2006,
   2008** (race refuelling legal 1994–2009); all others false.
2. **Real per-race weather (the deep-pass content)** — research which GPs that season were WET
   (rain-affected race day) and author those rounds' `weatherSlots` accordingly (e.g.
   `["Rain","Light Rain","Clear","Clear"]` for a drying race). Data-grounded ONLY: cite a source per
   wet race (Wikipedia race reports are fine); when uncertain leave Clear×4. Update via
   author_weekend per-round extension or surgical JsonNode edits keeping the CRLF/2-space contract.
   Remember Real Weather (OpenWeather) can't be used pre-1979 in AMS2 — manual slots are the way.
3. **FuelGuidance profile for the pack's class** — 18 distinct classes need researched tank-litres +
   one-tank-laps figures (sources: ams2cars.info, Reiza forum). ⚠ `FuelGuidance.cs` is ONE shared
   file — do all 18 classes as a SINGLE sequential slice (research can be parallel; the edit is one
   commit), NOT inside the per-pack agents.
4. **Ratings uplift where a community XML exists ON-MACHINE** (verified):
   `Y:\SteamLibrary\...\UserData\CustomAIDrivers\F-Vintage_Gen2.xml` (jusk, + 6 per-track variants)
   → **f1-1969** via `dotnet run tools/import_jusk_ai.cs -- <xml> packs/f1-1969 --drop-form --write`;
   `F-Classic_Gen2.xml` (full 1988 community grid) → **f1-1988** (inspect first — same import path).
   Other years: only if Mike downloads more XMLs into the install (jusk's OverTake series).
5. **Leftover alternates** — 7 placeholder rounds still have none (one each: 1985/1986/1988/1990/
   1991/2008/2016 — street circuits or Bannochbrae-era limits). Probably legitimately none; confirm
   and move on. **Bannochbrae ONLY ≤1974 — hard rule.**
6. Grids, history files, base ratings, era-art: **already complete — no work.**

**Parallelization plan (ultracode):** each pack is its own directory → per-season agents CAN run in
parallel with NO worktrees, as long as (a) `FuelGuidance.cs` is pre-done sequentially (step 3), and
(b) each agent touches ONLY `packs/f1-<year>/season.json` + reports research (weather cites) as
structured output for review. Suggested workflow shape: Phase 1 sequential (FuelGuidance slice) →
Phase 2 `pipeline(18 packs, research-weather+author agent, verify agent)` — the verify agent checks
CRLF/no-BOM intact, `dotnet test` ReferencePack+WeekendAuthoring green, weather slots ≤4, refuel flag
matches era, citations present → Phase 3 one commit per pack or one batch commit; sync
`dist/packs/*` (data-only — no exe rebuild needed unless FuelGuidance changed, which IS code → one
republish at the end). Year-by-year mode: same checklist, one season per sitting, Mike playtests
between.

**Update `WeekendAuthoringTests`/`ReferencePackTests` expectations if they assert exact session
shapes; keep the full suite + oracle green after every pack.**

## MISSION 3 — WHOLE-APP RESPONSIVENESS (big cross-cutting UI job — Mike's explicit ask)

`docs/dev/audits/audit-responsive.md` is the per-view worklist with exact lines. The dominant
anti-pattern: `ScrollViewer > StackPanel MaxWidth=<fixed> HorizontalAlignment="Left"` — hard-capped
left-hugging columns that waste ~60% of a 2560 window. Order of work:

0. **Shared tool first:** add `WidthFractionConverter` (sibling of `AspectRatioHeightConverter`,
   register in `Theme.xaml`) so panels can do proportional MaxWidth. One commit.
1. **BriefingView** (worst offender; seen every round): restructure to a two-column star grid
   (checklist `3*` / circuit+advisories `2*`), scale the 168px circuit Viewbox, label column →
   `Auto` + `SharedSizeGroup`.
2. **ResultEntryView**: footer DockPanel → star grid; keep the good `*,*,*` center.
3. **StandingsView**: drop/proportion the 760px caps; header/row fixed pixels → shared `Auto`
   columns + `SharedSizeGroup`; inspector overlay proportional (same fix in HistoryView line 342).
4. **HistoryView**: 820px cap → proportional; scale the 96/180px circuit Viewboxes.
5. **CalendarView**: 900px cap → proportional; scale the 168px map; photo MaxWidth → proportional.
6. **SkinsView / ConfirmView / StartView / WizardView / SettingsView / NewsView + windows**: per the
   audit (StartView's gallery cap is the most visible — 2 cards/row on a 2560 monitor today).
   Copy the WizardView UniformGrid + AspectHeight precedent for card grids.

Test at MinWidth 920, 1920, and 2560, AND at 130% AppUiScale (the audit notes fixed widths clip at
scale — star grids absorb it). Add/extend render tests per reworked view (measure at 920 and 2560,
assert no crash + key elements present). This mission is view-layer only — zero sim risk. Commit per
view. Parallelizable with worktree isolation if ultracode; sequential is safer (shared Theme.xaml).

## CONSTRAINTS (unchanged, load-bearing)

- Pack files: **CRLF + 2-space + UTF8-no-BOM** (`author_weekend.cs`/`import_jusk_ai.cs` WriteJson
  does this). `HubViewModel.cs` is CRLF with PUA icon glyphs — PowerShell string surgery beats Edit
  there.
- Sim-inert vs gated: weekend/weather/fuel/facts data = display-only, NO determinism gate needed but
  ReferencePack + WeekendAuthoring + full suite must stay green. jusk imports change NEW careers
  only (existing pin their pack) — note it in the commit. NEVER touch the oracle.
- **No round may ever default to a mod track**; alternates only via the tick. Bannochbrae ≤1974.
  No fantasy packs. Era-art + venue-photos: user-managed beside the exe, never committed.
- Republish the RC only when the app is CLOSED (`mv` the exe → timestamped `.old-*` backup first,
  `dotnet publish src/Companion.App/Companion.App.csproj -c Release -o dist`). Data-only pack changes:
  just `cp` into `dist/packs/` — no rebuild.
- No `gh` CLI; PR #4 is Mike's to merge. Never `git add -A` (scratchpad/, era-art, venue-photos).
- Commit every finished slice immediately (usage-limit safety).

## VERIFY-IN-GAME (Mike)

- **M1:** Calendar expander shows a FUN FACTS bullet list under the history line; 1967 R1 Kyalami
  says "hosts its first World Championship Grand Prix this season" (era cap working — no 1993 leak).
- **M2:** a 1995–2008 career briefing shows **Refuelling: Yes**; a researched wet race (e.g. a
  famously wet GP that season) shows Rain in its weather slots; every class gets a sensible fuel
  note; 1969 grid feels jusk-tuned (compare a few names' pace).
- **M3:** maximize/resize the window at each screen — content reflows, no dead right half, nothing
  clips at 130% text scale; briefing shows two columns at 2560.

## OPEN DECISIONS (ask Mike only when reached)

- M1: era-cap the existing History sentence too? (Recommended yes — same query change.)
- M2: parallel (ultracode, all 18 packs in one session) vs year-by-year with playtests between?
  Wet-weather research depth (every season vs the famous wet races only)?
- M3: two-column briefing vs lighter proportional-cap-only pass?
