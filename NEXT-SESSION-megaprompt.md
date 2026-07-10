# NEXT SESSION — skins everywhere → max grids → SMGP replica → beautification

Resume the AMS2 Career Companion (`Z:\Claude Code\ams2-career-companion` — WPF/.NET 10, single
self-contained exe). **READ MEMORY FIRST** (`MEMORY.md`, then `ams2-hub-build-progress.md` TOP),
then the grounding docs this plan is built on:

- `docs/dev/audits/audit-skins-rollout.md` — the 20-archive inventory, install state, conflict
  mechanism, race-by-race variant discovery, 1985 diagnosis, blue-flag options, juppo schema.
- `docs/dev/smgp-design.md` — the verified Super Monaco GP replica design (manual-sourced).
- Extracted pack metadata: `scratchpad/skins-study/` (per-archive override XMLs + AI files).

## STATE (verify with `git log`)

Branch `hub/increment-4`, pushed through **`dfecdb9`** (+ SMGP design doc after). RC
`dist/AMS2CareerCompanion.exe` = 0.6.0+1e1b992-era code + calendar fixes `137e78c` + juppo 1991
`ae8276f` — REPUBLISH when app is closed. Suite 1587 + 38 render; oracle 77/77. Mike is still
installing/uploading skin packs (newer ones unfinished). Mike's blue-flag decision is PENDING —
options in audit §(g); do not write blue-flag code until he picks.

## MISSION 1 — Skins foundation (sequential; everything else stands on it)

1. **Comment-tolerant community-XML parsing everywhere** (the 1985 pack's `--` comments break
   strict parsers): audit every reader of `Overrides\<model>\*.xml` (Skins lens scanner, livery
   activator) and route them through one comment-stripping loader (regex like import_jusk_ai.cs).
2. **Skin Season Manager**: per-model season swap of `Overrides\<model>\<model>.xml`
   (backup-first + marker, the AI-staging contract). Career load/stage applies the season set the
   pack declares (`skinSeason` key on the pack manifest → which variant XML family to activate).
   Unlocks: 1985 (vs 1983), 1975 (vs 1974), 1996 (vs 1997), 2010 (vs 2012), SMGP (vs 1990).
   Settings/Skins UI: show per-model which season is active; one-click swap; never touch
   unmarked user files without the force gate.
3. **Race-by-race variant binding**: at round staging, for each class model find the pack's
   per-race variant XML (`<model>_<token>.xml`, token-matched to the round venue like
   import_jusk_ai's mapping) and activate it (backup-first). Restore/next-swap next round. The
   packs shipping variants: 1986, 1990, 1991, 1992, 1993, 1995, 1996, 1997, 1998, 2010, 2012,
   2016, 2020 (list per model in the audit).
4. **Livery auto-activation** already exists (cap-aware activator) — extend it to activate a
   pack-declared ACTIVE SET (e.g. 1985's 10 slots swapped for the round's entry list).

## MISSION 2 — Max grids + skinpack rosters, every season (ultracode-parallel per pack)

Per the audit §(d), for EVERY bundled season (1988 is the finished template): canonicalize
entries.json to one-driver-per-seat bound to the installed skinpack's livery names; regenerate
every round's grid.starterDriverIds to the FULL cap-aware roster (no 10-car Kyalami — Mike's
rule: who's in the season = who's in the skinpack); player picking a non-default car replaces the
slowest seat at staging (generalize the 1988 mechanism). Where the pack ships its own
CustomAIDrivers XML (1986, 1995, 1996, 1997, 1998, 2010, 2012, 2016 — prefer "Realistic"
variants), import it via import_jusk_ai.cs as the ratings source. SEQUENTIAL PRE-SLICE first:
extend PackDriverRatings + aiOverrides + the staged-XML writer with juppo's scalar fields
(drag/power/weight scalars, setup_downforce±randomness, fuel_management) — sim-inert staging
data — then re-import Juppo 1991 to pick up his 49 per-track car-balance blocks. Pack data
changes = NEW careers only; keep suite + oracle green per pack; commit per pack.

## MISSION 3 — SUPER MONACO GP replica mode (the fun one; accuracy per smgp-design.md)

Build `packs/smgp-1` + the `careerStyle: "smgp"` mode: 16 country-named rounds in the game's
order (San Marino → Monaco finale), 9-6-4-3-2-1 no drops, 16 one-driver teams in LEVEL A–D
tiers, rosters + ratings from the SMGP skinpack's own CustomAIDrivers XML (apply the three
verified roster corrections), rival pick/forced-challenge system with the exact two-wins seat-swap
+ one-tier displacement rules, Zeroforce career-over, Madonna title-defense with the G. Ceara
R1+R2 event, two-titles completion. New fold rows = envelope-versioned + determinism-gated (the
called-shot precedent). Presentation strictly from the design doc's sourced vocabulary
("PRELIMINARY RACE", rival dossier cards, pit-crew advice lines, D.P., MINARAE intro) + user
asset slots under `data/ams2/smgp/`. Depends on M1's season manager (SMGP conflicts with 1990).

## MISSION 4 — Beautification + main menu (Mike's GUI vision)

- **Main menu before the career gallery**: a proper landing screen (New career / Continue /
  Modes incl. SMGP / Settings), background art slot, the app's first "front door".
- **Theme templates**: a few complete two-tone background+accent themes (F1-inspired), selectable
  in Settings, driven by user assets (`data/ams2/themes/<name>/…` — background images, accent
  pair, panel tint) reaching DEEP into the app (every view's Panel/Bg brushes, not just accents).
- **Career-gallery polish**: bring the recent-careers cards to the season-picker's standard
  (UniformGrid + AspectHeight hero, adaptive columns).
- **More tactile feel**: extend MotionAssist (press springs/ripples exist) with hover glows on
  cards, springy expanders, subtle parallax on hero images — restraint over spectacle.
- Document every asset slot in one place (Settings → "User art" panel listing folders).

## BACKLOG (after M1–M4)

New seasons from the directory: 1983 (F-Retro_Gen3 + TAMS2SP), 1996 (F-V10_Gen1 HC), 1998
(F-V10_Gen2 HC), 2010 + 2012 (F-Reiza — new class, needs class profile + caps), 1975 (needs M1
manager). JGTC 500 + Ferrari 355/488 Challenge: mod-content discovery in `Z:\RCM MODS AMS2` +
RCM/OverTake first (AMS2 has no base Ferrari/JGTC content), then normal pack building with
alternate-track-style install verification. Blue flags: implement whichever of audit §(g)'s
options Mike picks.

## CONSTRAINTS (unchanged, load-bearing)

CRLF+2-space+no-BOM pack files; sim-inert vs determinism-gated discipline (grid/roster changes =
pack data = new careers only; new fold rows = envelope version + gate); NEVER touch the oracle;
no `git add -A`; era-art/venue-photos/user assets never committed; republish exe only app-closed
(timestamped backup); commit every slice; no gh CLI (PR #4 is Mike's).
