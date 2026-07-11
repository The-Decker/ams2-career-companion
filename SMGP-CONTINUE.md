# CONTINUE SMGP — optimize-the-mode megaprompt (paste into a new session)

Resume the **AMS2 Career Companion** SMGP work (`Z:\Claude Code\ams2-career-companion`, WPF /
.NET 10, single self-contained exe, branch `hub/increment-4`). **DIRECTION (Mike, locked in
`docs/dev/smgp-design.md`): SMGP-ONLY until the Super Monaco GP replica mode is truly polished.**
This thread is the **optimization pass** — make the mode feel complete: skins that light up, a
fictional-universe history, the SEGA-style car card, and paddock news that reacts to your career.

## First: orient (do this before touching anything)
1. **Read memory**: `MEMORY.md` → `ams2-hub-build-progress.md` (TOP blocks = current) →
   `ams2-next-content-arc.md` → `ams2-mclaren-skin-pipeline.md`.
2. **Verify against the live repo** — `git log --oneline -20`, `git status`, read the files. Treat
   every path/line below as a hint to confirm; this repo moves fast.
3. **Discipline every slice** (non-negotiable): keep the full suite + RenderHarness green (`dotnet
   test Companion.slnx`); the **f1db oracle 77/77 is NEVER touched**; sim/fold changes are
   envelope-versioned + per-career gated (replay byte-identical); pack/grid/data changes affect
   **new careers only**; commit + push per slice; republish when the app is CLOSED (check
   `Get-Process AMS2CareerCompanion`), back up `dist/AMS2CareerCompanion.exe.old-<ts>`, copy the
   fresh exe from `src/Companion.App/bin/Release/net10.0-windows/win-x64/publish/`, sync ONLY
   changed data files into `dist/` (NEVER wholesale-copy — it deletes Mike's untracked art). No
   `gh` CLI — PRs via the cached git cred + `Z:\Claude Code\open-pr.ps1`.

## What is DONE (head ~`<check git>`; suite 1784 + 45 render, oracle 77/77)
- **34-car dynamic DNQ field** (`SmgpSeasonVariety` unaffected): 34 painted cars, per-round baked
  `grid.starterDriverIds` = top-min(26,cap) by raceSkill+FNV perturbation; slowest pre-qualify out,
  rotating race to race; player never DNQs; fold-inert. `tools/author_smgp.cs`, `SmgpDnqFieldTests`.
- **Dynamic per-rival quotes**: `data/rules/smgp/rival-quotes.json` (all 34 drivers, 3 ladder
  moods) via `Companion.Core.Smgp.SmgpRivalQuotes`; `CurrentSmgpBriefing` picks by tally + FNV seed.
- **Rival dossier redesign**: portrait hero + car thumbnail + centered copy (BriefingView).
- **Per-team player image** `player.<team>` (team helmet) on the Season's Grid "YOU" card + the
  character screen (`GridSeatChoice.PortraitKey`, `NewCareerWizardViewModel.PlayerImageKey`);
  README at `data/ams2/portraits/README.md`.
- **Wizard flow** = select car → **create character → Season Grid** → confirm.
- **Season Calendar** = a 4-in-a-row grid board (`CalendarView`) with circuit maps + facts.
- **Season 2+ variety** (`SmgpSeasonVariety`): shuffled calendar + fresh weather each year, Monaco
  finale kept; fold-inert, replay-safe.
- **Fonts**: BodyFont = **Roboto** (app-wide, Apache-2.0); Microsport = Season's Grid cards; Race
  Sport = season/career picker; Open Sans = character screen. (Retro Floral/Aeromove retired.)

## THE OPTIMIZATION QUEUE (Mike's asks — pick a slice, do it in slices)

### ⭐ A. AUTO-ACTIVATE the round's skins — "there HAS to be a way!" (Mike, MAXIMUM RESEARCH, MAXIMUM POWER)
The SMGP skins ship **INSTALLED — NOT ACTIVE** (letter/`##`-placeholder livery slots — e.g. Lares
#23, Feet #24, and the reserves). Today the smart-binding FLOORS a not-active livery to a
guaranteed base-game paint, and the user must hand-activate in the Skins tab. Mike wants it
**AUTOMATIC**: for each round, activate exactly the round's ≤26 **qualifiers'** skins (the DNQ field
already picks them — `grid.starterDriverIds`), so the grid shows the real painted cars, and the
9-ish DNQ'd cars' slots stay inert. Ties directly into the 34-skins-vs-26-livery-cap problem
(deferred "slice 3" in `ams2-next-content-arc.md`).
- **RESEARCH (fan out a workflow, be exhaustive):** how AMS2 loads active custom liveries at
  SESSION START vs the `Overrides\<model>\<model>.xml` files; whether rewriting those override files
  to mark the round's 26 qualifiers ACTIVE (and the rest inactive) BEFORE the player launches the
  race is enough to swap the active set per round **without a game restart** (the memory says a
  freshly-written slot needs a restart — CONFIRM/REFUTE this against the real install + a live
  test); the exact meaning of the `##` / letter-`X` placeholder slots and slot numbering (custom
  slots run 51..(50+cap)); whether the class livery cap 26 is a hard render limit or just distinct
  names. Read `src/Companion.Ams2/CustomAi/LiveryOverrideWriter.cs` (Activate exists, cap-safe,
  backup-first), `Grid/GridStager.cs` (writes livery_name), `Grid/BaseGameLiveryBinder.cs` (the
  floor), `SkinSeasonManager`, and the installed `Y:\SteamLibrary\...\Vehicles\Textures\
  CustomLiveries\Overrides\`. Prior context: `8ae0aff` (staging auto-activation, later REMOVED in
  `9673a81` because auto-activating a placeholder wrote a slot AMS2 hadn't loaded → pool-fill +
  restart). The goal now is DIFFERENT: activate the ROUND'S QUALIFIERS deliberately at staging,
  which is Mike's sanctioned per-race swap. Determine the safe mechanism, then build it
  (per-race livery staging), tests + a live in-game verify.

### ⭐ B. SMGP-universe "WHAT REALLY HAPPENED" — per race, unlocked when you finish it (Mike #3)
The History/Scrapbook already gates OFF the real-F1 "what really happened" for smgp packs (the
fictional SEGA world must not reveal real 1990 races). SMGP needs its **OWN** version: after you
apply/enter the result of a race, that race's **"What really happened"** unlocks and tells the
**SMGP universe's** account of the race you just finished (its fictional history — who the SEGA
world remembers winning there, the rivalry beats, the SMGP lore), NOT real F1. Design: author
per-round SMGP history narratives (data file, e.g. `data/rules/smgp/what-really-happened.json` keyed
by round), gated to reveal per-round once `MaxAppliedRound >= round` (read-side, no fold change —
like the news corpora). Wire into the History/Scrapbook view for smgp careers. Fan out the
narrative authoring (SEGA-arcade voice, matches `data/rules/news/smgp.json`).

### C. PER-TEAM CAR SPECS card — the SMGP car-select screen (waiting on Mike's numbers)
Mike will provide each team's **machine name / engine / max power / the ENG·T.M.·SUS·TIRE·BRA
bars** (the classic SMGP car-select screen he sent as concept). When he does: add a spec panel to
BOTH the **character screen** and the **rival screen**, beside the player image + car. Scaffold the
layout now if useful (bars + labels), fill the data when it lands. Likely a per-team data file
`data/ams2/smgp/car-specs.json` + a small stats-bar control.

### D. SMGP NEWS PHASE 2 — rival-event headlines (makes the seat change VISIBLE)
Read-side: extend `CareerSessionService.ReadFeed` to read the folded `smgp.seat`/`smgp.battle`/
career-over journal events and compose SMGP-flavored articles ("PLAYER SEIZES THE MADONNA SEAT!").
Add a `{rival}` token to `NewsFacts` (+ `FactTokens` in `NewsCorpusGuardTests`) and
`smgp.seat|swap|forfeit`/`smgp.title`/`smgp.career` article types to `smgp.json`. No fold change.

### E. Deferred SMGP tail
Per-round pit-crew advice (still the constant "PASS THE CARS AT THE HAIRPIN TURN!"); CareerOver
hard-stop UX; reshuffle-by-points between seasons; random AI-initiated challenges; two-titles
celebration UI.

## Mike's manual art drops (pending — the app shows framed placeholders until then)
- **Player images per team**: `data/ams2/portraits/player.<team>.jpg` (see the README; the yellow
  helmet = `player.minarae`).
- **Rival/driver portraits**: `data/ams2/portraits/driver.<id>.jpg` (car previews already extract).
- Car-spec numbers for card C.
