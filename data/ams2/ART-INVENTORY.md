# Art asset inventory

The manifest of every user-art asset the app resolves — **what's present and what's still missing**.
The live files under **`dist/data/ams2/` are the canonical art source**. Anything in `dist` wins:
never overwrite it from the tracked `data/ams2/` tree. Tracked files may be useful references, but
they are not authoritative art and can lag behind the runtime collection.

Regenerate the present/expected counts any time:
```powershell
(Get-ChildItem dist/data/ams2/era-art -Filter *.jpg).Count
(Get-ChildItem dist/data/ams2/portraits -Filter driver.*.jpg).Count
(Get-ChildItem dist/data/ams2/portraits -Filter player.*.jpg).Count
(Get-ChildItem dist/data/ams2/cars -Filter driver.*.png).Count
```

## Summary

| Category | Present | Expected | Status |
|---|---|---|---|
| **era-art** (`<year>.jpg` / `smgp.jpg`) | 20 | 20 (one per pack) | ✅ complete |
| **driver portraits** (`driver.<id>.jpg`) | 34 | 34 (SMGP roster) | ✅ complete |
| **per-team player images** (`player.<team>.jpg`) | 24 | 24 (SMGP teams) | ✅ complete |
| **car previews** (`cars/<driverId>.png`) | 34 | 34 | ✅ complete; extractor writes directly to canonical `dist` |
| **SMGP grid-car miniatures** (`smgp/grid-cars/<driverId>.png`) | 0 | 34 optional | ○ side previews provide the live fallback |
| **team logos** (`smgp/logos/team.<team>.png`) | 0 | 24 | ○ waiting for the final team palette |
| **round cards** (`smgp/rounds/<round>.jpg`) | 0 | 16 | ○ missing |
| **team photos** (`smgp/teams/<team>.jpg`) | 0 | 24 | ○ missing |
| **track-art** (`<trackId>.jpg`) | 0 | optional (one per AMS2 track id) | ○ none — optional drop-in, clean fallback |
| **history-art** (`<year>.jpg`) | 0 | optional (one per season year) | ○ none — optional drop-in, clean fallback |

## era-art — ✅ 20/20

One per pack, keyed by year (`smgp-1` → `smgp.jpg`). All present:
1967, 1969, 1974, 1978, 1985, 1986, 1988, 1990, 1991, 1992, 1993, 1995, 1997, 2000, 2005, 2006,
2008, 2016, 2020, smgp.

## Driver portraits — ✅ 34/34

Every SMGP roster driver has `portraits/driver.<id>.jpg`. (Real-F1 driver portraits are not part of
the current design — the History tab uses era-art + optional history-art.)

## Per-team player images — ✅ 24/24

`player.<team>.jpg` = the team-coloured player helmet shown on the Season's Grid "YOU" card + the
character screen (team id minus the `team.` prefix). **Present (24):** azalea, comet, cool, firenze,
iris, lares, madonna, minarae, moon, orchis, rigel, serga, zeroforce, bestowal, blanche, bullets,
dardan, feet, joke, linden, losel, may, millions, and tyrant.

## SMGP grid-car miniatures — 0/34 (optional upgrade)

The SMGP pixel starting straight first resolves `smgp/grid-cars/<driverId>.png`, then falls back to
the complete canonical `cars/<driverId>.png` set. Purpose-built miniatures should be 384×256
transparent RGBA PNGs with consistent framing, a three-quarter overhead arcade view, and the nose
pointing right to match the live side-preview fallback. Keep them driver-keyed: the 34 entries use
five body silhouettes, and teammate liveries/numbers can differ. Never replace the canonical side
previews to add these.

## track-art / history-art — ○ optional, none yet

Both are drop-in user assets with a clean fallback (no image → the panel simply omits it), so they
are not "missing" in the blocking sense. To add: `track-art/<trackId>.jpg` (track id from
`data/ams2/tracks.json`) shows on the Race Day briefing; `history-art/<year>.jpg` shows on the
History tab's "what really happened" panel. Populate if/when we want that reference content to feel
alive.

## How to add art

Drop the file directly into **`dist/data/ams2/<folder>/`** beside the app and keep the naming
convention above. Do not publish, sync, or copy a tracked `data/ams2/` version over a file in
`dist`. If a secondary archive or tracked reference is ever wanted, copy outward from canonical
`dist` only and preserve the `dist` file byte-for-byte.
