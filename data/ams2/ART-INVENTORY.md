# Art asset inventory

The manifest of every user-art asset the app resolves — **what's present and what's still missing**.
The live files under **`dist/data/ams2/` are the canonical art source**. Anything in `dist` wins:
never overwrite it from the tracked `data/ams2/` tree. Tracked files may be useful references, but
they are not authoritative art and can lag behind the runtime collection.

> **Promoted to the tracked tree — 2026-07-12** (Mike: "all art from dist can go live"): the SMGP art
> collection is now COMPLETE in `dist/`, and the whole set (`cars/`, `smgp/` — grid-cars/logos/rounds/
> teams/sponsors/finale/flags — and all portraits) has been copied into the tracked `data/ams2/` tree
> and committed, so it is version-controlled. `dist/` stays canonical: keep dropping NEW art there
> (never overwrite `dist` from the tracked tree) and re-promote outward when it's approved.

Regenerate the present/expected counts any time:
```powershell
(Get-ChildItem dist/data/ams2/era-art -Filter *.jpg).Count
(Get-ChildItem dist/data/ams2/portraits -Filter driver.*.jpg).Count
(Get-ChildItem dist/data/ams2/portraits -Filter player.*.jpg).Count
(Get-ChildItem dist/data/ams2/cars -Filter driver.*.png).Count
(Get-ChildItem dist/data/ams2/smgp/flags -Filter driver.*.png).Count
(Get-ChildItem dist/data/ams2/smgp/flags -Filter country.*.png).Count
```

## Summary

| Category | Present | Expected | Status |
|---|---|---|---|
| **era-art** (`<year>.jpg` / `smgp.jpg` / `<medium>.jpg`) | 25 | 25 (22 packs + 3 medium fallbacks) | ✅ complete |
| **driver portraits** (`driver.<id>.jpg`) | 34 | 34 (SMGP roster) | ✅ complete |
| **per-team player images** (`player.<team>.jpg`) | 24 | 24 (SMGP teams) | ✅ complete |
| **car previews** (`cars/<driverId>.png`) | 34 | 34 | ✅ complete; extractor writes directly to canonical `dist` |
| **SMGP national flags** (`smgp/flags/<driverId>.png`) | 34 | 34 | ✅ complete; converted locally from installed AMS2 country flags |
| **character nationality flags** (`smgp/flags/country.<slug>.png`) | 200 | 200 | ✅ complete; every selectable installed AMS2 country flag |
| **SMGP grid-car miniatures** (`smgp/grid-cars/<driverId>.png`) | 34 | 34 | ✅ complete |
| **team logos** (`smgp/logos/team.<team>.png`) | 24 | 24 | ✅ complete |
| **round cards** (`smgp/rounds/<round>.jpg`) | 16 | 16 | ✅ complete |
| **team photos** (`smgp/teams/<team>.jpg`) | 24 | 24 | ✅ complete |
| **rival banners** (`smgp/banners/team.<team>.png`) | 24 | 24 | ✅ complete; exact 1040×200 hero aspect |
| **sponsor logos** (`smgp/sponsors/<id>.png`) | 27 | 27 | ✅ complete |
| **campaign finale secrets** (`smgp/finale/special.jpg`, `ultimate.jpg`) | 2 | 2 | ✅ present — Mike's 17-season reward images |
| **embedded track banners** (`src/Companion.App/Assets/TrackBanners`) | 304 ids / 173 masters | every AMS2 track id | ✅ complete; 1920×440 |
| **track-art** (`<trackId>.jpg`) | 0 | optional (one per AMS2 track id) | ○ none — optional drop-in, clean fallback |
| **history-art** (`<year>.jpg`) | 0 | optional (one per season year) | ○ none — optional drop-in, clean fallback |

## era-art: 22 year photos + 3 era-medium fallbacks (25 total)

One per pack, keyed by year (`smgp-1` → `smgp.jpg`), plus the three era-medium fallbacks the resolver uses for any season year without its own photo (`EraArtResolver`: a year file wins, then the medium file for the year's era, telegram ≤ 1979, fax 1980-1993, email 1994+). All present:
1967, 1969, 1974, 1978, 1983, 1985, 1986, 1988, 1990, 1991, 1992, 1993, 1995, 1997, 2000, 2005,
2006, 2008, 2010, 2016, 2020, smgp, telegram, fax, email. The three medium fallbacks (`telegram.jpg` / `fax.jpg` / `email.jpg`, 1280×720, subject centered) are original generated flat-art compositions (telegraph key on ochre wire paper, thermal sheet out of a fax slot, an envelope over inbox rows), no external or copyrighted source material.

## Driver portraits — ✅ 34/34

Every SMGP roster driver has `portraits/driver.<id>.jpg`. (Real-F1 driver portraits are not part of
the current design — the History tab uses era-art + optional history-art.)

## Per-team player images — ✅ 24/24

`player.<team>.jpg` = the team-coloured player helmet shown on the Season's Grid "YOU" card + the
character screen (team id minus the `team.` prefix). **Present (24):** azalea, comet, cool, firenze,
iris, lares, madonna, minarae, moon, orchis, rigel, serga, zeroforce, bestowal, blanche, bullets,
dardan, feet, joke, linden, losel, may, millions, and tyrant.

## SMGP national flags — ✅ 34/34

The SMGP starting grid resolves `smgp/flags/<driverId>.png` so nationality remains an art-only,
driver-keyed concern and does not duplicate roster logic in the ViewModel. The 34 canonical PNGs
were converted locally from the matching 128×128 `GUI/CountryFlags/Flag_*.dds` files in Mike's AMS2
installation. Keep this `driver.*` compatibility set for authored AI-grid entries; the player's
new explicit nationality resolves through the separate `country.*` keyspace below.

## Character nationality flags — ✅ 200/200

`smgp/flags/country.<slug>.png` is the complete selectable character-country keyspace. The 200
128×128 RGBA PNGs were converted losslessly from the local AMS2 `GUI/CountryFlags/Flag_*.dds` set,
preserving each installed slug exactly and excluding the non-country `Flag_cancel.dds`. The
character creator binds these keys through `CountryOptions`; `driver.*` remains intact for AI-grid
compatibility.

## SMGP grid-car miniatures — ✅ 34/34 (updated 2026-07-13)

The SMGP pixel starting straight first resolves `smgp/grid-cars/<driverId>.png`, then falls back to
the complete canonical `cars/<driverId>.png` set. Purpose-built miniatures are 384×512 transparent
RGBA PNGs with consistent portrait framing and an overhead arcade view. The live grid rotates them
90° clockwise and scales them to roughly 220×150, leaving the source reusable elsewhere. Keep them
driver-keyed: the 34 entries use five body silhouettes, and teammate liveries/numbers can differ.
Never replace the canonical side previews to add these.

## SMGP team art — ✅ complete (dropped 2026-07-12)

The Paddock / Tycoon team surfaces resolve, all now present:
- **`smgp/logos/team.<team>.png`** — 24/24 team logos (the sponsor board + team cards).
- **`smgp/teams/<team>.jpg`** — 24/24 team photos (the Paddock team detail + promotion screen).
- **`smgp/rounds/<round>.jpg`** — 16/16 round cards (the calendar / upcoming-race hero).
- **`smgp/sponsors/<id>.png`** — 27/27 sponsor logos (the Paddock Sponsors tab; `data/rules/smgp/sponsors.json`).
- **`smgp/banners/team.<team>.png`** — 24/24 rival dossier heroes at the exact 1040×200 window aspect.

All absent-tolerant (a missing file falls back to a coloured placeholder). Keep dropping new/updated
art into `dist/data/ams2/smgp/<folder>/` and re-promote to the tracked tree when approved.

## SMGP campaign finale — ✅ 2/2 present (secret reward images)

The 17-season grand campaign's "final final screen" (Mike's spec, `docs/dev/smgp-17-seasons.md`) is
built around two **secret** hero images that the app loads ONLY on the finale screen and ONLY once
the campaign is beaten — the `HeroImageKey` is emitted solely by `CareerSessionService.SmgpFinale()`
when the unlock predicate holds, so no other screen ever binds them:

- **`smgp/finale/special.jpg`** — unlocked by COMPLETING all 17 seasons (surviving to the end without
  the career ending on the Level-D floor). "It's so special that no one can access it until you beat
  all 17."
- **`smgp/finale/ultimate.jpg`** — the deeper secret: unlocked only by being CHAMPION in all 17
  seasons (a flawless 17-from-17 run). Almost no one will ever see it — that's the point.

Both are absent-tolerant: a missing file just shows a sealed-vault placeholder on the finale screen
(the UNLOCK is the achievement, the image is the payoff). Drop them into
`dist/data/ams2/smgp/finale/` like every other art asset.

## track-art / history-art — ○ optional, none yet

Both are drop-in user assets with a clean fallback (no image → the panel simply omits it), so they
are not "missing" in the blocking sense. To add: `track-art/<trackId>.jpg` (track id from
`data/ams2/tracks.json`) shows on the Race Day briefing; `history-art/<year>.jpg` shows on the
History tab's "what really happened" panel. Populate if/when we want that reference content to feel
alive.

The Calendar is already fully illustrated independently of those optional loose files: its embedded
`TrackBanners/manifest.json` maps every current AMS2 track/layout id to one of 173 exact 1920×440
panoramic venue masters. Layout variants may intentionally share a venue master.

## How to add art

Drop the file directly into **`dist/data/ams2/<folder>/`** beside the app and keep the naming
convention above. Do not publish, sync, or copy a tracked `data/ams2/` version over a file in
`dist`. If a secondary archive or tracked reference is ever wanted, copy outward from canonical
`dist` only and preserve the `dist` file byte-for-byte.
