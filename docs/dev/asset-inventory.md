# SMGP Art Asset Inventory

**Owner:** Codex (GUI/art) · **Last audited:** 2026-07-12 by Claude (from a code + disk scan) ·
**Roster:** 34 drivers, 24 teams, 16 rounds — ids in `packs/smgp-1/{drivers,teams,entries}.json`.

Keep this file current: it's the single source of truth for what art the app expects and what's still
missing. When you drop assets, tick them off here.

## How the app loads art (drop-in, no rebuild)

`{exe}\data\ams2\<kind>\<key>.{jpg|jpeg|png}` — the `KeyedAssetImageConverter`
(`ConverterParameter=<kind>`). Drop a correctly-named file and it appears; a missing one shows a framed
placeholder. Repo copy lives at `data/ams2/<kind>/`; the running RC reads `dist/data/ams2/<kind>/`
(copy there too, or Claude will sync on the next build).

## Status (2026-07-12)

| Kind | Path | Key | Used by | Present | Missing |
|---|---|---|---|---:|---:|
| Driver **portrait** | `portraits/<driverId>.jpg` | driver id | grid, rival, paddock, dossier, promotion | **34 / 34** ✅ | 0 |
| Driver **car** render | `cars/<driverId>.png` | driver id | grid, rival, paddock | **0 / 34** | **34** ⛔ |
| **Player** image (per team) | `portraits/player.<team>.jpg` | team id w/o `team.` | player's grid/paddock/dossier card | **13 / 24** | **11** |
| Team **logo/icon** | `smgp/logos/<teamId>.png` | team id | paddock team cards | **0 / 24** | **24** |
| Team **photo** (large) | `smgp/teams/<team>.jpg` | team id w/o `team.` | promotion/demotion screen | **0 / 24** | **24** |
| **Round** card | `smgp/rounds/<roundNumber>.jpg` | round # (1-16) | rival / briefing | **0 / 16** | **16** |
| Rival **banner** | `smgp/banners/<teamId>.png` | team id | rival dossier header | **0 / 24** | **24** |

**Total missing: ~133.** Biggest impact first: **cars** (34 — blank on every screen a car appears),
then **player images** (11 — blank on the player's own cards), then team **logos** (paddock).

### The 11 missing `player.<team>` images
`player.millions`, `player.bestowal`, `player.blanche`, `player.tyrant`, `player.losel`, `player.may`,
`player.joke`, `player.bullets`, `player.dardan`, `player.linden`, `player.feet`.
(Present: madonna, firenze, iris, azalea, cool, comet, orchis, moon, zeroforce, serga, rigel + 2 others.)

### Everything else is 100% missing
- **cars/** — all 34 `cars/<driverId>.png`. See `drivers.json` for the ids (e.g. `driver.ayrton_senna`).
- **smgp/logos/** — all 24 `smgp/logos/team.<x>.png`.
- **smgp/teams/** — all 24 `smgp/teams/<x>.jpg` (no `team.` prefix, e.g. `smgp/teams/madonna.jpg`).
- **smgp/rounds/** — all 16 `smgp/rounds/<n>.jpg` (n = 1..16; the San Marino etc. venue cards).
- **smgp/banners/** — all 24 `smgp/banners/team.<x>.png` (optional flourish; lowest priority).

## Style + sizing

Match the existing look: **16-bit SEGA "Super Monaco GP" arcade** — the portraits already in
`portraits/` are the reference (detailed painted/pixel faces). Author source at ~2× the display size so
they stay crisp when the cards grow:

| Asset | Display size (current) | Suggested source | Format / notes |
|---|---|---|---|
| Driver portrait | up to ~180-360 sq | 512×512 | jpg, ~square, cropped head-and-shoulders (`UniformToFill`) |
| Car render | ~50-150 tall (rival), ~64-112 (grid/paddock) | 800×300 | **transparent PNG**, clean side-profile in team livery |
| Player image | same as portrait, team-coloured | 512×512 | jpg; the team-liveried "you" helmet/driver |
| Team logo | ~46 (list) → bigger detail | 256×256 | **transparent PNG**, crest/badge |
| Team photo | very large hero | 1280×720 | jpg, the team's garage/car hero shot |
| Round card | ~120-180 | 800×450 | jpg, the venue/skyline |

## Priority
1. **cars/** (34) — unblocks the biggest visual gap; cars are blank on grid + rival + paddock now.
2. **player.<team>** (11) — the player's own card is blank on those teams.
3. **smgp/logos** (24) — the paddock team view.
4. **smgp/teams** (24) — the promotion screen hero.
5. **smgp/rounds** (16), **smgp/banners** (24) — flourish.
