# SMGP Art Asset Inventory

**Owner:** Codex (GUI/art) · **Last audited:** 2026-07-13 · **Roster:** 34 drivers, 24 teams,
16 rounds — ids in `packs/smgp-1/{drivers,teams,entries}.json`.

This is the standing SMGP inventory. The broader cross-mode inventory remains in
`data/ams2/ART-INVENTORY.md`.

## Canonical source rule

The app resolves `{exe}\data\ams2\<kind>\<key>.{jpg|jpeg|png}` through `KeyedAssetImageConverter`.
The live files under **`dist/data/ams2/` are canonical; anything there is law**. Never copy a tracked
`data/ams2` file over its `dist` counterpart. If a secondary archive is wanted, derive it outward from
`dist` and preserve the canonical file byte-for-byte.

## Status

| Kind | Canonical path | Key | Used by | Present | Missing |
|---|---|---|---|---:|---:|
| Driver **portrait** | `portraits/<driverId>.jpg` | driver id | grid, rival, paddock, dossier, promotion | **34 / 34** ✅ | 0 |
| Driver **car** render | `cars/<driverId>.png` | driver id | grid, rival, paddock | **34 / 34** ✅ | 0 |
| **Player** image (per team) | `portraits/player.<team>.jpg` | team id without `team.` | player grid/paddock/dossier | **24 / 24** ✅ | 0 |
| Driver **national flag** | `smgp/flags/<driverId>.png` | driver id | SMGP pixel grid | **34 / 34** ✅ | 0 |
| **Grid car** | `smgp/grid-cars/<driverId>.png` | driver id | SMGP pixel grid | **34 / 34** ✅ | 0 |
| Team **logo/icon** | `smgp/logos/<teamId>.png` | team id | paddock team cards | **24 / 24** ✅ | 0 |
| Team **photo** | `smgp/teams/<team>.jpg` | team id without `team.` | promotion/demotion | **24 / 24** ✅ | 0 |
| **Round** card | `smgp/rounds/<roundNumber>.jpg` | round 1–16 | rival / briefing | **16 / 16** ✅ | 0 |
| Rival **banner** | `smgp/banners/<teamId>.png` | team id | rival dossier | **24 / 24** ✅ | 0 |

The live SMGP presentation set is complete: every roster driver has a portrait, car, flag, and overhead
grid sprite; every team has a player portrait, logo, garage photo, and rival banner; all 16 round cards
are present. The converter fallbacks remain defensive, but no current roster key depends on them.

## Current grid-art contracts

- `smgp/grid-cars/<driverId>.png` is preferred over `cars/<driverId>.png`. The canonical source is
  384×512 transparent RGBA, consistently framed in portrait orientation; `StartingGridView` rotates it
  90° clockwise at layout time so the source remains reusable and the live car points across its bay.
- New rival banners standardize on PNG at 1040×200 (2× the live 520×100 maximum). The resolver also
  accepts legacy JPG/JPEG, but new canonical assets use the inventory's PNG contract.
- `smgp/flags/<driverId>.png` is driver-keyed because `StartingGridSlot` intentionally remains in the
  ViewModel owner’s lane. The 34 canonical 128×128 PNGs were converted locally from the matching
  `GUI/CountryFlags/Flag_*.dds` files in Mike’s AMS2 installation.
- The synthetic player has no authored nationality, so its flag slot is hidden. Never display the donor
  AI driver’s nationality as the player’s.

## Style + sizing

Match the established 16-bit Super Monaco GP arcade treatment:

| Asset | Display | Suggested source | Notes |
|---|---:|---:|---|
| Driver portrait | 78px grid; player scales to ~113px | 512×512 | painted/pixel head-and-shoulders, square crop |
| Car preview | ~100–300px grid; 150px rival | 900×281 current | transparent PNG, exact team livery |
| Grid-car sprite | ~220×150 after layout rotation | 384×512 | transparent, consistent overhead angle |
| Player image | portrait sizes | 512×512 | team-coloured driver/helmet art |
| National flag | 38×28 grid frame | 128×128 current | canonical AMS2 flag icon, driver-keyed |
| Team logo | ~46px list → larger detail | 256×256 | transparent crest/badge |
| Team photo | large hero | 1280×720 | garage/car hero shot |
| Round card | ~120–180px | 800×450 | venue/skyline |
| Rival banner | up to 520×100 | 1040×200 | transparent PNG, team identity strip |

## Priority

The current roster has no blocking art gaps. Future work is replacement/expansion art only: add new
keys alongside new content, keep every source at its documented aspect, and rerun the RenderHarness
asset-contract tests before promotion to `dist`.
