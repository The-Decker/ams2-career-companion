# NEXT SESSION — beautification (M4), anchored on making SMGP its own front door

Resume the AMS2 Career Companion (`Z:\Claude Code\ams2-career-companion` — WPF/.NET 10, single
self-contained exe). **READ MEMORY FIRST** (`MEMORY.md`, then `ams2-hub-build-progress.md` TOP —
the 2026-07-10 blocks are current), then `docs/dev/smgp-design.md` (the SMGP mode, now COMPLETE).

## STATE (verify with `git log`)

Branch `hub/increment-4`, pushed, ~220 commits ahead of `main`. Suite **1716 + 40 render green;
oracle 77/77 UNTOUCHED**. RC `dist/AMS2CareerCompanion.exe` republished (see memory for the exact
head/hash). **The SMGP replica mode is COMPLETE end-to-end** — folded state, rival battles + the
YES-confirm dossier, seat swaps, the Ceara title defense, Zeroforce game-over, the 26-car field
(24 base + the opt-in Iris & Azalea McLaren-mod tick), D-only rookie starts, correct 1989-modelled
circuits. The Setup Gamble panel's shipped visibility inversion is fixed. The phantom `azure_circuit_88`
is gone from all 17 packs.

## MISSION 4 — beautification (open-ended; Mike wants eyes on the aesthetics, so go slice-by-slice
and show him)

The through-line is the **main-menu landing screen** — the app's first "front door", before the
career gallery. It is also where the two locked directions land:

- **SMGP is a SEPARATE CAREER ENTITY** (Mike, locked — see CLAUDE.md + smgp-design.md). The landing
  must present it as its own mode: **New career · Continue · Modes (→ Super Monaco GP) · Settings**,
  with a background-art slot. SMGP should NOT sit in the historical-season picker as just another
  pack — split it out. (The wizard already gates all the smgp behaviour on `careerStyle == "smgp"`;
  this is the UI front door for it.)
- **Senna is always OP** in SMGP (the one to beat) — a flavour beat the mode screen can lean on.

Then the rest of M4, each its own commit, in order:
(a) **main-menu landing** (above) — the biggest piece; do it first.
(b) **career-gallery card parity** with the season picker (UniformGrid + AspectHeight hero, adaptive
    columns — reuse `WidthToColumns`/`AspectHeight`).
(c) **theme templates** (`data/ams2/themes/<name>/`, two-tone F1 background + accent reaching
    Panel/Bg brushes, not just accents; a couple of complete themes, Settings-selectable).
(d) **MotionAssist extensions** (hover glows on cards, springy expanders, subtle hero parallax —
    restraint over spectacle).
(e) a Settings **"User art"** panel listing every drop-in asset folder (era-art, portraits, cars,
    smgp banners/rounds, track-art, venue-photos, themes) with the exact path + key format each.

## SMGP era-art (small, do it early — it's been deferred twice)

The season-picker/gallery era image is keyed by a YEAR parsed from the pack title; "Super Monaco GP"
has no year, and its season year 1990 collides with the real F1 1990 pack. Add a **pack-id-keyed
fallback** to `EraArtResolver`/`EraImageConverter` (check `data/ams2/era-art/<packId>.jpg` — e.g.
`smgp-1.jpg` — before the year), and thread it through the picker (Title→pack) + the gallery
(stored pack id). ~20 min.

## CONSTRAINTS (unchanged, load-bearing)

CRLF+2-space+no-BOM pack/data files; sim-inert vs determinism-gated discipline (new fold rows =
envelope version + per-career gate; grid/roster changes = pack data = new careers only); NEVER
touch the oracle; no `git add -A` (stage named paths); era-art/venue-photos/user assets never
committed; **republish the exe only when the app is closed** (timestamped backup;
`dotnet publish src/Companion.App -c Release -o dist`, then sync changed pack files into `dist/packs`);
commit AND push every slice; no `gh` CLI (PR #4 is Mike's).

## LEFTOVERS (only if M4 lands + budget remains)

The M1 (skins-foundation) adversarial review never closed (usage-limit death). Deferred SMGP tail:
CareerOver hard-stop UX, reshuffle-by-points between seasons, random AI-initiated challenges,
per-round pit-crew advice + per-rival quote DATA files (currently two constants). Backlog seasons
(1983/1996/1998/2010/2012) and 1975-via-manager remain parked.
