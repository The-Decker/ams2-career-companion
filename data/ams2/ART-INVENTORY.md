# Art asset inventory

The manifest of every user-art asset the app resolves — **what's present and what's still missing**.
As of **2026-07-11** these assets are now **tracked in git** (previously drop-in-only under `dist/`);
`data/ams2/` is the source of truth, synced to `dist/data/ams2/` beside the app at publish time.

Regenerate the present/expected counts any time:
```
ls data/ams2/era-art/*.jpg | wc -l          # era-art present
ls data/ams2/portraits/driver.*.jpg | wc -l # driver portraits present
ls data/ams2/portraits/player.*.jpg | wc -l # per-team player images present
```

## Summary

| Category | Present | Expected | Status |
|---|---|---|---|
| **era-art** (`<year>.jpg` / `smgp.jpg`) | 20 | 20 (one per pack) | ✅ complete |
| **driver portraits** (`driver.<id>.jpg`) | 34 | 34 (SMGP roster) | ✅ complete |
| **per-team player images** (`player.<team>.jpg`) | 13 | 24 (SMGP teams) | ◐ **11 missing** |
| **car previews** (`cars/<driverId>.png`) | 34 | 34 | ✅ auto-generated (`tools/extract_car_previews.cs`) — not tracked, regenerable |
| **track-art** (`<trackId>.jpg`) | 0 | optional (one per AMS2 track id) | ○ none — optional drop-in, clean fallback |
| **history-art** (`<year>.jpg`) | 0 | optional (one per season year) | ○ none — optional drop-in, clean fallback |

## era-art — ✅ 20/20

One per pack, keyed by year (`smgp-1` → `smgp.jpg`). All present:
1967, 1969, 1974, 1978, 1985, 1986, 1988, 1990, 1991, 1992, 1993, 1995, 1997, 2000, 2005, 2006,
2008, 2016, 2020, smgp.

## Driver portraits — ✅ 34/34

Every SMGP roster driver has `portraits/driver.<id>.jpg`. (Real-F1 driver portraits are not part of
the current design — the History tab uses era-art + optional history-art.)

## Per-team player images — ◐ 13/24 (11 MISSING)

`player.<team>.jpg` = the team-coloured player helmet shown on the Season's Grid "YOU" card + the
character screen (team id minus the `team.` prefix). **Present (13):** azalea, comet, cool, firenze,
iris, lares, madonna, minarae, moon, orchis, rigel, serga, zeroforce.

**MISSING (11) — need to be added:**
`player.bestowal`, `player.blanche`, `player.bullets`, `player.dardan`, `player.feet`,
`player.joke`, `player.linden`, `player.losel`, `player.may`, `player.millions`, `player.tyrant`.

## track-art / history-art — ○ optional, none yet

Both are drop-in user assets with a clean fallback (no image → the panel simply omits it), so they
are not "missing" in the blocking sense. To add: `track-art/<trackId>.jpg` (track id from
`data/ams2/tracks.json`) shows on the Race Day briefing; `history-art/<year>.jpg` shows on the
History tab's "what really happened" panel. Populate if/when we want that reference content to feel
alive.

## How to add art (now that it's tracked)

Drop the file into the matching **`data/ams2/<folder>/`** (the tracked source, NOT `dist/`), keep the
naming convention above, then `git add` it and re-sync to `dist/` at the next publish. Update the
counts in this file when a gap is filled.
