# CONTINUE — resume prompt (paste into a new session)

Resume the **AMS2 Career Companion** (`Z:\Claude Code\ams2-career-companion`, WPF / .NET 10, single
self-contained exe, branch **`hub/increment-4`**, head **`b49c000`**). Two agents work this repo in
parallel: **Claude = SMGP mode + the app**, **Codex = the 1967 F1 era** (its own worktree/branch
`era/1967` — see `CODEX-1967-BRIEF.md`; its work merges into `hub/increment-4` at clean points).

## First: orient
1. **Read memory**: `MEMORY.md` → `ams2-hub-build-progress.md` (TOP blocks = current, read the first
   3-4) → `ams2-smgp-skin-install.md` → `ams2-next-content-arc.md` → `mike-build-maximally.md`.
2. **Verify the repo**: `git log --oneline -8`, `git status`. Treat every path/line below as a hint to
   confirm; the repo moves fast (a babysit/auto-finalize pipeline also commits/pushes on its own).
3. **Discipline (non-negotiable)**: keep the full suite + RenderHarness green (`dotnet test
   Companion.slnx` — **1811 + 46**); **f1db oracle 77/77 NEVER touched**; **byte-identical replay is a
   LOCKED invariant** (envelope-versioned + per-career gated; pack/grid changes NEW-careers-only);
   commit + push per slice; republish (`dotnet publish src/Companion.App -c Release`) only when the app
   is CLOSED, backup-first (`dist/AMS2CareerCompanion.exe.old-<ts>`), sync only changed data. No `gh`
   CLI — PRs via the cached git cred + `Z:\Claude Code\open-pr.ps1`. **Art is now TRACKED** in git
   (`data/ams2/{era-art,portraits}`, source of truth synced to dist) — see `data/ams2/ART-INVENTORY.md`.

## What is DONE and shipped (head `b49c000`)
- **SMGP CLEAN SEAT-SWAP** (`f277a95`): the player is their OWN distinct driver
  (`RoundGridResolver.SyntheticPlayerDriverId`) — the car's AI benches while they hold it and returns
  when they move on; no cascade, `AiSeatOverrides` always empty; NEW smgp careers only. Fixed the
  byte-identical-replay blocker in `ReplayService.SeasonPlayerDriverId`; `PlayerDisplayName()` seam so
  the player never renders as the benched AI or the raw id.
- **SMGP IN-GAME SKINS / POOL-FILL — FIXED + CONFIRMED IN-GAME** (`6c2df67`, "phase 1"). RCM had
  stripped Mike's SMGP+McLaren mod → reinstalled from the archives (see `ams2-smgp-skin-install`). The
  pool-fill (AMS2 stock-filling model slots with "Gino Mandelli" etc.) is beaten: AMS2's grid generator
  picks base-livery SLOTS on its own, so staging now names EVERY livery it can field — all 34 SMGP
  customs (incl. the per-race DNQ tail via `RoundGridResolver.Resolve(ignoreStarters:true)`) + every
  base-game class livery from `official-liveries.json`, mapped weakest-first. **Mike's in-game test: all
  26 cars now show SMGP driver names, zero stock.** Cosmetic staging only; oracle/replay untouched.
- **Almanac** (`ff62523`): SMGP "What Really Happened" per-race history on the History tab (smgp-gated).
- **Dynamic per-race DNQ** (`87bc627`): 34-car field → ~26 grid, baked deterministic starterDriverIds.
- Plus: rival dossier + dynamic quotes, per-team player image, wizard flow, calendar grid, fonts, etc.
- **Codex's 1967** merged (`b49c000`): 1960s news depth, f1-1967 source parity, history/provenance +
  new guard/parity tests. Clean lane separation (no SMGP/shared-code touched).

## SMGP OPEN ITEMS (pick per Mike)
- **⭐ THE CHARACTER CREATOR / finish increment 4** — Mike's stated next want ("i want to really work on
  the character creator and finish increment 4"). The character system is built (`character-system.md`,
  632 lines; `CharacterView`, the wizard character step, 33+ perks). Survey what's done vs the design +
  propose the remaining work to "finish". Likely the real next focus.
- **Skins PHASE 2 (optional, cosmetic)** — the ~5 base-slot cars show SMGP NAMES on DEFAULT paint (not
  their SMGP skin). Full paint needs the authoritative livery map from a one-time **`-showliveryIDs`**
  AMS2 launch (dumps to `~/Documents/Automobilista 2/log/`) → complete `official-liveries.json` + override
  every base slot. Parked until Mike does that launch; low priority (names already correct).
- **SMGP news Phase 2 (item D)** — read-side `ReadFeed` projects the folded `smgp.seat`/`smgp.battle`/
  `smgp.title` journal rows into headlines ("YOU SEIZE THE MADONNA SEAT!"). Scoped in
  `ams2-next-content-arc`; needs a `{rival}` token on NewsFacts + smgp article types in `smgp.json`.
- **Per-team CAR SPECS card (item C)** — machine/engine/power + ENG-TM-SUS-TIRE-BRA bars on the
  character + rival screens. Blocked on Mike's numbers.
- **11 missing `player.<team>` art** (ART-INVENTORY): bestowal/blanche/bullets/dardan/feet/joke/
  linden/losel/may/millions/tyrant — Mike's manual drops into `data/ams2/portraits/`.
