# CONTINUE SMGP — resume prompt (paste into a new session)

Resume the **AMS2 Career Companion** SMGP work (`Z:\Claude Code\ams2-career-companion`, WPF /
.NET 10, single self-contained exe, branch `hub/increment-4`). **DIRECTION (Mike, locked in
`docs/dev/smgp-design.md`): SMGP-ONLY until the Super Monaco GP replica mode is DONE** — no other
packs/features until then.

## First: orient (do this before touching anything)
1. **Read memory**: `MEMORY.md` → `ams2-hub-build-progress.md` (TOP block = current) →
   `ams2-next-content-arc.md` (holds the two open SMGP tasks with full plans) →
   `ams2-mclaren-skin-pipeline.md`.
2. **Verify against the live repo** — `git log --oneline -15`, `git status`, and read the files;
   treat every path/line below as a hint to confirm, this repo moves fast.
3. **Discipline every slice** (non-negotiable): keep the full suite + RenderHarness green (`dotnet
   test Companion.slnx`); the **f1db oracle 77/77 is NEVER touched**; sim/fold changes are
   envelope-versioned + per-career gated (replay byte-identical); pack/grid/data changes affect
   **new careers only**; commit + push per slice; show Mike between slices. Commit-message bodies
   with `()`/`'` break PowerShell here-strings — use `git commit -F <file>`. No `gh` CLI — PRs via
   the cached git cred + `Z:\Claude Code\open-pr.ps1`.
4. **Republish when the app is closed** (code changes aren't visible until then): `dotnet publish
   src/Companion.App/Companion.App.csproj -c Release`, back up `dist/AMS2CareerCompanion.exe` to
   `.old-<timestamp>`, copy the fresh exe from
   `src/Companion.App/bin/Release/net10.0-windows/win-x64/publish/`. Sync only the changed data
   files into `dist/` — NEVER wholesale-copy (it would delete Mike's untracked art: era-art,
   portraits, cars). Check `Get-Process AMS2CareerCompanion` first.

## What is DONE (head ~`4079470`, RC republished at HEAD; suite 1755 + 41 render)
- **Rival ladder**: two-wins seat swap VERIFIED wired end-to-end (`SmgpBattleFoldDeterminismTests`);
  the briefing copy is streak-aware. Name the rival BEFORE each race; a win only counts when he is
  your named rival.
- **Weekend**: full-race — Warm Up (practice) 60 min + Preliminary Race (qualifying) 30 min; the
  briefing heads the section "Qualifying (Preliminary Race)".
- **News**: a dedicated colorful SMGP outlet `data/rules/news/smgp.json` (SEGA world), routed by
  `NewsFacts.PreferredEra="smgp"` (never the historical 1990s corpus; sentinel era range 9000-9099).
- **The Season's Grid** wizard step: renamed from "Choose your grid"; portrait square-frame;
  smaller tiles; centered names; no checkbox for SMGP (forced whole field); driver/team names in
  the bundled **Victory Striker** font, the season/career-name selector in **Race Sport** (both
  embedded in `src/Companion.App/Fonts/`, referenced via `/Fonts/#<family>` in Theme.xaml).
- **Era art**: identity-keyed so SMGP shows `dist/data/ams2/era-art/smgp.jpg` (the SEGA Grand Prix
  art), not the shared-year 1990 photo (`EraArtResolver.SmgpArtKey`, `RecentCareer.CareerStyle`).

## OPEN SMGP TASKS (the two things left — pick with Mike, do in slices)

### A. Dynamic per-race DNQ field — "use the max, slowest swap out, like 1988" (Mike's #1)
**Fully scoped in `ams2-next-content-arc.md`.** It's a TOOL+DATA change (no engine change — the
resolver already intersects entries with `grid.starterDriverIds` + `CapToGridSize` keeps the
player + highest raceSkill and drops the slowest tail, exactly the 1988 pattern).
1. `tools/author_smgp.cs`: add the 8 reserve SECOND-CARS to the TEAMS array → **34 entries**
   (Bestowal +#8 Blume g3m1, Millions +#5 Jones g3m3, Blanche +#10 White g3m4, Tyrant +#12 Gould
   g3m4, Losel +#14 Dehehe g3m4, Dardan +#19 Alfven g3m4, Minarae +#21 Nono g3m4, Rigel +#27
   Chardin g3m2 — driver ids `driver.michael_blume/nigel_jones/paul_white/gilles_gould/willian_
   dehehe/keke_alfven/julianno_nono/tristan_chardin`, all already in `drivers.json`). FieldSize
   26→34, GenericBase 24→32.
2. Per round: `grid.size = min(34, trackCap)` (26, Monaco 25); generate `starterDriverIds` = top
   gridSize by `raceSkill + a deterministic per-round perturbation` (e.g. FNV(round, driverId) →
   ±0.03) so the STRONG always qualify (Senna/Ceara/McLarens) and the WEAK rotate through DNQ —
   dynamic per race, BAKED so it's deterministic/replay-safe like 1988.
3. Regen the pack; drop the reserve-only section; update `pack.json` notes.
4. Update `SmgpRosterDriftTests` — the "unbound skins = reserves" assertion changes (0 unbound now).
5. Full suite: `ReferencePackTests` (pack loads), `SmgpBattleFoldDeterminismTests` (ladder still
   folds — two-car teams already supported), byte-identical replay. NEW careers only.
6. **Then slice 3 = per-race LIVERY staging**: 34 skins, class cap 26 → per race activate the
   round's ≤26 qualifiers' skins (the reserves ship X-slot inactive; until this, a qualifying
   reserve floors to base-game paint per the active-only rule — which is Mike's sanctioned per-race
   swap now). The wizard step already shows all entries display-only for smgp.

### B. SMGP news Phase 2 — rival-event headlines (makes the seat change VISIBLE)
Read-side: extend `CareerSessionService.ReadFeed` to read the folded `smgp.seat` / `smgp.battle` /
career-over journal events and compose SMGP-flavored articles ("PLAYER SEIZES THE MADONNA SEAT!").
Add a `{rival}` token to `NewsFacts` (+ the `FactTokens` set in `NewsCorpusGuardTests`) and
`smgp.seat|swap`/`smgp.seat|forfeit` / `smgp.title` / `smgp.career` article types to `smgp.json`.
No fold change (the events are already journaled).

## Deferred SMGP tail (after A + B)
CareerOver hard-stop UX, reshuffle-by-points between seasons, random AI-initiated challenges,
per-round pit-crew advice + per-rival quote DATA files, two-titles completion celebration UI.
